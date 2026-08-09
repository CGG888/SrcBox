using System.Windows;

namespace LibmpvIptvClient
{
    public partial class UploadQueueWindow : Window
    {
        public UploadQueueWindow()
        {
            InitializeComponent();
        }
        void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
        void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }
    }
}
