using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Dialogs;

namespace MatePadToolbox.Controls
{
    /// <summary>
    /// 通用疑难解答按钮，根据功能键加载对应的问答 JSON 配置。
    /// </summary>
    public sealed partial class TroubleshootButton : UserControl
    {
        public static readonly DependencyProperty FeatureKeyProperty =
            DependencyProperty.Register(nameof(FeatureKey), typeof(string), typeof(TroubleshootButton), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FeatureNameProperty =
            DependencyProperty.Register(nameof(FeatureName), typeof(string), typeof(TroubleshootButton), new PropertyMetadata(string.Empty));

        /// <summary>功能键，对应 faq 目录下的 {FeatureKey}.json。</summary>
        public string FeatureKey
        {
            get => (string)GetValue(FeatureKeyProperty);
            set => SetValue(FeatureKeyProperty, value);
        }

        /// <summary>功能名称，用于弹窗标题。</summary>
        public string FeatureName
        {
            get => (string)GetValue(FeatureNameProperty);
            set => SetValue(FeatureNameProperty, value);
        }

        public TroubleshootButton()
        {
            this.InitializeComponent();
        }

        private async void Troubleshoot_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = (sender as FrameworkElement)?.XamlRoot;
            if (xamlRoot == null) return;

            var dialog = new TroubleshootDialog(FeatureKey, FeatureName)
            {
                XamlRoot = xamlRoot
            };
            _ = dialog.LoadEntriesAsync(_ => { });
            await dialog.ShowAsync();
        }
    }
}
