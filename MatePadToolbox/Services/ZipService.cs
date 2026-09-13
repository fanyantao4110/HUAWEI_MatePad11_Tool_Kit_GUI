using System.IO;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 压缩包服务：调用 7z.exe 解压（对应 bat 中的 7z x 命令）。
    /// </summary>
    public static class ZipService
    {
        /// <summary>
        /// 解压压缩包到指定目录。
        /// </summary>
        public static async Task<bool> ExtractAsync(string zipFile, string outDir, Action<string> log, CancellationToken ct = default)
        {
            if (!File.Exists(AppConfig.Zip7Exe))
            {
                log("[错误] 缺少 tools\\7z.exe");
                return false;
            }
            if (!File.Exists(zipFile))
            {
                log($"[错误] 压缩包不存在：{zipFile}");
                return false;
            }

            Directory.CreateDirectory(outDir);
            log($"正在解压到 {outDir} ...");
            var args = $"x \"{zipFile}\" -o\"{outDir}\" -y";
            var lastLine = string.Empty;
            var code = await ProcessRunner.RunAsync(AppConfig.Zip7Exe, args, line =>
            {
                // 7z 输出较长，只回显进度/结果关键行
                if (line.Contains('%') || line.Contains("Everything is Ok") || line.Contains("Error"))
                {
                    log($"[7z] {line}");
                }
                lastLine = line;
            }, AppConfig.ToolsDir, ct);

            if (code != 0)
            {
                log($"[错误] 解压失败！（退出码 {code}）{lastLine}");
                return false;
            }
            log("解压完成。");
            return true;
        }
    }
}
