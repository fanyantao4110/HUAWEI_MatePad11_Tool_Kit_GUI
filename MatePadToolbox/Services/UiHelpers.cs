using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 通用 UI 助手：设备状态选择弹窗、确认弹窗、Fastboot 模式准备流程。
    /// </summary>
    public static class UiHelpers
    {
        public enum DeviceStateChoice
        {
            SystemMode,
            FastbootMode,
            Cancel
        }

        /// <summary>
        /// 确认弹窗，返回用户是否点击"继续"。
        /// </summary>
        public static async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message,
            string primaryText = "继续", string closeText = "取消")
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };
            return await dlg.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>
        /// 询问设备当前状态（对应 bat 的 :prepare_fastboot_mode 开头的选择）。
        /// </summary>
        public static async Task<DeviceStateChoice> AskDeviceStateAsync(XamlRoot xamlRoot)
        {
            var dlg = new ContentDialog
            {
                Title = "请确认设备当前状态",
                Content = "选择设备当前所处模式：\n\n· 系统模式 —— 平板正常开机，已开启 USB 调试\n· Fastboot模式 —— 已通过按键手动进入 Fastboot",
                PrimaryButtonText = "系统模式",
                SecondaryButtonText = "Fastboot模式",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };
            var result = await dlg.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => DeviceStateChoice.SystemMode,
                ContentDialogResult.Secondary => DeviceStateChoice.FastbootMode,
                _ => DeviceStateChoice.Cancel
            };
        }

        /// <summary>
        /// 询问 9008 模式下的系统版本（类似 AskDeviceStateAsync）。
        /// 返回 null=取消，true=鸿蒙2-3，false=鸿蒙4。
        /// </summary>
        public static async Task<bool?> Ask9008StateAsync(XamlRoot xamlRoot)
        {
            var dlg = new ContentDialog
            {
                Title = "请确认设备系统版本",
                Content = "选择设备当前系统版本：",
                PrimaryButtonText = "鸿蒙2 - 鸿蒙3（通过 ADB 自动进入 9008）",
                SecondaryButtonText = "鸿蒙4（需工程线/短接进入 9008）",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };
            var result = await dlg.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => true,
                ContentDialogResult.Secondary => false,
                _ => null
            };
        }

        /// <summary>
        /// 完整准备 Fastboot 模式：询问状态 → （系统模式则 adb reboot bootloader）→ 等待 Fastboot 设备。
        /// 对应 bat 的 :prepare_fastboot_mode + :wait_fastboot_device。
        /// </summary>
        public static async Task<bool> PrepareFastbootAsync(XamlRoot xamlRoot, Action<string> log, CancellationToken ct = default)
        {
            var choice = await AskDeviceStateAsync(xamlRoot);
            switch (choice)
            {
                case DeviceStateChoice.SystemMode:
                    log("正在尝试通过 ADB 重启设备...");
                    if (!await DeviceService.WaitForAdbAsync(log, ct)) return false;
                    log("设备已连接，正在重启进入 Fastboot 模式...");
                    await DeviceService.RebootToBootloaderAsync(log);
                    break;

                case DeviceStateChoice.FastbootMode:
                    log("请确保已手动让设备进入 Fastboot 模式（按住音量下键 + 电源键）。");
                    break;

                default:
                    log("[已取消]");
                    return false;
            }

            return await DeviceService.WaitForFastbootAsync(log, ct);
        }
    }
}
