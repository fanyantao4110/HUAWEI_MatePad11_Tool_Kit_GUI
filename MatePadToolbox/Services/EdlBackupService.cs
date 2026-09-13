using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 9008 回读全分区服务。
    /// 原生合并自高通工具箱框架脚本（酷安@某贼）：
    ///   toolbox.bat READ-ALL → qctool.bat edlreadall → info.bat qcedl / partable.bat readgpt / read.bat qcedlxml
    /// GPT 分区表解析用纯 C# 实现（替代 ptanalyzer.exe），端口检测用现有 ComPortDetector（替代 devcon.exe）。
    /// 与原脚本一致：默认不回读 userdata / last_parti / mindows* 分区；不弹出记事本确认。
    /// </summary>
    public static partial class EdlBackupService
    {
        /// <summary>GPT 中的一个分区条目。</summary>
        public sealed record GptPartition(
            int Index, string Name, ulong StartSector, ulong SizeSectors,
            string TypeGuid);

        /// <summary>设备存储信息（对应 info.bat 的 info__qcedl__* 三个变量）。</summary>
        public sealed record MemoryInfo(string MemType, int SectorSize, int LunNum);

        /// <summary>
        /// 默认跳过不回读的分区（对应 qctool.bat 中 busybox sed 去掉 [] 的分区）。
        /// </summary>
        public static readonly HashSet<string> DefaultSkipPartitions = new(StringComparer.Ordinal)
        {
            "userdata", "last_parti", "mindowsesp", "mindowswin", "mindowsdat"
        };

        [GeneratedRegex(@"(\d+)%")]
        private static partial Regex PercentRegex();

        // ==================== 主流程（对应 qctool.bat :EDLREADALL） ====================

        /// <summary>
        /// 执行 9008 回读全分区。
        /// </summary>
        /// <param name="saveRoot">保存目录（bat 中的 filefolder），内部会创建 QCTool_ParReadback_时间戳 子目录</param>
        /// <param name="port">9008 设备 COM 端口号（firehose 需已上传）</param>
        public static async Task<bool> RunReadAllAsync(string saveRoot, int port, Action<string> log, CancellationToken ct = default)
        {
            // ---------- 目录准备（对应 bat 的 md 语句与 gettime.exe 时间戳） ----------
            if (!Directory.Exists(saveRoot))
            {
                log($"[错误] 找不到保存目录：{saveRoot}");
                return false;
            }
            var baktime = DateTime.Now.ToString("yyyy.MM.dd-HH.mm.ss");
            var imgpath = Path.Combine(saveRoot, $"QCTool_ParReadback_{baktime}");
            var imagesDir = Path.Combine(imgpath, "images");
            var origDir = Path.Combine(imgpath, "gpt_and_xml", "orig");
            var createDir = Path.Combine(imgpath, "gpt_and_xml", "create");
            try
            {
                Directory.CreateDirectory(imagesDir);
                Directory.CreateDirectory(origDir);
                Directory.CreateDirectory(createDir);
            }
            catch (Exception ex)
            {
                log($"[错误] 创建 {imgpath} 失败：{ex.Message}");
                return false;
            }

            // ---------- 读取设备信息（对应 call info qcedl %port%） ----------
            var info = await DetectMemoryInfoAsync(port, log, ct);
            if (info == null) return false;
            log($"存储类型: {info.MemType}   lun数目: {info.LunNum}   扇区大小: {info.SectorSize}b");

            // ---------- partition.xml 表头（对应 bat 的 echo 生成） ----------
            var partXml = new StringBuilder();
            partXml.AppendLine("<?xml version=\"1.0\" ?>");
            partXml.AppendLine("<configuration>");
            partXml.AppendLine("<parser_instructions>");
            partXml.AppendLine("WRITE_PROTECT_BOUNDARY_IN_KB=0");
            partXml.AppendLine($"SECTOR_SIZE_IN_BYTES = {info.SectorSize}");
            partXml.AppendLine("GROW_LAST_PARTITION_TO_FILL_DISK=true");
            partXml.AppendLine("</parser_instructions>");

            var flashAll = new StringBuilder();
            var xmlList = new List<string>();

            // ---------- 逐 LUN 回读分区表并生成 rawprogram xml ----------
            for (int lun = 0; lun < info.LunNum; lun++)
            {
                ct.ThrowIfCancellationRequested();
                log($"===== 处理分区表 {lun} =====");

                var mainGpt = Path.Combine(origDir, $"gpt_main{lun}.bin");
                var backupGpt = Path.Combine(origDir, $"gpt_backup{lun}.bin");
                if (!await ReadGptAsync(port, info, lun, false, mainGpt, log, ct)) return false;
                if (!await ReadGptAsync(port, info, lun, true, backupGpt, log, ct)) return false;

                // 解析分区表（对应 ptanalyzer.exe -o normal_clear）
                List<GptPartition> parts;
                try
                {
                    parts = ParseGpt(File.ReadAllBytes(mainGpt), info.SectorSize);
                }
                catch (Exception ex)
                {
                    log($"[错误] 解析分区表{lun}失败：{ex.Message}");
                    return false;
                }
                if (parts.Count == 0)
                {
                    log($"[错误] 分区表{lun}中没有解析到任何分区。");
                    return false;
                }
                log($"分区表{lun}解析完成，共 {parts.Count} 个分区。");

                // partition.xml 的 physical_partition 段（对应 ptanalyzer -o xml_partition | find "partition label="）
                partXml.AppendLine("<physical_partition>");
                foreach (var p in parts)
                {
                    partXml.AppendLine(
                        $"<partition label=\"{p.Name}\" addoffset=\"0\" size_in_kb=\"{p.SizeSectors * (ulong)info.SectorSize / 1024}\" " +
                        $"type=\"{p.TypeGuid}\" bootable=\"false\" readonly=\"true\" filename=\"{p.Name}.img\" />");
                }

                // 对应 ptanalyzer -shownonpartitioned y：lun0 以外每个 lun 末尾补一条 last_parti 占位条目
                if (lun > 0)
                {
                    partXml.AppendLine(
                        "<partition label=\"last_parti\" addoffset=\"0\" size_in_kb=\"0\" " +
                        "type=\"00000000-0000-0000-0000-000000000000\" bootable=\"false\" readonly=\"true\" filename=\"last_parti.img\" />");
                }
                partXml.AppendLine("</physical_partition>");

                // rawprogram{lun}.xml（对应 bat 的 echo program 行）
                var rpPath = Path.Combine(origDir, $"rawprogram{lun}.xml");
                var rp = new StringBuilder();
                rp.AppendLine("<?xml version=\"1.0\" ?>");
                rp.AppendLine("<data>");
                bool firstOfLun = true;
                foreach (var p in parts)
                {
                    // 当前 lun 第 1 个分区前加入分区表刷入脚本行（bat 中原样注释掉）
                    if (firstOfLun)
                    {
                        flashAll.AppendLine($"::fastboot flash partition:{lun} %~dp0images\\gpt_both{lun}.bin || @echo \"Flash gpt_both{lun}.bin error\"");
                        firstOfLun = false;
                    }

                    bool skip = DefaultSkipPartitions.Contains(p.Name);
                    var filename = skip ? "" : $"{p.Name}.img";
                    rp.AppendLine(
                        $"<program filename=\"{filename}\" label=\"{p.Name}\" physical_partition_number=\"{lun}\" " +
                        $"start_sector=\"{p.StartSector}\" num_partition_sectors=\"{p.SizeSectors}\" " +
                        $"SECTOR_SIZE_IN_BYTES=\"{info.SectorSize}\"/>");

                    if (skip)
                    {
                        flashAll.AppendLine($"::fastboot flash {p.Name} %~dp0images\\{p.Name}.img || @echo \"Flash {p.Name} error\"");
                        log($"跳过回读 {p.Name}（lun:{lun}，编号:{p.Index}，默认不回读，与原脚本一致）");
                    }
                    else
                    {
                        flashAll.AppendLine($"fastboot flash {p.Name} %~dp0images\\{p.Name}.img || @echo \"Flash {p.Name} error\"");
                    }
                }

                // 分区表自身的 program 条目（ufs 与其他存储的扇区数不同）
                int gptMainSec = info.SectorSize == 4096 ? 6 : 34;
                int gptBakSec = info.SectorSize == 4096 ? 5 : 33;
                string bakStart = info.SectorSize == 4096 ? "NUM_DISK_SECTORS-5." : "NUM_DISK_SECTORS-33.";
                rp.AppendLine(
                    $"<program filename=\"gpt_main{lun}.bin\" label=\"PrimaryGPT\" physical_partition_number=\"{lun}\" " +
                    $"start_sector=\"0\" num_partition_sectors=\"{gptMainSec}\" SECTOR_SIZE_IN_BYTES=\"{info.SectorSize}\"/>");
                rp.AppendLine(
                    $"<program filename=\"gpt_backup{lun}.bin\" label=\"BackupGPT\" physical_partition_number=\"{lun}\" " +
                    $"start_sector=\"{bakStart}\" num_partition_sectors=\"{gptBakSec}\" SECTOR_SIZE_IN_BYTES=\"{info.SectorSize}\"/>");
                rp.AppendLine("</data>");
                File.WriteAllText(rpPath, rp.ToString(), Encoding.ASCII);
                xmlList.Add(rpPath);
            }

            partXml.AppendLine("</configuration>");
            var partXmlPath = Path.Combine(origDir, "partition.xml");
            File.WriteAllText(partXmlPath, partXml.ToString(), Encoding.ASCII);
            File.WriteAllText(Path.Combine(imgpath, "flash_all.bat"), flashAll.ToString(), Encoding.ASCII);

            // ---------- ptool 生成新分区表和 xml（对应 bat 的 if exist ptool.exe，可选） ----------
            if (File.Exists(AppConfig.PtoolExe))
            {
                log("开始生成分区表和xml文件...");
                var code = await ProcessRunner.RunAsync(
                    AppConfig.PtoolExe,
                    $"-x \"{partXmlPath}\" -t \"{createDir}\"",
                    line => log($"[ptool] {line}"),
                    AppConfig.ToolsDir, ct);
                log(code == 0 ? "[OK] 生成分区表和xml文件完成。" : "[警告] ptool 生成失败（不影响回读结果）。");
            }

            // ---------- 开始回读（对应 call read qcedlxml，xml 列表以逗号分隔一次传入） ----------
            log("开始回读全分区，耗时较长，请勿断开数据线...");
            var xmls = string.Join(",", xmlList);
            int rc = await RunFhLoaderRead(port, info.MemType, xmls, imagesDir, log, ct, showPercent: true);
            if (rc != 0)
            {
                log("[错误] 9008回读失败！");
                return false;
            }
            MovePortTrace(imagesDir);

            // ---------- 复制 rawprogram*.xml 到 images（对应 bat 的 copy /Y） ----------
            foreach (var x in xmlList)
            {
                try { File.Copy(x, Path.Combine(imagesDir, Path.GetFileName(x)), true); }
                catch { /* 忽略复制失败 */ }
            }

            log("==========================================================");
            log($"  9008回读全分区完成！");
            log($"  镜像保存在：{imagesDir}");
            log($"  分区表与XML：{origDir}");
            log($"  一键线刷脚本：{Path.Combine(imgpath, "flash_all.bat")}");
            log("==========================================================");
            return true;
        }

        // ==================== 设备信息探测（对应 info.bat :QCEDL） ====================

        /// <summary>
        /// 探测存储类型 / 扇区大小 / LUN 总数。
        /// 与 info.bat 一致：先试 ufs（部分 ufs 设备试 emmc 会掉端口），再 emmc，再 spinor。
        /// </summary>
        public static async Task<MemoryInfo?> DetectMemoryInfoAsync(int port, Action<string> log, CancellationToken ct = default)
        {
            log("读取设备信息...");

            // ufs：读 6 个 4096 扇区，应得到 24576 字节
            if (await ProbeMemoryAsync(port, "ufs", 4096, 6, 24576, ct))
            {
                // 测试 ufs 可用 lun 总数（对应 :QCEDL-TESTLUNNUM）
                log("测试ufs可用lun总数...");
                int lunnum = 0;
                for (int n = 0; n <= 8; n++)
                {
                    if (!await ProbeLunAsync(port, n, ct)) break;
                    lunnum++;
                }
                if (lunnum == 0)
                {
                    log("[错误] 当前设备可用lun总数为0，读取设备信息失败。");
                    return null;
                }
                if (lunnum > 8) log($"[警告] 当前设备可用lun总数为{lunnum}，常规lun总数应小于等于8。");
                if (lunnum < 6) log($"[警告] 当前设备可用lun总数为{lunnum}，常规lun总数应大于等于6。");
                return new MemoryInfo("ufs", 4096, lunnum);
            }
            log("ufs 探测失败，尝试 emmc...");

            // emmc：读 34 个 512 扇区，应得到 17408 字节
            if (await ProbeMemoryAsync(port, "emmc", 512, 34, 17408, ct))
            {
                return new MemoryInfo("emmc", 512, 1);
            }
            log("emmc 探测失败，尝试 spinor...");

            // spinor：读 6 个 4096 扇区，应得到 24576 字节
            if (await ProbeMemoryAsync(port, "spinor", 4096, 6, 24576, ct))
            {
                return new MemoryInfo("spinor", 4096, 1);
            }

            log("[错误] 9008读取设备信息失败（已尝试 ufs / emmc / spinor）。");
            return null;
        }

        /// <summary>读取 LUN0 主 GPT 指定扇区数，检查输出文件大小（对应 info.bat 的 tmp.bin 探测）。</summary>
        private static async Task<bool> ProbeMemoryAsync(int port, string mem, int secsize, int sectors, long expectedSize, CancellationToken ct)
        {
            var tmpBin = Path.Combine(AppConfig.TmpDir, "tmp.bin");
            try { if (File.Exists(tmpBin)) File.Delete(tmpBin); } catch { }

            var xml = AppConfig.WriteTmpFile("tmp.xml",
                $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{secsize}\" filename=\"tmp.bin\" " +
                $"physical_partition_number=\"0\" label=\"PrimaryGPT\" start_sector=\"0\" num_partition_sectors=\"{sectors}\" /></data>");

            var rc = await RunFhLoaderRead(port, mem, xml, AppConfig.TmpDir, _ => { }, ct, showPercent: false);
            if (rc != 0) return false;
            if (!File.Exists(tmpBin)) return false;
            return new FileInfo(tmpBin).Length == expectedSize;
        }

        /// <summary>尝试读取指定 LUN 的主 GPT 并验证（对应 :QCEDL-TESTLUNNUM 循环体）。</summary>
        private static async Task<bool> ProbeLunAsync(int port, int lun, CancellationToken ct)
        {
            var tmpBin = Path.Combine(AppConfig.TmpDir, $"gpt_main{lun}.bin");
            try { if (File.Exists(tmpBin)) File.Delete(tmpBin); } catch { }

            var xml = AppConfig.WriteTmpFile("tmp.xml",
                $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"4096\" filename=\"gpt_main{lun}.bin\" " +
                $"physical_partition_number=\"{lun}\" label=\"PrimaryGPT\" start_sector=\"0\" num_partition_sectors=\"6\" /></data>");

            var rc = await RunFhLoaderRead(port, "ufs", xml, AppConfig.TmpDir, _ => { }, ct, showPercent: false);
            if (rc != 0 || !File.Exists(tmpBin)) return false;
            try
            {
                var parts = ParseGpt(File.ReadAllBytes(tmpBin), 4096);
                return parts.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        // ==================== 分区表回读（对应 partable.bat :QCEDL-READGPT） ====================

        /// <summary>回读指定 LUN 的主/备份分区表到目标文件。</summary>
        private static async Task<bool> ReadGptAsync(
            int port, MemoryInfo info, int lun, bool backup, string destFile, Action<string> log, CancellationToken ct)
        {
            int sec = info.SectorSize;
            string label, start, count;
            if (!backup)
            {
                label = "PrimaryGPT";
                start = "0";
                count = sec == 4096 ? "6" : "34";
            }
            else
            {
                label = "BackupGPT";
                start = sec == 4096 ? "NUM_DISK_SECTORS-5." : "NUM_DISK_SECTORS-33.";
                count = sec == 4096 ? "5" : "33";
            }

            var xml = AppConfig.WriteTmpFile("tmp.xml",
                $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{sec}\" filename=\"{Path.GetFileName(destFile)}\" " +
                $"physical_partition_number=\"{lun}\" label=\"{label}\" start_sector=\"{start}\" num_partition_sectors=\"{count}\" /></data>");

            log($"正在9008回读分区表{(backup ? "backup" : "main")}{lun}...");
            var rc = await RunFhLoaderRead(port, info.MemType, xml, Path.GetDirectoryName(destFile)!, log, ct, showPercent: false);
            if (rc != 0 || !File.Exists(destFile))
            {
                log($"[错误] 9008回读分区表{(backup ? "backup" : "main")}{lun}失败。");
                return false;
            }
            MovePortTrace(Path.GetDirectoryName(destFile)!);
            log($"9008回读分区表{(backup ? "backup" : "main")}{lun}完成。");
            return true;
        }

        // ==================== fh_loader 调用（对应 read.bat :QCEDLXML 的核心命令） ====================

        private static async Task<int> RunFhLoaderRead(
            int port, string memType, string sendXml, string outDir,
            Action<string> log, CancellationToken ct, bool showPercent)
        {
            if (!File.Exists(AppConfig.FhLoaderExe))
            {
                log("[错误] 缺少 tools\\fh_loader.exe");
                return -1;
            }

            Directory.CreateDirectory(outDir);
            var args =
                $"--port=\\\\.\\COM{port} --memoryname={memType} --sendxml=\"{sendXml}\" --convertprogram2read " +
                $"--mainoutputdir=\"{outDir}\" --skip_configure" +
                (showPercent ? " --showpercentagecomplete" : "") + " --noprompt";

            int lastPct = -1;
            return await ProcessRunner.RunAsync(AppConfig.FhLoaderExe, args, line =>
            {
                var m = PercentRegex().Match(line);
                if (m.Success)
                {
                    // 进度行去重：每 10% 输出一次
                    int pct = int.Parse(m.Groups[1].Value);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        if (pct % 10 == 0 || pct == 100) log($"[进度] {pct}%");
                    }
                    return;
                }
                if (!string.IsNullOrWhiteSpace(line)) log($"[fh_loader] {line}");
            }, AppConfig.ToolsDir, ct);
        }

        /// <summary>把 fh_loader 生成的 port_trace.txt 挪到 tmp（对应 bat 的 move /Y）。</summary>
        private static void MovePortTrace(string dir)
        {
            try
            {
                var trace = Path.Combine(dir, "port_trace.txt");
                if (File.Exists(trace))
                {
                    Directory.CreateDirectory(AppConfig.TmpDir);
                    File.Move(trace, Path.Combine(AppConfig.TmpDir, "port_trace.txt"), true);
                }
            }
            catch { /* 忽略 */ }
        }

        // ==================== GPT 解析（替代 ptanalyzer.exe） ====================

        /// <summary>
        /// 解析 GPT 主分区表二进制（gpt_mainN.bin），返回按槽位顺序的分区列表（编号从 1 开始，
        /// 与 ptanalyzer -o normal_clear 的 [n] 编号一致）。
        /// </summary>
        public static List<GptPartition> ParseGpt(byte[] data, int sectorSize)
        {
            if (data.Length < sectorSize * 2)
                throw new InvalidDataException($"镜像过小（{data.Length} 字节），无法包含 GPT 头部。");

            int off = sectorSize; // LBA1 = GPT 头部
            var sig = Encoding.ASCII.GetString(data, off, 8);
            if (sig != "EFI PART")
                throw new InvalidDataException($"未找到 GPT 签名（EFI PART），实际为：{sig}");

            ulong entLba = BitConverter.ToUInt64(data, off + 72);
            uint numEnt = BitConverter.ToUInt32(data, off + 80);
            uint entSize = BitConverter.ToUInt32(data, off + 84);
            if (entSize < 128 || numEnt == 0 || numEnt > 512)
                throw new InvalidDataException($"GPT 头部参数异常：num={numEnt}, size={entSize}");

            // 先用 long 做边界检查（防止溢出），检查通过后再转 int
            long baseOffL = (long)entLba * sectorSize;
            if (baseOffL + (long)numEnt * entSize > data.Length)
                throw new InvalidDataException("GPT 分区条目超出镜像范围（回读的扇区数不足）。");

            int baseOff = (int)baseOffL;
            int entSizeI = (int)entSize;

            var list = new List<GptPartition>();
            for (int i = 0; i < numEnt; i++)
            {
                int e = baseOff + i * entSizeI;      // ← int，切片语法合法
                var typeGuid = data[e..(e + 16)];
                if (typeGuid.All(b => b == 0)) continue; // 空槽位
                ulong first = BitConverter.ToUInt64(data, e + 32);
                ulong last  = BitConverter.ToUInt64(data, e + 40);
                string name = Encoding.Unicode.GetString(data, e + 56, 72).TrimEnd('\0');
                if (last < first) continue; // 异常条目跳过

                list.Add(new GptPartition(
                    list.Count + 1, name, first, last - first + 1,
                    GuidToUuid(typeGuid)));
            }
            return list;
        }

        /// <summary>
        /// GPT GUID 是混合端序（前 3 段小端），转换为 ptanalyzer partition.xml 使用的
        /// 标准 uuid 小写格式 "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"。
        /// </summary>
        private static string GuidToUuid(byte[] g)
        {
            uint d1 = BitConverter.ToUInt32(g, 0);
            ushort d2 = BitConverter.ToUInt16(g, 4);
            ushort d3 = BitConverter.ToUInt16(g, 6);
            var tail = g[8..16].Select(b => b.ToString("x2"));
            return $"{d1:x8}-{d2:x4}-{d3:x4}-{string.Join("", tail.Take(2))}-{string.Join("", tail.Skip(2))}";
        }
    }
}