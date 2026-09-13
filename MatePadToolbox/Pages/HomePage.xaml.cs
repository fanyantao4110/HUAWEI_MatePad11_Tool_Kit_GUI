using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 主页：功能总览与核心工具检查。
    /// </summary>
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
        }

        private void CheckTools_Click(object sender, RoutedEventArgs e)
        {
            var missing = AppConfig.CheckCoreTools();
            if (missing.Count == 0)
            {
                ToolCheckBar.Severity = InfoBarSeverity.Success;
                ToolCheckBar.Title = "核心工具齐全";
                ToolCheckBar.Message = "7z.exe / fastboot.exe / adb.exe 均已就绪。";
                Log.Append("[OK] 核心工具检查通过。");
            }
            else
            {
                ToolCheckBar.Severity = InfoBarSeverity.Error;
                ToolCheckBar.Title = "缺少核心工具";
                ToolCheckBar.Message = string.Join("；", missing.Select(Path.GetFileName));
                foreach (var f in missing)
                {
                    Log.Append($"[错误] 缺少 {f}");
                }
            }
            ToolCheckBar.IsOpen = true;
        }

        private void OpenBaseDir_Click(object sender, RoutedEventArgs e)
        {
            ProcessRunner.ExploreFolder(AppConfig.BaseDir);
        }
    }
}
