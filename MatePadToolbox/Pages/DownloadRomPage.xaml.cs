using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Models;
using MatePadToolbox.Services;
using Windows.ApplicationModel.DataTransfer;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 下载第三方系统页。
    /// 对应 bat 的 :download_rom_menu / :download_rom_list / :download_rom_only / :update_json_files。
    /// </summary>
    public sealed partial class DownloadRomPage : Page
    {
        private List<RomEntry> _roms = new();

        public DownloadRomPage()
        {
            this.InitializeComponent();
        }

        private string SelectedVersion => RomConfigService.Versions[VersionCombo.SelectedIndex];

        private async void LoadRomList_Click(object sender, RoutedEventArgs e)
        {
            BtnLoad.IsEnabled = false;
            try
            {
                Log.Append($"正在加载 鸿蒙{SelectedVersion} 的系统列表...");
                _roms = await RomConfigService.LoadRomListAsync(SelectedVersion, Log.Append);
                RomList.ItemsSource = _roms;

                if (_roms.Count == 0)
                {
                    Log.Append("[错误] 没有找到任何系统，请检查 JSON 文件内容，或点击【在线更新全部配置文件】。");
                }
                else
                {
                    RomList.SelectedIndex = 0;
                    Log.Append($"[OK] 共找到 {_roms.Count} 个系统。");
                }
            }
            finally
            {
                BtnLoad.IsEnabled = true;
            }
        }

        private async void UpdateJson_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateJson.IsEnabled = false;
            try
            {
                await RomConfigService.UpdateAllJsonAsync(Log.Append);
            }
            finally
            {
                BtnUpdateJson.IsEnabled = true;
            }
        }

        private void DownloadRom_Click(object sender, RoutedEventArgs e)
        {
            if (RomList.SelectedItem is not RomEntry rom)
            {
                Log.Append("[提示] 请先选择要下载的系统。");
                return;
            }
            if (string.IsNullOrWhiteSpace(rom.Url))
            {
                Log.Append("[错误] 该系统没有可用的下载链接。");
                return;
            }

            Log.Append($"已选择系统：{rom.Name}");
            Log.Append($"下载链接已复制到剪贴板，将为您自动打开浏览器。如果未自动打开，请自行打开浏览器并粘贴网址。");
            CopyToClipboard(rom.Url);
            ProcessRunner.OpenUrl(rom.Url);
        }

        private void OpenSystemDir_Click(object sender, RoutedEventArgs e)
        {
            ProcessRunner.ExploreFolder(AppConfig.SystemDir);
        }

        private void OpenBaseDir_Click(object sender, RoutedEventArgs e)
        {
            ProcessRunner.ExploreFolder(AppConfig.BaseRomDir);
        }

        private void BaseUrl_Click(object sender, RoutedEventArgs e)
        {
            Log.Append("底包下载链接已复制到剪贴板，将为您自动打开。如果未自动打开，请自行打开浏览器并粘贴网址。");
            CopyToClipboard(AppConfig.BasePackageUrl);
            ProcessRunner.OpenUrl(AppConfig.BasePackageUrl);
        }

        private static void CopyToClipboard(string url)
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(url);
                dp.RequestedOperation = DataPackageOperation.Copy;
                Clipboard.SetContent(dp);
            }
            catch
            {
                // 剪切板不可用时静默忽略
            }
        }
    }
}
