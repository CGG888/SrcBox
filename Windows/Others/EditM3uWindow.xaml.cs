using System;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class EditM3uWindow : Window
    {
        public string SourceName { get; private set; } = "";
        public string SourceUrl { get; private set; } = "";
        public bool IsDeleteRequested { get; private set; } = false;

        public EditM3uWindow(string name, string url)
        {
            InitializeComponent();
            TxtName.Text = name;
            TxtUrl.Text = url;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SourceName = TxtName.Text.Trim();
            SourceUrl = TxtUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                System.Windows.MessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Prompt_InputName", "请输入名称"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TxtUrl.Text))
            {
                System.Windows.MessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Prompt_InputUrl", "请输入地址"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Confirm_DeleteSource", "确定要删除这个源吗？"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                IsDeleteRequested = true;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
            }
            else
            {
                DragMove();
            }
        }

        private void BtnCloseTitle_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
