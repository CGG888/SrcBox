using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the offline speech model: what the manifest promises, how a half-finished download resumes,
    /// and that a short file is rejected instead of being loaded as a model.
    /// </summary>
    [TestClass]
    public class VoiceModelStoreTests
    {
        private const long ModelBytes = 238303479L;
        private const long TokensBytes = 399355L;
        private const long MvnBytes = 11203L;
        private const long ConfigBytes = 1333L;

        [TestMethod]
        public void Manifest_ListsTheFilesTheEngineOpens()
        {
            CollectionAssert.AreEqual(
                new[] { "model.int8.onnx", "tokens.txt", "am.mvn", "asr.yaml" },
                VoiceModelStore.Files.Select(f => f.Name).ToArray());

            Assert.AreEqual(ModelBytes + TokensBytes + MvnBytes + ConfigBytes, VoiceModelStore.TotalBytes);
            Assert.AreEqual(228, VoiceModelStore.TotalMegabytes);
        }

        [TestMethod]
        public void Url_TargetsTheHostThisNetworkCanReach()
        {
            // The upstream project publishes on GitHub releases and Hugging Face; neither is reachable
            // here, so the download URL must stay on ModelScope.
            Assert.AreEqual(
                "https://modelscope.cn/api/v1/models/manyeyes/sensevoice-small-wenetspeech-yue-int8-onnx/repo?Revision="
                + VoiceModelStore.ModelRevision
                + "&FilePath=model.int8.onnx",
                VoiceModelStore.Files[0].Url);

            StringAssert.Contains(VoiceModelStore.BuildUrl("tokens.txt"), "FilePath=tokens.txt");
        }

        [TestMethod]
        public void TheSharedClientSendsAUserAgent()
        {
            // The LFS endpoint the big file redirects to answers 403 to a request without a User-Agent,
            // and HttpClient sends none by default, so the download could never start without this.
            StringAssert.StartsWith(
                VoiceModelStore.SharedHttpClient.DefaultRequestHeaders.UserAgent.ToString(),
                VoiceModelStore.ModelUserAgent);
        }

        [TestMethod]
        public void Missing_ReportsEveryFileOfAnEmptyDirectory()
        {
            var dir = NewDirectory();
            try
            {
                Assert.AreEqual(4, VoiceModelStore.Missing(dir).Count);
                Assert.IsFalse(VoiceModelStore.IsInstalled(dir));
                Assert.AreEqual(0L, VoiceModelStore.InstalledBytes(dir));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Missing_KeepsAFileWhoseSizeIsWrong()
        {
            var dir = NewDirectory();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "tokens.txt"), new byte[TokensBytes]);
                // A truncated model is the failure this check exists for: it must not count as installed.
                File.WriteAllBytes(Path.Combine(dir, "model.int8.onnx"), new byte[1024]);

                var missing = VoiceModelStore.Missing(dir);

                Assert.AreEqual(3, missing.Count);
                Assert.IsTrue(missing.Any(f => f.Name == "model.int8.onnx"));
                Assert.IsFalse(missing.Any(f => f.Name == "tokens.txt"));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Installed_AcceptsTheManifestSizes()
        {
            var dir = NewDirectory();
            try
            {
                foreach (var file in VoiceModelStore.Files)
                {
                    // Sparse files keep a quarter of a gigabyte out of the test's disk usage.
                    using var stream = new FileStream(Path.Combine(dir, file.Name), FileMode.CreateNew);
                    stream.SetLength(file.SizeBytes);
                }

                Assert.IsTrue(VoiceModelStore.IsInstalled(dir));
                Assert.AreEqual(VoiceModelStore.TotalBytes, VoiceModelStore.InstalledBytes(dir));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_WritesEveryFileAndReachesOneHundredPercent()
        {
            var dir = NewDirectory();
            try
            {
                var files = new[]
                {
                    new VoiceModelFile("tokens.json", 4),
                    new VoiceModelFile("am.mvn", 3)
                };
                using var client = new HttpClient(new StubHandler(("tokens.json", new byte[] { 1, 2, 3, 4 }), ("am.mvn", new byte[] { 9, 8, 7 })));
                var reported = new List<int>();

                VoiceModelStore
                    .DownloadAsync(files, dir, new RecordingProgress(p => reported.Add(p.Percent)), client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
                CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Path.Combine(dir, "am.mvn")));
                Assert.IsFalse(File.Exists(Path.Combine(dir, "tokens.json.part")));
                Assert.AreEqual(100, reported.Last());
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_ReportsEachPhaseOnceInOrderAndEndsAtOneHundredPercent()
        {
            var dir = NewDirectory();
            try
            {
                var tokens = new byte[] { 1, 2, 3, 4 };
                var files = new[] { new VoiceModelFile("tokens.json", tokens.Length, Sha256(tokens)) };
                using var client = new HttpClient(new StubHandler(("tokens.json", tokens)));
                var progress = new RecordingProgress();

                VoiceModelStore
                    .DownloadAsync(files, dir, progress, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                // The window tells the user which phase it is in, so every phase has to be announced: connect,
                // receive, check what landed, done.
                CollectionAssert.AreEqual(
                    new[]
                    {
                        VoiceModelPhase.Connecting,
                        VoiceModelPhase.Downloading,
                        VoiceModelPhase.Verifying,
                        VoiceModelPhase.Completed
                    },
                    progress.Reports.Select(p => p.Phase).ToArray());

                var last = progress.Reports.Last();
                Assert.AreEqual(100, last.Percent);
                Assert.AreEqual(tokens.Length, last.BytesDone);
                Assert.AreEqual(tokens.Length, last.BytesTotal);

                var receiving = progress.Reports.First(p => p.Phase == VoiceModelPhase.Downloading);
                Assert.AreEqual(1, receiving.FileIndex);
                Assert.AreEqual(1, receiving.FileCount);
                Assert.AreEqual("tokens.json", receiving.FileName);
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_ResumesAHalfWrittenFileWithARangeRequest()
        {
            var dir = NewDirectory();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "tokens.json.part"), new byte[] { 1, 2 });
                var handler = new StubHandler(("tokens.json", new byte[] { 1, 2, 3, 4, 5 }));
                using var client = new HttpClient(handler);

                VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 5) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new[] { "bytes=2-" }, handler.Ranges);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_RestartsWhenThePartFileIsAlreadyFull()
        {
            var dir = NewDirectory();
            try
            {
                // A part file that holds every byte but never got renamed (the process died in the last
                // moment) used to be resumed from its end, and a range starting at the end is 416 forever.
                File.WriteAllBytes(Path.Combine(dir, "tokens.json.part"), new byte[] { 9, 9, 9, 9 });
                var handler = new StubHandler(("tokens.json", new byte[] { 1, 2, 3, 4 }));
                using var client = new HttpClient(handler);

                VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 4) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
                CollectionAssert.AreEqual(new string?[] { null }, handler.Ranges);
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_StartsOverWhenTheServerRefusesTheRange()
        {
            var dir = NewDirectory();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "tokens.json.part"), new byte[] { 1, 2 });
                var handler = new StubHandler(("tokens.json", new byte[] { 1, 2, 3, 4 })) { RejectRanges = true };
                using var client = new HttpClient(handler);

                VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 4) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new string?[] { "bytes=2-", null }, handler.Ranges);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_RejectsAShortFile()
        {
            var dir = NewDirectory();
            try
            {
                using var client = new HttpClient(new StubHandler(("tokens.json", new byte[] { 1, 2, 3 })));

                var error = Assert.ThrowsException<IOException>(() => VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 5) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

                StringAssert.Contains(error.Message, "expected 5");
                Assert.IsFalse(File.Exists(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Resolve_FallsBackToTheDefaultDirectory()
        {
            Assert.AreEqual(VoiceModelStore.DefaultDirectory, VoiceModelStore.Resolve(null));
            Assert.AreEqual(VoiceModelStore.DefaultDirectory, VoiceModelStore.Resolve("   "));
            Assert.AreEqual(@"D:\models", VoiceModelStore.Resolve(@" D:\models "));
        }

        [TestMethod]
        public void Manifest_PublishesALowerCaseSha256ForEveryFile()
        {
            foreach (var file in VoiceModelStore.Files)
            {
                Assert.IsTrue(file.HasChecksum, file.Name + " has no checksum");
                Assert.AreEqual(64, file.Sha256.Length, file.Name);
                StringAssert.Matches(
                    file.Sha256,
                    new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"),
                    file.Name + " is not lower case hex");
            }

            // The file the engine actually loads, against the value the pinned revision publishes.
            Assert.AreEqual(
                "d7c9e0f5ca8592826c664c0f913e52a6d914abd2244f256d797c79cac637d684",
                VoiceModelStore.Files[0].Sha256);
        }

        [TestMethod]
        public void Revision_IsPinnedToACommitInsteadOfMaster()
        {
            // Master would let an upstream re-upload swap the model under a fixed checksum.
            Assert.AreNotEqual("master", VoiceModelStore.ModelRevision);
            Assert.AreEqual(40, VoiceModelStore.ModelRevision.Length);
        }

        [TestMethod]
        public void Download_AcceptsAFileWhoseChecksumMatches()
        {
            var dir = NewDirectory();
            try
            {
                var bytes = new byte[] { 1, 2, 3, 4 };
                using var client = new HttpClient(new StubHandler(("tokens.json", bytes)));

                VoiceModelStore
                    .DownloadAsync(
                        new[] { new VoiceModelFile("tokens.json", 4, Sha256(bytes)) },
                        dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_RejectsAFileWhoseChecksumDoesNotMatch()
        {
            var dir = NewDirectory();
            try
            {
                using var client = new HttpClient(new StubHandler(("tokens.json", new byte[] { 1, 2, 3, 4 })));

                var error = Assert.ThrowsException<IOException>(() => VoiceModelStore
                    .DownloadAsync(
                        new[] { new VoiceModelFile("tokens.json", 4, Sha256(new byte[] { 9, 9, 9, 9 })) },
                        dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

                StringAssert.Contains(error.Message, "sha256");
                Assert.IsFalse(File.Exists(Path.Combine(dir, "tokens.json")));
                Assert.IsFalse(File.Exists(Path.Combine(dir, "tokens.json.part")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_ChecksAResumedFileAsOneWholeFile()
        {
            var dir = NewDirectory();
            try
            {
                var full = new byte[] { 1, 2, 3, 4, 5 };
                File.WriteAllBytes(Path.Combine(dir, "tokens.json.part"), new byte[] { 1, 2 });
                var handler = new StubHandler(("tokens.json", full));
                using var client = new HttpClient(handler);

                VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 5, Sha256(full)) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new[] { "bytes=2-" }, handler.Ranges);
                CollectionAssert.AreEqual(full, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Download_FetchesACompleteLookingFileAgainWhenItsBytesAreWrong()
        {
            // Repairing a damaged model is the point of the check: the size is right, the bytes are not.
            var dir = NewDirectory();
            try
            {
                var good = new byte[] { 1, 2, 3, 4 };
                File.WriteAllBytes(Path.Combine(dir, "tokens.json"), new byte[] { 4, 3, 2, 1 });
                using var client = new HttpClient(new StubHandler(("tokens.json", good)));

                VoiceModelStore
                    .DownloadAsync(new[] { new VoiceModelFile("tokens.json", 4, Sha256(good)) }, dir, null, client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(good, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Verify_RejectsAMissingOrMismatchedFileAndAcceptsTheRightBytes()
        {
            var dir = NewDirectory();
            try
            {
                var good = new byte[] { 7, 7, 7 };
                var file = new VoiceModelFile("tokens.json", 3, Sha256(good));

                Assert.IsFalse(Verify(dir, file), "a missing file must not verify");

                File.WriteAllBytes(Path.Combine(dir, "tokens.json"), new byte[] { 8, 8, 8 });
                Assert.IsFalse(Verify(dir, file), "wrong bytes of the right size must not verify");

                File.WriteAllBytes(Path.Combine(dir, "tokens.json"), good);
                Assert.IsTrue(Verify(dir, file));
            }
            finally
            {
                CleanUp(dir);
            }
        }

        [TestMethod]
        public void Verify_ReportsTheCheckingPhaseAndFinishesAtOneHundredPercent()
        {
            var dir = NewDirectory();
            try
            {
                var good = new byte[] { 7, 7, 7 };
                var file = new VoiceModelFile("tokens.json", 3, Sha256(good));
                File.WriteAllBytes(Path.Combine(dir, "tokens.json"), good);
                var progress = new RecordingProgress();

                var verified = VoiceModelStore
                    .VerifyAsync(new[] { file }, dir, progress, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.IsTrue(verified);
                Assert.IsTrue(progress.Reports.Count > 0, "checking an installed model still has to report progress");
                Assert.IsTrue(
                    progress.Reports.All(p => p.Phase == VoiceModelPhase.Verifying || p.Phase == VoiceModelPhase.Completed),
                    "the window must be able to say it is checking, not downloading");
                Assert.AreEqual(100, progress.Reports.Last().Percent);
            }
            finally
            {
                CleanUp(dir);
            }
        }

        private static bool Verify(string directory, VoiceModelFile file) =>
            VoiceModelStore
                .VerifyAsync(new[] { file }, directory, null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        /// <summary>Records reports on the calling thread; <see cref="Progress{T}"/> would post them instead.</summary>
        private sealed class RecordingProgress : IProgress<VoiceModelProgress>
        {
            private readonly Action<VoiceModelProgress>? _onReport;

            public RecordingProgress(Action<VoiceModelProgress>? onReport = null) => _onReport = onReport;

            public List<VoiceModelProgress> Reports { get; } = new List<VoiceModelProgress>();

            public void Report(VoiceModelProgress value)
            {
                Reports.Add(value);
                _onReport?.Invoke(value);
            }
        }

        private static string Sha256(byte[] bytes) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        private static string NewDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "srcbox-voice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void CleanUp(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Serves the manifest from memory so the download logic is tested without a network.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Dictionary<string, byte[]> _files;

            public StubHandler(params (string Name, byte[] Bytes)[] files)
            {
                _files = files.ToDictionary(f => f.Name, f => f.Bytes);
            }

            public List<string?> Ranges { get; } = new List<string?>();

            /// <summary>Answers every ranged request with 416, the way a server that will not resume does.</summary>
            public bool RejectRanges { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var query = request.RequestUri!.Query;
                var name = Uri.UnescapeDataString(query.Substring(query.IndexOf("FilePath=", StringComparison.Ordinal) + "FilePath=".Length));
                if (name.Contains('&')) name = name.Substring(0, name.IndexOf('&'));

                var bytes = _files[name];
                var range = request.Headers.Range;
                Ranges.Add(range?.ToString());

                if (RejectRanges && range != null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
                }

                if (range?.Ranges.First().From is long from)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(bytes.Skip((int)from).ToArray())
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
        }
    }
}
