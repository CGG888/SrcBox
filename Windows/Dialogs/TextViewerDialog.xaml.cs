using System;
using System.Windows;
using System.Windows.Input;

namespace LibmpvIptvClient
{
    public partial class TextViewerDialog : Window
    {
        public TextViewerDialog(string title, string content)
        {
            InitializeComponent();
            try { LblTitle.Text = title; } catch { }
            try { TxtContent.Text = content ?? ""; } catch { }
        }
        void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1)
            {
                try { DragMove(); } catch { }
            }
        }
        void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        void BtnCopyAll_Click(object sender, RoutedEventArgs e)
        {
            try { System.Windows.Clipboard.SetText(TxtContent.Text ?? ""); } catch { }
        }
        void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var q = (TxtSearch.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q)) return;
            var text = TxtContent.Text ?? "";
            var start = TxtContent.SelectionStart + TxtContent.SelectionLength;
            if (start < 0 || start >= text.Length) start = 0;
            var idx = text.IndexOf(q, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 && start > 0) idx = text.IndexOf(q, 0, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                TxtContent.Focus();
                TxtContent.Select(idx, q.Length);
                TxtContent.ScrollToLine(TxtContent.GetLineIndexFromCharacterIndex(idx));
            }
        }
        void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            var q = (TxtSearch.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q)) return;
            var text = TxtContent.Text ?? "";
            var start = TxtContent.SelectionStart - 1;
            if (start < 0) start = text.Length - 1;
            var idx = LastIndexOf(text, q, start);
            if (idx < 0 && start < text.Length - 1) idx = LastIndexOf(text, q, text.Length - 1);
            if (idx >= 0)
            {
                TxtContent.Focus();
                TxtContent.Select(idx, q.Length);
                TxtContent.ScrollToLine(TxtContent.GetLineIndexFromCharacterIndex(idx));
            }
        }
        int LastIndexOf(string text, string value, int start)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return -1;
            var s = Math.Min(start, text.Length - 1);
            for (int i = s; i >= 0; i--)
            {
                if (i + 1 >= value.Length)
                {
                    var subStart = i - value.Length + 1;
                    if (subStart >= 0)
                    {
                        if (string.Compare(text, subStart, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0)
                            return subStart;
                    }
                }
            }
            return -1;
        }
    }
}
