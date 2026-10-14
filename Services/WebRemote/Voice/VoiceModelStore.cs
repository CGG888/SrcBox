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

    /// <summary>Which part of the download a progress report describes, so the window can say what it does.</summary>
    public enum VoiceModelPhase
    {
        /// <summary>Reaching the model host and reading the first response headers.</summary>
        Connecting,

        /// <summary>Receiving the bytes of one file.</summary>
        Downloading,

        /// <summary>Reading back what arrived and hashing it against the published checksum.</summary>
        Verifying,

        /// <summary>Every file is on disk and verified.</summary>
        Completed
    }

    /// <summary>
    /// One progress report. It carries the numbers a person needs to judge whether the download is healthy -
    /// how much is done, how fast, how much is left - instead of a bare percentage that sits still for the
    /// largest file and then jumps.
    /// </summary>
    public readonly struct VoiceModelProgress
    {
        public VoiceModelProgress(
            VoiceModelPhase phase,
            int fileIndex,
            int fileCount,
            string fileName,
            long bytesDone,
            long bytesTotal,
            double bytesPerSecond,
            TimeSpan? remaining,
            int percent)
        {
            Phase = phase;
            FileIndex = fileIndex;
            FileCount = fileCount;
            FileName = fileName;
            BytesDone = bytesDone;
            BytesTotal = bytesTotal;
            BytesPerSecond = bytesPerSecond;
            Remaining = remaining;
            Percent = percent;
        }

        /// <summary>What the run is doing right now.</summary>
        public VoiceModelPhase Phase { get; }

        /// <summary>1-based index of the file being worked on; equal to the file count once finished.</summary>
        public int FileIndex { get; }

        /// <summary>How many files the manifest has.</summary>
        public int FileCount { get; }

        /// <summary>Name of the file being worked on; empty when no single file is current.</summary>
        public string FileName { get; }

        /// <summary>Bytes of the whole manifest already accounted for.</summary>
        public long BytesDone { get; }

        /// <summary>Bytes of the whole manifest.</summary>
        public long BytesTotal { get; }

        /// <summary>Average bytes per second since the run started; 0 until enough time passed.</summary>
        public double BytesPerSecond { get; }

        /// <summary>Estimated time left, null while no rate is known yet.</summary>
        public TimeSpan? Remaining { get; }

        /// <summary>Whole percent of the manifest, 0-100.</summary>
        public int Percent { get; }
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
        /// Downloads what is missing into <paramref name="directory"/>, reporting the phase, the percentage
        /// and the speed. A partially written file is resumed through a byte range, so a dropped Wi-Fi
        /// connection costs only the part that had not arrived.
        /// </summary>
        public static Task DownloadAsync(string? directory, IProgress<VoiceModelProgress>? progress, CancellationToken ct) =>
            DownloadAsync(Files, Resolve(directory), progress, SharedClient, ct);

        /// <summary>
        /// Hashes every file of the manifest against its published checksum and reports how far it got.
        /// This is the question a directory listing cannot answer: is this really the published model.
        /// </summary>
        public static Task<bool> VerifyAsync(string? directory, IProgress<VoiceModelProgress>? progress, CancellationToken ct) =>
            VerifyAsync(Files, Resolve(directory), progress, ct);

        /// <summary>The verification itself, over an explicit list so tiny files can stand in for the model.</summary>
        internal static async Task<bool> VerifyAsync(
            IReadOnlyList<VoiceModelFile> files,
            string directory,
            IProgress<VoiceModelProgress>? progress,
            CancellationToken ct)
        {
            var totalBytes = files.Sum(f => f.SizeBytes);
            var report = new ProgressReporter(progress, totalBytes, files.Count);
            var verified = true;

            for (var index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                var file = files[index];
                var number = index + 1;
                var path = Path.Combine(directory, file.Name);

                void Advance(long inFile) => report.Phase(VoiceModelPhase.Verifying, file, number, inFile);

                report.Phase(VoiceModelPhase.Verifying, file, number, 0, force: true);

                // No checksum means the file cannot be verified at all, which is a failure of the check,
                // not a pass: the shipped manifest always carries one.
                var matched = file.HasChecksum
                              && FileIsComplete(path, file.SizeBytes)
                              && await Task.Run(() => FileMatchesChecksum(file, path, Advance, ct), ct).ConfigureAwait(false);

                if (!matched) verified = false;

                report.Complete(file);
            }

            report.Done(files.Count);
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
            IProgress<VoiceModelProgress>? progress,
            HttpClient client,
            CancellationToken ct)
        {
            Directory.CreateDirectory(directory);

            var totalBytes = files.Sum(f => f.SizeBytes);
            var report = new ProgressReporter(progress, totalBytes, files.Count);

            for (var index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();

                var file = files[index];
                var number = index + 1;
                var finalPath = Path.Combine(directory, file.Name);
                if (FileIsComplete(finalPath, file.SizeBytes))
                {
                    // A file that is already there is hashed anyway: "complete" is only a size, and a
                    // corrupted one is fetched again below instead of loading as a broken model.
                    void AdvanceExisting(long inFile) => report.Phase(VoiceModelPhase.Verifying, file, number, inFile);

                    report.Phase(VoiceModelPhase.Verifying, file, number, 0, force: true);
                    if (FileMatchesChecksum(file, finalPath, AdvanceExisting, ct))
                    {
                        report.Complete(file);
                        continue;
                    }
                }

                var partPath = finalPath + ".part";
                long alreadyThere = 0;
                if (File.Exists(partPath))
                {
                    alreadyThere = new FileInfo(partPath).Length;
                    // A part file that already holds every byte would be resumed from its end, and a range that
                    // starts at the end of a file is answered with 416 for good: start over instead.
                    if (alreadyThere >= file.SizeBytes) alreadyThere = 0;
                    if (alreadyThere == 0) TryDelete(partPath);
                }

                report.Phase(VoiceModelPhase.Connecting, file, number, alreadyThere, force: true);

                HttpResponseMessage response;
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
                    if (alreadyThere > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(alreadyThere, null);

                    response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                }

                if (alreadyThere > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // The server will not resume from what we have, so that part file is not usable: drop it and
                    // ask for the whole file again instead of leaving the download broken with a 416.
                    response.Dispose();
                    TryDelete(partPath);
                    alreadyThere = 0;
                    report.Phase(VoiceModelPhase.Connecting, file, number, 0, force: true);

                    using var retry = new HttpRequestMessage(HttpMethod.Get, file.Url);
                    response = await client
                        .SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                }

                using (response)
                {
                    // A server that ignores the range starts over; the file then has to start over too.
                    if (alreadyThere > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        alreadyThere = 0;
                        TryDelete(partPath);
                    }

                    response.EnsureSuccessStatusCode();

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
                        inFile += read;
                        report.Phase(VoiceModelPhase.Downloading, file, number, inFile);
                    }

                    target.Flush();
                    if (inFile != file.SizeBytes)
                    {
                        throw new IOException(string.Format(
                            CultureInfo.InvariantCulture,
                            "model file {0} is {1} bytes, expected {2}",
                            file.Name, inFile, file.SizeBytes));
                    }
                }

                if (file.HasChecksum)
                {
                    // The bytes are read back and hashed once the file is closed for writing: checking what
                    // actually landed on disk is a stronger question than hashing the stream in flight, and it
                    // gives the window a real phase to show instead of a percentage that stops moving. The
                    // manifest is small enough that reading it twice costs a fraction of a second.
                    report.Phase(VoiceModelPhase.Verifying, file, number, file.SizeBytes, force: true);
                    var actual = HashFile(partPath, null, ct);
                    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        // Wrong content cannot be resumed into something right, so the partial file goes.
                        TryDelete(partPath);
                        throw new IOException(string.Format(
                            CultureInfo.InvariantCulture,
                            "model file {0} failed its sha256 check (expected {1}, got {2})",
                            file.Name, file.Sha256, actual));
                    }
                }

                TryDelete(finalPath);
                File.Move(partPath, finalPath);
                report.Complete(file);
            }

            report.Done(files.Count);
        }

        /// <summary>
        /// Turns raw byte counts into the reports the progress window shows: the phase, the percent of the whole
        /// manifest, the average rate and the time left. It exists so the download and the verification pass
        /// report the same way, and so a report is only raised when something visible changed.
        /// </summary>
        private sealed class ProgressReporter
        {
            private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

            private readonly IProgress<VoiceModelProgress>? _progress;
            private readonly long _totalBytes;
            private readonly int _fileCount;
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

            private long _completedBytes;
            private DateTime _lastReportUtc = DateTime.MinValue;
            private int _lastPercent = -1;

            public ProgressReporter(IProgress<VoiceModelProgress>? progress, long totalBytes, int fileCount)
            {
                _progress = progress;
                _totalBytes = totalBytes;
                _fileCount = fileCount;
            }

            /// <summary>Moves the cursor past a file that is finished and verified.</summary>
            public void Complete(VoiceModelFile file) => _completedBytes += file.SizeBytes;

            /// <summary>
            /// Reports one file's phase; <paramref name="inFile"/> is how many bytes of that file are accounted
            /// for. A report is dropped while neither the percent changed nor <see cref="Interval"/> passed.
            /// </summary>
            public void Phase(VoiceModelPhase phase, VoiceModelFile? file, int fileIndex, long inFile, bool force = false)
            {
                if (_progress == null || _totalBytes <= 0) return;

                var done = _completedBytes + inFile;
                var percent = (int)Math.Clamp(done * 100 / _totalBytes, 0, 100);
                var now = DateTime.UtcNow;
                if (!force && percent == _lastPercent && now - _lastReportUtc < Interval) return;

                _lastPercent = percent;
                _lastReportUtc = now;

                var seconds = _clock.Elapsed.TotalSeconds;
                var speed = seconds > 0.5 ? done / seconds : 0;
                var remaining = speed > 1 && done < _totalBytes
                    ? TimeSpan.FromSeconds((_totalBytes - done) / speed)
                    : (TimeSpan?)null;

                _progress.Report(new VoiceModelProgress(
                    phase, fileIndex, _fileCount, file?.Name ?? string.Empty, done, _totalBytes, speed, remaining, percent));
            }

            /// <summary>Last report of a run: every byte is on disk and verified.</summary>
            public void Done(int fileCount)
            {
                _completedBytes = _totalBytes;
                Phase(VoiceModelPhase.Completed, null, fileCount, 0, force: true);
            }
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

        /// <summary>
        /// Name sent with every model request. The LFS endpoint the large file redirects to answers 403
        /// ("You don't have permission to access the URL on this server") to a request without a
        /// User-Agent, and HttpClient sends none by default, so the 228 MB file could never arrive.
        /// </summary>
        internal const string ModelUserAgent = "SrcBox";

        private static readonly HttpClient SharedClient = CreateSharedClient();

        private static HttpClient CreateSharedClient()
        {
            HttpClient? client = null;
            try
            {
                var version = typeof(VoiceModelStore).Assembly.GetName().Version?.ToString(3);
                client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    string.IsNullOrEmpty(version) ? ModelUserAgent : ModelUserAgent + "/" + version);
                return client;
            }
            catch
            {
                client?.Dispose();
                throw;
            }
        }

        /// <summary>The shared client, exposed so a test can prove the User-Agent really is set.</summary>
        internal static HttpClient SharedHttpClient => SharedClient;
    }
}
