using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MatePadToolbox.Models;
using MatePadToolbox.Services;
using System.IO;

namespace MatePadToolbox.Pages
{
    /// <summary>
    /// 刷入第三方系统页：Fastboot 模式与 9008 模式。
    /// 对应 bat 的 :flash_entry_menu / :flash_system_entry / :flash_9008_menu 及其子流程。
    /// </summary>
    public sealed partial class FlashSystemPage : Page
    {
        private CancellationTokenSource? _cts;
        private List<string> _baseZips = new();
        private int? _q9008Port;

        public FlashSystemPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) => RefreshBaseList();
        }

        // ==================== 公共 ====================

        private void RefreshBaseList_Click(object sender, RoutedEventArgs e) => RefreshBaseList();

        private void RefreshBaseList()
        {
            _baseZips = Directory.Exists(AppConfig.BaseRomDir)
                ? Directory.EnumerateFiles(AppConfig.BaseRomDir, "*.zip").OrderBy(f => f).ToList()
                : new List<string>();

            var names = _baseZips.Select(Path.GetFileName).ToList();
            FbBaseCombo.ItemsSource = names;
            Q9008BaseCombo.ItemsSource = names;
            if (_baseZips.Count > 0)
            {
                FbBaseCombo.SelectedIndex = 0;
                Q9008BaseCombo.SelectedIndex = 0;
            }
            else
            {
                Log.Append($"[提示] 未在 base 目录找到任何 ZIP 底包（{AppConfig.BaseRomDir}）。");
            }
        }

        private string? GetSelectedBaseZip(ComboBox combo)
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= _baseZips.Count) return null;
            return _baseZips[combo.SelectedIndex];
        }

        private void LogSummary(FlashSummary s, string successMsg)
        {
            if (s.AllSuccess)
            {
                Log.Append("===============================================");
                Log.Append($"  {successMsg}");
                Log.Append("===============================================");
            }
            else
            {
                Log.Append("===============================================");
                Log.Append($"  刷写完成，但有 {s.FailedParts.Count} 个分区失败：");
                Log.Append($"  {string.Join(" ", s.FailedParts)}");
                Log.Append("===============================================");
            }
        }

        /// <summary>刷入 misc 分区（清除恢复指令），对应 bat 中的 misc 处理。</summary>
        private async Task FlashMiscFastbootAsync(CancellationToken ct)
        {
            Log.Append("尝试刷写 misc 分区（清除恢复指令）...");
            if (!File.Exists(AppConfig.MiscImg))
            {
                Log.Append("[错误] 缺少 tools\\rawprogram\\misc.img");
                return;
            }
            var ok = await DeviceService.FlashPartitionAsync("misc", AppConfig.MiscImg, Log.Append, ct);
            if (!ok) Log.Append("[警告] 写入 misc 分区失败！");
        }

        private void SetBusy(bool busy)
        {
            BtnFbBase.IsEnabled = !busy;
            BtnFbVbmeta.IsEnabled = !busy;
            BtnFbSuper.IsEnabled = !busy;
            BtnFbOneClick.IsEnabled = !busy;
            BtnFbCancel.IsEnabled = busy;
            Btn9008Connect.IsEnabled = !busy;
            Btn9008Base.IsEnabled = !busy;
            Btn9008Vbmeta.IsEnabled = !busy;
            Btn9008Super.IsEnabled = !busy;
            Btn9008OneClick.IsEnabled = !busy;
            Btn9008Cancel.IsEnabled = busy;
            FbBaseCombo.IsEnabled = !busy;
            Q9008BaseCombo.IsEnabled = !busy;
        }

        // ==================== Fastboot 模式 ====================

        private async void FbFlashBase_Click(object sender, RoutedEventArgs e)
        {
            var zip = GetSelectedBaseZip(FbBaseCombo);
            if (zip == null) { Log.Append("[错误] 请先选择底包 ZIP。"); return; }
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "警告", "此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;
                await FlashBaseZipViaFastbootAsync(zip, _cts.Token);

                if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "底包刷写完成", "是否继续去除 avb 校验？", "继续", "返回"))
                    return;
                await FlashVbmetaViaFastbootAsync(_cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void FbFlashVbmeta_Click(object sender, RoutedEventArgs e)
        {
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "警告", "此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;
                await FlashVbmetaViaFastbootAsync(_cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void FbFlashSuper_Click(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;
                await FlashSuperViaFastbootAsync(_cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void FbOneClick_Click(object sender, RoutedEventArgs e)
        {
            var zip = GetSelectedBaseZip(FbBaseCombo);
            if (zip == null) { Log.Append("[错误] 请先选择底包 ZIP。"); return; }
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "一键执行",
                "将依次执行：刷入底包 → 去除 avb 校验 → 刷入 super.img。\n此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("准备进入 Fastboot 模式...");
                if (!await UiHelpers.PrepareFastbootAsync(this.XamlRoot, Log.Append, _cts.Token)) return;

                Log.Append("===== 步骤 1/3：刷入底包 =====");
                if (!await FlashBaseZipViaFastbootAsync(zip, _cts.Token)) return;

                Log.Append("===== 步骤 2/3：去除 avb 校验 =====");
                if (!await FlashVbmetaViaFastbootAsync(_cts.Token, askReboot: false)) return;

                Log.Append("===== 步骤 3/3：刷入 super.img =====");
                await FlashSuperViaFastbootAsync(_cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private void FbCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        /// <summary>Fastboot 刷底包：解压 ZIP → 递归刷入所有 img → 刷 misc → 清理。</summary>
        private async Task<bool> FlashBaseZipViaFastbootAsync(string zip, CancellationToken ct)
        {
            Log.Append($"已选择：{Path.GetFileName(zip)}");
            AppConfig.ResetCacheDir();
            if (!await ZipService.ExtractAsync(zip, AppConfig.CacheDir, Log.Append, ct)) return false;

            var summary = await DeviceService.FlashAllImagesAsync(AppConfig.CacheDir, recursive: true, Log.Append, ct);
            AppConfig.ResetCacheDir();
            LogSummary(summary, "底包刷入全部成功！");
            await FlashMiscFastbootAsync(ct);
            return true;
        }

        /// <summary>Fastboot 去除 avb 校验：解压 vbmeta.zip → 刷入所有 img → 刷 misc → 清理。</summary>
        private async Task<bool> FlashVbmetaViaFastbootAsync(CancellationToken ct, bool askReboot = true)
        {
            if (!File.Exists(AppConfig.VbmetaZip))
            {
                Log.Append("[错误] 未找到 vbmeta\\vbmeta.zip，请将去除 avb 校验的包重命名为 vbmeta.zip 放入 vbmeta 目录。");
                return false;
            }

            AppConfig.ResetCacheDir();
            if (!await ZipService.ExtractAsync(AppConfig.VbmetaZip, AppConfig.CacheDir, Log.Append, ct)) return false;

            var summary = await DeviceService.FlashAllImagesAsync(AppConfig.CacheDir, recursive: false, Log.Append, ct);
            await FlashMiscFastbootAsync(ct);
            AppConfig.ResetCacheDir();
            LogSummary(summary, "去除 avb 校验刷写成功！");

            if (askReboot)
            {
                if (await UiHelpers.ConfirmAsync(this.XamlRoot, "操作完成", "是否重启设备？", "重启", "不重启"))
                {
                    await DeviceService.RebootAsync(Log.Append);
                }
            }
            return true;
        }

        /// <summary>Fastboot 刷入 system 目录下的所有 img（super.img）。</summary>
        private async Task<bool> FlashSuperViaFastbootAsync(CancellationToken ct)
        {
            var imgs = Directory.Exists(AppConfig.SystemDir)
                ? Directory.EnumerateFiles(AppConfig.SystemDir, "*.img").ToList()
                : new List<string>();

            if (imgs.Count == 0)
            {
                Log.Append("[错误] 未在 system 目录找到任何 .img 文件。");
                Log.Append($"请确认已将下载的 img 文件放入：{AppConfig.SystemDir}");
                return false;
            }

            Log.Append("检测到以下 img 镜像文件，将直接刷入：");
            foreach (var f in imgs) Log.Append($"  {Path.GetFileName(f)}");

            var summary = await DeviceService.FlashAllImagesAsync(AppConfig.SystemDir, recursive: false, Log.Append, ct);
            LogSummary(summary, "所有镜像刷写成功！");

            Log.Append("正在重启设备...");
            await DeviceService.RebootAsync(Log.Append);
            Log.Append("刷入完成，设备正在重启！");
            return true;
        }

        // ==================== 9008 模式 ====================

        private async void Q9008Connect_Click(object sender, RoutedEventArgs e)
        {
            var rebootViaAdb = await UiHelpers.Ask9008StateAsync(this.XamlRoot);
            if (rebootViaAdb == null) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                var port = await EdlService.ConnectAsync(rebootViaAdb.Value, Log.Append, _cts.Token);
                if (port > 0)
                {
                    _q9008Port = port;
                    PortStatusText.Text = $"已连接：COM{port}";
                    Log.Append("编程器和端口配置完成，设备已经连接。");
                }
                else
                {
                    _q9008Port = null;
                    PortStatusText.Text = "未连接";
                }
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private bool Require9008Connection()
        {
            if (_q9008Port.HasValue) return true;
            Log.Append("[错误] 尚未连接 9008 设备，请先点击【连接9008端口】。");
            return false;
        }

        private async void Q9008FlashBase_Click(object sender, RoutedEventArgs e)
        {
            if (!Require9008Connection()) return;
            var zip = GetSelectedBaseZip(Q9008BaseCombo);
            if (zip == null) { Log.Append("[错误] 请先选择底包 ZIP。"); return; }
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "警告", "此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await FlashBaseVia9008Async(_q9008Port!.Value, zip, _cts.Token);
                await AskReboot9008Async(_q9008Port.Value, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void Q9008FlashVbmeta_Click(object sender, RoutedEventArgs e)
        {
            if (!Require9008Connection()) return;
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "警告", "此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await FlashVbmetaVia9008Async(_q9008Port!.Value, _cts.Token);
                await AskReboot9008Async(_q9008Port.Value, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void Q9008FlashSuper_Click(object sender, RoutedEventArgs e)
        {
            if (!Require9008Connection()) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await FlashSuperVia9008Async(_q9008Port!.Value, _cts.Token);
                await AskReboot9008Async(_q9008Port.Value, _cts.Token);
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private async void Q9008OneClick_Click(object sender, RoutedEventArgs e)
        {
            var zip = GetSelectedBaseZip(Q9008BaseCombo);
            if (zip == null) { Log.Append("[错误] 请先选择底包 ZIP。"); return; }
            if (!await UiHelpers.ConfirmAsync(this.XamlRoot, "9008 一键执行",
                "将依次执行：选择系统版本 → 连接9008 → 刷入底包 → 去除avb校验 → 刷入super.img。\n此操作将会清除所有数据！\n\n是否继续？")) return;

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                Log.Append("===== 步骤 1/4：选择系统版本 =====");
                var rebootViaAdb = await UiHelpers.Ask9008StateAsync(this.XamlRoot);
                if (rebootViaAdb == null) { Log.Append("[已取消]"); return; }

                Log.Append("===== 步骤 2/4：连接 9008 =====");
                var port = await EdlService.ConnectAsync(rebootViaAdb.Value, Log.Append, _cts.Token);
                if (port <= 0) return;
                _q9008Port = port;
                PortStatusText.Text = $"已连接：COM{port}";

                Log.Append("===== 步骤 3/5：刷入底包 =====");
                if (!await FlashBaseVia9008Async(port, zip, _cts.Token)) return;

                Log.Append("===== 步骤 4/5：去除 avb 校验 =====");
                if (!await FlashVbmetaVia9008Async(port, _cts.Token)) return;

                Log.Append("===== 步骤 5/5：刷入 super.img =====");
                if (!await FlashSuperVia9008Async(port, _cts.Token)) return;

                Log.Append("正在重启设备...");
                await EdlService.RebootDeviceAsync(port, AppConfig.UnlockDir, Log.Append, _cts.Token);
                Log.Append("如果设备没有自动进入任何模式，请长按电源键重新激活开机！");
                Log.Append("===============================================");
                Log.Append("  9008模式一键执行完成！");
                Log.Append("===============================================");
                _q9008Port = null;
                PortStatusText.Text = "未连接";
            }
            catch (OperationCanceledException) { Log.Append("[已取消]"); }
            finally { SetBusy(false); }
        }

        private void Q9008Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        /// <summary>9008 刷底包：校验 rawprogram0-5.xml → 解压 → 逐个 sendxml。</summary>
        private async Task<bool> FlashBaseVia9008Async(int port, string zip, CancellationToken ct)
        {
            // 检查 rawprogram0-5.xml 是否齐全
            var missing = Enumerable.Range(0, 6)
                .Select(i => Path.Combine(AppConfig.RawprogramDir, $"rawprogram{i}.xml"))
                .Where(f => !File.Exists(f))
                .ToList();
            if (missing.Count > 0)
            {
                Log.Append("[错误] 刷机文件不完整，请确保 tools\\rawprogram 下存在 rawprogram0.xml - rawprogram5.xml");
                return false;
            }

            Log.Append($"已选择：{Path.GetFileName(zip)}");
            AppConfig.ResetCacheDir();
            if (!await ZipService.ExtractAsync(zip, AppConfig.CacheDir, Log.Append, ct)) return false;

            Log.Append("开始刷写分区...");
            var allOk = true;
            for (int i = 0; i <= 5; i++)
            {
                var xml = Path.Combine(AppConfig.RawprogramDir, $"rawprogram{i}.xml");
                Log.Append($"正在刷写 rawprogram{i}.xml ...");
                var ok = await EdlService.SendXmlAsync(port, xml, AppConfig.CacheDir, Log.Append, ct);
                Log.Append(ok ? $"[OK] rawprogram{i}.xml 刷写成功。" : $"[错误] 刷写 rawprogram{i}.xml 失败！");
                allOk &= ok;
            }

            AppConfig.ResetCacheDir();
            return allOk;
        }

        /// <summary>9008 去除 avb 校验：vbmeta.zip → rawprogram_vbmeta.xml + misc。</summary>
        private async Task<bool> FlashVbmetaVia9008Async(int port, CancellationToken ct)
        {
            if (!File.Exists(Path.Combine(AppConfig.RawprogramDir, "rawprogram_vbmeta.xml")))
            {
                Log.Append("[错误] 缺少 tools\\rawprogram\\rawprogram_vbmeta.xml");
                return false;
            }
            if (!File.Exists(AppConfig.MiscImg))
            {
                Log.Append("[错误] 缺少 tools\\rawprogram\\misc.img");
                return false;
            }
            if (!File.Exists(AppConfig.VbmetaZip))
            {
                Log.Append("[错误] 未找到 vbmeta\\vbmeta.zip，请将去除 avb 校验的包重命名为 vbmeta.zip 放入 vbmeta 目录。");
                return false;
            }

            AppConfig.ResetCacheDir();
            if (!await ZipService.ExtractAsync(AppConfig.VbmetaZip, AppConfig.CacheDir, Log.Append, ct)) return false;

            Log.Append("开始刷写 vbmeta 分区...");
            var vbXml = Path.Combine(AppConfig.RawprogramDir, "rawprogram_vbmeta.xml");
            if (!await EdlService.SendXmlAsync(port, vbXml, AppConfig.CacheDir, Log.Append, ct))
            {
                Log.Append("[错误] 刷写 vbmeta 分区失败！");
                return false;
            }
            Log.Append("[OK] vbmeta 分区刷写成功。");

            Log.Append("尝试刷写 misc 分区（清除恢复指令）...");
            var miscXml = AppConfig.WriteTmpFile("rawprogram_misc.xml", EdlService.MiscXml);
            if (!await EdlService.SendXmlAsync(port, miscXml, AppConfig.RawprogramDir, Log.Append, ct))
            {
                Log.Append("[错误] 刷写 misc 分区失败！");
                return false;
            }
            Log.Append("[OK] misc 分区刷写成功。");

            AppConfig.ResetCacheDir();
            return true;
        }

        /// <summary>9008 刷入 super.img。</summary>
        private async Task<bool> FlashSuperVia9008Async(int port, CancellationToken ct)
        {
            var superImg = Path.Combine(AppConfig.SystemDir, "super.img");
            if (!File.Exists(superImg))
            {
                Log.Append("[错误] 未找到 super.img。");
                Log.Append($"请将 super.img 放入：{AppConfig.SystemDir}");
                return false;
            }
            if (!File.Exists(Path.Combine(AppConfig.RawprogramDir, "rawprogram_super.xml")))
            {
                Log.Append("[错误] 缺少 tools\\rawprogram\\rawprogram_super.xml");
                return false;
            }

            Log.Append("开始刷写 super 分区...");
            var superXml = Path.Combine(AppConfig.RawprogramDir, "rawprogram_super.xml");
            if (!await EdlService.SendXmlAsync(port, superXml, AppConfig.SystemDir, Log.Append, ct))
            {
                Log.Append("[错误] 刷写 super 分区失败！");
                return false;
            }
            Log.Append("[OK] super 分区刷写成功。");
            return true;
        }

        /// <summary>9008 操作完成后询问是否重启。</summary>
        private async Task AskReboot9008Async(int port, CancellationToken ct)
        {
            if (await UiHelpers.ConfirmAsync(this.XamlRoot, "刷写完成", "是否重启设备？", "重启", "不重启"))
            {
                await EdlService.RebootDeviceAsync(port, AppConfig.UnlockDir, Log.Append, ct);
                Log.Append("如果设备没有自动进入任何模式，请长按电源键重新激活开机！");
                _q9008Port = null;
                PortStatusText.Text = "未连接";
            }
        }
    }
}
