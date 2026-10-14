using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The vocabulary published under voice/ is what a fresh install and the upgrade check read. It has to
    /// stay identical to the built-in tables, so a word added in code is a word that must be published too.
    /// </summary>
    [TestClass]
    public class VoiceGrammarGoldenTests
    {
        private const string PackPath = "voice/v1/zh-CN.json";
        private const string ManifestPath = "voice/manifest.json";

        [TestMethod]
        public void PublishedPack_MatchesTheBuiltInTables()
        {
            var published = VoiceGrammarPack.Parse(File.ReadAllText(RepositoryFile(PackPath)), out var error);
            Assert.IsNotNull(published, error);

            var problems = published!.Validate();
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));

            // The published copy is generated from the built-in tables; only the date it was cut is its own.
            var expected = VoiceGrammar.Default.AsPack();
            published.Updated = expected.Updated = "";

            Assert.AreEqual(expected.ToJson(), published.ToJson(),
                "voice/ is out of date: set SRCBOX_WRITE_GOLDEN=1 and run this test class to regenerate it");
        }

        [TestMethod]
        public void PublishedPack_IsWhatTheGrammarLoads()
        {
            var published = VoiceGrammarPack.Parse(File.ReadAllText(RepositoryFile(PackPath)), out var error);
            Assert.IsNotNull(published, error);

            var merged = VoiceGrammar.Merge(VoiceGrammar.Default, published!, PackPath);

            Assert.AreEqual(VoiceGrammar.Default.Version, merged.Version);
            CollectionAssert.AreEqual(VoiceGrammar.Default.NoiseWords.ToList(), merged.NoiseWords.ToList());
            CollectionAssert.AreEqual(VoiceGrammar.Default.Prepositions.ToList(), merged.Prepositions.ToList());
            CollectionAssert.AreEqual(VoiceGrammar.Default.Day.ToList(), merged.Day.ToList());
            CollectionAssert.AreEqual(VoiceGrammar.Default.Weekdays.Select(d => d.Day).ToList(),
                merged.Weekdays.Select(d => d.Day).ToList());
            CollectionAssert.AreEqual(VoiceGrammar.Default.Parts.Select(Describe).ToList(),
                merged.Parts.Select(Describe).ToList());
            CollectionAssert.AreEqual(VoiceGrammar.Default.Patterns.Select(p => p.Matcher.ToString()).ToList(),
                merged.Patterns.Select(p => p.Matcher.ToString()).ToList());
        }

        [TestMethod]
        public void PublishedManifest_PointsAtAPackThatIsThereAndMatchesItsHash()
        {
            var manifest = VoiceGrammarManifest.Parse(File.ReadAllText(RepositoryFile(ManifestPath)), out var error);
            Assert.IsNotNull(manifest, error);
            Assert.IsTrue(manifest!.Packs.Count > 0, "the manifest lists no pack");

            foreach (var pack in manifest.Packs)
            {
                StringAssert.StartsWith(pack.Url, "https://github.com/CGG888/SrcBox/releases/download/voice-pack-",
                    $"{pack.Lang}: the release a pack is published under has to carry its version");
                StringAssert.EndsWith(pack.Url, "/" + Path.GetFileName(pack.Path));

                var file = RepositoryFile(pack.Path);
                Assert.IsTrue(File.Exists(file), pack.Path + " is missing");
                Assert.AreEqual(new FileInfo(file).Length, pack.Bytes, pack.Path + ": the manifest byte count is stale");

                using var stream = File.OpenRead(file);
                var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                Assert.AreEqual(hash, pack.Sha256, pack.Path + ": the manifest checksum is stale");
            }

            // The language the app ships with has to resolve, whatever else the manifest carries.
            Assert.IsNotNull(manifest.For("zh-CN"));
        }

        [TestMethod]
        public void RewriteThePublishedPack()
        {
            if (Environment.GetEnvironmentVariable("SRCBOX_WRITE_GOLDEN") != "1")
            {
                Assert.Inconclusive("set SRCBOX_WRITE_GOLDEN=1 to regenerate " + PackPath);
                return;
            }

            var pack = VoiceGrammar.Default.AsPack();
            pack.Updated = DateTime.Now.ToString("yyyy-MM-dd");
            var path = RepositoryFile(PackPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, pack.ToJson() + Environment.NewLine);
            Console.WriteLine($"wrote {path} ({new FileInfo(path).Length} bytes)");
        }

        // ---------------------------------------------------------------- fixtures

        private static string Describe(VoiceGrammarPart part) =>
            $"{part.From}-{part.To} {part.Label}: {string.Join(",", part.Words)}";

        /// <summary>
        /// Where the repository is. The caller's own path is compiled in, which keeps the lookup working when
        /// the tests are built into a scratch directory outside the working copy.
        /// </summary>
        private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? ".");
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Tests")) &&
                    File.Exists(Path.Combine(directory.FullName, "LibmpvIptvClient.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            Assert.Fail("the repository root was not found from " + sourceFile);
            return "";
        }

        private static string RepositoryFile(string relative) =>
            Path.Combine(RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
