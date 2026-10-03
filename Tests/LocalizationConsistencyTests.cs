using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Guards the localization files: the four languages must define the same keys, and every text key the
    /// code or XAML asks for must exist. Both invariants had drifted (4 keys resolved to an empty dynamic
    /// resource, 35 keys fell back to hard coded Chinese in every language).
    /// The language files are copied next to the tests, so the key-set check always runs; the "referenced
    /// keys" check needs the source tree and simply does nothing when it is not available.
    /// </summary>
    [TestClass]
    public class LocalizationConsistencyTests
    {
        static readonly string[] Languages = { "zh-CN", "en-US", "zh-TW", "ru-RU" };

        // Only text keys are compared across languages; brush/style keys live in other dictionaries.
        static readonly string[] TextPrefixes =
        {
            "UI_", "Menu_", "Common_", "Overlay_", "Drawer_", "Subtitle_", "M3U_", "Prompt_", "Confirm_",
            "Dialog_", "Btn_", "Progress_", "Update_", "Recording_", "Msg_", "Reminder_", "EPG_",
            "ScheduledRecordingList_", "About_", "Err_", "Link_", "Ratio_", "Settings_",
            "CloseConfirm_", "Timeshift", "PlayMode_", "Source_"
        };

        /// <summary>Directory of the test assembly itself. AppContext.BaseDirectory points at the test host
        /// (the reflection runner), which is not where the language files were copied to.</summary>
        static string TestDirectory()
            => Path.GetDirectoryName(typeof(LocalizationConsistencyTests).Assembly.Location) ?? AppContext.BaseDirectory;

        static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestDirectory());
            for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LibmpvIptvClient.csproj"))) return dir.FullName;
            }
            return null;
        }

        static string? LanguageFile(string lang)
        {
            var copied = Path.Combine(TestDirectory(), "Resources", $"Strings.{lang}.xaml");
            if (File.Exists(copied)) return copied;

            var root = FindRepoRoot();
            if (root != null)
            {
                var inRepo = Path.Combine(root, "Resources", $"Strings.{lang}.xaml");
                if (File.Exists(inRepo)) return inRepo;
            }
            return null;
        }

        static HashSet<string> ReadKeys(string path)
        {
            var text = File.ReadAllText(path);
            return Regex.Matches(text, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                        .ToHashSet(StringComparer.Ordinal);
        }

        static bool IsTextKey(string key) => TextPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

        [TestMethod]
        public void EveryLanguageDefinesTheSameKeys()
        {
            var sets = new Dictionary<string, HashSet<string>>();
            foreach (var lang in Languages)
            {
                var path = LanguageFile(lang);
                if (path == null)
                {
                    Console.WriteLine($"找不到 {lang} 语言文件，跳过该检查");
                    return;
                }
                sets[lang] = ReadKeys(path);
            }

            var union = sets.Values.SelectMany(s => s).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var lang in Languages)
            {
                var missing = union.Where(k => !sets[lang].Contains(k)).ToList();
                Assert.AreEqual(0, missing.Count,
                    $"{lang} 缺少 {missing.Count} 个 key: {string.Join(", ", missing.Take(20))}");
            }
        }

        [TestMethod]
        public void EveryReferencedTextKeyIsDefined()
        {
            var root = FindRepoRoot();
            if (root == null)
            {
                Console.WriteLine("不在源码树中运行，跳过引用检查");
                return;
            }

            var defined = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
                foreach (var key in ReadKeys(file)) defined.Add(key);
            }

            var referenced = new HashSet<string>(StringComparer.Ordinal);
            var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                                   .Concat(Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories));
            foreach (var file in sources)
            {
                if (file.Contains(@"\obj\") || file.Contains(@"\bin\") || file.Contains(@"\Resources\")) continue;
                var text = File.ReadAllText(file);

                foreach (Match m in Regex.Matches(text, "(?:Localizer\\.S|ResxLocalizer\\.Get|Localizer\\.Get)\\(\"([A-Za-z][A-Za-z0-9_]+)\""))
                    referenced.Add(m.Groups[1].Value);

                foreach (Match m in Regex.Matches(text, "\\{DynamicResource\\s+([A-Za-z][A-Za-z0-9_]+)\\}"))
                    referenced.Add(m.Groups[1].Value);
            }

            var missing = referenced.Where(k => IsTextKey(k) && !defined.Contains(k))
                                    .OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.AreEqual(0, missing.Count,
                $"有 {missing.Count} 个文本 key 未定义（界面会空白或显示硬编码中文）: {string.Join(", ", missing.Take(20))}");
        }
    }
}
