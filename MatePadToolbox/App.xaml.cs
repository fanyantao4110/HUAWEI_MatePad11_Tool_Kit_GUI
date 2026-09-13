using Microsoft.UI.Xaml;
using System.Text;

namespace MatePadToolbox
{
    /// <summary>
    /// 应用程序入口。负责初始化窗口与全局异常处理。
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            // 注册 GBK(936) 代码页，adb / fastboot / fh_loader 等工具在中文 Windows 下输出为 GBK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // 记录并吞掉异常，避免 UI 线程直接崩溃
            System.Diagnostics.Debug.WriteLine($"[未处理异常] {e.Exception}");
            e.Handled = true;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 启动时创建程序所需的全部目录
            Services.AppConfig.EnsureDirectories();

            _window = new MainWindow();
            _window.Activate();
        }

        public static Window? MainWindow => ((App)Current)._window;
    }
}
