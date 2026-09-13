using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 解锁 BL 页。
    /// 对应 bat 的 :unlock_submenu / :unlock_os23 / :unlock_os4_engineering / :unlock_os4_downgrade。
    /// </summary>
    public sealed partial class UnlockBlPage : Page
    {
        private CancellationTokenSource? _cts;

        public UnlockBlPage()
        {
            this.InitializeComponent();
            OsVersionGroup.SelectionChanged += (_, _) => UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            Os4OptionsPanel.Visibility = OsVersionGroup.SelectedIndex == 1
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void StartUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (OsVersionGroup.SelectedIndex == 1 && Os4MethodGroup.SelectedIndex == 1)
            {
                ShowDowngradeHint();
                return;
            }

            // 确认弹窗
            var confirm = new ContentDialog
            {
                Title = "确认解锁",
                Content = "解锁 BL 可能会丢失数据，请确认已备份。\n前置条件：平板已开启 USB 调试并连接电脑。\n\n是否继续？",
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            var rebootViaAdb = OsVersionGroup.SelectedIndex == 0;
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await UnlockAsync(rebootViaAdb, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Append("[已取消] 解锁流程已停止。");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task UnlockAsync(bool rebootViaAdb, CancellationToken ct)
        {
            // 1. 文件检查（对应 bat 的 exist 检查）
            if (!File.Exists(AppConfig.DevprgElf))
            {
                Log.Append("[错误] 缺少底层文件 unlock\\Huawei865870_devprg.elf");
                return;
            }
            if (!File.Exists(AppConfig.AblUnlockImg))
            {
                Log.Append("[错误] 缺少ABL解锁文件 unlock\\Huawei865870_abl_unlock.img");
                return;
            }
            Log.Append("[OK] 文件检查通过。");

            // 2. 进入 9008 模式
            if (rebootViaAdb)
            {
                Log.Append("检测 ADB 设备...");
                if (!await DeviceService.WaitForAdbAsync(Log.Append, ct)) return;
                Log.Append("[OK] 设备已连接。");
                await DeviceService.RebootToEdlAsync(Log.Append);
                Log.Append("等待设备进入 9008 模式...");
            }
            else
            {
                Log.Append("等待设备进入 9008 模式（请确保已通过工程线/短接进入）...");
            }

            var port = await ComPortDetector.WaitFor9008Async(15, 2000, Log.Append, ct);
            if (!port.HasValue)
            {
                Log.Append("[错误] 等待超时，未检测到 9008 设备。");
                if (!rebootViaAdb) Log.Append("请重新确认已通过探针/短接方式进入 9008。");
                return;
            }
            Log.Append($"[OK] 检测到 9008 设备，端口 COM{port.Value}");

            // 3. 上传编程器
            if (!await EdlService.UploadFirehoseAsync(port.Value, Log.Append, ct)) return;

            // 4. 配置端口
            if (!await EdlService.ConfigurePortAsync(port.Value, AppConfig.UnlockDir, Log.Append, ct)) return;

            // 5. 写入解锁 ABL
            Log.Append("正在解锁...");
            var ok = await EdlService.SendXmlContentAsync(
                port.Value, "rawprogram0.xml", EdlService.UnlockAblXml, AppConfig.UnlockDir, Log.Append, ct);

            if (!ok)
            {
                Log.Append("[错误] 解锁失败，正在重启设备...");
                await EdlService.RebootDeviceAsync(port.Value, AppConfig.UnlockDir, Log.Append, ct);
                return;
            }

            Log.Append("[OK] 解锁成功，设备将自动重启。");
            await EdlService.RebootDeviceAsync(port.Value, AppConfig.UnlockDir, Log.Append, ct);
            Log.Append("==========================================================");
            Log.Append(" 解锁完成！如果设备没有自动进入任何模式，然后长按电源键重新激活开机！");
            Log.Append("==========================================================");
        }

        /// <summary>鸿蒙4 降级说明（对应 bat 的 :unlock_os4_downgrade）。</summary>
        private void ShowDowngradeHint()
        {
            Log.Append("==========================================================");
            Log.Append("  请先返回侧边栏选择【降级系统/回锁BL】进行降级操作！");
            Log.Append("  降级完成后，再回到解锁BL页面选择【鸿蒙2 - 鸿蒙3】方式解锁。");
            Log.Append("==========================================================");
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void SetBusy(bool busy)
        {
            BtnStart.IsEnabled = !busy;
            BtnCancel.IsEnabled = busy;
            OsVersionGroup.IsEnabled = !busy;
            Os4MethodGroup.IsEnabled = !busy;
        }
    }
}
