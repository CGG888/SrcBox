using System;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class ShortcutsWindow : Window
    {
        public ShortcutsWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}