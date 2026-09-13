using System.IO;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 高通 9008（EDL）模式操作服务：
    /// 封装 QSaharaServer（上传 firehose 编程器）与 fh_loader（端口配置/发送 XML/重启）。
    /// 对应 bat 中反复出现的 9008 刷机流程。
    /// </summary>
    public static class EdlService
    {
        // ==================== XML 模板（与 bat 中 echo 生成的内容完全一致） ====================

        /// <summary>端口配置 XML。</summary>
        public const string ConfigureXml =
            "<?xml version=\"1.0\" ?><data><configure MemoryName=\"ufs\" Verbose=\"0\" AlwaysValidate=\"0\" " +
            "MaxDigestTableSizeInBytes=\"8192\" MaxPayloadSizeToTargetInBytes=\"1048576\" ZlpAwareHost=\"1\" " +
            "SkipStorageInit=\"0\" /></data>";

        /// <summary>解锁 BL：写入已解锁 ABL 镜像的 XML。</summary>
        public const string UnlockAblXml =
            "<?xml version=\"1.0\" ?><data><program filename=\"Huawei865870_abl_unlock.img\" label=\"abl\" " +
            "physical_partition_number=\"4\" start_sector=\"49670\" num_partition_sectors=\"1024\" " +
            "SECTOR_SIZE_IN_BYTES=\"4096\"/></data>";

        /// <summary>读取 boot 分区的 XML（配合 fh_loader 的 --convertprogram2read 使用）。</summary>
        public const string ReadBootXml =
            "<?xml version=\"1.0\" ?><data><program filename=\"boot.img\" physical_partition_number=\"4\" " +
            "label=\"boot\" start_sector=\"67334\" num_partition_sectors=\"24576\" SECTOR_SIZE_IN_BYTES=\"4096\" " +
            "sparse=\"false\"/></data>";

        /// <summary>重启设备 XML。</summary>
        public const string RebootXml =
            "<?xml version=\"1.0\" ?><data><power DelayInSeconds=\"0\" value=\"reset\" /></data>";

        /// <summary>写入 misc 分区的 XML。</summary>
        public const string MiscXml =
            "<?xml version=\"1.0\" ?><data><program filename=\"misc.img\" label=\"misc\" " +
            "physical_partition_number=\"0\" start_sector=\"9480\" num_partition_sectors=\"512\" " +
            "SECTOR_SIZE_IN_BYTES=\"4096\"/></data>";

        private static string ComPath(int port) => $@"\\.\COM{port}";

        /// <summary>
        /// 通过 QSaharaServer 上传 firehose 编程器（Huawei865870_devprg.elf）。
        /// </summary>
        public static async Task<bool> UploadFirehoseAsync(int port, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.QSaharaServerExe))
            {
                log("[错误] 缺少 tools\\QSaharaServer.exe");
                return false;
            }
            if (!File.Exists(AppConfig.DevprgElf))
            {
                log("[错误] 缺少底层文件 unlock\\Huawei865870_devprg.elf");
                return false;
            }

            log("开始上传编程器...");
            var args = $"-p {ComPath(port)} -s 13:\"{AppConfig.DevprgElf}\"";
            var code = await ProcessRunner.RunAsync(AppConfig.QSaharaServerExe, args,
                line => log($"[Sahara] {line}"), AppConfig.ToolsDir, ct);
            if (code != 0)
            {
                log("[错误] 上传编程器失败!");
                return false;
            }
            log("[OK] 编程器上传成功。");
            return true;
        }

        /// <summary>
        /// 使用 fh_loader 配置端口。
        /// </summary>
        /// <param name="searchPath">镜像搜索目录（对应 --search_path）</param>
        public static async Task<bool> ConfigurePortAsync(int port, string searchPath, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.FhLoaderExe))
            {
                log("[错误] 缺少 tools\\fh_loader.exe");
                return false;
            }

            var xmlPath = AppConfig.WriteTmpFile("configure.xml", ConfigureXml);
            log("正在配置端口...");
            var args = $"--port={ComPath(port)} --memoryname=ufs --configure=\"{xmlPath}\" " +
                       $"--search_path=\"{searchPath}\" --mainoutputdir=\"{AppConfig.LogDir}\" --noprompt";
            var code = await ProcessRunner.RunAsync(AppConfig.FhLoaderExe, args,
                line => log($"[fh_loader] {line}"), AppConfig.ToolsDir, ct);
            if (code != 0)
            {
                log("[错误] 配置端口失败!");
                return false;
            }
            log("[OK] 端口配置成功。");
            return true;
        }

        /// <summary>
        /// 自动配置端口（对应原版 write.bat qcedlsendfh 的 auto 配置端口步骤）：
        /// 依次尝试 ufs / emmc / spinor，每次由 fh_loader 自动发送 configure 初始化存储
        /// （不传 --skip_configure），并依据 port_trace.txt 中的 configure ACK 判断是否成功。
        /// 上传编程器后必须先执行此步骤，后续带 --skip_configure 的读取/回读才能访问存储。
        /// </summary>
        public static async Task<bool> ConfigurePortAutoAsync(int port, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.FhLoaderExe))
            {
                log("[错误] 缺少 tools\\fh_loader.exe");
                return false;
            }

            (string Mem, int SecSize, int Sectors)[] probes =
            {
                ("ufs", 4096, 6),
                ("emmc", 512, 34),
                ("spinor", 4096, 6),
            };

            foreach (var (mem, secSize, sectors) in probes)
            {
                ct.ThrowIfCancellationRequested();
                log($"尝试{mem}模式配置端口...");
                try
                {
                    var traceFile = Path.Combine(AppConfig.TmpDir, "port_trace.txt");
                    try { if (File.Exists(traceFile)) File.Delete(traceFile); } catch { }

                    var xml = AppConfig.WriteTmpFile("configure_probe.xml",
                        $"<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{secSize}\" filename=\"tmp.bin\" " +
                        $"physical_partition_number=\"0\" label=\"PrimaryGPT\" start_sector=\"0\" num_partition_sectors=\"{sectors}\" /></data>");

                    var args = $"--port={ComPath(port)} --memoryname={mem} --sendxml=\"{xml}\" --convertprogram2read " +
                               $"--mainoutputdir=\"{AppConfig.TmpDir}\" --noprompt";
                    await ProcessRunner.RunAsync(AppConfig.FhLoaderExe, args,
                        line => log($"[fh_loader] {line}"), AppConfig.ToolsDir, ct);

                    if (File.Exists(traceFile))
                    {
                        var trace = File.ReadAllText(traceFile);
                        if (trace.Contains("Got the ACK for the <configure>") ||
                            trace.Contains("Target returned NAK for your <configure> but it does not seem to be an error"))
                        {
                            log($"[OK] {mem}模式配置端口成功。");
                            return true;
                        }
                    }
                    log($"{mem}模式配置端口失败。");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log($"[警告] {mem}模式配置端口异常：{ex.Message}");
                }
            }

            log("[错误] 自动配置端口失败（已尝试 ufs / emmc / spinor）。");
            return false;
        }

        /// <summary>
        /// 使用 fh_loader 发送一个 rawprogram XML 到设备。
        /// </summary>
        public static async Task<bool> SendXmlAsync(
            int port, string xmlPath, string searchPath, Action<string> log, CancellationToken ct = default)
        {
            var args = $"--port={ComPath(port)} --memoryname=ufs --sendxml=\"{xmlPath}\" " +
                       $"--search_path=\"{searchPath}\" --mainoutputdir=\"{AppConfig.LogDir}\" " +
                       $"--skip_configure --noprompt";
            var code = await ProcessRunner.RunAsync(AppConfig.FhLoaderExe, args,
                line => log($"[fh_loader] {line}"), AppConfig.ToolsDir, ct);
            return code == 0;
        }

        /// <summary>
        /// 写入临时 XML 文件并发送到设备。
        /// </summary>
        public static async Task<bool> SendXmlContentAsync(
            int port, string fileName, string xmlContent, string searchPath, Action<string> log, CancellationToken ct = default)
        {
            var xmlPath = AppConfig.WriteTmpFile(fileName, xmlContent);
            return await SendXmlAsync(port, xmlPath, searchPath, log, ct);
        }

        /// <summary>
        /// 通过 9008 重启设备。
        /// </summary>
        public static async Task<bool> RebootDeviceAsync(int port, string searchPath, Action<string> log, CancellationToken ct = default)
        {
            log("正在重启设备...");
            var ok = await SendXmlContentAsync(port, "reboot.xml", RebootXml, searchPath, log, ct);
            if (ok) log("[OK] 重启指令已发送。");
            return ok;
        }

        /// <summary>
        /// 完整的 9008 连接流程：（可选 ADB 重启进 9008）→ 等待端口 → 上传编程器 + 配置端口。
        /// 对应 bat 的 :q9008_do_connect，并按解锁BL的逻辑支持鸿蒙2-3 自动进入 / 鸿蒙4 手动进入。
        /// </summary>
        /// <param name="rebootViaAdb">true=鸿蒙2-3：先等 ADB 设备再 adb reboot edl；false=鸿蒙4：等待用户手动进入 9008</param>
        public static async Task<int> ConnectAsync(
            bool rebootViaAdb, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.DevprgElf))
            {
                log("[错误] 缺少编程文件 unlock\\Huawei865870_devprg.elf");
                return -1;
            }

            if (rebootViaAdb)
            {
                log("检测 ADB 设备...");
                if (!await DeviceService.WaitForAdbAsync(log, ct)) return -1;
                log("[OK] 设备已连接。");
                await DeviceService.RebootToEdlAsync(log);
                log("等待设备进入 9008 模式...");
            }
            else
            {
                log("请确认设备已进入 9008 模式（鸿蒙4 需通过工程线/短接方式进入），正在等待设备连接...");
            }

            var port = await ComPortDetector.WaitFor9008Async(15, 2000, log, ct);
            if (!port.HasValue)
            {
                log("[错误] 等待超时，未检测到 9008 设备。");
                if (!rebootViaAdb) log("请重新确认已通过探针/短接方式进入 9008。");
                return -1;
            }
            log($"[OK] 已检测到 9008 设备，端口 COM{port.Value}");

            if (!await UploadFirehoseAsync(port.Value, log, ct)) return -1;
            if (!await ConfigurePortAsync(port.Value, AppConfig.CacheDir, log, ct)) return -1;
            return port.Value;
        }
    }
}
