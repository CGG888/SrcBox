using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LibmpvIptvClient.Diagnostics
{
    /// <summary>
    /// Packs the files a playback report needs - the player log and the newest mpv logs - into a single zip,
    /// so a reporter attaches one file instead of hunting for the right log in the logs directory. The mpv
    /// side is what names the RTSP session that never showed a picture, and it is the file users kept losing.
    /// </summary>
    internal static class LogBundleExporter
    {
        /// <summary>How many of the (verbose) mpv logs travel with a bundle.</summary>
        internal const int MaxMpvLogs = 4;

        /// <summary>Writes the bundle and returns how many files went into it (0 means there was nothing to export).</summary>
        internal static int Build(string destinationZip, string logDir, string? currentLogFile, string? currentDebugLogFile, int maxMpvLogs = MaxMpvLogs)
        {
            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(currentLogFile) && File.Exists(currentLogFile))
                files.Add(currentLogFile!);
            if (!string.IsNullOrWhiteSpace(currentDebugLogFile) && File.Exists(currentDebugLogFile))
                files.Add(currentDebugLogFile!);

            if (!string.IsNullOrEmpty(logDir) && Directory.Exists(logDir))
            {
                files.AddRange(Directory.GetFiles(logDir, "mpv-*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(Math.Max(0, maxMpvLogs)));
            }

            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) return 0;

            using var zip = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                AddFile(zip, file, UniqueName(used, Path.GetFileName(file)));
            }
            return files.Count;
        }

        private static string UniqueName(HashSet<string> used, string name)
        {
            if (used.Add(name)) return name;
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var i = 2; ; i++)
            {
                var candidate = $"{stem}-{i}{extension}";
                if (used.Add(candidate)) return candidate;
            }
        }

        private static void AddFile(ZipArchive zip, string path, string entryName)
        {
            // mpv keeps its log file open: read it with sharing instead of letting the zip API fail on it.
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }
}
