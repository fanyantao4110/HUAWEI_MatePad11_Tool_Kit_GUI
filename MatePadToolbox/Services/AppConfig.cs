using System.IO;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 全局路径与目录配置。
    /// 目录结构与原 bat 版保持一致：所有资源目录位于程序 exe 所在目录下。
    /// </summary>
    public static class AppConfig
    {
        /// <summary>程序根目录（exe 所在目录）。PublishSingleFile 模式下用 ProcessPath 替代 BaseDirectory。</summary>
        public static string BaseDir { get; } = Path.GetDirectoryName(Environment.ProcessPath)!.TrimEnd('\\');

        // ==================== 目录 ====================
        public static string ToolsDir => Path.Combine(BaseDir, "tools");
        public static string DriverDir => Path.Combine(ToolsDir, "driver");
        public static string RawprogramDir => Path.Combine(ToolsDir, "rawprogram");
        public static string ApatchToolsDir => Path.Combine(ToolsDir, "apatch_tools");
        public static string HiSuiteProxyDir => Path.Combine(ToolsDir, "HiSuiteProxy");
        public static string ApksDir => Path.Combine(ToolsDir, "apks");
        public static string UnlockDir => Path.Combine(BaseDir, "unlock");
        public static string SystemDir => Path.Combine(BaseDir, "system");
        public static string CacheDir => Path.Combine(BaseDir, "cache");
        public static string TmpDir => Path.Combine(BaseDir, "tmp");
        public static string OutDir => Path.Combine(BaseDir, "out");
        public static string BaseRomDir => Path.Combine(BaseDir, "base");
        public static string RamdiskDir => Path.Combine(BaseDir, "ramdisk");
        public static string VbmetaDir => Path.Combine(BaseDir, "vbmeta");
        public static string LogDir => Path.Combine(BaseDir, "log");
        public static string ScreenshotsDir => Path.Combine(BaseDir, "screenshots");

        // ==================== 可执行文件 ====================
        public static string AdbExe => Path.Combine(ToolsDir, "adb.exe");
        public static string FastbootExe => Path.Combine(ToolsDir, "fastboot.exe");
        public static string Zip7Exe => Path.Combine(ToolsDir, "7z.exe");
        public static string QSaharaServerExe => Path.Combine(ToolsDir, "QSaharaServer.exe");
        public static string FhLoaderExe => Path.Combine(ToolsDir, "fh_loader.exe");
        public static string AdbDriverInstaller => Path.Combine(DriverDir, "adbdriver.exe");
        public static string QcDriverInstaller => Path.Combine(DriverDir, "qcdriver.exe");
        public static string KptoolsExe => Path.Combine(ApatchToolsDir, "kptools.exe");
        public static string KpimgFile => Path.Combine(ApatchToolsDir, "kpimg-android");
        public static string HiSuite1Exe => Path.Combine(HiSuiteProxyDir, "HiSuite1.exe");
        public static string HiSuiteProxyExe => Path.Combine(HiSuiteProxyDir, "HiSuite Proxy V3.exe");
        public static string MagiskApk => Path.Combine(ApksDir, "Magisk.apk");
        public static string ApatchApk => Path.Combine(ApksDir, "Apatch.apk");

        // ==================== 9008 回读全分区 ====================
        /// <summary>ptool.exe（可选）：存在时回读完成后自动生成分区表和 xml 文件到 gpt_and_xml\create。</summary>
        public static string PtoolExe => Path.Combine(ToolsDir, "ptool.exe");
        /// <summary>9008 备份默认保存目录。</summary>
        public static string BackupDir => Path.Combine(BaseDir, "backup");

        // ==================== 固件/镜像文件 ====================
        public static string DevprgElf => Path.Combine(UnlockDir, "Huawei865870_devprg.elf");
        public static string AblUnlockImg => Path.Combine(UnlockDir, "Huawei865870_abl_unlock.img");
        public static string VbmetaZip => Path.Combine(VbmetaDir, "vbmeta.zip");
        public static string MiscImg => Path.Combine(RawprogramDir, "misc.img");

        // ==================== 其他常量 ====================
        public const string OfficialRomShareUrl = "https://1821068313.share.123pan.cn/123pan/N3vxjv-rUlHh";
        public const string BasePackageUrl = "https://1821068313.share.123pan.cn/123pan/N3vxjv-EKnhh";
        public const string DowngradeSubmitUrl = "https://ffusersubmission.byethost17.com/";

        /// <summary>创建所有必备目录（对应 bat 开头的 md 语句）。</summary>
        public static void EnsureDirectories()
        {
            foreach (var dir in new[]
            {
                ScreenshotsDir, SystemDir, CacheDir, TmpDir, OutDir,
                BaseRomDir, RamdiskDir, VbmetaDir, RawprogramDir, LogDir
            })
            {
                try { Directory.CreateDirectory(dir); } catch { /* 忽略 */ }
            }
        }

        /// <summary>
        /// 检查核心工具是否齐全（对应 bat 开头的 exist 检查）。
        /// 返回缺失文件列表，空列表表示全部齐全。
        /// </summary>
        public static List<string> CheckCoreTools()
        {
            var missing = new List<string>();
            foreach (var f in new[] { Zip7Exe, FastbootExe, AdbExe })
            {
                if (!File.Exists(f)) missing.Add(f);
            }
            return missing;
        }

        /// <summary>清空并重建 cache 目录（对应 bat 中 rd /s /q cache + md cache）。</summary>
        public static void ResetCacheDir()
        {
            try
            {
                if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true);
                Directory.CreateDirectory(CacheDir);
            }
            catch { /* 忽略占用异常 */ }
        }

        /// <summary>向 tmp 目录写入 XML 文件，返回完整路径。</summary>
        public static string WriteTmpFile(string fileName, string content)
        {
            Directory.CreateDirectory(TmpDir);
            var path = Path.Combine(TmpDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }
    }
}
