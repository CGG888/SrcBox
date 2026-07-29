using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace LibmpvIptvClient.Diagnostics
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5
    }

    public static class Logger
    {
        public static event Action<string>? OnMessage;
        public static event Action<LogLevel, string>? OnMessageLeveled;

        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public static bool DebugEnabled { get; set; } = false;

        private static readonly object _fileLock = new object();
        private static string _logDirectory = "";
        private static string _logFilePath = "";
        private static int _logFileIndex = 1;
        private static long _currentFileSize = 0;

        private const long MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024;
        private const int MAX_LOG_FILES = 5;

        public static void InitializeFileLogging(string logDir)
        {
            if (string.IsNullOrEmpty(logDir)) return;

            _logDirectory = logDir;
            if (!Directory.Exists(_logDirectory))
            {
                try { Directory.CreateDirectory(_logDirectory); } catch { return; }
            }

            FindNextLogFileIndex();
            _logFilePath = Path.Combine(_logDirectory, $"iptv_{DateTime.Now:yyyyMMdd}_{_logFileIndex:D2}.log");
            _currentFileSize = 0;

            WriteToFile($"[SYSTEM] 日志文件创建: {_logFilePath}");
        }

        private static void FindNextLogFileIndex()
        {
            _logFileIndex = 1;
            if (!Directory.Exists(_logDirectory)) return;

            var existingFiles = Directory.GetFiles(_logDirectory, "iptv_*.log");
            foreach (var f in existingFiles)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var parts = name.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int idx))
                {
                    if (idx >= _logFileIndex) _logFileIndex = idx + 1;
                }
            }
        }

        private static void WriteToFile(string message)
        {
            if (string.IsNullOrEmpty(_logDirectory)) return;

            lock (_fileLock)
            {
                try
                {
                    if (_currentFileSize >= MAX_FILE_SIZE_BYTES)
                    {
                        _logFileIndex++;
                        _logFilePath = Path.Combine(_logDirectory, $"iptv_{DateTime.Now:yyyyMMdd}_{_logFileIndex:D2}.log");
                        _currentFileSize = 0;

                        CleanupOldLogFiles();
                    }

                    var bytes = System.Text.Encoding.UTF8.GetBytes(message + Environment.NewLine);
                    using (var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                    }
                    _currentFileSize += bytes.Length;
                }
                catch { }
            }
        }

        private static void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logDirectory)) return;

                var files = Directory.GetFiles(_logDirectory, "iptv_*.log");
                if (files.Length <= MAX_LOG_FILES) return;

                var sortedFiles = files
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.CreationTime)
                    .ToList();

                var toDelete = sortedFiles.Take(sortedFiles.Count - MAX_LOG_FILES);
                foreach (var file in toDelete)
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch { }
        }

        public static string? GetLatestLogFile()
        {
            if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory)) return null;

            var files = Directory.GetFiles(_logDirectory, "iptv_*.log");
            if (files.Length == 0) return null;

            return files.OrderByDescending(f => new FileInfo(f).CreationTime).FirstOrDefault();
        }

        public static string? GetLogDirectory() => _logDirectory;

        private static string GetTag(string message)
        {
            if (message.StartsWith("[") && message.Length > 2)
            {
                var endIdx = message.IndexOf(']', 1);
                if (endIdx > 1 && endIdx < 15)
                {
                    return message.Substring(1, endIdx - 1);
                }
            }
            return "";
        }

        private static string GetDisplayMessage(string message)
        {
            if (message.StartsWith("[") && message.Length > 2)
            {
                var endIdx = message.IndexOf(']', 1);
                if (endIdx > 1 && endIdx < 15)
                {
                    var rest = message.Substring(endIdx + 1).TrimStart(' ', ']');
                    return rest;
                }
            }
            return message;
        }

        public static void Log(string message, LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
        {
            if (level < MinimumLevel) return;

            if (level == LogLevel.Trace || level == LogLevel.Debug)
            {
                if (!DebugEnabled) return;
            }

            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var prefix = level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Fatal => "FATAL",
                _ => "INFO"
            };

            var tag = GetTag(message);
            var displayMsg = GetDisplayMessage(message);
            var displayMsg2 = string.IsNullOrEmpty(tag) ? displayMsg : message.Contains("]") ? message.Substring(message.IndexOf(']') + 1).TrimStart(' ', ']') : message;

            var displayFormat = $"[{DateTime.Now:HH:mm:ss}] [{prefix}] {displayMsg2}";
            var fileFormat = $"[{DateTime.Now:HH:mm:ss.fff}][{prefix}][{fileName}.{caller}] {LogRedactor.Redact(message)}";

            OnMessage?.Invoke(displayFormat);
            OnMessageLeveled?.Invoke(level, fileFormat);
            WriteToFile(fileFormat);
        }

        public static void Trace(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Trace, caller, filePath);

        public static void Debug(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Debug, caller, filePath);

        public static void Info(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Info, caller, filePath);

        public static void Warn(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Warning, caller, filePath);

        public static void Error(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Error, caller, filePath);

        public static void Fatal(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
            => Log(message, LogLevel.Fatal, caller, filePath);
    }
}
