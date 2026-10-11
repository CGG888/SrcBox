using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services.WebRemote
{
    /// <summary>
    /// Serves the PWA icon set. The images are embedded in the assembly, so the endpoint also works from
    /// a published build, with a disk fallback under docs/public/pwa for development tweaks.
    /// </summary>
    public static class WebRemoteIconProvider
    {
        /// <summary>
        /// Icon file names that may be requested (and that the manifest references).
        /// </summary>
        public static readonly IReadOnlyList<string> SupportedIcons = new[]
        {
            "icon-192.png",
            "icon-512.png",
            "maskable-512.png",
            "apple-touch-icon-180.png"
        };

        private const string ResourcePrefix = "SrcBox.Pwa.";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// PNG bytes for one of the <see cref="SupportedIcons"/>, or null when the name is unknown.
        /// </summary>
        public static byte[]? GetIcon(string? fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            if (string.IsNullOrEmpty(name)) return null;
            if (!SupportedIcons.Any(icon => string.Equals(icon, name, StringComparison.OrdinalIgnoreCase))) return null;

            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
            }

            var bytes = ReadEmbedded(name) ?? ReadFromDisk(name);
            if (bytes == null)
            {
                Logger.Warn($"[WebRemote] PWA icon not found: {name}");
                return null;
            }

            lock (Gate) Cache[name] = bytes;
            return bytes;
        }

        /// <summary>
        /// Directories that may hold the application assets: the output folder, the project root and the
        /// folder of the running executable.
        /// </summary>
        internal static IReadOnlyList<string> AssetRoots()
        {
            var roots = new List<string>();

            try { roots.Add(AppContext.BaseDirectory); } catch { }
            try { roots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))); } catch { }
            try
            {
                var executable = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(executable)) roots.Add(Path.GetDirectoryName(executable) ?? "");
            }
            catch
            {
                // Process inspection can fail; the other roots are enough.
            }

            return roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// First existing file among the asset roots for the given relative path, or null.
        /// </summary>
        internal static byte[]? ReadAsset(params string[] relativeParts)
        {
            foreach (var root in AssetRoots())
            {
                try
                {
                    var path = root;
                    foreach (var part in relativeParts) path = Path.Combine(path, part);
                    if (File.Exists(path)) return File.ReadAllBytes(path);
                }
                catch
                {
                    // Ignore unreadable candidates and keep looking.
                }
            }
            return null;
        }

        private static byte[]? ReadEmbedded(string name)
        {
            try
            {
                using var stream = typeof(WebRemoteIconProvider).Assembly
                    .GetManifestResourceStream(ResourcePrefix + name);
                if (stream == null) return null;

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to read embedded icon {name}: {ex.Message}");
                return null;
            }
        }

        private static byte[]? ReadFromDisk(string name)
        {
            return ReadAsset("docs", "public", "pwa", name);
        }
    }
}
