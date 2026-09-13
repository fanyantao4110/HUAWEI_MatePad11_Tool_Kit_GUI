using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MatePadToolbox.Services;
using System.Diagnostics;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// ADB/Fastboot 命令行页：内置持久化 cmd 会话（工作目录为 tools）。
    /// 对应 bat 的 :adb_cmd（原实现是 start cmd /k cd tools）。
    /// </summary>
    public sealed partial class AdbConsolePage : Page
    {
        private Process? _cmdProcess;

        public AdbConsolePage()
        {
            this.InitializeComponent();
            this.Unloaded += (_, _) => StopConsole();
        }

        private void StartConsole_Click(object sender, RoutedEventArgs e) => StartConsole();

        private void ClearLog_Click(object sender, RoutedEventArgs e) => Log.Clear();

        private void StartConsole()
        {
            if (_cmdProcess is { HasExited: false })
            {
                Log.Append("[提示] 终端已在运行。");
                return;
            }

            Directory.CreateDirectory(AppConfig.ToolsDir);
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/q",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = ProcessRunner.Gbk,
                StandardErrorEncoding = ProcessRunner.Gbk,
                StandardInputEncoding = ProcessRunner.Gbk,
                WorkingDirectory = AppConfig.ToolsDir,
            };

            try
            {
                _cmdProcess = new Process { StartInfo = psi };
                _cmdProcess.OutputDataReceived += (_, ev) =>
                {
                    if (ev.Data != null) Log.Append(ev.Data);
                };
                _cmdProcess.ErrorDataReceived += (_, ev) =>
                {
                    if (ev.Data != null) Log.Append(ev.Data);
                };
                _cmdProcess.EnableRaisingEvents = true;
                _cmdProcess.Exited += (_, _) =>
                {
                    Log.Append("[提示] 命令行会话已结束。");
                    try { DispatcherQueue.TryEnqueue(() => SetRunning(false)); } catch { /* 窗口已关闭 */ }
                };
                _cmdProcess.Start();
                _cmdProcess.BeginOutputReadLine();
                _cmdProcess.BeginErrorReadLine();

                SetRunning(true);
                Log.Append("==========================================================");
                Log.Append(" 终端已启动，工作目录：" + AppConfig.ToolsDir);
                Log.Append(" 提示：输入 cls 无法清屏，可用\"清空\"按钮；输入 exit 退出。");
                Log.Append("==========================================================");
                CmdInput.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 启动终端失败：{ex.Message}");
            }
        }

        private void StopConsole_Click(object sender, RoutedEventArgs e)
        {
            StopConsole();
            Log.Append("[提示] 终端已关闭。");
        }

        private void StopConsole()
        {
            try
            {
                if (_cmdProcess is { HasExited: false })
                {
                    _cmdProcess.Kill(entireProcessTree: true);
                }
            }
            catch { /* 忽略 */ }
            finally
            {
                _cmdProcess?.Dispose();
                _cmdProcess = null;
            }
            DispatcherQueue.TryEnqueue(() => SetRunning(false));
        }

        private void SendCmd_Click(object sender, RoutedEventArgs e) => SendCommand();

        private void CmdInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendCommand();
                e.Handled = true;
            }
        }

        private void SendCommand()
        {
            var cmd = CmdInput.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            if (_cmdProcess is not { HasExited: false })
            {
                Log.Append("[提示] 终端未启动，请先点击【启动终端】。");
                return;
            }

            try
            {
                Log.Append($"> {cmd}");
                _cmdProcess.StandardInput.WriteLine(cmd);
                _cmdProcess.StandardInput.Flush();
                CmdInput.Text = string.Empty;
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 写入命令失败：{ex.Message}");
            }
        }

        private void SetRunning(bool running)
        {
            BtnStart.IsEnabled = !running;
            BtnStop.IsEnabled = running;
            CmdInput.IsEnabled = running;
            BtnSend.IsEnabled = running;
            ConsoleStatus.Text = running ? "运行中" : "未启动";
        }
    }
}
