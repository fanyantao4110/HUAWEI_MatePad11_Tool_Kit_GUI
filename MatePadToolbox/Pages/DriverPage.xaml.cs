using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 安装驱动程序页：ADB 驱动 / 高通 9008 驱动。
    /// 对应 bat 的 :install_driver / :install_adb_driver / :install_qc_driver。
    /// </summary>
    public sealed partial class DriverPage : Page
    {
        public DriverPage()
        {
            this.InitializeComponent();
        }

        private async void InstallAdbDriver_Click(object sender, RoutedEventArgs e)
        {
            await InstallDriverAsync(AppConfig.AdbDriverInstaller, "tools\\driver\\adbdriver.exe", "ADB");
        }

        private async void InstallQcDriver_Click(object sender, RoutedEventArgs e)
        {
            await InstallDriverAsync(AppConfig.QcDriverInstaller, "tools\\driver\\qcdriver.exe", "Qualcomm 9008");
        }

        /// <summary>
        /// 启动驱动安装器并等待完成（对应 bat 的 start /wait）。
        /// </summary>
        private async Task InstallDriverAsync(string installerPath, string relativeName, string driverName)
        {
            if (!File.Exists(installerPath))
            {
                Log.Append($"[错误] 未找到 {relativeName}");
                return;
            }

            SetBusy(true);
            Log.Append($"正在启动 {driverName} 驱动安装程序...");
            try
            {
                var proc = ProcessRunner.StartShell(installerPath);
                if (proc != null)
                {
                    await Task.Run(() =>
                    {
                        try { proc.WaitForExit(); } catch { /* 忽略 */ }
                    });
                }
                Log.Append("安装流程结束。如驱动仍未生效，请重启电脑后重试。");
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 启动安装程序失败：{ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            BtnAdbDriver.IsEnabled = !busy;
            BtnQcDriver.IsEnabled = !busy;
        }
    }
}
