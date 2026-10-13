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
                "https://modelscope.cn/api/v1/models/manyeyes/sensevoice-small-wenetspeech-yue-int8-onnx/repo?Revision=master&FilePath=model.int8.onnx",
                VoiceModelStore.Files[0].Url);

            StringAssert.Contains(VoiceModelStore.BuildUrl("tokens.txt"), "FilePath=tokens.txt");
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
                var reported = new List<double>();

                VoiceModelStore
                    .DownloadAsync(files, dir, new Progress<double>(reported.Add), client, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, "tokens.json")));
                CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Path.Combine(dir, "am.mvn")));
                Assert.IsFalse(File.Exists(Path.Combine(dir, "tokens.json.part")));
                Assert.AreEqual(100d, reported.Last());
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

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var query = request.RequestUri!.Query;
                var name = Uri.UnescapeDataString(query.Substring(query.IndexOf("FilePath=", StringComparison.Ordinal) + "FilePath=".Length));
                if (name.Contains('&')) name = name.Substring(0, name.IndexOf('&'));

                var bytes = _files[name];
                var range = request.Headers.Range;
                Ranges.Add(range?.ToString());

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
