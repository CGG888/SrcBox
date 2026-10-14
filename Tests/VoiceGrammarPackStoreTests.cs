using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Installing a grammar pack: what is accepted, what is refused, and what a refused pack leaves behind.
    /// </summary>
    [TestClass]
    public class VoiceGrammarPackStoreTests
    {
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "srcbox-voice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            VoiceGrammar.DirectoryOverride = _dir;
            VoiceGrammar.Reload();
        }

        [TestCleanup]
        public void Cleanup()
        {
            VoiceGrammar.DirectoryOverride = null;
            VoiceGrammar.Reload();
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { }
        }

        [TestMethod]
        public async Task InstallAsync_PutsThePackInForce()
        {
            var json = Pack("zh-CN", "2026.10.14", "\"回看\", \"节目\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"));
            var result = await VoiceGrammarPackStore.InstallAsync(Entry(json), null, CancellationToken.None, null, new Serve(json));

            Assert.IsTrue(result, VoiceGrammarPackStore.LastError);
            Assert.AreEqual("", VoiceGrammarPackStore.LastError);

            var grammar = VoiceGrammar.Current;
            Assert.IsFalse(grammar.IsBuiltIn);
            Assert.AreEqual("2026.10.14", grammar.Version);
            Assert.AreEqual("zh-CN", grammar.Lang);
            Assert.AreEqual(2, grammar.NoiseWords.Count);
            Assert.AreEqual(1, grammar.Patterns.Count);

            // The sections the pack leaves out must still come from the built-in tables.
            Assert.AreEqual(VoiceGrammar.Default.Day.Count, grammar.Day.Count);
            Assert.AreEqual(VoiceGrammar.Default.Weekdays.Count, grammar.Weekdays.Count);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "current.json")));
        }

        [TestMethod]
        public async Task InstallAsync_RefusesAPackThatDoesNotMatchItsChecksum()
        {
            var json = Pack("zh-CN", "2026.10.14", "\"回看\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"));
            var entry = Entry(json);
            entry.Sha256 = new string('0', 64);

            var result = await VoiceGrammarPackStore.InstallAsync(entry, null, CancellationToken.None, null, new Serve(json));

            Assert.IsFalse(result);
            StringAssert.Contains(VoiceGrammarPackStore.LastError, "checksum");
            Assert.IsTrue(VoiceGrammar.Current.IsBuiltIn);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "current.json")));
        }

        [TestMethod]
        public async Task InstallAsync_RefusesAPackThatDoesNotValidate()
        {
            var json = Pack("zh-CN", "2026.10.14", "\"回看\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"))
                .Replace("\"schema\": 1", "\"schema\": 9");

            var result = await VoiceGrammarPackStore.InstallAsync(Entry(json), null, CancellationToken.None, null, new Serve(json));

            Assert.IsFalse(result);
            StringAssert.Contains(VoiceGrammarPackStore.LastError, "not usable");
            Assert.IsTrue(VoiceGrammar.Current.IsBuiltIn);
        }

        [TestMethod]
        public async Task InstallAsync_RefusesAPackOfTheWrongSize()
        {
            var json = Pack("zh-CN", "2026.10.14", "\"回看\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"));
            var entry = Entry(json);
            entry.Bytes += 10;

            var result = await VoiceGrammarPackStore.InstallAsync(entry, null, CancellationToken.None, null, new Serve(json));

            Assert.IsFalse(result);
            StringAssert.Contains(VoiceGrammarPackStore.LastError, "bytes");
            Assert.IsTrue(VoiceGrammar.Current.IsBuiltIn);
        }

        [TestMethod]
        public async Task Remove_TakesTheGrammarBackToTheBuiltIn()
        {
            var json = Pack("zh-CN", "2026.10.14", "\"回看\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"));
            Assert.IsTrue(await VoiceGrammarPackStore.InstallAsync(Entry(json), null, CancellationToken.None, null, new Serve(json)));
            Assert.IsFalse(VoiceGrammar.Current.IsBuiltIn);

            Assert.IsTrue(VoiceGrammarPackStore.Remove());

            Assert.IsTrue(VoiceGrammar.Current.IsBuiltIn);
            Assert.AreEqual(VoiceGrammar.BuiltInVersion, VoiceGrammar.InstalledVersion);
            Assert.AreEqual(0, Directory.GetFiles(_dir, "pack-*.json").Length);
        }

        [TestMethod]
        public void InstallFromFile_InstallsAFileTheUserPicked()
        {
            var json = Pack("zh-CN", "2026.11.01", "\"回看\", \"节目\", \"看看\"", Phrase("switch_channel", "(?<num>[0-9]{1,3})"));
            var path = Path.Combine(_dir, "handmade.json");
            File.WriteAllText(path, json);

            Assert.IsTrue(VoiceGrammarPackStore.InstallFromFile(path), VoiceGrammarPackStore.LastError);

            Assert.AreEqual("2026.11.01", VoiceGrammar.Current.Version);
            Assert.AreEqual(3, VoiceGrammar.Current.NoiseWords.Count);
        }

        [TestMethod]
        public void InstallFromFile_RefusesAFileThatDoesNotValidate()
        {
            var path = Path.Combine(_dir, "broken.json");
            File.WriteAllText(path, "{ \"schema\": 1, \"lang\": \"zh-CN\", \"version\": \"1\", \"phrases\": [ { \"name\": \"x\", \"verb\": \"Teleport\", \"pattern\": \"x\" } ] }");

            Assert.IsFalse(VoiceGrammarPackStore.InstallFromFile(path));
            StringAssert.Contains(VoiceGrammarPackStore.LastError, "Teleport");
            Assert.IsTrue(VoiceGrammar.Current.IsBuiltIn);
        }

        [DataTestMethod]
        [DataRow("2026.10.14", "", true)]
        [DataRow("2026.10.14", "2026.10.13", true)]
        [DataRow("2026.10.14", "2026.10.14", false)]
        [DataRow("2026.10.13", "2026.10.14", false)]
        [DataRow("", "2026.10.14", false)]
        [DataRow("v2026.10.14", "2026.10.13", true)]
        public void IsNewer_ComparesTheVersionStrings(string candidate, string current, bool expected)
        {
            Assert.AreEqual(expected, VoiceGrammarPackStore.IsNewer(candidate, current));
        }

        [TestMethod]
        public void ManifestUrls_TryTheReleaseThenTheMirrorsThenTheCdn()
        {
            var urls = VoiceGrammarPackStore.ManifestUrls(new[] { "https://ghproxy.net/", "  " });

            Assert.AreEqual(3, urls.Count);
            StringAssert.StartsWith(urls[0], "https://github.com/CGG888/SrcBox/releases/download/voice-pack/");
            Assert.AreEqual("https://ghproxy.net/" + urls[0], urls[1]);
            StringAssert.Contains(urls[2], "cdn.jsdelivr.net/gh/CGG888/SrcBox@main/voice/manifest.json");
        }

        [TestMethod]
        public void PackUrls_TryTheAssetThenTheCdnThenTheMirrors()
        {
            var entry = new VoiceGrammarPackEntry
            {
                Url = "https://github.com/CGG888/SrcBox/releases/download/voice-pack-2026.10.14/zh-CN.json",
                Path = "voice/2026.10.14/zh-CN.json"
            };

            var urls = VoiceGrammarPackStore.PackUrls(entry, new[] { "https://ghproxy.net" });

            Assert.AreEqual(3, urls.Count);
            Assert.AreEqual(entry.Url, urls[0]);
            StringAssert.Contains(urls[1], "cdn.jsdelivr.net/gh/CGG888/SrcBox@main/voice/2026.10.14/zh-CN.json");
            Assert.AreEqual("https://ghproxy.net/" + entry.Url, urls[2]);
        }

        [TestMethod]
        public void Manifest_ReadsTheLanguageAndFallsBack()
        {
            const string json = """
            {
              "schema": 1,
              "updated": "2026-10-14",
              "packs": [
                { "lang": "zh-CN", "version": "2026.10.14", "path": "voice/2026.10.14/zh-CN.json", "bytes": 2048 },
                { "lang": "en-US", "version": "2026.10.14", "path": "voice/2026.10.14/en-US.json", "bytes": 2048 }
              ]
            }
            """;

            var manifest = VoiceGrammarManifest.Parse(json, out var error);
            Assert.IsNotNull(manifest, error);
            Assert.AreEqual("2026-10-14", manifest!.Updated);
            Assert.AreEqual("zh-CN", manifest.For("zh-CN")!.Lang);
            Assert.AreEqual("en-US", manifest.For("EN-us")!.Lang);
            Assert.AreEqual("zh-CN", manifest.For("ru-RU")!.Lang);
            Assert.AreEqual("zh-CN", manifest.For(null)!.Lang);
        }

        [TestMethod]
        public void Manifest_RefusesAForeignSchema()
        {
            Assert.IsNull(VoiceGrammarManifest.Parse("{\"schema\":2,\"packs\":[]}", out var error));
            StringAssert.Contains(error, "schema 2");
        }

        [TestMethod]
        public async Task FetchManifestAsync_ReadsTheFirstUrlThatAnswers()
        {
            const string json = "{\"schema\":1,\"updated\":\"2026-10-14\",\"packs\":[{\"lang\":\"zh-CN\",\"version\":\"1\"}]}";
            var handler = new Serve(json);

            var manifest = await VoiceGrammarPackStore.FetchManifestAsync(
                new[] { "https://example.invalid/manifest.json" }, CancellationToken.None, handler);

            Assert.IsNotNull(manifest, VoiceGrammarPackStore.LastError);
            Assert.AreEqual(1, handler.Calls);
            Assert.AreEqual("zh-CN", manifest!.For("zh-CN")!.Lang);
        }

        [TestMethod]
        public async Task FetchManifestAsync_SaysWhyWhenNothingAnswers()
        {
            var handler = new Serve("nope", HttpStatusCode.NotFound);

            var manifest = await VoiceGrammarPackStore.FetchManifestAsync(
                new[] { "https://example.invalid/manifest.json" }, CancellationToken.None, handler);

            Assert.IsNull(manifest);
            StringAssert.Contains(VoiceGrammarPackStore.LastError, "404");
        }

        // ---------------------------------------------------------------- fixtures

        private static VoiceGrammarPackEntry Entry(string json) => new()
        {
            Lang = "zh-CN",
            Version = "2026.10.14",
            Url = "https://example.invalid/zh-CN.json",
            Sha256 = Sha256(json),
            Bytes = Encoding.UTF8.GetByteCount(json)
        };

        private static string Pack(string lang, string version, string noiseWords, string phrases) =>
            "{ \"schema\": 1," +
            $" \"lang\": \"{lang}\", \"version\": \"{version}\"," +
            $" \"noiseWords\": [{noiseWords}], \"phrases\": [{phrases}] }}";

        private static string Phrase(string name, string pattern) =>
            $"{{ \"name\": \"{name}\", \"verb\": \"SwitchChannel\", \"pattern\": \"{pattern.Replace("\\", "\\\\")}\" }}";

        private static string Sha256(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        /// <summary>Answers every request with one body, so the store's URL walk can be observed.</summary>
        private sealed class Serve : HttpMessageHandler
        {
            private readonly string _body;
            private readonly HttpStatusCode _status;

            public Serve(string body, HttpStatusCode status = HttpStatusCode.OK)
            {
                _body = body;
                _status = status;
            }

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(_body))
                });
            }
        }
    }
}
