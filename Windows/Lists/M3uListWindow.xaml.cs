using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class M3uListWindow : Window
    {
        private ObservableCollection<M3uSource> _items = new ObservableCollection<M3uSource>();
        public M3uListWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                LoadData();
            };
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { }
        }
        void LoadData()
        {
            try
            {
                var list = AppSettings.Current.SavedSources ?? new System.Collections.Generic.List<M3uSource>();
                _items = new ObservableCollection<M3uSource>(list);
                Grid.ItemsSource = _items;
            }
            catch { }
        }
        void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is M3uSource src)
                {
                    var dlg = new EditM3uWindow(src.Name, src.Url) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    try { LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(dlg); } catch { }
                    if (dlg.ShowDialog() == true)
                    {
                        if (dlg.IsDeleteRequested)
                        {
                            AppSettings.Current.SavedSources.RemoveAll(x => x.Name == src.Name && x.Url == src.Url);
                        }
                        else
                        {
                            src.Name = dlg.SourceName;
                            src.Url = dlg.SourceUrl;
                        }
                        AppSettings.Current.Save();
                        LoadData();
                    }
                }
            }
            catch { }
        }
        void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var checkedItems = _items.Where(x => x.IsSelected).ToList();
                if (checkedItems.Count == 0 && Grid.SelectedItems != null && Grid.SelectedItems.Count > 0)
                {
                    checkedItems = Grid.SelectedItems.Cast<M3uSource>().ToList();
                }
                if (checkedItems.Count > 0)
                {
                    foreach (var it in checkedItems)
                    {
                        _items.Remove(it);
                        AppSettings.Current.SavedSources.RemoveAll(x => x.Name == it.Name && x.Url == it.Url);
                    }
                    AppSettings.Current.Save();
                }
            }
            catch { }
        }
        void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            try { foreach (var i in _items) i.IsSelected = true; Grid.Items.Refresh(); } catch { }
        }
        void BtnInvert_Click(object sender, RoutedEventArgs e)
        {
            try { foreach (var i in _items) i.IsSelected = !i.IsSelected; Grid.Items.Refresh(); } catch { }
        }
        void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { BtnMaximize_Click(sender, e); return; }
            try { DragMove(); } catch { }
        }
        void BtnMinimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        void BtnClose_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
