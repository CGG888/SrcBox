using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using LibmpvIptvClient.Services.WebRemote.Voice;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Shows what the offline speech model download is doing. A quarter of a gigabyte arrives over the phone's
    /// Wi-Fi, so waiting with no window is not acceptable: this says which phase it is in, how much arrived,
    /// how fast and how much is left, and it can be stopped and resumed later.
    /// </summary>
    public partial class VoiceModelDownloadWindow : Window
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _finished;
        private bool _closed;

        public VoiceModelDownloadWindow()
        {
            InitializeComponent();

            TxtIntro.Text = string.Format(
                CultureInfo.CurrentCulture,
                Helpers.Localizer.S(
                    "UI_WebRemote_VoiceModelDialog_Intro",
                    "The offline speech model is about {0} MB and is downloaded once."),
                VoiceModelStore.TotalMegabytes);
            TxtStage.Text = Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Connecting", "Connecting...");

            Loaded += (_, _) => Start();
        }

        /// <summary>True when every file of the model is on disk and matches its published checksum.</summary>
        public bool Succeeded { get; private set; }

        /// <summary>True when the download was stopped by hand; what arrived is kept for the next attempt.</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>Why the download failed, or null when it did not.</summary>
        public string? Failure { get; private set; }

        private async void Start()
        {
            var progress = new Progress<VoiceModelProgress>(UpdateProgress);
            try
            {
                await VoiceModelStore.DownloadAsync(null, progress, _cancellation.Token);
                Succeeded = true;
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
            }
            catch (Exception ex)
            {
                Failure = ex.Message;
            }

            Finish();
        }

        private void UpdateProgress(VoiceModelProgress progress)
        {
            if (_closed) return;

            PbProgress.Value = progress.Percent;
            TxtStage.Text = progress.Phase switch
            {
                VoiceModelPhase.Connecting => Helpers.Localizer.S(
                    "UI_WebRemote_VoiceModelDialog_Connecting", "Connecting..."),
                VoiceModelPhase.Verifying => string.Format(
                    CultureInfo.CurrentCulture,
                    Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Verifying", "Verifying ({0}%)"),
                    progress.Percent),
                VoiceModelPhase.Completed => Helpers.Localizer.S(
                    "UI_WebRemote_VoiceModelDialog_Completed", "Download finished."),
                _ => string.Format(
                    CultureInfo.CurrentCulture,
                    Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Downloading", "Downloading ({0}%)"),
                    progress.Percent)
            };

            TxtDetail.Text = progress.FileIndex > 0 && progress.FileName.Length > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_File", "File {0}/{1}: {2}"),
                    progress.FileIndex, progress.FileCount, progress.FileName)
                : string.Empty;

            var bytes = string.Format(
                CultureInfo.CurrentCulture,
                Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Bytes", "{0} / {1}"),
                FormatMegabytes(progress.BytesDone), FormatMegabytes(progress.BytesTotal));

            TxtRate.Text = progress.BytesPerSecond > 0
                ? bytes + " · " + string.Format(
                    CultureInfo.CurrentCulture,
                    Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Rate", "avg {0}/s · {1} left"),
                    FormatMegabytes(progress.BytesPerSecond), FormatRemaining(progress.Remaining))
                : bytes;
        }

        private void Finish()
        {
            if (_closed) return;

            _finished = true;
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnClose.Visibility = Visibility.Visible;

            if (Succeeded)
            {
                PbProgress.Value = 100;
                TxtStage.Text = Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Completed", "Download finished.");
                TxtRate.Text = string.Empty;
                return;
            }

            TxtStage.Text = Helpers.Localizer.S("UI_WebRemote_VoiceModelDialog_Failed", "Download failed");
            TxtDetail.Text = Failure ?? string.Empty;
            TxtRate.Text = WasCancelled
                ? Helpers.Localizer.S(
                    "UI_WebRemote_VoiceModelDialog_Cancelled",
                    "Stopped; what arrived is kept, and the next attempt continues from it.")
                : string.Empty;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => _cancellation.Cancel();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing the window stops the download; whatever arrived stays as a .part file to resume from.
            if (!_finished) _cancellation.Cancel();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _cancellation.Dispose();
            base.OnClosed(e);
        }

        private static string FormatMegabytes(double bytes) =>
            string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / (1024d * 1024d));

        private static string FormatRemaining(TimeSpan? remaining)
        {
            if (remaining == null) return "--:--";

            var span = remaining.Value;
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }
}
