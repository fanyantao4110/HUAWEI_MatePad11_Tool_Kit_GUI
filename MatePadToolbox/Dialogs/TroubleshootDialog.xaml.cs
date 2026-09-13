using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Models;
using MatePadToolbox.Services;
using System.Collections.Generic;

namespace MatePadToolbox.Dialogs
{
    /// <summary>
    /// 疑难解答弹窗：展示当前功能的问答列表，并支持下载/更新配置。
    /// </summary>
    public sealed partial class TroubleshootDialog : ContentDialog
    {
        private readonly string _featureKey;

        public TroubleshootDialog(string featureKey, string featureName)
        {
            this.InitializeComponent();
            _featureKey = featureKey;
            this.Title = $"疑难解答 - {featureName}";
            this.PrimaryButtonClick += TroubleshootDialog_PrimaryButtonClick;
        }

        /// <summary>
        /// 异步加载问答列表并显示。
        /// </summary>
        public async Task LoadEntriesAsync(Action<string> log)
        {
            var entries = await TroubleshootService.LoadAsync(_featureKey, log);
            TroubleshootList.ItemsSource = entries;

            if (entries.Count == 0)
            {
                ShowInfo("未加载到任何问答，可点击下载配置获取最新内容。", InfoBarSeverity.Warning);
            }
            else
            {
                ShowInfo($"已加载 {entries.Count} 条问答。", InfoBarSeverity.Success);
            }
        }

        private async void TroubleshootDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var log = new Action<string>(msg => ShowInfo(msg, InfoBarSeverity.Informational));
                var ok = await TroubleshootService.DownloadAsync(_featureKey, log);
                if (ok)
                {
                    var entries = await TroubleshootService.LoadAsync(_featureKey, log);
                    TroubleshootList.ItemsSource = entries;
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            DownloadStatusBar.Message = message;
            DownloadStatusBar.Severity = severity;
            DownloadStatusBar.IsOpen = true;
        }
    }
}
