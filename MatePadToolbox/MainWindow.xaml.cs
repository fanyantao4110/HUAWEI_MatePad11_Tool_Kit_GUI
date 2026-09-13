using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Dialogs;
using MatePadToolbox.Pages;
using MatePadToolbox.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace MatePadToolbox
{
    /// <summary>
    /// 主窗口：左侧导航 + 内容页 Frame。
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Windows 10 (1809+) 下让标题栏跟随深浅色：WinUI3 不会自动设置 DWM 深色标题栏，
        // 需要手动调用 DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)。
        // 不同系统版本接受不同属性值：Win11 / Win10 1903+ 用 20；部分 Win10 版本（含本机 1909）
        // 只接受 19。因此依次尝试，成功即停止。
        private static readonly int[] DWM_IMMERSIVE_DARK_MODE_ATTRIBUTES = { 20, 19 };

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private readonly Dictionary<string, Type> _pages = new()
        {
            { "home", typeof(HomePage) },
            { "driver", typeof(DriverPage) },
            { "unlock", typeof(UnlockBlPage) },
            { "root", typeof(RootPage) },
            { "download", typeof(DownloadRomPage) },
            { "flash", typeof(FlashSystemPage) },
            { "official", typeof(FlashOfficialPage) },
            { "downgrade", typeof(DowngradePage) },
            { "console", typeof(AdbConsolePage) },
            { "backup9008", typeof(Backup9008Page) },
        };

        private bool _disclaimerShown;

        public MainWindow()
        {
            this.InitializeComponent();

            // 窗口标题与尺寸
            this.Title = "华为MatePad 11 综合工具箱";
            if (this.AppWindow is Microsoft.UI.Windowing.AppWindow appWindow)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
            }

            // 标题栏主题：内容加载完成即应用（不依赖激活事件，避免启动时标题栏仍是白色），
            // 激活与系统深浅色切换时实时更新。
            this.Activated += (_, _) => ApplyTitleBarTheme("Activated");
            if (Content is FrameworkElement root)
            {
                root.Loaded += (_, _) => ApplyTitleBarTheme("Loaded");
                root.ActualThemeChanged += (_, _) => ApplyTitleBarTheme("ActualThemeChanged");
            }
        }

        /// <summary>
        /// 根据当前实际主题设置 DWM 标题栏深浅色（Windows 10 必需）。
        /// </summary>
        private void ApplyTitleBarTheme(string why = "")
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero) return;
                if (Content is not FrameworkElement root) return;

                int dark = root.ActualTheme == ElementTheme.Dark ? 1 : 0;
                // TEMP DIAG
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "titlebar_diag.log"),
                        $"{DateTime.Now:HH:mm:ss.fff} why={why} theme={root.ActualTheme} dark={dark} hwnd={hwnd}\r\n");
                }
                catch { }
                foreach (int attribute in DWM_IMMERSIVE_DARK_MODE_ATTRIBUTES)
                {
                    int hr = DwmSetWindowAttribute(hwnd, attribute, ref dark, sizeof(int));
                    if (hr >= 0) break; // S_OK / S_FALSE 均视为成功
                }

                // 强制 DWM 立即按新属性重绘标题栏，避免首次显示时仍用浅色帧
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch
            {
                // 标题栏主题设置失败不应影响主流程
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(HomePage));

            if (!_disclaimerShown)
            {
                _disclaimerShown = true;
                _ = ShowDisclaimerAsync();
            }
        }

        private async System.Threading.Tasks.Task ShowDisclaimerAsync()
        {
            var dlg = new DisclaimerDialog
            {
                XamlRoot = NavView.XamlRoot
            };
            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                // 不同意免责声明 → 直接退出程序
                Application.Current.Exit();
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is not NavigationViewItem item) return;
            var tag = item.Tag?.ToString() ?? string.Empty;

            if (tag == "devmgr")
            {
                // 设备管理器：直接打开系统管理控制台
                try
                {
                    Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowToast($"打开设备管理器失败：{ex.Message}");
                }
                return;
            }

            if (_pages.TryGetValue(tag, out var pageType))
            {
                if (ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }

        private async void ShowToast(string message)
        {
            try
            {
                var dlg = new ContentDialog
                {
                    Title = "提示",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = NavView.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch { /* 忽略弹窗异常 */ }
        }
    }
}
