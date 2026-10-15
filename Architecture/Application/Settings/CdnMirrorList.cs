using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LibmpvIptvClient.Architecture.Application.Settings;

/// <summary>
/// The gh-proxy style mirrors used when <c>api.github.com</c> is unreachable or rate limited (which is the
/// normal case on a mainland connection). The list ships embedded in the assembly: the only copy in the
/// repository lives under <c>docs/public/screenshots/cdn.md</c>, which is published to the documentation
/// site and therefore never reached an installed build - both the update check and the update dialog used
/// to look for it in the publish folder and silently ended up with an empty mirror list.
/// A file next to the executable still wins, so a portable install can be re-pointed by hand.
/// </summary>
public static class CdnMirrorList
{
    /// <summary>Logical name of the embedded copy of docs/public/screenshots/cdn.md.</summary>
    internal const string ResourceName = "SrcBox.CdnMirrors.md";

    /// <summary>Relative locations still honoured next to the executable (portable override).</summary>
    static readonly string[] FileCandidates =
    {
        Path.Combine("docs", "screenshots", "cdn.md"),
        Path.Combine("docs", "cdn.md")
    };

    /// <summary>Mirror prefixes in the order they should be tried.</summary>
    public static IReadOnlyList<string> Load()
    {
        var mirrors = new List<string>();

        foreach (var line in ReadEmbeddedLines()) Add(mirrors, line);

        foreach (var relative in FileCandidates)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative);
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path)) Add(mirrors, line);
                }
            }
            catch { }
        }

        return mirrors;
    }

    static IEnumerable<string> ReadEmbeddedLines()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null) return Array.Empty<string>();

            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Keeps the http(s) entries, trimmed and de-duplicated, ignoring comments and blanks.</summary>
    static void Add(List<string> mirrors, string? line)
    {
        var value = (line ?? "").Trim();
        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        if (mirrors.Exists(m => string.Equals(m, value, StringComparison.OrdinalIgnoreCase))) return;
        mirrors.Add(value);
    }
}
