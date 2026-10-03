using System;
using System.Collections.Generic;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class MainShellTests
    {
        [TestMethod]
        public void Initialization_ShouldInstantiateAllSubViewModels()
        {
            var shell = new MainShellViewModel();

            Assert.IsNotNull(shell.DialogActions, "DialogActions should not be null");
            Assert.IsNotNull(shell.ShortcutActions, "ShortcutActions should not be null");
            Assert.IsNotNull(shell.TitleBarActions, "TitleBarActions should not be null");
            Assert.IsNotNull(shell.PlaybackActions, "PlaybackActions should not be null");
            Assert.IsNotNull(shell.ViewToggleActions, "ViewToggleActions should not be null");
            Assert.IsNotNull(shell.OverlayBindingActions, "OverlayBindingActions should not be null");
            Assert.IsNotNull(shell.OverlayPreviewActions, "OverlayPreviewActions should not be null");
            Assert.IsNotNull(shell.EpgActions, "EpgActions should not be null");
            Assert.IsNotNull(shell.EpgReminderActions, "EpgReminderActions should not be null");
            Assert.IsNotNull(shell.EpgReminderSyncActions, "EpgReminderSyncActions should not be null");
            Assert.IsNotNull(shell.EpgSelectionSyncActions, "EpgSelectionSyncActions should not be null");
            Assert.IsNotNull(shell.ChannelListActions, "ChannelListActions should not be null");
            Assert.IsNotNull(shell.ChannelInteractionActions, "ChannelInteractionActions should not be null");
            Assert.IsNotNull(shell.ChannelPlaybackSyncActions, "ChannelPlaybackSyncActions should not be null");
            Assert.IsNotNull(shell.PlaybackStatusOverlaySyncActions, "PlaybackStatusOverlaySyncActions should not be null");
            Assert.IsNotNull(shell.PlaybackSpeedOverlaySyncActions, "PlaybackSpeedOverlaySyncActions should not be null");
            Assert.IsNotNull(shell.VolumeMuteOverlaySyncActions, "VolumeMuteOverlaySyncActions should not be null");
            Assert.IsNotNull(shell.PlaybackPauseOverlaySyncActions, "PlaybackPauseOverlaySyncActions should not be null");
            Assert.IsNotNull(shell.RecordingActions, "RecordingActions should not be null");
            Assert.IsNotNull(shell.HistoryActions, "HistoryActions should not be null");
            Assert.IsNotNull(shell.TimeshiftOverlaySyncActions, "TimeshiftOverlaySyncActions should not be null");
            Assert.IsNotNull(shell.SourceLoader, "SourceLoader should not be null");
            Assert.IsNotNull(shell.DragDropActions, "DragDropActions should not be null");
            Assert.IsNotNull(shell.WindowStateActions, "WindowStateActions should not be null");
        }

        [TestMethod]
        public void VolumeProperty_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            bool notified = false;
            shell.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainShellViewModel.Volume)) notified = true;
            };
            
            shell.Volume = 80;
            Assert.IsTrue(notified);
            Assert.AreEqual(80, shell.Volume);
        }

        [TestMethod]
        public void IsMutedProperty_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            bool notified = false;
            shell.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(MainShellViewModel.IsMuted)) notified = true;
            };
            
            shell.IsMuted = true;
            Assert.IsTrue(notified);
            Assert.IsTrue(shell.IsMuted);
        }

        [TestMethod]
        public void IsPausedProperty_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            bool notified = false;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainShellViewModel.IsPaused)) notified = true;
            };

            shell.IsPaused = true;
            Assert.IsTrue(notified);
            Assert.IsTrue(shell.IsPaused);
        }

        [TestMethod]
        public void PlaybackSpeedProperty_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            bool notified = false;
            shell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainShellViewModel.PlaybackSpeed)) notified = true;
            };

            shell.PlaybackSpeed = 1.5;
            Assert.IsTrue(notified);
            Assert.AreEqual(1.5, shell.PlaybackSpeed);
        }
        [TestMethod]
        public void TimeshiftProperties_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            // Timeshift can only be enabled with a player engine and a playing url; provide both so the
            // test exercises the real accept path instead of silently keeping the flag false.
            shell.InjectServices(new LibmpvIptvClient.Services.EpgService(), new FakePlayerEngine(), null!);
            shell.CurrentUrl = "http://example.com/live";
            var notifiedProps = new System.Collections.Generic.HashSet<string>();
            var oldEnabled = AppSettings.Current.Timeshift.Enabled;
            var oldFormat = AppSettings.Current.Timeshift.UrlFormat;
            
            shell.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName != null) notifiedProps.Add(e.PropertyName);
            };
            try
            {
                AppSettings.Current.Timeshift.Enabled = true;
                AppSettings.Current.Timeshift.UrlFormat = "http://example.com?start={start}&end={end}";

                shell.IsTimeshiftActive = true;
                shell.TimeshiftMin = DateTime.MinValue.AddHours(1);
                shell.TimeshiftMax = DateTime.MaxValue.AddHours(-1);
                shell.TimeshiftStart = DateTime.Now;
                shell.TimeshiftCursorSec = 123.45;

                Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.IsTimeshiftActive)));
                Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.TimeshiftMin)));
                Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.TimeshiftMax)));
                Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.TimeshiftStart)));
                Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.TimeshiftCursorSec)));
                
                Assert.IsTrue(shell.IsTimeshiftActive);
                Assert.AreEqual(123.45, shell.TimeshiftCursorSec);
            }
            finally
            {
                AppSettings.Current.Timeshift.Enabled = oldEnabled;
                AppSettings.Current.Timeshift.UrlFormat = oldFormat;
            }
        }

        [TestMethod]
        public void DataProperties_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            var notifiedProps = new HashSet<string>();
            shell.PropertyChanged += (s, e) => { if (e.PropertyName != null) notifiedProps.Add(e.PropertyName); };

            var list = new List<Channel>();
            shell.Channels = list;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.Channels)));
            Assert.AreSame(list, shell.Channels);

            var ch = new Channel { Name = "Test" };
            shell.CurrentChannel = ch;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.CurrentChannel)));
            Assert.AreSame(ch, shell.CurrentChannel);

            var srcs = new List<Source>();
            shell.CurrentSources = srcs;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.CurrentSources)));
            Assert.AreSame(srcs, shell.CurrentSources);

            shell.SelectedGroup = "News";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.SelectedGroup)));
            Assert.AreEqual("News", shell.SelectedGroup);
        }

        [TestMethod]
        public void UiProperties_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            var notifiedProps = new HashSet<string>();
            shell.PropertyChanged += (s, e) => { if (e.PropertyName != null) notifiedProps.Add(e.PropertyName); };

            shell.IsDrawerCollapsed = false;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.IsDrawerCollapsed)));
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.DrawerGridLength)));
            Assert.IsFalse(shell.IsDrawerCollapsed);
            Assert.AreEqual(380.0, shell.DrawerGridLength.Value);

            shell.DrawerWidth = 450;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.DrawerWidth)));
            // DrawerGridLength should be notified again
            Assert.AreEqual(450.0, shell.DrawerGridLength.Value);

            shell.IsDrawerCollapsed = true;
            Assert.AreEqual(0.0, shell.DrawerGridLength.Value);

            shell.PlaybackStatusText = "Buffering...";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.PlaybackStatusText)));
            Assert.AreEqual("Buffering...", shell.PlaybackStatusText);

            var brush = System.Windows.Media.Brushes.Red;
            shell.PlaybackStatusBrush = brush;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.PlaybackStatusBrush)));
            Assert.AreSame(brush, shell.PlaybackStatusBrush);

            shell.CurrentAspect = "16:9";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.CurrentAspect)));
            Assert.AreEqual("16:9", shell.CurrentAspect);
        }

        [TestMethod]
        public void MiscProperties_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            var notifiedProps = new HashSet<string>();
            shell.PropertyChanged += (s, e) => { if (e.PropertyName != null) notifiedProps.Add(e.PropertyName); };

            shell.CurrentUrl = "http://test.com";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.CurrentUrl)));
            Assert.AreEqual("http://test.com", shell.CurrentUrl);

            shell.IsSeeking = true;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.IsSeeking)));
            Assert.IsTrue(shell.IsSeeking);

            var dates = new List<DateTime> { DateTime.Today };
            shell.AvailableDates = dates;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.AvailableDates)));
            Assert.AreSame(dates, shell.AvailableDates);

            var tomorrow = DateTime.Today.AddDays(1);
            shell.CurrentEpgDate = tomorrow;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.CurrentEpgDate)));
            Assert.AreEqual(tomorrow, shell.CurrentEpgDate);
        }

        [TestMethod]
        public void PlaybackControlProperties_ShouldNotifyChanges()
        {
            var shell = new MainShellViewModel();
            var notifiedProps = new HashSet<string>();
            shell.PropertyChanged += (s, e) => { if (e.PropertyName != null) notifiedProps.Add(e.PropertyName); };

            shell.ElapsedTimeText = "01:23";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.ElapsedTimeText)));
            Assert.AreEqual("01:23", shell.ElapsedTimeText);

            shell.DurationText = "10:00";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.DurationText)));
            Assert.AreEqual("10:00", shell.DurationText);

            shell.SpeedText = "2.0x";
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.SpeedText)));
            Assert.AreEqual("2.0x", shell.SpeedText);

            shell.IsSpeedEnabled = true;
            Assert.IsTrue(notifiedProps.Contains(nameof(MainShellViewModel.IsSpeedEnabled)));
            Assert.IsTrue(shell.IsSpeedEnabled);
        }
    }
}
