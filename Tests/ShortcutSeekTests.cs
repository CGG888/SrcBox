using System.Windows.Input;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class ShortcutSeekTests
    {
        // Left/Right must only seek while an archive (replay/timeshift) stream is playing; during
        // live playback they keep cycling the sources of the current channel.
        [TestMethod]
        public void LeftRightKeys_SeekOnlyInArchivePlayback()
        {
            var shell = new MainShellViewModel();
            var vm = shell.ShortcutActions;

            shell.PlaybackMode = PlaybackMode.Live;
            Assert.AreEqual(MainWindowShortcutAction.PreviousSource, vm.ResolveAction(Key.Left, ModifierKeys.None));
            Assert.AreEqual(MainWindowShortcutAction.NextSource, vm.ResolveAction(Key.Right, ModifierKeys.None));

            shell.PlaybackMode = PlaybackMode.Replay;
            Assert.AreEqual(MainWindowShortcutAction.SeekBackward, vm.ResolveAction(Key.Left, ModifierKeys.None));
            Assert.AreEqual(MainWindowShortcutAction.SeekForward, vm.ResolveAction(Key.Right, ModifierKeys.None));

            shell.PlaybackMode = PlaybackMode.Timeshift;
            Assert.AreEqual(MainWindowShortcutAction.SeekBackward, vm.ResolveAction(Key.Left, ModifierKeys.None));
        }

        // Live playback also carries the currently airing EPG program; that must not turn the
        // arrow keys into (useless) seeks on an unseekable live stream.
        [TestMethod]
        public void LeftRightKeys_DoNotSeekDuringLivePlayback_WithEpgProgram()
        {
            var shell = new MainShellViewModel();
            var vm = shell.ShortcutActions;

            shell.PlaybackMode = PlaybackMode.Live;
            shell.CurrentPlayingProgram = new LibmpvIptvClient.Models.EpgProgram
            {
                Title = "新闻联播",
                Start = System.DateTime.Now.AddMinutes(-10),
                End = System.DateTime.Now.AddMinutes(20)
            };

            Assert.AreEqual(MainWindowShortcutAction.PreviousSource, vm.ResolveAction(Key.Left, ModifierKeys.None));
            Assert.AreEqual(MainWindowShortcutAction.NextSource, vm.ResolveAction(Key.Right, ModifierKeys.None));
        }

        // Regression guard: executing an archive seek without a player/channel must stay a no-op
        // instead of throwing from the shortcut path.
        [TestMethod]
        public void ArchiveSeek_WithoutChannel_DoesNotThrow()
        {
            var shell = new MainShellViewModel();
            var vm = shell.ShortcutActions;
            shell.PlaybackMode = PlaybackMode.Replay;

            vm.ExecuteAction(vm.ResolveAction(Key.Left, ModifierKeys.None));
            vm.ExecuteAction(vm.ResolveAction(Key.Right, ModifierKeys.None));
        }
    }
}
