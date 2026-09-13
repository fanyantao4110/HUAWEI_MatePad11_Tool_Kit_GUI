using System.Diagnostics;
using System.IO;
using System.Text;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 外部进程封装：负责运行 adb / fastboot / fh_loader / QSaharaServer / 7z 等，
    /// 并以流式方式回传输出行。
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>GBK 编码（代码页 936），用于解析中文 Windows 下工具的输出。</summary>
        public static readonly Encoding Gbk;

        static ProcessRunner()
        {
            // 双保险：确保 GBK 代码页已注册（App 启动时也会注册一次）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936);
        }

        /// <summary>
        /// 运行外部程序并等待结束，逐行回传 stdout/stderr。
        /// </summary>
        /// <param name="fileName">可执行文件路径</param>
        /// <param name="arguments">命令行参数</param>
        /// <param name="onLine">每行输出的回调（线程安全，调用方自行调度 UI）</param>
        /// <param name="workingDirectory">工作目录，null 表示使用默认</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>进程退出码</returns>
        public static Task<int> RunAsync(
            string fileName,
            string arguments,
            Action<string>? onLine = null,
            string? workingDirectory = null,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Gbk,
                StandardErrorEncoding = Gbk,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            Process proc;
            try
            {
                proc = new Process { StartInfo = psi };
                proc.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                onLine?.Invoke($"[错误] 无法创建进程：{ex.Message}");
                tcs.SetResult(-1);
                return tcs.Task;
            }

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) onLine?.Invoke(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onLine?.Invoke(e.Data);
            };
            proc.Exited += (_, _) =>
            {
                try
                {
                    // 等待异步输出管道排空
                    proc.WaitForExit();
                    tcs.TrySetResult(proc.ExitCode);
                }
                catch
                {
                    tcs.TrySetResult(-1);
                }
                finally
                {
                    proc.Dispose();
                }
            };

            try
            {
                if (!proc.Start())
                {
                    onLine?.Invoke("[错误] 进程启动失败。");
                    tcs.TrySetResult(-1);
                    proc.Dispose();
                    return tcs.Task;
                }
            }
            catch (Exception ex)
            {
                onLine?.Invoke($"[错误] 启动失败：{ex.Message}");
                tcs.TrySetResult(-1);
                proc.Dispose();
                return tcs.Task;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 支持取消：强制结束进程
            ct.Register(() =>
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                        onLine?.Invoke("[提示] 操作已被取消。");
                    }
                }
                catch { /* 忽略 */ }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 运行外部程序，返回退出码与完整输出文本（适合短命令，如 fastboot devices）。
        /// </summary>
        public static async Task<(int ExitCode, string Output)> RunCaptureAsync(
            string fileName, string arguments, string? workingDirectory = null, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            var code = await RunAsync(fileName, arguments, line =>
            {
                lock (sb) sb.AppendLine(line);
            }, workingDirectory, ct);
            lock (sb) return (code, sb.ToString());
        }

        /// <summary>
        /// 以 Shell 方式打开文件/程序（等价于 bat 的 start），如驱动安装器、explorer 目录等。
        /// </summary>
        public static Process? StartShell(string target, string? workingDirectory = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }
            return Process.Start(psi);
        }

        /// <summary>
        /// 以管理员权限运行（等价于需要提权的命令），返回进程；调用方可 WaitForExit。
        /// </summary>
        public static Process? StartElevated(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            return Process.Start(psi);
        }

        /// <summary>打开网址（默认浏览器）。</summary>
        public static void OpenUrl(string url)
        {
            StartShell(url);
        }

        /// <summary>用资源管理器打开目录。</summary>
        public static void ExploreFolder(string dir)
        {
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
    }
}
