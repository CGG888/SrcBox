using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public class RecordingProgressEventArgs : EventArgs
    {
        public string RecordingId { get; }
        public long SizeBytes { get; }
        public TimeSpan Elapsed { get; }
        public double Progress { get; }

        public RecordingProgressEventArgs(string recordingId, long sizeBytes, TimeSpan elapsed, double progress)
        {
            RecordingId = recordingId;
            SizeBytes = sizeBytes;
            Elapsed = elapsed;
            Progress = progress;
        }
    }

    public class RecordingCompletedEventArgs : EventArgs
    {
        public string RecordingId { get; }
        public string? FilePath { get; }
        public long SizeBytes { get; }
        public TimeSpan Duration { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }

        public RecordingCompletedEventArgs(string recordingId, string? filePath, long sizeBytes, TimeSpan duration, bool success, string? errorMessage = null)
        {
            RecordingId = recordingId;
            FilePath = filePath;
            SizeBytes = sizeBytes;
            Duration = duration;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }

    public class BackgroundRecordingInstance : IDisposable
    {
        private readonly string _id;
        private readonly string _url;
        private readonly string _filePath;
        private readonly int _durationSeconds;
        private readonly CancellationTokenSource _cts;
        private Task? _recordingTask;
        private bool _disposed;

        public event EventHandler<RecordingProgressEventArgs>? Progress;
        public event EventHandler<RecordingCompletedEventArgs>? Completed;
        public event EventHandler<string>? Failed;

        public string Id => _id;
        public string FilePath => _filePath;
        public ScheduledRecordingStatus Status { get; private set; } = ScheduledRecordingStatus.Waiting;
        public DateTime? StartTime { get; private set; }

        public BackgroundRecordingInstance(string id, string url, string filePath, int durationSeconds)
        {
            _id = id;
            _url = url;
            _filePath = filePath;
            _durationSeconds = durationSeconds;
            _cts = new CancellationTokenSource();
        }

        public void Start()
        {
            if (Status == ScheduledRecordingStatus.Recording)
                return;

            Status = ScheduledRecordingStatus.Recording;
            StartTime = DateTime.Now;

            _recordingTask = Task.Run(async () =>
            {
                try
                {
                    using var httpClient = new System.Net.Http.HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(30);

                    var buffer = new byte[81920];
                    var totalBytesRead = 0L;
                    var lastProgressReport = DateTime.MinValue;

                    var response = await httpClient.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, _cts.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    await using var outputStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await using var inputStream = await response.Content.ReadAsStreamAsync(_cts.Token).ConfigureAwait(false);

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        // Check if duration has elapsed
                        if (_durationSeconds > 0)
                        {
                            var elapsed = (DateTime.Now - StartTime.Value).TotalSeconds;
                            if (elapsed >= _durationSeconds)
                            {
                                LibmpvIptvClient.Diagnostics.Logger.Info($"[BackRecord] duration {_durationSeconds}s reached (elapsed={elapsed:F0}s), stopping");
                                break;
                            }
                        }

                        var bytesRead = await inputStream.ReadAsync(buffer, _cts.Token);
                        if (bytesRead == 0)
                            break;

                        await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cts.Token);
                        totalBytesRead += bytesRead;

                        var elapsedTime = DateTime.Now - StartTime.Value;
                        var progress = _durationSeconds > 0 ? Math.Min(1.0, elapsedTime.TotalSeconds / _durationSeconds) : 0;

                        if ((DateTime.Now - lastProgressReport).TotalMilliseconds >= 1000)
                        {
                            Progress?.Invoke(this, new RecordingProgressEventArgs(_id, totalBytesRead, elapsedTime, progress));
                            lastProgressReport = DateTime.Now;
                        }
                    }

                    if (!_cts.Token.IsCancellationRequested)
                    {
                        var finalElapsed = DateTime.Now - StartTime.Value;
                        Completed?.Invoke(this, new RecordingCompletedEventArgs(_id, _filePath, totalBytesRead, finalElapsed, true));
                        Status = ScheduledRecordingStatus.Completed;
                    }
                }
                catch (OperationCanceledException)
                {
                    Status = ScheduledRecordingStatus.Cancelled;
                    Completed?.Invoke(this, new RecordingCompletedEventArgs(_id, _filePath, 0, TimeSpan.Zero, false, "Cancelled"));
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                    Status = ScheduledRecordingStatus.Failed;
                    Completed?.Invoke(this, new RecordingCompletedEventArgs(_id, _filePath, 0, TimeSpan.Zero, false, ex.Message));
                }
            }, _cts.Token);
        }

        public void Stop()
        {
            if (Status != ScheduledRecordingStatus.Recording)
                return;

            _cts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _cts.Dispose();
        }
    }
}
