using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Models;
using MatePadToolbox.Services;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 刷入官方鸿蒙系统（救砖）页。
    /// 对应 bat 的 :flash_official_zip。
    /// </summary>
    public sealed partial class FlashOfficialPage : Page
    {
        private CancellationTokenSource? _cts;
        private List<string> _zipFiles = new();

        public FlashOfficialPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) => RefreshZipList();
        }

        private void RefreshZipList_Click(object sender, RoutedEventArgs e) => RefreshZipList();

        private void RefreshZipList()
        {
            _zipFiles = Directory.Exists(AppConfig.SystemDir)
                ? Directory.EnumerateFiles(AppConfig.SystemDir, "*.zip").OrderBy(f => f).ToList()
                : new List<string>();

            ZipCombo.ItemsSource = _zipFiles.Select(Path.GetFileName).ToList();
            if (_zipFiles.Count > 0)
            {
                ZipCombo.SelectedIndex = 0;
            }
            else
            {
                Log.Append($"[提示] 未在 system 目录找到任何 ZIP 文件（{AppConfig.SystemDir}）。");
            }
        }

        private void OpenOfficialUrl_Click(object sender, RoutedEventArgs e)
        {
            Log.Append("官方系统包下载链接已复制到剪贴板，将为您自动打开浏览器。如果未自动打开，请自行打开浏览器并粘贴网址。");
            try
            {
                var dp = new DataPackage();
                dp.SetText(AppConfig.OfficialRomShareUrl);
                dp.RequestedOperation = DataPackageOperation.Copy;
                Clipboard.SetContent(dp);
            }
            catch { /* 剪切板不可用时静默忽略 */ }
            ProcessRunner.OpenUrl(AppConfig.OfficialRomShareUrl);
        }

        private void OpenSystemDir_Click(object sender, RoutedEventArgs e)
        {
            ProcessRunner.ExploreFolder(AppConfig.SystemDir);
        }

        private async void FlashOfficial_Click(object sender, RoutedEventArgs e)
        {
            if (ZipCombo.SelectedIndex < 0 || ZipCombo.SelectedIndex >= _zipFiles.Count)
            {
                Log.Append("[错误] 请先选择要刷入的 ZIP。");
                return;
            }
            var zip = _zipFiles[ZipCombo.SelectedIndex];

            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "救砖刷写",
                $"将解压并刷入：{Path.GetFileName(zip)}\n此操作会覆盖全部分区。\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append($"已选择：{Path.GetFileName(zip)}");

                AppConfig.ResetCacheDir();
                if (!await ZipService.ExtractAsync(zip, AppConfig.CacheDir, Log.Append, _cts.Token)) return;

                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;

                Log.Append("设备已连接，开始递归刷入所有镜像...");
                FlashSummary summary = await DeviceService.FlashAllImagesAsync(
                    AppConfig.CacheDir, recursive: true, Log.Append, _cts.Token);

                AppConfig.ResetCacheDir();

                if (summary.AllSuccess)
                {
                    Log.Append("===============================================");
                    Log.Append("  官方系统所有分区刷入成功！");
                    Log.Append("===============================================");
                }
                else
                {
                    Log.Append("===============================================");
                    Log.Append($"  刷写完成，但有 {summary.FailedParts.Count} 个分区失败：");
                    Log.Append($"  {string.Join(" ", summary.FailedParts)}");
                    Log.Append("===============================================");
                }

                await DeviceService.RebootAsync(Log.Append);
                Log.Append("操作完成，设备正在重启！");
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

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void SetBusy(bool busy)
        {
            BtnFlash.IsEnabled = !busy;
            BtnCancel.IsEnabled = busy;
            ZipCombo.IsEnabled = !busy;
        }
    }
}
