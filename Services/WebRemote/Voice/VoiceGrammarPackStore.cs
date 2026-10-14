using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>Where a pack run is right now.</summary>
    public enum VoiceGrammarPackPhase
    {
        /// <summary>Reading the manifest that says which pack is current.</summary>
        Checking,

        /// <summary>Receiving the pack itself.</summary>
        Downloading,

        /// <summary>Hashing and validating what arrived before it is allowed to replace anything.</summary>
        Verifying,

        /// <summary>The pack is installed and in force.</summary>
        Completed
    }

    /// <summary>A pack run's progress. One small file, so the numbers are only ever a percentage and a size.</summary>
    public readonly struct VoiceGrammarPackProgress
    {
        public VoiceGrammarPackProgress(
            VoiceGrammarPackPhase phase, string version, long bytesDone, long bytesTotal, int percent)
        {
            Phase = phase;
            Version = version;
            BytesDone = bytesDone;
            BytesTotal = bytesTotal;
            Percent = percent;
        }

        public VoiceGrammarPackPhase Phase { get; }

        /// <summary>Version the run is about.</summary>
        public string Version { get; }

        public long BytesDone { get; }

        public long BytesTotal { get; }

        public int Percent { get; }
    }

    /// <summary>One language's pack, as the manifest describes it.</summary>
    public sealed class VoiceGrammarPackEntry
    {
        public string Lang { get; set; } = "";
        public string Version { get; set; } = "";

        /// <summary>Release asset URL; empty when the pack is only reachable through the repository path.</summary>
        public string Url { get; set; } = "";

        /// <summary>Path inside the repository, used to build a CDN fallback.</summary>
        public string Path { get; set; } = "";

        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// The file published at the stable <c>voice-pack</c> release tag: which pack each language should be
    /// running. The tag is re-published in place, so a client only ever has to know one URL, and the packs
    /// themselves are immutable files under it.
    /// </summary>
    public sealed class VoiceGrammarManifest
    {
        public const int CurrentSchema = 1;

        public int Schema { get; set; } = CurrentSchema;
        public string Updated { get; set; } = "";
        public List<VoiceGrammarPackEntry> Packs { get; set; } = new();

        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public static VoiceGrammarManifest? Parse(string? json, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return null; }
            if (json.Length > 256 * 1024) { error = "too large"; return null; }
            try
            {
                var manifest = JsonSerializer.Deserialize<VoiceGrammarManifest>(json, Json);
                if (manifest == null) { error = "empty"; return null; }
                if (manifest.Schema != CurrentSchema)
                {
                    error = $"schema {manifest.Schema} is not {CurrentSchema}";
                    return null;
                }
                return manifest;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>The pack for a language, or for the first entry when the language is not listed.</summary>
        public VoiceGrammarPackEntry? For(string? lang)
        {
            var wanted = (lang ?? "").Trim();
            if (wanted.Length > 0)
            {
                foreach (var entry in Packs)
                    if (entry.Lang.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return entry;
            }
            return Packs.FirstOrDefault();
        }
    }

    /// <summary>
    /// Installs grammar packs. A pack is one small JSON file, so the whole download is repeated instead of
    /// resumed, and nothing on disk is touched until the bytes have been hashed and the grammar they carry
    /// has been validated: a bad pack must never be able to leave the user without a working vocabulary.
    /// </summary>
    public static class VoiceGrammarPackStore
    {
        public const string Owner = "CGG888";
        public const string Repository = "SrcBox";

        /// <summary>The mutable release tag that only ever carries <see cref="ManifestFileName"/>.</summary>
        public const string StableTag = "voice-pack";

        public const string ManifestFileName = "manifest.json";

        /// <summary>Path of the manifest inside the repository, for the CDN fallback.</summary>
        public const string ManifestPath = "voice/" + ManifestFileName;

        /// <summary>Where a pack is installed; the same place a hand-dropped pack is read from.</summary>
        public static string DefaultDirectory => VoiceGrammar.DefaultDirectory;

        /// <summary>Why the last run failed; empty when it succeeded.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>Release asset URL of the manifest.</summary>
        public static string ManifestUrl =>
            $"https://github.com/{Owner}/{Repository}/releases/download/{StableTag}/{ManifestFileName}";

        /// <summary>
        /// Manifest URLs to try in order: the release asset, the mirrors the update check already trusts, and
        /// finally the CDN. The mirrors are used exactly as the update check uses them - as a prefix.
        /// </summary>
        public static IReadOnlyList<string> ManifestUrls(IReadOnlyList<string>? mirrors = null)
        {
            var urls = new List<string> { ManifestUrl };
            foreach (var mirror in mirrors ?? ConfiguredMirrors())
            {
                var prefix = (mirror ?? "").Trim().TrimEnd('/');
                if (prefix.Length > 0) urls.Add(prefix + "/" + ManifestUrl);
            }
            urls.Add($"https://cdn.jsdelivr.net/gh/{Owner}/{Repository}@main/{ManifestPath}");
            return urls;
        }

        /// <summary>URLs to try for one pack: its release asset, the same asset through a mirror, the CDN.</summary>
        public static IReadOnlyList<string> PackUrls(VoiceGrammarPackEntry entry, IReadOnlyList<string>? mirrors = null)
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Url)) urls.Add(entry.Url.Trim());
            if (!string.IsNullOrWhiteSpace(entry.Path))
                urls.Add($"https://cdn.jsdelivr.net/gh/{Owner}/{Repository}@main/{entry.Path.TrimStart('/')}");
            var direct = urls.FirstOrDefault();
            if (direct != null)
            {
                foreach (var mirror in mirrors ?? ConfiguredMirrors())
                {
                    var prefix = (mirror ?? "").Trim().TrimEnd('/');
                    if (prefix.Length > 0) urls.Add(prefix + "/" + direct);
                }
            }
            return urls;
        }

        /// <summary>True when <paramref name="candidate"/> is a later version than <paramref name="current"/>.</summary>
        public static bool IsNewer(string? candidate, string? current)
        {
            var newer = (candidate ?? "").Trim();
            var installed = (current ?? "").Trim();
            if (newer.Length == 0) return false;
            if (installed.Length == 0) return true;
            try
            {
                return Version.Parse(Normalize(newer)) > Version.Parse(Normalize(installed));
            }
            catch (Exception)
            {
                return !newer.Equals(installed, StringComparison.OrdinalIgnoreCase);
            }

            static string Normalize(string value) => value.TrimStart('v', 'V').Split('-')[0];
        }

        /// <summary>Reads the manifest, trying every URL in turn.</summary>
        public static Task<VoiceGrammarManifest?> FetchManifestAsync(IReadOnlyList<string>? urls, CancellationToken ct) =>
            FetchManifestAsync(urls, ct, null);

        internal static async Task<VoiceGrammarManifest?> FetchManifestAsync(
            IReadOnlyList<string>? urls, CancellationToken ct, HttpMessageHandler? handler)
        {
            LastError = "";
            using var client = handler == null ? null : new HttpClient(handler, false);
            var http = handler == null ? VoiceModelStore.SharedHttpClient : client!;
            foreach (var url in urls ?? ManifestUrls())
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                    using var response = await http.GetAsync(url, linked.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        LastError = $"the manifest is not there ({(int)response.StatusCode})";
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(linked.Token);
                    var manifest = VoiceGrammarManifest.Parse(json, out var error);
                    if (manifest == null) { LastError = error; continue; }
                    return manifest;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { LastError = ex.Message; }
            }

            if (LastError.Length == 0) LastError = "the manifest could not be read";
            return null;
        }

        /// <summary>
        /// The pack this language should be running, or null when the installed one is already current.
        /// </summary>
        public static VoiceGrammarPackEntry? Update(VoiceGrammarManifest? manifest, string? lang)
        {
            var entry = manifest?.For(lang);
            if (entry == null) { LastError = "the manifest lists no pack"; return null; }
            if (string.IsNullOrWhiteSpace(entry.Version)) { LastError = "the listed pack has no version"; return null; }
            if (!IsNewer(entry.Version, VoiceGrammar.InstalledVersion))
            {
                LastError = "";
                return null;
            }
            return entry;
        }

        /// <summary>Downloads and installs one pack. Returns false and sets <see cref="LastError"/> on failure.</summary>
        public static Task<bool> InstallAsync(
            VoiceGrammarPackEntry entry,
            IProgress<VoiceGrammarPackProgress>? progress,
            CancellationToken ct,
            IReadOnlyList<string>? mirrors = null) =>
            InstallAsync(entry, progress, ct, mirrors, null);

        internal static async Task<bool> InstallAsync(
            VoiceGrammarPackEntry? entry,
            IProgress<VoiceGrammarPackProgress>? progress,
            CancellationToken ct,
            IReadOnlyList<string>? mirrors,
            HttpMessageHandler? handler)
        {
            LastError = "";
            if (entry == null) { LastError = "no pack to install"; return false; }

            using var owned = handler == null ? null : new HttpClient(handler, false) { Timeout = TimeSpan.FromSeconds(60) };
            var http = handler == null ? VoiceModelStore.SharedHttpClient : owned!;

            var dir = DefaultDirectory;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }

            progress?.Report(new VoiceGrammarPackProgress(
                VoiceGrammarPackPhase.Checking, entry.Version, 0, entry.Bytes, 0));

            var urls = PackUrls(entry, mirrors);
            if (urls.Count == 0) { LastError = "the pack has no URL"; return false; }

            var temp = Path.Combine(dir, "pack.tmp");
            foreach (var url in urls)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                    using var response = await http.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        LastError = $"the pack is not there ({(int)response.StatusCode})";
                        continue;
                    }

                    var total = response.Content.Headers.ContentLength ?? entry.Bytes;
                    TryDelete(temp);
                    await using (var target = File.Create(temp))
                    await using (var source = await response.Content.ReadAsStreamAsync(linked.Token))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, linked.Token)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), linked.Token);
                            done += read;
                            var percent = total > 0 ? (int)Math.Clamp(done * 100 / total, 0, 100) : 0;
                            progress?.Report(new VoiceGrammarPackProgress(
                                VoiceGrammarPackPhase.Downloading, entry.Version, done, total, percent));
                        }
                    }

                    var problem = Validate(temp, entry);
                    if (problem.Length > 0) { LastError = problem; continue; }
                    progress?.Report(new VoiceGrammarPackProgress(
                        VoiceGrammarPackPhase.Verifying, entry.Version, entry.Bytes, entry.Bytes, 100));

                    Install(temp, entry);
                    progress?.Report(new VoiceGrammarPackProgress(
                        VoiceGrammarPackPhase.Completed, entry.Version, entry.Bytes, entry.Bytes, 100));
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { LastError = ex.Message; }
            }

            TryDelete(temp);
            if (LastError.Length == 0) LastError = "the pack could not be downloaded";
            return false;
        }

        /// <summary>
        /// Installs a pack the user picked themselves. It is validated the same way as a downloaded one, so a
        /// hand-made file cannot leave the app without a vocabulary either.
        /// </summary>
        public static bool InstallFromFile(string path)
        {
            LastError = "";
            try
            {
                var dir = DefaultDirectory;
                System.IO.Directory.CreateDirectory(dir);
                var text = File.ReadAllText(path);
                var pack = VoiceGrammarPack.Parse(text, out var error);
                if (pack == null) { LastError = error; return false; }
                var problems = pack.Validate();
                if (problems.Count > 0) { LastError = string.Join("; ", problems); return false; }

                var temp = Path.Combine(dir, "pack.tmp");
                File.WriteAllText(temp, text);
                var entry = new VoiceGrammarPackEntry
                {
                    Lang = pack.Lang,
                    Version = pack.Version,
                    Sha256 = VoiceModelStore.HashFile(temp, null, CancellationToken.None),
                    Bytes = new FileInfo(temp).Length
                };
                Install(temp, entry);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>Removes the installed pack; the built-in grammar takes over again.</summary>
        public static bool Remove()
        {
            LastError = "";
            try
            {
                var dir = DefaultDirectory;
                if (System.IO.Directory.Exists(dir))
                {
                    TryDelete(Path.Combine(dir, "current.json"));
                    TryDelete(Path.Combine(dir, "pack.json"));
                    foreach (var file in System.IO.Directory.GetFiles(dir, "pack-*.json"))
                        TryDelete(file);
                }
                VoiceGrammar.Reload();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The pack a dropped file carries, for the settings page to show before installing.</summary>
        public static VoiceGrammarPack? Inspect(string path, out string error)
        {
            try
            {
                var pack = VoiceGrammarPack.Parse(File.ReadAllText(path), out error);
                if (pack == null) return null;
                var problems = pack.Validate();
                if (problems.Count > 0) { error = string.Join("; ", problems); return null; }
                return pack;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static string[] ConfiguredMirrors()
        {
            try
            {
                return (AppSettings.Current.UpdateCdnMirrors ?? new List<string>()).ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Checks the downloaded file: size, checksum and the grammar it carries.</summary>
        private static string Validate(string path, VoiceGrammarPackEntry entry)
        {
            var info = new FileInfo(path);
            if (entry.Bytes > 0 && info.Length != entry.Bytes)
                return string.Format(CultureInfo.InvariantCulture,
                    "the pack is {0} bytes, not {1}", info.Length, entry.Bytes);

            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                var hash = VoiceModelStore.HashFile(path, null, CancellationToken.None);
                if (!hash.Equals(entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    return "the pack does not match its checksum";
            }

            var pack = VoiceGrammarPack.Parse(File.ReadAllText(path), out var error);
            if (pack == null) return "the pack cannot be read: " + error;
            var problems = pack.Validate();
            return problems.Count > 0 ? "the pack is not usable: " + string.Join("; ", problems) : "";
        }

        /// <summary>
        /// Moves a validated pack into place and points the grammar at it. The pointer is written last and
        /// atomically, so an interrupted install leaves the previous vocabulary in force.
        /// </summary>
        private static void Install(string temp, VoiceGrammarPackEntry entry)
        {
            var dir = DefaultDirectory;
            var safe = Sanitize(entry.Lang.Length > 0 ? entry.Lang : "und");
            var version = Sanitize(entry.Version.Length > 0 ? entry.Version : "0");
            var name = $"pack-{safe}-{version}.json";
            var target = Path.Combine(dir, name);

            File.Move(temp, target, true);
            foreach (var old in System.IO.Directory.GetFiles(dir, "pack-*.json"))
            {
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) TryDelete(old);
            }

            var pointer = new VoiceGrammarPointer
            {
                Lang = entry.Lang,
                Version = entry.Version,
                File = name,
                Sha256 = entry.Sha256,
                Bytes = new FileInfo(target).Length,
                Installed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
            var json = JsonSerializer.Serialize(pointer, VoiceGrammarPointer.Json);

            var pointerPath = Path.Combine(dir, "current.json");
            var staging = pointerPath + ".tmp";
            File.WriteAllText(staging, json);
            File.Move(staging, pointerPath, true);
            VoiceGrammar.Reload();
        }

        private static string Sanitize(string value)
        {
            var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_').ToArray());
            return cleaned.Length == 0 ? "und" : cleaned;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // A leftover temp file is harmless; the next run overwrites it.
            }
        }
    }
}
