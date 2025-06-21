using System;
using System.Windows.Controls;

namespace QuquPlot.Utils
{
    public static class DebugUtils
    {
        /// <summary>
        /// 向TextBox追加调试信息，并自动滚动到底部
        /// </summary>
        /// <param name="textBox">目标TextBox</param>
        /// <param name="message">要追加的消息</param>
        public static void AppendDebugInfo(TextBox textBox, string message)
        {
#if DEBUG
            textBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            textBox.ScrollToEnd();
#endif
        }

        /// <summary>
        /// 清空调试信息
        /// </summary>
        public static void ClearDebug(TextBox textBox)
        {
            textBox.Clear();
        }

        /// <summary>
        /// 复制调试信息到剪贴板
        /// </summary>
        public static void CopyDebug(TextBox textBox)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
            {
                System.Windows.Clipboard.SetText(textBox.Text);
            }
        }
    }
} 