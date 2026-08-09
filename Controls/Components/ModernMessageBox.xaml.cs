using System;
using System.Windows;
using System.Diagnostics;
using System.Windows.Input;

namespace LibmpvIptvClient
{
    public partial class ModernMessageBox : Window
    {
        public bool Result { get; private set; } = false;
        public bool Remember { get; private set; } = false;
        enum Choice { Dismiss, Yes, No, Ok }
        private Choice _choice = Choice.Dismiss;

        public ModernMessageBox(string title, string message, MessageBoxButton buttons = MessageBoxButton.OK, string? linkUrl = null, bool showRemember = false)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;

            if (!string.IsNullOrEmpty(linkUrl))
            {
                TxtLink.Visibility = Visibility.Visible;
                if (TxtLink.Inlines.FirstInline is System.Windows.Documents.Hyperlink hl)
                {
                    hl.NavigateUri = new Uri(linkUrl);
                    if (linkUrl.Contains("github.com"))
                    {
                        var firstInline = hl.Inlines.FirstInline;
                        var linkText = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Link_GitHubRepo", "访问 GitHub 项目主页");
                        if (firstInline is System.Windows.Documents.Run run)
                        {
                            run.Text = linkText;
                        }
                        else if (firstInline is System.Windows.Documents.InlineUIContainer uiContainer && uiContainer.Child is System.Windows.Controls.TextBlock tb)
                        {
                            tb.Text = linkText;
                        }
                    }
                }
            }

            if (showRemember)
            {
                ChkRemember.Visibility = Visibility.Visible;
                TxtRemember.Text = LibmpvIptvClient.Helpers.Localizer.S("CloseConfirm_Remember", "记住此次选择");
            }

            if (buttons == MessageBoxButton.YesNo)
            {
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Btn_Yes", "是");
                BtnNo.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Btn_No", "否");
            }
            else if (buttons == MessageBoxButton.OKCancel)
            {
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_OK", "确定");
                BtnNo.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Cancel", "取消");
            }
            else
            {
                BtnOk.Visibility = Visibility.Visible;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _choice = Choice.Dismiss;
            Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _choice = Choice.Dismiss;
                Close();
            }
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Remember = ChkRemember.IsChecked == true;
            Result = true;
            _choice = Choice.Yes;
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Remember = ChkRemember.IsChecked == true;
            Result = false;
            _choice = Choice.No;
            DialogResult = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            _choice = Choice.Ok;
            DialogResult = true;
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
        }

        public static bool? Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, string? linkUrl = null, bool showRemember = false)
        {
            var dlg = new ModernMessageBox(title, message, buttons, linkUrl, showRemember);
            if (owner != null)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                // Inherit topmost from owner if possible, or force it if owner is topmost
                if (owner.Topmost) dlg.Topmost = true;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Topmost = true; // Default to topmost if no owner
            }
            dlg.ShowDialog();
            return dlg._choice switch
            {
                Choice.Yes => true,
                Choice.No => false,
                _ => (bool?)null
            };
        }

        public static (bool? Result, bool Remember) ShowWithRemember(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, string? linkUrl = null)
        {
            var dlg = new ModernMessageBox(title, message, buttons, linkUrl, true);
            if (owner != null)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (owner.Topmost) dlg.Topmost = true;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Topmost = true;
            }
            dlg.ShowDialog();
            var result = dlg._choice switch
            {
                Choice.Yes => true,
                Choice.No => false,
                _ => (bool?)null
            };
            return (result, dlg.Remember);
        }

        public static bool? ShowCustom(Window? owner, string message, string title, string yesLabel, string noLabel, bool enableYes, bool enableNo, string? linkUrl = null)
        {
            var dlg = new ModernMessageBox(title, message, MessageBoxButton.YesNo, linkUrl);
            try
            {
                dlg.BtnYes.Content = yesLabel ?? dlg.BtnYes.Content;
                dlg.BtnNo.Content = noLabel ?? dlg.BtnNo.Content;
                dlg.BtnYes.IsEnabled = enableYes;
                dlg.BtnNo.IsEnabled = enableNo;
            }
            catch { }
            if (owner != null)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (owner.Topmost) dlg.Topmost = true;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Topmost = true;
            }
            dlg.ShowDialog();
            return dlg._choice switch
            {
                Choice.Yes => true,
                Choice.No => false,
                _ => (bool?)null
            };
        }
    }
}
