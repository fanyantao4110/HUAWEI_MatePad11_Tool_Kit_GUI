using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 9008 备份页：回读全分区（READ-ALL）。
    /// 功能原生合并自高通工具箱框架脚本 toolbox.bat READ-ALL → qctool edlreadall，
    /// 全部流程在软件内部执行：检测 9008 → 上传编程器 → 识别存储/LUN → 回读分区表 →
    /// 解析 GPT → 生成 rawprogram XML / partition.xml / flash_all.bat → 回读全部分区。
    /// 不弹出记事本，全程日志显示在界面中。
    /// </summary>
    public sealed partial class Backup9008Page : Page
    {
        private CancellationTokenSource? _cts;

        public Backup9008Page()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(SavePathBox.Text))
                {
                    SavePathBox.Text = AppConfig.BackupDir;
                }
            };
        }

        private async void StartBackup_Click(object sender, RoutedEventArgs e)
        {
            // 保存目录
            var saveRoot = (SavePathBox.Text ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(saveRoot))
            {
                Log.Append("[错误] 请先选择保存目录。");
                return;
            }
            try
            {
                Directory.CreateDirectory(saveRoot);
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 无法创建保存目录：{ex.Message}");
                return;
            }

            // 文件检查
            if (!File.Exists(AppConfig.QSaharaServerExe))
            {
                Log.Append("[错误] 缺少 tools\\QSaharaServer.exe");
                return;
            }
            if (!File.Exists(AppConfig.FhLoaderExe))
            {
                Log.Append("[错误] 缺少 tools\\fh_loader.exe");
                return;
            }
            if (!File.Exists(AppConfig.DevprgElf))
            {
                Log.Append("[错误] 缺少底层文件 unlock\\Huawei865870_devprg.elf");
                return;
            }
            Log.Append("[OK] 文件检查通过。");

            // 确认弹窗
            var confirm = new ContentDialog
            {
                Title = "回读全分区",
                Content = "将回读设备全部分区镜像（userdata、last_parti 等默认跳过），耗时较长。\n\n前置条件：平板已进入 9008 模式并连接电脑（可在解锁BL/Root页面通过 ADB 自动进入，或用工程线/短接进入）。\n\n是否继续？",
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                // 等待 9008 设备（对应 bat 的 chkdev qcedl）
                Log.Append("请确认设备已进入 9008 模式，正在等待设备连接...");
                var port = await ComPortDetector.WaitFor9008Async(60, 2000, Log.Append, _cts.Token);
                if (!port.HasValue)
                {
                    Log.Append("[错误] 等待超时，未检测到 9008 设备。");
                    Log.Append("请确认已通过探针/短接或 ADB（adb reboot edl）进入 9008。");
                    return;
                }
                Log.Append($"[OK] 已检测到 9008 设备，端口 COM{port.Value}");

                // 上传编程器（对应框架的 write qcedlsendfh：QSaharaServer -s 13:firehose）
                if (!await EdlService.UploadFirehoseAsync(port.Value, Log.Append, _cts.Token)) return;

                // 配置端口（对应 write qcedlsendfh %port% %fh% auto 的配置端口步骤：
                // 初始化存储后才能进行后续 --skip_configure 的读取，否则会提示读取设备信息失败）
                if (!await EdlService.ConfigurePortAutoAsync(port.Value, Log.Append, _cts.Token)) return;

                // 回读全分区（对应 qctool edlreadall 的完整流程）
                var ok = await EdlBackupService.RunReadAllAsync(saveRoot, port.Value, Log.Append, _cts.Token);
                if (ok)
                {
                    Log.Append("9008回读全分区完成。");
                }
            }
            catch (OperationCanceledException)
            {
                Log.Append("[已取消] 回读全分区已停止。");
            }
            catch (Exception ex)
            {
                Log.Append($"[错误] 发生异常：{ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                if (App.MainWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                }
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    SavePathBox.Text = folder.Path;
                }
            }
            catch (Exception ex)
            {
                Log.Append($"[提示] 打开目录选择器失败（{ex.Message}），可直接在输入框中填写路径。");
            }
        }

        private void OpenBackupDir_Click(object sender, RoutedEventArgs e)
        {
            var dir = (SavePathBox.Text ?? "").Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                ProcessRunner.ExploreFolder(dir);
            }
            else
            {
                ProcessRunner.ExploreFolder(AppConfig.BackupDir);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void SetBusy(bool busy)
        {
            BtnStart.IsEnabled = !busy;
            BtnCancel.IsEnabled = busy;
            BtnBrowse.IsEnabled = !busy;
            SavePathBox.IsEnabled = !busy;
        }
    }
}
