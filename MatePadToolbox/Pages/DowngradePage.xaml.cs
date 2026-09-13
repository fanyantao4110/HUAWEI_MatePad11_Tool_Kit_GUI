using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.Diagnostics;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 降级系统 / 回锁BL 页。
    /// 对应 bat 的 :downgrade_menu 及其 6 个子功能。
    /// </summary>
    public sealed partial class DowngradePage : Page
    {
        public DowngradePage()
        {
            this.InitializeComponent();
        }

        // ==================== 各步骤 ====================

        private async void Step1_Click(object sender, RoutedEventArgs e) => await InstallHiSuite1Async();

        private void Step2_Click(object sender, RoutedEventArgs e) => OpenProxyAndScreenshots();

        private void Step3_Click(object sender, RoutedEventArgs e) => OpenDowngradeSiteAndScreenshots();

        private async void Step4_Click(object sender, RoutedEventArgs e) => await SetSystemDateAsync();

        private void Step5_Click(object sender, RoutedEventArgs e) => OpenHiSuiteAndScreenshots();

        private async void OneClick_Click(object sender, RoutedEventArgs e)
        {
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "一键执行",
                "将按顺序执行：\n[1] 安装旧版华为手机助手\n[2] 打开 HiSuite Proxy 及图片\n[3] 打开降级地址及图片\n[4] 修改系统时间为 2023-01-01\n[5] 打开华为手机助手及图片\n\n请确认已备份数据并了解风险。是否继续？"))
                return;

            SetBusy(true);
            try
            {
                Log.Append("==== 步骤 1/5：安装旧版华为手机助手 ====");
                await InstallHiSuite1Async();
                if (!await AskContinueAsync("步骤 1 完成，请安装完成后点击继续。")) return;

                Log.Append("==== 步骤 2/5：打开 HiSuite Proxy 及图片 ====");
                OpenProxyAndScreenshots();
                if (!await AskContinueAsync("请按图片教程操作，完成后点击继续。")) return;

                Log.Append("==== 步骤 3/5：打开降级地址及图片 ====");
                OpenDowngradeSiteAndScreenshots();
                if (!await AskContinueAsync("请按图片教程操作，完成后点击继续。")) return;

                Log.Append("==== 步骤 4/5：修改系统时间 ====");
                await SetSystemDateAsync();
                if (!await AskContinueAsync("步骤 4 完成，点击继续。")) return;

                Log.Append("==== 步骤 5/5：打开华为手机助手及图片 ====");
                OpenHiSuiteAndScreenshots();
                Log.Append("===============================================");
                Log.Append("  一键执行完成！请按图片教程在华为手机助手中操作降级。");
                Log.Append("===============================================");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ==================== 步骤实现 ====================

        private async Task InstallHiSuite1Async()
        {
            var exe = AppConfig.HiSuite1Exe;
            if (!File.Exists(exe))
            {
                Log.Append($"[错误] 未找到 {exe}");
                return;
            }
            Log.Append("正在启动旧版华为手机助手安装程序...");
            var proc = ProcessRunner.StartShell(exe);
            if (proc != null)
            {
                await Task.Run(() => { try { proc.WaitForExit(); } catch { } });
            }
            Log.Append("安装流程结束。");
        }

        private void OpenProxyAndScreenshots()
        {
            var exe = AppConfig.HiSuiteProxyExe;
            if (File.Exists(exe))
            {
                ProcessRunner.StartShell(exe);
                Log.Append("[OK] 已启动 HiSuite Proxy V3。");
            }
            else
            {
                Log.Append($"[错误] 未找到 {exe}");
            }
            OpenScreenshots("proxy");
        }

        private void OpenDowngradeSiteAndScreenshots()
        {
            Log.Append($"正在打开降级申请网址：{AppConfig.DowngradeSubmitUrl}");
            ProcessRunner.OpenUrl(AppConfig.DowngradeSubmitUrl);
            OpenScreenshots("降级");
        }

        private async Task SetSystemDateAsync()
        {
            Log.Append("正在修改系统时间为 2023-01-01（需要管理员权限）...");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"Set-Date '2023-01-01 00:00:00'\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                var proc = Process.Start(psi);
                if (proc != null) await Task.Run(() => { try { proc.WaitForExit(); } catch { } });
                Log.Append("[OK] 系统时间已修改为 2023-01-01。");
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 修改系统时间失败：{ex.Message}");
            }
        }

        private void OpenHiSuiteAndScreenshots()
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HiSuite", "HiSuite.exe");
            if (!File.Exists(exe))
            {
                exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HiSuite", "HiSuite.exe");
            }
            if (File.Exists(exe))
            {
                ProcessRunner.StartShell(exe);
                Log.Append("[OK] 已启动华为手机助手。");
            }
            else
            {
                Log.Append("[提示] 未找到华为手机助手，请确认已安装。");
            }
            OpenScreenshots("助手");
        }

        private void OpenScreenshots(string subDir)
        {
            var dir = Path.Combine(AppConfig.ScreenshotsDir, subDir);
            if (Directory.Exists(dir))
            {
                ProcessRunner.ExploreFolder(dir);
                Log.Append($"[OK] 已打开截图目录：screenshots\\{subDir}");
            }
            else
            {
                Log.Append($"[提示] 截图目录不存在：screenshots\\{subDir}");
            }
        }

        private async Task<bool> AskContinueAsync(string message)
        {
            return await UiHelpers.ConfirmAsync(this.XamlRoot, "继续", message, "继续", "取消");
        }

        private void SetBusy(bool busy)
        {
            BtnStep1.IsEnabled = !busy;
            BtnStep2.IsEnabled = !busy;
            BtnStep3.IsEnabled = !busy;
            BtnStep4.IsEnabled = !busy;
            BtnStep5.IsEnabled = !busy;
            BtnOneClick.IsEnabled = !busy;
        }
    }
}
