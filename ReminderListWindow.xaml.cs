using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class ReminderListWindow : Window
    {
        public static event Action? RemindersChanged;
        private ObservableCollection<ScheduledReminder> _items = new ObservableCollection<ScheduledReminder>();
        public ReminderListWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => { try { LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { } };
            LoadData();
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
                var list = AppSettings.Current.ScheduledReminders ?? new System.Collections.Generic.List<ScheduledReminder>();
                var q = list.AsEnumerable();
                if (CbOnlyFuture?.IsChecked == true)
                {
                    var now = DateTime.UtcNow;
                    q = q.Where(x => x.StartAtUtc >= now || (x.StartAtUtc < now && !x.Completed));
                }
                _items = new ObservableCollection<ScheduledReminder>(q.OrderBy(x => x.StartAtUtc));
                Grid.ItemsSource = _items;
                try
                {
                    var title = LibmpvIptvClient.Helpers.ResxLocalizer.Get("ReminderList_Title", "预约管理");
                    var summary = string.Format("{0}: {1}  {2}: {3}",
                        LibmpvIptvClient.Helpers.ResxLocalizer.Get("UI_Total", "共"),
                        _items.Count,
                        LibmpvIptvClient.Helpers.ResxLocalizer.Get("UI_Future", "未来"),
                        _items.Count(i => i.StartAtUtc > DateTime.UtcNow));
                    TxtSummary.Text = summary;
                    this.Title = title;
                }
                catch
                {
                    TxtSummary.Text = $"共 {_items.Count} 条（未来 {_items.Count(i => i.StartAtUtc > DateTime.UtcNow)} 条）";
                }
            }
            catch { }
        }
        void FilterChanged(object sender, RoutedEventArgs e) => LoadData();
        void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is ScheduledReminder r)
                {
                    var dlg = new ReminderDialog(r.ChannelName, r.Note, r.StartAtUtc.ToLocalTime()) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = this.Topmost };
                    if (dlg.ShowDialog() == true)
                    {
                        r.PreAlertSeconds = dlg.PreAlertSeconds;
                        r.Action = dlg.Action;
                        try { r.PlayMode = dlg.PlayMode; } catch { }
                        try { r.RecordMode = dlg.RecordMode; } catch { }
                        try { r.RecordDurationMin = dlg.RecordDurationMin; } catch { }
                        AppSettings.Current.Save();
                        LibmpvIptvClient.Services.ReminderService.Instance.Start();
                        LoadData();
                        try { RemindersChanged?.Invoke(); } catch { }
                    }
                }
            }
            catch { }
        }
        void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 优先按复选框 Selected 批量删除
                var checkedItems = _items.Where(x => x.Selected).ToList();
                if (checkedItems.Count > 0)
                {
                    foreach (var r in checkedItems)
                    {
                        _items.Remove(r);
                        AppSettings.Current.ScheduledReminders.RemoveAll(x => x.Id == r.Id);
                    }
                    AppSettings.Current.Save();
                    LibmpvIptvClient.Services.ReminderService.Instance.Start();
                    LoadData();
                    try { RemindersChanged?.Invoke(); } catch { }
                }
                else if (Grid.SelectedItems != null && Grid.SelectedItems.Count > 0)
                {
                    var sel = Grid.SelectedItems.Cast<ScheduledReminder>().ToList();
                    foreach (var r in sel)
                    {
                        _items.Remove(r);
                        AppSettings.Current.ScheduledReminders.RemoveAll(x => x.Id == r.Id);
                    }
                    AppSettings.Current.Save();
                    LibmpvIptvClient.Services.ReminderService.Instance.Start();
                    LoadData();
                    try { RemindersChanged?.Invoke(); } catch { }
                }
            }
            catch { }
        }
        void BtnTestSuccess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is ScheduledReminder r)
                {
                    var win = new ReminderToastWindow(r.ChannelId, r.ChannelName, r.Note, r.StartAtUtc.ToLocalTime(), r.ChannelLogo, false);
                    win.Owner = this;
                    win.Show();
                }
            }
            catch { }
        }
        void BtnTestDue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is ScheduledReminder r)
                {
                    var win = new ReminderToastWindow(r.ChannelId, r.ChannelName, r.Note, r.StartAtUtc.ToLocalTime(), r.ChannelLogo, true);
                    win.Owner = this;
                    win.Show();
                }
            }
            catch { }
        }
        void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            try { foreach (var i in _items) i.Selected = true; Grid.Items.Refresh(); } catch { }
        }
        void BtnInvert_Click(object sender, RoutedEventArgs e)
        {
            try { foreach (var i in _items) i.Selected = !i.Selected; Grid.Items.Refresh(); } catch { }
        }
    }
}
