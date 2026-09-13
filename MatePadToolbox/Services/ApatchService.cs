using System.IO;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// APatch Root 服务：提取 boot → kptools 修补 → 刷回修补后的 boot。
    /// 对应 bat 中的 :apatch_extract_os23 / :apatch_extract_os4 / :apatch_patch / :apatch_flash。
    /// </summary>
    public static class ApachService
    {
        public static string BootImgPath => Path.Combine(AppConfig.OutDir, "boot.img");
        public static string PatchedBootPath => Path.Combine(AppConfig.OutDir, "apatch_patched_boot.img");

        /// <summary>
        /// 步骤一：通过 9008 模式提取 boot 分区。
        /// </summary>
        /// <param name="rebootViaAdb">鸿蒙2/3 需要先 adb reboot edl；鸿蒙4 由用户自行进入 9008</param>
        public static async Task<bool> ExtractBootAsync(bool rebootViaAdb, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.DevprgElf))
            {
                log("[错误] 缺少编程文件 unlock\\Huawei865870_devprg.elf");
                return false;
            }
            log("[OK] 文件检查通过。");

            if (rebootViaAdb)
            {
                log("检测 ADB 设备...");
                if (!await DeviceService.WaitForAdbAsync(log, ct)) return false;
                await DeviceService.RebootToEdlAsync(log);
                log("等待设备进入 9008 模式...");
            }
            else
            {
                log("等待设备进入 9008 模式（请确保已通过探针/短接进入）...");
            }

            var port = await ComPortDetector.WaitFor9008Async(15, 2000, log, ct);
            if (!port.HasValue)
            {
                log("[错误] 等待超时，未检测到 9008 设备。");
                return false;
            }
            log($"[OK] 检测到 9008 设备，端口 COM{port.Value}");

            if (!await EdlService.UploadFirehoseAsync(port.Value, log, ct)) return false;
            if (!await EdlService.ConfigurePortAsync(port.Value, AppConfig.TmpDir, log, ct)) return false;

            log("boot 分区信息: LUN=4, 起始扇区=67334, 扇区数=24576 (96MB)");
            var readXml = AppConfig.WriteTmpFile("read_boot.xml", EdlService.ReadBootXml);
            Directory.CreateDirectory(AppConfig.OutDir);

            log("正在读取 boot 分区（可能需要几分钟）...");
            var args = $"--port=\\\\.\\COM{port.Value} --memoryname=ufs --sendxml=\"{readXml}\" " +
                       $"--convertprogram2read --mainoutputdir=\"{AppConfig.OutDir}\" --skip_configure --noprompt";
            var code = await ProcessRunner.RunAsync(AppConfig.FhLoaderExe, args,
                line => log($"[fh_loader] {line}"), AppConfig.ToolsDir, ct);

            if (code != 0 || !File.Exists(BootImgPath))
            {
                log("[错误] 提取 boot 分区失败！正在重启设备...");
                await EdlService.RebootDeviceAsync(port.Value, AppConfig.UnlockDir, log, ct);
                return false;
            }

            log($"[OK] boot 提取成功，文件已保存到：{BootImgPath}");
            await EdlService.RebootDeviceAsync(port.Value, AppConfig.UnlockDir, log, ct);
            log("设备正在重启，等待进入系统后继续修补...");
            return true;
        }

        /// <summary>
        /// 步骤二：使用 kptools 修补 boot.img（unpack → 修补内核 → repack）。
        /// </summary>
        public static async Task<bool> PatchBootAsync(Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.KptoolsExe))
            {
                log("[错误] 缺少文件 tools\\apatch_tools\\kptools.exe");
                return false;
            }
            if (!File.Exists(AppConfig.KpimgFile))
            {
                log("[错误] 缺少文件 tools\\apatch_tools\\kpimg-android");
                return false;
            }
            if (!File.Exists(BootImgPath))
            {
                log("[错误] 未找到 boot.img，请先提取 boot 分区。");
                return false;
            }
            log("[OK] 文件检查通过，开始修补...");

            var workDir = AppConfig.ApatchToolsDir;
            var kernelFile = Path.Combine(workDir, "kernel");
            var kernelBackup = Path.Combine(workDir, "kernel-b");
            var newBootFile = Path.Combine(workDir, "new-boot.img");

            // 1. 解包 boot.img
            log("正在解包 boot.img ...");
            var code = await ProcessRunner.RunAsync(AppConfig.KptoolsExe, $"unpack \"{BootImgPath}\"",
                line => log($"[kptools] {line}"), workDir, ct);
            if (code != 0)
            {
                log("[错误] 解包 boot.img 失败！");
                return false;
            }
            if (!File.Exists(kernelFile))
            {
                log("[错误] 解包后未找到 kernel 文件。");
                return false;
            }

            // 2. 修补内核
            if (File.Exists(kernelBackup)) File.Delete(kernelBackup);
            File.Move(kernelFile, kernelBackup);
            log("正在修补内核...");
            code = await ProcessRunner.RunAsync(AppConfig.KptoolsExe,
                "-p --image kernel-b --kpimg kpimg-android --out kernel",
                line => log($"[kptools] {line}"), workDir, ct);
            if (code != 0)
            {
                log("[错误] 内核修补失败！");
                return false;
            }

            // 3. 重新打包
            log("正在重新打包 boot.img ...");
            code = await ProcessRunner.RunAsync(AppConfig.KptoolsExe, $"repack \"{BootImgPath}\"",
                line => log($"[kptools] {line}"), workDir, ct);
            if (code != 0 || !File.Exists(newBootFile))
            {
                log("[错误] 重打包 boot.img 失败！");
                return false;
            }

            // 4. 整理产物
            Directory.CreateDirectory(AppConfig.OutDir);
            File.Move(newBootFile, PatchedBootPath, overwrite: true);
            try
            {
                if (File.Exists(kernelFile)) File.Delete(kernelFile);
                if (File.Exists(kernelBackup)) File.Delete(kernelBackup);
            }
            catch { /* 清理失败不影响结果 */ }

            log($"[OK] 修补完成，文件已保存到：{PatchedBootPath}");
            return true;
        }

        /// <summary>
        /// 步骤三：通过 Fastboot 刷入修补后的 boot。
        /// </summary>
        public static async Task<bool> FlashPatchedBootAsync(Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(PatchedBootPath))
            {
                log("[错误] 未找到 apatch_patched_boot.img，请先完成修补。");
                return false;
            }
            log("[OK] 找到修补后的镜像。");

            if (!await DeviceService.WaitForFastbootAsync(log, ct)) return false;

            if (!await DeviceService.FlashPartitionAsync("boot", PatchedBootPath, log, ct))
            {
                return false;
            }
            await DeviceService.RebootAsync(log);
            log("APatch Root 完成！首次开机后请打开 APatch 应用完成后续激活。");
            return true;
        }
    }
}
