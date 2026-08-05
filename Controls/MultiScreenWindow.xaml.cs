using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using LibmpvIptvClient.Models;
using WF = System.Windows.Forms;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient.Controls
{
    public partial class MultiScreenWindow : Window
    {
        private int _screenCount;
        private int _cols;
        private int _rows;
        private int _focusedIndex = -1;
        private MpvInterop[] _players;
        private Border[] _borders;
        private Channel[] _channels;
        private WF.Panel[] _panels;
        private System.Windows.Controls.TextBlock[] _screenNumbers;
        private WF.Label[] _numberLabels;
        private Func<IReadOnlyList<Channel>>? _getChannelsCallback;
        private List<ChannelGroupData> _channelGroups = new();
        private double _volume = 60;
        private bool _isFullscreen = false;
        private System.Windows.Threading.DispatcherTimer? _topBarTimer;
        private System.Windows.Threading.DispatcherTimer? _mousePollTimer;

        public MultiScreenWindow(int screenCount, Func<IReadOnlyList<Channel>>? getChannelsCallback, List<ChannelGroupData>? channelGroups = null)
        {
            InitializeComponent();
            _screenCount = screenCount;
            _getChannelsCallback = getChannelsCallback;
            _channelGroups = channelGroups ?? new();
            _players = new MpvInterop[screenCount];
            _borders = new Border[screenCount];
            _channels = new Channel[screenCount];
            _panels = new WF.Panel[screenCount];
            _screenNumbers = new System.Windows.Controls.TextBlock[screenCount];
            _numberLabels = new WF.Label[screenCount];

            SetupGrid();
            Loaded += OnLoaded;
            Activated += OnActivated;
            MouseMove += OnMouseMove;
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            CheckShowTopBar();
        }

        private void Overlay_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isFullscreen) return;

            var mousePos = WF.Control.MousePosition;
            var topBarRect = new System.Drawing.Rectangle(
                (int)Left, (int)Top, (int)Width, 28);

            if (topBarRect.Contains(mousePos))
            {
                TopBarBorder.Visibility = Visibility.Visible;
                _topBarTimer?.Stop();
            }
        }

        private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isFullscreen) return;

            var mousePos = WF.Control.MousePosition;
            var topBarRect = new System.Drawing.Rectangle(
                (int)Left, (int)Top, (int)Width, 28);

            if (topBarRect.Contains(mousePos))
            {
                TopBarBorder.Visibility = Visibility.Visible;
                _topBarTimer?.Stop();
            }
            else if (TopBarBorder.Visibility == Visibility.Visible)
            {
                TopBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void CheckShowTopBar()
        {
            if (!_isFullscreen) return;

            var mousePos = WF.Control.MousePosition;
            var topBarRect = new System.Drawing.Rectangle(
                (int)Left, (int)Top, (int)Width, 28);

            if (topBarRect.Contains(mousePos))
            {
                TopBarBorder.Visibility = Visibility.Visible;
                _topBarTimer?.Stop();
            }
            else if (TopBarBorder.Visibility == Visibility.Visible)
            {
                TopBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void OnActivated(object? sender, EventArgs e)
        {
            Focus();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            CreatePlayers();
            Focus();
        }

        private void SetupGrid()
        {
            switch (_screenCount)
            {
                case 4: _cols = 2; _rows = 2; break;
                case 6: _cols = 3; _rows = 2; break;
                case 9: _cols = 3; _rows = 3; break;
                default: _cols = 2; _rows = 2; break;
            }

            for (int i = 0; i < _rows; i++)
            {
                MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            for (int j = 0; j < _cols; j++)
            {
                MainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int i = 0; i < _screenCount; i++)
            {
                int row = i / _cols;
                int col = i % _cols;

                var panel = new WF.Panel 
                { 
                    BackColor = System.Drawing.Color.Black, 
                    Dock = WF.DockStyle.Fill 
                };
                panel.MouseDown += Panel_MouseDown;
                panel.MouseUp += Panel_MouseUp;
                panel.MouseMove += Panel_MouseMove;

                var screenNumberLabel = new WF.Label
                {
                    Text = (i + 1).ToString(),
                    ForeColor = System.Drawing.Color.FromArgb(180, 200, 200, 200),
                    Font = new System.Drawing.Font("Arial", 36, System.Drawing.FontStyle.Bold),
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Dock = WF.DockStyle.None,
                    Size = new System.Drawing.Size(60, 60),
                    Location = new System.Drawing.Point(0, 0),
                    Tag = i,
                    BackColor = System.Drawing.Color.Transparent
                };
                screenNumberLabel.Paint += (s, e) =>
                {
                    if (s is WF.Label lbl && lbl.Tag is int idx)
                    {
                        if (_channels[idx] == null)
                        {
                            lbl.Visible = true;
                        }
                        else
                        {
                            lbl.Visible = false;
                        }
                    }
                };

                var border = new Border 
                { 
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)),
                    Background = System.Windows.Media.Brushes.Black,
                    Tag = i
                };

                var host = new WindowsFormsHost { Child = panel };
                host.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                host.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;

                var overlay = new Border 
                { 
                    Background = System.Windows.Media.Brushes.Transparent,
                    Tag = i,
                    IsHitTestVisible = true
                };
                overlay.MouseEnter += Overlay_MouseEnter;
                overlay.MouseMove += Overlay_MouseMove;

                var contentGrid = new System.Windows.Controls.Grid();
                contentGrid.Children.Add(host);
                contentGrid.Children.Add(overlay);
                border.Child = contentGrid;

                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                MainGrid.Children.Add(border);

                _panels[i] = panel;
                _borders[i] = border;
                _screenNumbers[i] = new System.Windows.Controls.TextBlock { Tag = i };
                _numberLabels[i] = screenNumberLabel;
                panel.Controls.Add(screenNumberLabel);
            }
        }

        private void Panel_MouseDown(object? sender, WF.MouseEventArgs e)
        {
            if (sender is WF.Panel panel)
            {
                int index = Array.IndexOf(_panels, panel);
                if (index >= 0)
                {
                    SetFocus(index);
                }
            }
        }

        private void Panel_MouseUp(object? sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Right)
            {
                if (sender is WF.Panel panel)
                {
                    int index = Array.IndexOf(_panels, panel);
                    if (index >= 0)
                    {
                        SetFocus(index);
                        ShowContextMenu(index, e.Location);
                    }
                }
            }
        }

        private void Panel_MouseMove(object? sender, WF.MouseEventArgs e)
        {
            if (!_isFullscreen) return;

            var mousePos = WF.Control.MousePosition;
            var topBarRect = new System.Drawing.Rectangle(
                (int)Left, (int)Top, (int)Width, 28);

            if (topBarRect.Contains(mousePos))
            {
                TopBarBorder.Visibility = Visibility.Visible;
                _topBarTimer?.Stop();
            }
            else if (TopBarBorder.Visibility == Visibility.Visible)
            {
                TopBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void TopBarBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _topBarTimer?.Stop();
        }

        private void TopBarBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isFullscreen && TopBarBorder.Visibility == Visibility.Visible)
            {
                TopBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowContextMenu(int screenIndex, System.Drawing.Point position)
        {
            var menu = new ContextMenuStrip();

            var channelList = GetChannelList();

            var playControl = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_PlayControl")?.ToString() ?? "播放控制");
            playControl.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_PlayPause")?.ToString() ?? "播放/暂停", null, (s, e) => TogglePlayPause(screenIndex)));
            playControl.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_Stop")?.ToString() ?? "停止", null, (s, e) => Stop(screenIndex)));
            playControl.DropDownItems.Add(new ToolStripSeparator());
            playControl.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_Mute")?.ToString() ?? "静音", null, (s, e) => ToggleMute(screenIndex)));
            playControl.DropDownItems.Add(new ToolStripSeparator());
            playControl.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_VolumeUp")?.ToString() ?? "音量 +", null, (s, e) => AdjustVolume(screenIndex, 10)));
            playControl.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_VolumeDown")?.ToString() ?? "音量 -", null, (s, e) => AdjustVolume(screenIndex, -10)));
            playControl.DropDownItems.Add(new ToolStripMenuItem(string.Format(System.Windows.Application.Current.FindResource("MultiScreen_CurrentVolume")?.ToString() ?? "当前音量: {0}%", (int)_volume), null) { Enabled = false });
            menu.Items.Add(playControl);

            var channelMenu = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_SwitchChannel")?.ToString() ?? "切换频道");
            if (_channelGroups.Count == 0)
            {
                foreach (var ch in channelList)
                {
                    var item = new ToolStripMenuItem(ch.Name, null, (s, e) => PlayChannel(screenIndex, ch));
                    if (_channels[screenIndex]?.Name == ch.Name)
                        item.Checked = true;
                    channelMenu.DropDownItems.Add(item);
                }
            }
            else
            {
                foreach (var group in _channelGroups)
                {
                    var groupItem = new ToolStripMenuItem(group.Name);
                    foreach (var ch in group.Channels)
                    {
                        var chItem = new ToolStripMenuItem(ch.Name, null, (s, e) => PlayChannel(screenIndex, ch));
                        if (_channels[screenIndex]?.Name == ch.Name)
                            chItem.Checked = true;
                        groupItem.DropDownItems.Add(chItem);
                    }
                    channelMenu.DropDownItems.Add(groupItem);
                }
            }
            menu.Items.Add(channelMenu);

            var channelNav = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_ChannelNav")?.ToString() ?? "频道切换");
            channelNav.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_PrevChannel")?.ToString() ?? "上一频道", null, (s, e) => PrevChannel(screenIndex)));
            channelNav.DropDownItems.Add(new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_NextChannel")?.ToString() ?? "下一频道", null, (s, e) => NextChannel(screenIndex)));
            menu.Items.Add(channelNav);

            var ratioMenu = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_AspectRatio")?.ToString() ?? "画面比例");
            var ratios = new (string label, string value)[] { 
                (System.Windows.Application.Current.FindResource("MultiScreen_AspectDefault")?.ToString() ?? "默认", "default"), 
                ("16:9", "16:9"), 
                ("4:3", "4:3"), 
                (System.Windows.Application.Current.FindResource("MultiScreen_AspectStretch")?.ToString() ?? "拉伸", "stretch"), 
                (System.Windows.Application.Current.FindResource("MultiScreen_AspectFill")?.ToString() ?? "填充", "fill"), 
                (System.Windows.Application.Current.FindResource("MultiScreen_AspectCrop")?.ToString() ?? "裁剪", "crop") 
            };
            foreach (var (label, value) in ratios)
            {
                var ratioItem = new ToolStripMenuItem(label, null, (s, e) => SetAspectRatio(screenIndex, value));
                ratioMenu.DropDownItems.Add(ratioItem);
            }
            menu.Items.Add(ratioMenu);

            menu.Items.Add(new ToolStripSeparator());

            var closeItem = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_CloseThisScreen")?.ToString() ?? "关闭此屏幕", null, (s, e) => CloseScreen(screenIndex));
            menu.Items.Add(closeItem);

            var resetAllItem = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_ResetAll")?.ToString() ?? "全部重置", null, (s, e) => ResetAll(screenIndex));
            menu.Items.Add(resetAllItem);

            menu.Items.Add(new ToolStripSeparator());

            if (_isFullscreen)
            {
                var exitFullscreenItem = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_ExitFullscreen")?.ToString() ?? "退出全屏", null, (s, e) => ExitFullscreen());
                menu.Items.Add(exitFullscreenItem);
            }

            var exitItem = new ToolStripMenuItem(System.Windows.Application.Current.FindResource("MultiScreen_ExitMultiScreen")?.ToString() ?? "退出多屏", null, (s, e) => Close());
            menu.Items.Add(exitItem);

            menu.Show(_panels[screenIndex], position);
        }

        private IReadOnlyList<Channel> GetChannelList()
        {
            if (_getChannelsCallback != null)
            {
                return _getChannelsCallback();
            }
            return Array.Empty<Channel>();
        }

        private void CreatePlayers()
        {
            for (int i = 0; i < _screenCount; i++)
            {
                var mpv = new MpvInterop();
                mpv.Create();
                mpv.SetSettings(AppSettings.Current);
                mpv.SetWid(_panels[i].Handle);
                mpv.Initialize();
                mpv.SetVolume(_volume);
                mpv.Mute(true);
                _players[i] = mpv;
            }
        }

        private void PlayChannel(int screenIndex, Channel channel)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            
            _channels[screenIndex] = channel;
            var url = channel.Sources.Count > 0 ? channel.Sources[0].Url : "";
            if (!string.IsNullOrEmpty(url))
            {
                _players[screenIndex]?.LoadFile(url);
            }
            
            if (_numberLabels[screenIndex] != null)
            {
                _numberLabels[screenIndex].Visible = false;
            }
            
            if (_focusedIndex == -1)
            {
                SetFocus(screenIndex);
            }
        }

        private void TogglePlayPause(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            _players[screenIndex]?.Pause(false);
        }

        private void Stop(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            _players[screenIndex]?.Stop();
        }

        private void ToggleMute(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            var current = _players[screenIndex]?.GetString("mute");
            var isMuted = string.Equals(current, "yes", StringComparison.OrdinalIgnoreCase);
            _players[screenIndex]?.Mute(!isMuted);
        }

        private void AdjustVolume(int screenIndex, int delta)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            _volume = Math.Max(0, Math.Min(100, _volume + delta));
            _players[screenIndex]?.SetVolume(_volume);
        }

        private void SetAspectRatio(int screenIndex, string ratio)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            _players[screenIndex]?.SetAspectRatio(ratio);
        }

        private void PrevChannel(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            var current = _channels[screenIndex];
            var all = GetChannelList().ToList();
            if (all.Count == 0) return;
            
            int idx = current != null ? all.IndexOf(current) : -1;
            int prevIdx = idx <= 0 ? all.Count - 1 : idx - 1;
            PlayChannel(screenIndex, all[prevIdx]);
        }

        private void NextChannel(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            var current = _channels[screenIndex];
            var all = GetChannelList().ToList();
            if (all.Count == 0) return;
            
            int idx = current != null ? all.IndexOf(current) : -1;
            int nextIdx = idx >= all.Count - 1 ? 0 : idx + 1;
            PlayChannel(screenIndex, all[nextIdx]);
        }

        private void CloseScreen(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;
            _players[screenIndex]?.Stop();
            _players[screenIndex]?.Mute(true);
            _channels[screenIndex] = null;
            if (_numberLabels[screenIndex] != null)
            {
                _numberLabels[screenIndex].Visible = true;
            }
        }

        private void ResetAll(int currentIndex)
        {
            for (int i = 0; i < _screenCount; i++)
            {
                _players[i]?.Stop();
                _channels[i] = null;
                if (_numberLabels[i] != null)
                {
                    _numberLabels[i].Visible = true;
                }
            }
            _focusedIndex = -1;
            if (currentIndex >= 0 && currentIndex < _screenCount)
            {
                SetFocus(currentIndex);
            }
        }

        private void SetFocus(int screenIndex)
        {
            if (screenIndex < 0 || screenIndex >= _screenCount) return;

            if (_focusedIndex >= 0 && _focusedIndex < _screenCount && _borders[_focusedIndex] != null)
            {
                _borders[_focusedIndex].BorderThickness = new Thickness(1);
                _borders[_focusedIndex].BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255));
                if (_players[_focusedIndex] != null)
                    _players[_focusedIndex].Mute(true);
            }

            _focusedIndex = screenIndex;
            if (_focusedIndex >= 0 && _focusedIndex < _screenCount && _borders[_focusedIndex] != null)
            {
                _borders[_focusedIndex].BorderThickness = new Thickness(2);
                _borders[_focusedIndex].BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0));
                if (_players[_focusedIndex] != null)
                    _players[_focusedIndex].Mute(false);
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
            {
                ExitFullscreen();
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleMaximize();
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = System.Windows.WindowState.Minimized;
        }

        private void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen)
            {
                ExitFullscreen();
            }
            else
            {
                ToggleMaximize();
            }
        }

        private void ToggleMaximize()
        {
            if (WindowState == System.Windows.WindowState.Maximized)
                WindowState = System.Windows.WindowState.Normal;
            else
                WindowState = System.Windows.WindowState.Maximized;
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen)
            {
                ExitFullscreen();
            }
            else
            {
                EnterFullscreen();
            }
        }

        private void EnterFullscreen()
        {
            _isFullscreen = true;
            _previousWindowState = WindowState;
            _previousRestoreBounds = new System.Windows.Rect(Left, Top, Width, Height);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            var screen = WF.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
            WindowState = System.Windows.WindowState.Normal;
            ShowInTaskbar = false;
            TopBarBorder.Visibility = Visibility.Collapsed;

            StartMousePoll();
        }

        private void StartMousePoll()
        {
            _mousePollTimer?.Stop();
            _mousePollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _mousePollTimer.Tick += (s, e) => PollMousePosition();
            _mousePollTimer.Start();
        }

        private void StopMousePoll()
        {
            _mousePollTimer?.Stop();
            _mousePollTimer = null;
        }

        private void PollMousePosition()
        {
            if (!_isFullscreen) return;

            var mousePos = WF.Control.MousePosition;
            var topBarRect = new System.Drawing.Rectangle(
                (int)Left, (int)Top, (int)Width, 28);

            if (topBarRect.Contains(mousePos))
            {
                TopBarBorder.Visibility = Visibility.Visible;
                _topBarTimer?.Stop();
            }
            else if (TopBarBorder.Visibility == Visibility.Visible)
            {
                TopBarBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ExitFullscreen()
        {
            _isFullscreen = false;
            StopMousePoll();
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _previousWindowState;
            Left = _previousRestoreBounds.Left;
            Top = _previousRestoreBounds.Top;
            Width = _previousRestoreBounds.Width;
            Height = _previousRestoreBounds.Height;
            ShowInTaskbar = true;
            TopBarBorder.Visibility = Visibility.Visible;
        }

        private System.Windows.WindowState _previousWindowState = System.Windows.WindowState.Normal;
        private System.Windows.Rect _previousRestoreBounds;

        [DllImport("user32.dll")]
        private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Cleanup()
        {
            for (int i = 0; i < _screenCount; i++)
            {
                try { _players[i]?.Dispose(); } catch { }
                _players[i] = null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Cleanup();
            base.OnClosed(e);
        }
    }
}
