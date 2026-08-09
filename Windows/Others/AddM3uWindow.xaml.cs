using System;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class AddM3uWindow : Window
    {
        public string SourceName { get; private set; } = "";
        public string SourceUrl { get; private set; } = "";

        public AddM3uWindow()
        {
            InitializeComponent();
        }

        public void HideNameField()
        {
            LblName.Visibility = Visibility.Collapsed;
            TxtName.Visibility = Visibility.Collapsed;
            // Adjust row height if needed, but collapsed should work
            // The new grid structure is inside Border -> Grid -> Grid (row 1)
            var contentGrid = (System.Windows.Controls.Grid)((System.Windows.Controls.Grid)((System.Windows.Controls.Border)Content).Child).Children[1];
            contentGrid.RowDefinitions[0].Height = new GridLength(0);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SourceName = TxtName.Text.Trim();
            SourceUrl = TxtUrl.Text.Trim();

            if (TxtName.Visibility == Visibility.Visible && string.IsNullOrEmpty(SourceName))
            {
                System.Windows.MessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Prompt_InputName", "请输入名称"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(SourceUrl))
            {
                System.Windows.MessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Prompt_InputUrl", "请输入地址"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
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
