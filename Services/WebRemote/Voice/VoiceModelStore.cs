using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>One file of the offline recognition model, with the size the host must deliver.</summary>
    public sealed class VoiceModelFile
    {
        public VoiceModelFile(string name, long sizeBytes)
        {
            Name = name;
            SizeBytes = sizeBytes;
        }

        /// <summary>File name inside the model directory; the engine opens it by this name.</summary>
        public string Name { get; }

        /// <summary>Exact size in bytes; a shorter or longer file is a failed download, not a model.</summary>
        public long SizeBytes { get; }

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

        /// <summary>Revision of <see cref="ModelRepository"/>; "master" is the only one published.</summary>
        public const string ModelRevision = "master";

        /// <summary>Model host. Kept public so a mirror can be configured later without touching the code.</summary>
        public const string ModelHost = "https://modelscope.cn";

        /// <summary>File names inside the model directory. The engine opens them by name, so they are named once.</summary>
        public const string ModelFileName = "model.int8.onnx";
        public const string TokensFileName = "tokens.txt";
        public const string MvnFileName = "am.mvn";
        public const string ConfigFileName = "asr.yaml";

        /// <summary>
        /// Every file the engine needs, sized exactly as published. Sizes are part of the manifest on
        /// purpose: a truncated download then fails immediately instead of loading as a broken model.
        /// </summary>
        public static IReadOnlyList<VoiceModelFile> Files { get; } = new[]
        {
            new VoiceModelFile(ModelFileName, 238303479L),
            new VoiceModelFile(TokensFileName, 399355L),
            new VoiceModelFile(MvnFileName, 11203L),
            new VoiceModelFile(ConfigFileName, 1333L)
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
                    completedBytes += file.SizeBytes;
                    Report(0);
                    continue;
                }

                var partPath = finalPath + ".part";
                long alreadyThere = 0;
                if (File.Exists(partPath))
                {
                    alreadyThere = new FileInfo(partPath).Length;
                    if (alreadyThere > file.SizeBytes) alreadyThere = 0;
                    if (alreadyThere == 0) TryDelete(partPath);
                }

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
                }

                TryDelete(finalPath);
                File.Move(partPath, finalPath);
                completedBytes += file.SizeBytes;
                Report(0);
            }

            progress?.Report(100);
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
