using System.Management;
using System.Text.RegularExpressions;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 检测高通 9008（EDL）设备的 COM 端口。
    /// 对应 bat 中的 :get_com_port 子过程（WMI 查询 Win32_PnPEntity）。
    /// </summary>
    public static partial class ComPortDetector
    {
        [GeneratedRegex(@"COM(\d+)")]
        private static partial Regex ComRegex();

        /// <summary>
        /// 查找 9008/QDLoader 设备的 COM 端口号；未找到返回 null。
        /// </summary>
        public static int? Find9008Port()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%9008%' OR Name LIKE '%QDLoader%'");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? string.Empty;
                    var m = ComRegex().Match(name);
                    if (m.Success)
                    {
                        return int.Parse(m.Groups[1].Value);
                    }
                }
            }
            catch
            {
                // WMI 查询失败（极少见），返回未找到
            }
            return null;
        }

        /// <summary>
        /// 轮询等待 9008 设备出现。
        /// </summary>
        /// <param name="retries">最多重试次数（bat 中为 15 次，每次间隔 2 秒）</param>
        /// <param name="intervalMs">轮询间隔毫秒</param>
        /// <param name="onProgress">进度回调（第几次/总次数）</param>
        /// <returns>找到的 COM 端口号；超时返回 null</returns>
        public static async Task<int?> WaitFor9008Async(
            int retries = 15,
            int intervalMs = 2000,
            Action<string>? onProgress = null,
            CancellationToken ct = default)
        {
            for (int i = 1; i <= retries; i++)
            {
                ct.ThrowIfCancellationRequested();
                var port = Find9008Port();
                if (port.HasValue) return port.Value;

                onProgress?.Invoke($"等待中... ({i}/{retries})");
                await Task.Delay(intervalMs, ct);
            }
            return null;
        }
    }
}
