using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace MatePadToolbox.Controls
{
    /// <summary>
    /// 通用日志输出面板：线程安全，可从任意线程追加文本。
    /// </summary>
    public sealed partial class LogPanel : UserControl
    {
        private const int MaxLines = 3000;

        public LogPanel()
        {
            this.InitializeComponent();
        }

        /// <summary>追加一行日志（自动调度到 UI 线程）。</summary>
        public void Append(string line)
        {
            var queue = DispatcherQueue;
            if (queue.HasThreadAccess)
            {
                AppendCore(line);
            }
            else
            {
                queue.TryEnqueue(() => AppendCore(line));
            }
        }

        /// <summary>清空日志。</summary>
        public void Clear()
        {
            var queue = DispatcherQueue;
            if (queue.HasThreadAccess)
            {
                LogText.Text = string.Empty;
            }
            else
            {
                queue.TryEnqueue(() => LogText.Text = string.Empty);
            }
        }

        private void AppendCore(string line)
        {
            LogText.Text += line + "\n";

            // 防止日志过长拖慢渲染
            var lines = LogText.Text.Split('\n');
            if (lines.Length > MaxLines)
            {
                LogText.Text = string.Join('\n', lines[^MaxLines..]);
            }

            ScrollArea.ChangeView(null, ScrollArea.ScrollableHeight, null, disableAnimation: true);
        }
    }
}
