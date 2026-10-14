using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>One file of the offline recognition model, with the size and checksum the host must deliver.</summary>
    public sealed class VoiceModelFile
    {
        public VoiceModelFile(string name, long sizeBytes, string? sha256 = null)
        {
            Name = name;
            SizeBytes = sizeBytes;
            Sha256 = sha256 ?? "";
        }

        /// <summary>File name inside the model directory; the engine opens it by this name.</summary>
        public string Name { get; }

        /// <summary>Exact size in bytes; a shorter or longer file is a failed download, not a model.</summary>
        public long SizeBytes { get; }

        /// <summary>
        /// Published SHA-256 of the file as lower case hex, taken from the repository API and checked
        /// against a finished download. Empty only for test files that do not exercise verification.
        /// </summary>
        public string Sha256 { get; }

        /// <summary>True when this file can be checked against a published checksum.</summary>
        public bool HasChecksum => Sha256.Length > 0;

        /// <summary>Where this file is fetched from, so a mirror only has to change one place.</summary>
        public string Url => VoiceModelStore.BuildUrl(Name);
    }

    /// <summary>
    /// The offline speech model, fetched once and then kept on disk. The engine assemblies arrive with the
    /// package; only the model is downloaded, and it comes from ModelScope because that is the model host
    /// this app's target networks can actually reach - Hugging Face and the GitHub release assets are not
    /// reachable from them, which is why the URLs are not the ones the upstream project documents.
    /// </summary>
    public static class VoiceModelStore
    {
        /// <summary>
        /// ModelScope repository holding the int8 SenseVoice export published by the engine's own author.
        /// Its file names are the ones the engine opens (asr.yaml / tokens.txt); the FunASR exports under
        /// "iic" ship tokens.json and a different yaml, so they are not interchangeable here.
        /// </summary>
        public const string ModelRepository = "manyeyes/sensevoice-small-wenetspeech-yue-int8-onnx";

        /// <summary>
        /// Revision of <see cref="ModelRepository"/>, pinned to the commit the checksums below were
        /// published with. Following "master" instead would let an upstream re-upload swap the model under
        /// a fixed checksum, which then fails as a corrupt download long after the cause.
        /// </summary>
        public const string ModelRevision = "0c40ce0c6b6cf193a74af4193fc25ba4050f563b";

        /// <summary>Model host. Kept public so a mirror can be configured later without touching the code.</summary>
        public const string ModelHost = "https://modelscope.cn";

        /// <summary>File names inside the model directory. The engine opens them by name, so they are named once.</summary>
        public const string ModelFileName = "model.int8.onnx";
        public const string TokensFileName = "tokens.txt";
        public const string MvnFileName = "am.mvn";
        public const string ConfigFileName = "asr.yaml";

        /// <summary>
        /// Every file the engine needs, sized and hashed exactly as published. The sizes catch a truncated
        /// download; the checksums catch a file that arrived whole but wrong, which no size can.
        /// </summary>
        public static IReadOnlyList<VoiceModelFile> Files { get; } = new[]
        {
            new VoiceModelFile(ModelFileName, 238303479L, "d7c9e0f5ca8592826c664c0f913e52a6d914abd2244f256d797c79cac637d684"),
            new VoiceModelFile(TokensFileName, 399355L, "d92f88b9ece933daf711e145723a76fd3859aa8a8c3771f1cbb7a721ad067193"),
            new VoiceModelFile(MvnFileName, 11203L, "29b3c740a2c0cfc6b308126d31d7f265fa2be74f3bb095cd2f143ea970896ae5"),
            new VoiceModelFile(ConfigFileName, 1333L, "d5f448a3a0e45d80345c27d82283e50c5a969a77004164966cbfcd441e68427d")
        };

        /// <summary>Bytes one complete download moves; shown before the user starts it.</summary>
        public static long TotalBytes => Files.Sum(f => f.SizeBytes);

        /// <summary>Total size in whole megabytes, for a message that names the cost up front.</summary>
        public static long TotalMegabytes => (TotalBytes + 1024 * 1024 - 1) / (1024 * 1024);

        /// <summary>Where the model lives when the user configured no other place.</summary>
        public static string DefaultDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SrcBox", "speech", "sensevoice-small-int8");

        /// <summary>Download URL of one file; the escaped path is what ModelScope expects.</summary>
        public static string BuildUrl(string fileName) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}/api/v1/models/{1}/repo?Revision={2}&FilePath={3}",
                ModelHost,
                ModelRepository,
                ModelRevision,
                Uri.EscapeDataString(fileName));

        /// <summary>Turns an empty setting into the default directory.</summary>
        public static string Resolve(string? directory) =>
            string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory!.Trim();

        /// <summary>Files still missing or incomplete in <paramref name="directory"/>.</summary>
        public static IReadOnlyList<VoiceModelFile> Missing(string? directory)
        {
            var dir = Resolve(directory);
            return Files.Where(f => !FileIsComplete(Path.Combine(dir, f.Name), f.SizeBytes)).ToList();
        }

        /// <summary>True when every file of the manifest is present with its published size.</summary>
        public static bool IsInstalled(string? directory) => Missing(directory).Count == 0;

        /// <summary>Bytes already on disk, so the status line can say how far a download got.</summary>
        public static long InstalledBytes(string? directory)
        {
            var dir = Resolve(directory);
            long total = 0;
            foreach (var file in Files)
            {
                var info = new FileInfo(Path.Combine(dir, file.Name));
                if (info.Exists) total += Math.Min(info.Length, file.SizeBytes);
            }

            return total;
        }

        /// <summary>
        /// Downloads what is missing into <paramref name="directory"/>, reporting percentage complete.
        /// A partially written file is resumed through a byte range, so a dropped Wi-Fi connection costs
        /// only the part that had not arrived.
        /// </summary>
        public static Task DownloadAsync(string? directory, IProgress<double>? progress, CancellationToken ct) =>
            DownloadAsync(Files, Resolve(directory), progress, SharedClient, ct);

        /// <summary>
        /// Hashes every file of the manifest against its published checksum and reports how far it got,
        /// 0-100. This is the question a directory listing cannot answer: is this really the published model.
        /// </summary>
        public static Task<bool> VerifyAsync(string? directory, IProgress<double>? progress, CancellationToken ct) =>
            VerifyAsync(Files, Resolve(directory), progress, ct);

        /// <summary>The verification itself, over an explicit list so tiny files can stand in for the model.</summary>
        internal static async Task<bool> VerifyAsync(
            IReadOnlyList<VoiceModelFile> files,
            string directory,
            IProgress<double>? progress,
            CancellationToken ct)
        {
            var totalBytes = files.Sum(f => f.SizeBytes);
            long completedBytes = 0;
            var lastPercent = -1;
            var verified = true;

            void Report(long inFile)
            {
                if (progress == null || totalBytes <= 0) return;
                var percent = (int)Math.Clamp((completedBytes + inFile) * 100 / totalBytes, 0, 100);
                if (percent == lastPercent) return;
                lastPercent = percent;
                progress.Report(percent);
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var path = Path.Combine(directory, file.Name);
                // No checksum means the file cannot be verified at all, which is a failure of the check,
                // not a pass: the shipped manifest always carries one.
                var matched = file.HasChecksum
                              && FileIsComplete(path, file.SizeBytes)
                              && await Task.Run(() => FileMatchesChecksum(file, path, Report, ct), ct).ConfigureAwait(false);

                if (!matched) verified = false;

                completedBytes += file.SizeBytes;
                Report(0);
            }

            progress?.Report(100);
            return verified;
        }

        /// <summary>Removes the model directory; used by the settings button that frees the space again.</summary>
        public static void Delete(string? directory)
        {
            var dir = Resolve(directory);
            if (!Directory.Exists(dir)) return;

            foreach (var file in Files)
            {
                var path = Path.Combine(dir, file.Name);
                TryDelete(path);
                TryDelete(path + ".part");
            }
        }

        /// <summary>
        /// The download itself, over an explicit file list so the resume and verification logic can be
        /// tested without moving a quarter of a gigabyte.
        /// </summary>
        internal static async Task DownloadAsync(
            IReadOnlyList<VoiceModelFile> files,
            string directory,
            IProgress<double>? progress,
            HttpClient client,
            CancellationToken ct)
        {
            Directory.CreateDirectory(directory);

            var totalBytes = files.Sum(f => f.SizeBytes);
            long completedBytes = 0;
            var lastPercent = -1;

            void Report(long inFile)
            {
                if (progress == null || totalBytes <= 0) return;
                var percent = (int)Math.Clamp((completedBytes + inFile) * 100 / totalBytes, 0, 100);
                if (percent == lastPercent) return;
                lastPercent = percent;
                progress.Report(percent);
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var finalPath = Path.Combine(directory, file.Name);
                if (FileIsComplete(finalPath, file.SizeBytes))
                {
                    // A file that is already there is hashed anyway: "complete" is only a size, and a
                    // corrupted one is fetched again below instead of loading as a broken model.
                    if (FileMatchesChecksum(file, finalPath, Report, ct))
                    {
                        completedBytes += file.SizeBytes;
                        Report(0);
                        continue;
                    }
                }

                var partPath = finalPath + ".part";
                long alreadyThere = 0;
                if (File.Exists(partPath))
                {
                    alreadyThere = new FileInfo(partPath).Length;
                    if (alreadyThere > file.SizeBytes) alreadyThere = 0;
                    if (alreadyThere == 0) TryDelete(partPath);
                }

                // A checksum mismatch is thrown after the streams are closed, so the partial file is not
                // locked by the very cleanup that has to remove it.
                string? checksumFailure = null;

                using (var request = new HttpRequestMessage(HttpMethod.Get, file.Url))
                {
                    if (alreadyThere > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(alreadyThere, null);

                    using var response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);

                    // A server that ignores the range starts over; the file then has to start over too.
                    if (alreadyThere > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        alreadyThere = 0;
                        TryDelete(partPath);
                    }

                    response.EnsureSuccessStatusCode();

                    // The checksum is computed while the bytes are written; a resumed file starts from the
                    // bytes already on disk, so the hash still covers the whole file.
                    using var checksum = file.HasChecksum ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                    if (checksum != null && alreadyThere > 0) AppendFileToHash(checksum, partPath, ct);

                    var mode = alreadyThere > 0 ? FileMode.Append : FileMode.Create;
                    using var target = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, 81920, true);
                    using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                    var buffer = new byte[81920];
                    var inFile = alreadyThere;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                        if (read <= 0) break;

                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        checksum?.AppendData(buffer, 0, read);
                        inFile += read;
                        Report(inFile - alreadyThere);
                    }

                    target.Flush();
                    if (inFile != file.SizeBytes)
                    {
                        throw new IOException(string.Format(
                            CultureInfo.InvariantCulture,
                            "model file {0} is {1} bytes, expected {2}",
                            file.Name, inFile, file.SizeBytes));
                    }

                    if (checksum != null)
                    {
                        var actual = Convert.ToHexString(checksum.GetHashAndReset()).ToLowerInvariant();
                        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            // Wrong content cannot be resumed into something right, so the partial file goes.
                            checksumFailure = string.Format(
                                CultureInfo.InvariantCulture,
                                "model file {0} failed its sha256 check (expected {1}, got {2})",
                                file.Name, file.Sha256, actual);
                        }
                    }
                }

                if (checksumFailure != null)
                {
                    TryDelete(partPath);
                    throw new IOException(checksumFailure);
                }

                TryDelete(finalPath);
                File.Move(partPath, finalPath);
                completedBytes += file.SizeBytes;
                Report(0);
            }

            progress?.Report(100);
        }

        /// <summary>
        /// True when the file's bytes hash to the published checksum. A file without a checksum is accepted,
        /// because a caller that lists no checksum asked for a size-only download.
        /// </summary>
        private static bool FileMatchesChecksum(VoiceModelFile file, string path, Action<long>? advanced, CancellationToken ct)
        {
            if (!file.HasChecksum) return true;

            try
            {
                return string.Equals(HashFile(path, advanced, ct), file.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Lower case hex SHA-256 of one file, reporting the bytes read so far as it goes.</summary>
        internal static string HashFile(string path, Action<long>? advanced, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, false);

            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, count);
                read += count;
                advanced?.Invoke(read);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        /// <summary>Feeds the bytes already on disk into a running hash, so a resumed download is checked whole.</summary>
        private static void AppendFileToHash(IncrementalHash hash, string path, CancellationToken ct)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, false);

            var buffer = new byte[81920];
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, count);
            }
        }

        private static bool FileIsComplete(string path, long expectedBytes)
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == expectedBytes;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // A locked leftover is not worth failing the download for; the next run reports it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
    }
}
