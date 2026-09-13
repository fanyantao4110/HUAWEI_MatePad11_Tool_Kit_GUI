using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// Root 页：Magisk 方式（刷 ramdisk）与 APatch 方式（提取/修补/刷入 boot）。
    /// 对应 bat 的 :root_menu / :magisk_root / :apatch_*。
    /// </summary>
    public sealed partial class RootPage : Page
    {
        private CancellationTokenSource? _cts;
        private List<string> _ramdiskFiles = new();

        public RootPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) => RefreshRamdiskList();
        }

        // ==================== Magisk ====================

        private void RefreshRamdisk_Click(object sender, RoutedEventArgs e) => RefreshRamdiskList();

        private void RefreshRamdiskList()
        {
            _ramdiskFiles = Directory.Exists(AppConfig.RamdiskDir)
                ? Directory.EnumerateFiles(AppConfig.RamdiskDir, "*.img").OrderBy(f => f).ToList()
                : new List<string>();

            RamdiskCombo.ItemsSource = _ramdiskFiles.Select(Path.GetFileName).ToList();
            if (_ramdiskFiles.Count == 0)
            {
                Log.Append("[提示] 未在 ramdisk 目录找到任何镜像文件。");
            }
            else if (RamdiskCombo.SelectedIndex < 0)
            {
                RamdiskCombo.SelectedIndex = 0;
            }
        }

        private async void MagiskFlash_Click(object sender, RoutedEventArgs e)
        {
            if (RamdiskCombo.SelectedIndex < 0 || RamdiskCombo.SelectedIndex >= _ramdiskFiles.Count)
            {
                Log.Append("[错误] 请先选择要刷入的 ramdisk 镜像。");
                return;
            }
            var img = _ramdiskFiles[RamdiskCombo.SelectedIndex];

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append($"已选择：{Path.GetFileName(img)}");
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;

                Log.Append("开始刷写 ramdisk 分区...");
                var ok = await DeviceService.FlashPartitionAsync("ramdisk", img, Log.Append, _cts.Token);
                if (!ok)
                {
                    Log.Append("[错误] 刷入失败！");
                    return;
                }
                Log.Append("[OK] 刷入成功，正在重启设备...");
                await DeviceService.RebootAsync(Log.Append);
                Log.Append("Magisk 刷入完成！");
            }
            catch (OperationCanceledException)
            {
                Log.Append("[已取消]");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void MagiskCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void InstallMagisk_Click(object sender, RoutedEventArgs e)
        {
            await InstallApkAsync(AppConfig.MagiskApk, "Magisk");
        }

        // ==================== APatch ====================

        private async void ApatchOneClick_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                var rebootViaAdb = ApachOsGroup.SelectedIndex == 0;
                Log.Append("===== 步骤 1/3：提取 boot 分区 =====");
                if (!await ApachService.ExtractBootAsync(rebootViaAdb, Log.Append, _cts.Token)) return;

                Log.Append("===== 步骤 2/3：APatch 修补 boot =====");
                if (!await ApachService.PatchBootAsync(Log.Append, _cts.Token)) return;

                Log.Append("===== 步骤 3/3：刷入修补后的 boot =====");
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;
                if (!await ApachService.FlashPatchedBootAsync(Log.Append, _cts.Token)) return;
            }
            catch (OperationCanceledException)
            {
                Log.Append("[已取消]");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ApatchExtract_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                var rebootViaAdb = ApachOsGroup.SelectedIndex == 0;
                await ApachService.ExtractBootAsync(rebootViaAdb, Log.Append, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void ApatchPatch_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await ApachService.PatchBootAsync(Log.Append, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void ApatchFlash_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;
                await ApachService.FlashPatchedBootAsync(Log.Append, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private void ApatchCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void InstallApatch_Click(object sender, RoutedEventArgs e)
        {
            await InstallApkAsync(AppConfig.ApatchApk, "APatch");
        }

        private async Task InstallApkAsync(string apkPath, string name)
        {
            if (!File.Exists(apkPath))
            {
                Log.Append($"[错误] 未找到 {name} 安装包：{apkPath}");
                return;
            }

            Log.Append($"正在通过 ADB 安装 {name} 管理器...");
            SetBusy(true);
            try
            {
                var exitCode = await ProcessRunner.RunAsync(
                    AppConfig.AdbExe,
                    $"install -r \"{apkPath}\"",
                    Log.Append,
                    ct: CancellationToken.None);
                if (exitCode == 0)
                    Log.Append($"[OK] {name} 管理器安装成功！");
                else
                    Log.Append($"[错误] {name} 管理器安装失败，退出码：{exitCode}");
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 安装 {name} 失败：{ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            BtnMagiskFlash.IsEnabled = !busy;
            BtnMagiskCancel.IsEnabled = busy;
            BtnInstallMagisk.IsEnabled = !busy;
            BtnApatchOneClick.IsEnabled = !busy;
            BtnApatchExtract.IsEnabled = !busy;
            BtnApatchPatch.IsEnabled = !busy;
            BtnApatchFlash.IsEnabled = !busy;
            BtnApatchCancel.IsEnabled = busy;
            BtnInstallApatch.IsEnabled = !busy;
            ApachOsGroup.IsEnabled = !busy;
            RamdiskCombo.IsEnabled = !busy;
        }
    }
}
