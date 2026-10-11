using System;
using System.Windows;
using System.Windows.Controls;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class MainWindowWindowStateActionsTests
    {
        [TestMethod]
        public void InitialState_ShouldBeWindowed()
        {
            var vm = new MainWindowWindowStateActionsViewModel();
            Assert.IsFalse(vm.IsFullscreen);
            Assert.IsNull(vm.FullscreenWindow);
        }

        [TestMethod]
        public void ToggleFullscreen_WhenAlreadyWindowed_DoesNothing()
        {
            var vm = new MainWindowWindowStateActionsViewModel();

            // Toggling to windowed while already windowed is a no-op: no window is created or touched.
            vm.ToggleFullscreen(false, new FullscreenContext());

            Assert.IsFalse(vm.IsFullscreen);
            Assert.IsNull(vm.FullscreenWindow);
            Assert.IsNull(vm.FullscreenPanel);
        }

        [TestMethod]
        public void Presenter_EnterWithoutWindow_Fails()
        {
            // The presenter must report failure instead of throwing, so the caller can roll back.
            Assert.IsNull(WindowFullscreenPresenter.Enter(null!));
        }
    }
}
