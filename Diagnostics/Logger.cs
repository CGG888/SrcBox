using System;
using System.IO;
using System.Linq;
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
        private static string _debugLogFilePath = "";
        private static string _currentDate = "";

        private const int MAX_LOG_FILES = 14;

        public static void InitializeFileLogging(string logDir)
        {
            if (string.IsNullOrEmpty(logDir)) return;

            _logDirectory = logDir;
            if (!Directory.Exists(_logDirectory))
            {
                try { Directory.CreateDirectory(_logDirectory); } catch { return; }
            }

            _currentDate = DateTime.Now.ToString("yyyyMMdd");
            _logFilePath = Path.Combine(_logDirectory, $"SrcBox-{_currentDate}.log");
            _debugLogFilePath = Path.Combine(_logDirectory, $"SrcBox-{_currentDate}.debug.log");

            WriteToFile($"[SYSTEM] 日志文件创建: {_logFilePath}");
            if (DebugEnabled)
            {
                WriteToDebugFile($"[SYSTEM] Debug日志文件创建: {_debugLogFilePath}");
            }
        }

        private static void EnsureLogPath()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (today != _currentDate)
            {
                _currentDate = today;
                _logFilePath = Path.Combine(_logDirectory, $"SrcBox-{_currentDate}.log");
                _debugLogFilePath = Path.Combine(_logDirectory, $"SrcBox-{_currentDate}.debug.log");
            }
        }

        private static void WriteToFile(string message)
        {
            if (string.IsNullOrEmpty(_logDirectory)) return;

            lock (_fileLock)
            {
                try
                {
                    EnsureLogPath();
                    var bytes = System.Text.Encoding.UTF8.GetBytes(message + Environment.NewLine);
                    using (var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                    }
                }
                catch { }
            }
        }

        private static void WriteToDebugFile(string message)
        {
            if (string.IsNullOrEmpty(_logDirectory)) return;

            lock (_fileLock)
            {
                try
                {
                    EnsureLogPath();
                    var bytes = System.Text.Encoding.UTF8.GetBytes(message + Environment.NewLine);
                    using (var fs = new FileStream(_debugLogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                    }
                }
                catch { }
            }
        }

        public static void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logDirectory)) return;

                var files = Directory.GetFiles(_logDirectory, "SrcBox-*.log");
                if (files.Length <= MAX_LOG_FILES) return;

                var sortedFiles = files
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                var toDelete = sortedFiles.Skip(MAX_LOG_FILES);
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

            EnsureLogPath();
            return _logFilePath;
        }

        public static string? GetLatestDebugLogFile()
        {
            if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory)) return null;

            EnsureLogPath();
            return _debugLogFilePath;
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

            var displayMsg = GetDisplayMessage(message);
            var displayFormat = $"[{DateTime.Now:HH:mm:ss}] [{prefix}] {displayMsg}";
            var fileFormat = $"[{DateTime.Now:HH:mm:ss.fff}][{prefix}][{fileName}.{caller}] {LogRedactor.Redact(message)}";

            OnMessage?.Invoke(displayFormat);
            OnMessageLeveled?.Invoke(level, fileFormat);

            if (level == LogLevel.Debug || level == LogLevel.Trace)
            {
                WriteToDebugFile(fileFormat);
            }
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
