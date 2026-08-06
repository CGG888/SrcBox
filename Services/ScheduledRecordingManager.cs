using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public class ScheduledRecordingManager : IDisposable
    {
        private static readonly Lazy<ScheduledRecordingManager> _lazy = new Lazy<ScheduledRecordingManager>(() => new ScheduledRecordingManager());
        public static ScheduledRecordingManager Instance => _lazy.Value;

        private readonly ConcurrentDictionary<string, ScheduledRecordingInfo> _recordings = new();
        private readonly ConcurrentDictionary<string, BackgroundRecordingInstance> _activeInstances = new();
        private int _maxBackgroundRecordings = 3;
        private bool _disposed;

        public event EventHandler<ScheduledRecordingInfo>? RecordingStarted;
        public event EventHandler<ScheduledRecordingInfo>? RecordingStopped;
        public event EventHandler<ScheduledRecordingInfo>? RecordingUpdated;
        public event EventHandler<(string Id, bool Success, string? Error)>? RecordingCompleted;
        public event EventHandler<string>? StopFrontRecordingRequested;

        public int ActiveFrontRecordingCount => _recordings.Values.Count(r => r.Type == RecordingType.Front && r.Status == ScheduledRecordingStatus.Recording);
        public int ActiveBackRecordingCount => _activeInstances.Count;
        public int MaxBackgroundRecordings => _maxBackgroundRecordings;

        private ScheduledRecordingManager()
        {
            RefreshMaxBackgroundRecordings();
        }

        public void RefreshMaxBackgroundRecordings()
        {
            try
            {
                var config = LibmpvIptvClient.AppSettings.Current?.Recording;
                _maxBackgroundRecordings = config?.MaxBackgroundRecordings ?? 3;
            }
            catch
            {
                _maxBackgroundRecordings = 3;
            }
        }

        public List<ScheduledRecordingInfo> GetAll()
        {
            return _recordings.Values.OrderBy(r => r.ScheduledStart).ToList();
        }

        public List<ScheduledRecordingInfo> GetActive()
        {
            return _recordings.Values.Where(r => r.Status == ScheduledRecordingStatus.Recording || r.Status == ScheduledRecordingStatus.Waiting).OrderBy(r => r.ScheduledStart).ToList();
        }

        public bool IsFrontRecordingActive()
        {
            return _recordings.Values.Any(r => r.Type == RecordingType.Front && (r.Status == ScheduledRecordingStatus.Recording || r.Status == ScheduledRecordingStatus.Waiting));
        }

        public bool IsRealtimeRecordingActive()
        {
            return _recordings.Values.Any(r => r.Type == RecordingType.Realtime && r.Status == ScheduledRecordingStatus.Recording);
        }

        public bool CanStartFrontRecording()
        {
            return !IsFrontRecordingActive() && !IsRealtimeRecordingActive();
        }

        public bool CanStartBackRecording()
        {
            return _activeInstances.Count < _maxBackgroundRecordings;
        }

        public ScheduledRecordingInfo? Get(string id)
        {
            return _recordings.TryGetValue(id, out var info) ? info : null;
        }

        public void Add(ScheduledRecordingInfo info)
        {
            info.StatusLabel = GetStatusLabel(info.Status);
            _recordings[info.Id] = info;
        }

        public void Remove(string id)
        {
            _recordings.TryRemove(id, out _);
        }

        public void UpdateStatus(string id, ScheduledRecordingStatus status, string? errorMessage = null)
        {
            if (_recordings.TryGetValue(id, out var info))
            {
                info.Status = status;
                info.StatusLabel = GetStatusLabel(status);
                if (errorMessage != null)
                    info.ErrorMessage = errorMessage;
                RecordingUpdated?.Invoke(this, info);
            }
        }

        public void UpdateProgress(string id, long sizeBytes, TimeSpan elapsed)
        {
            if (_recordings.TryGetValue(id, out var info))
            {
                info.SizeBytes = sizeBytes;
                info.SizeLabel = FormatSize(sizeBytes);
                RecordingUpdated?.Invoke(this, info);
            }
        }

        public void StartFrontRecording(ScheduledRecordingInfo info, Action<string> onRecordingStarted, Action<string> onRecordingStopped)
        {
            if (!CanStartFrontRecording())
                return;

            info.Type = RecordingType.Front;
            info.Status = ScheduledRecordingStatus.Recording;
            info.ActualStartTime = DateTime.Now;
            info.StatusLabel = GetStatusLabel(info.Status);
            info.SizeLabel = FormatSize(0);

            _recordings[info.Id] = info;
            onRecordingStarted?.Invoke(info.Id);
            RecordingStarted?.Invoke(this, info);
        }

        public string? StartBackRecording(ScheduledRecordingInfo info, Func<string, string, int, BackgroundRecordingInstance> createInstance)
        {
            if (!CanStartBackRecording())
                return null;

            info.Type = RecordingType.Back;
            info.Status = ScheduledRecordingStatus.Recording;
            info.ActualStartTime = DateTime.Now;
            info.StatusLabel = GetStatusLabel(info.Status);
            info.SizeLabel = FormatSize(0);

            _recordings[info.Id] = info;

            var durationSeconds = (int)(info.ScheduledEnd - info.ScheduledStart).TotalSeconds;
            var instance = createInstance(info.Id, info.ChannelId, durationSeconds);

            instance.Progress += (_, e) =>
            {
                UpdateProgress(info.Id, e.SizeBytes, e.Elapsed);
            };

            instance.Completed += (_, e) =>
            {
                info.Status = e.Success ? ScheduledRecordingStatus.Completed : ScheduledRecordingStatus.Failed;
                info.StatusLabel = GetStatusLabel(info.Status);
                info.SizeBytes = e.SizeBytes;
                info.SizeLabel = FormatSize(e.SizeBytes);
                info.ActualEndTime = DateTime.Now;
                if (e.Success && info.ActualStartTime.HasValue)
                    info.ActualDurationMin = (int)(info.ActualEndTime.Value - info.ActualStartTime.Value).TotalMinutes;
                if (!string.IsNullOrEmpty(e.ErrorMessage))
                    info.ErrorMessage = e.ErrorMessage;

                _activeInstances.TryRemove(info.Id, out var _removed1);
                RecordingCompleted?.Invoke(this, (info.Id, e.Success, e.ErrorMessage));
                RecordingStopped?.Invoke(this, info);
                RecordingUpdated?.Invoke(this, info);
            };

            instance.Failed += (_, error) =>
            {
                info.Status = ScheduledRecordingStatus.Failed;
                info.StatusLabel = GetStatusLabel(info.Status);
                info.ErrorMessage = error;

                _activeInstances.TryRemove(info.Id, out var _removed2);
                RecordingCompleted?.Invoke(this, (info.Id, false, error));
                RecordingStopped?.Invoke(this, info);
                RecordingUpdated?.Invoke(this, info);
            };

            _activeInstances[info.Id] = instance;
            instance.Start();

            RecordingStarted?.Invoke(this, info);
            return info.Id;
        }

        public void StopRecording(string id)
        {
            if (_recordings.TryGetValue(id, out var info))
            {
                if (info.Type == RecordingType.Front)
                {
                    info.Status = ScheduledRecordingStatus.Cancelled;
                    info.StatusLabel = GetStatusLabel(info.Status);
                    info.ActualEndTime = DateTime.Now;
                    if (info.ActualStartTime.HasValue)
                        info.ActualDurationMin = (int)(info.ActualEndTime.Value - info.ActualStartTime.Value).TotalMinutes;
                    StopFrontRecordingRequested?.Invoke(this, id);
                    RecordingStopped?.Invoke(this, info);
                    RecordingUpdated?.Invoke(this, info);
                }
                else if (info.Type == RecordingType.Back && _activeInstances.TryGetValue(id, out var instance))
                {
                    instance.Stop();
                }
            }
        }

        public void CancelScheduled(string id)
        {
            if (_recordings.TryGetValue(id, out var info))
            {
                if (info.Status == ScheduledRecordingStatus.Waiting)
                {
                    info.Status = ScheduledRecordingStatus.Cancelled;
                    info.StatusLabel = GetStatusLabel(info.Status);
                    RecordingStopped?.Invoke(this, info);
                    RecordingUpdated?.Invoke(this, info);
                }
            }
        }

        public void RemoveCompleted(string id)
        {
            Remove(id);
        }

        private static string GetStatusLabel(ScheduledRecordingStatus status)
        {
            return status switch
            {
                ScheduledRecordingStatus.Waiting => Helpers.ResxLocalizer.Get("Recording_Waiting", "等待中"),
                ScheduledRecordingStatus.Recording => Helpers.ResxLocalizer.Get("Recording_Recording", "录制中"),
                ScheduledRecordingStatus.Completed => Helpers.ResxLocalizer.Get("Recording_Completed", "已完成"),
                ScheduledRecordingStatus.Failed => Helpers.ResxLocalizer.Get("Recording_Failed", "失败"),
                ScheduledRecordingStatus.Cancelled => Helpers.ResxLocalizer.Get("Recording_Cancelled", "已取消"),
                _ => ""
            };
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var instance in _activeInstances.Values)
            {
                instance.Stop();
                instance.Dispose();
            }

            _activeInstances.Clear();
            _recordings.Clear();
        }
    }
}
