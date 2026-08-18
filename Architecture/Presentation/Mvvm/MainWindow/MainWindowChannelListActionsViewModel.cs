using System;
using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

public sealed record ChannelFilterResult(IReadOnlyList<Channel> Channels, string CountText);

public sealed class ChannelGroupItem
{
    public string Name { get; set; } = "";
    public List<Channel> Items { get; set; } = new List<Channel>();
    public int Count => Items?.Count ?? 0;
}

public sealed class MainWindowChannelListActionsViewModel : ViewModelBase
{
    public ChannelFilterResult BuildChannelFilterResult(
        IReadOnlyList<Channel> channels,
        string searchKey,
        string? selectedGroup)
    {
        var safeSearch = (searchKey ?? "").Trim();
        IEnumerable<Channel> baseList = channels ?? Array.Empty<Channel>();
        if (!string.IsNullOrEmpty(safeSearch))
        {
            var q = safeSearch.ToLowerInvariant();
            baseList = baseList.Where(c => c?.Name != null && c.Name.ToLowerInvariant().Contains(q));
        }
        if (!string.IsNullOrEmpty(selectedGroup))
        {
            baseList = baseList.Where(c => string.Equals(c.Group, selectedGroup, StringComparison.OrdinalIgnoreCase));
        }
        var distinct = DistinctByName(baseList);
        var totalCount = distinct.Count;
        var countText = string.IsNullOrEmpty(safeSearch)
            ? string.Format(LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_CountAll", "共 {0} 个频道"), totalCount)
            : string.Format(LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_FilteredCount", "筛选到 {0} / {1}"), distinct.Count, totalCount);
        return new ChannelFilterResult(distinct, countText);
    }

    public IReadOnlyList<ChannelGroupItem> BuildGroups(IReadOnlyList<Channel> channels, string? sourceUrl = null)
    {
        var map = new Dictionary<string, List<Channel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in channels ?? Array.Empty<Channel>())
        {
            if (string.IsNullOrWhiteSpace(c.Group)) continue;
            if (!map.TryGetValue(c.Group, out var list))
            {
                list = new List<Channel>();
                map[c.Group] = list;
            }
            list.Add(c);
        }
        var groups = new List<ChannelGroupItem>();
        foreach (var kv in map)
        {
            var items = DistinctByName(kv.Value);
            for (int i = 0; i < items.Count; i++) items[i].DisplayIndex = i + 1;
            groups.Add(new ChannelGroupItem { Name = $"{kv.Key}", Items = items });
        }
        var savedOrder = ResolveGroupOrder(sourceUrl);
        groups.Sort((a, b) =>
        {
            int indexA = savedOrder.IndexOf(a.Name);
            int indexB = savedOrder.IndexOf(b.Name);
            if (indexA >= 0 && indexB >= 0) return indexA.CompareTo(indexB);
            if (indexA >= 0) return -1;
            if (indexB >= 0) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return groups;
    }

    private List<string> ResolveGroupOrder(string? sourceUrl)
    {
        var bySource = AppSettings.Current.ChannelGroupOrderBySource;
        if (!string.IsNullOrEmpty(sourceUrl) && bySource != null
            && bySource.TryGetValue(sourceUrl, out var perSource) && perSource != null && perSource.Count > 0)
            return perSource;
        return AppSettings.Current.ChannelGroupOrder ?? new List<string>();
    }

    public IReadOnlyList<Channel> BuildDistinctByNamePreserveOrder(IReadOnlyList<Channel> list)
    {
        var map = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Channel>();
        if (list == null) return result;
        foreach (var c in list)
        {
            if (c == null || c.Name == null) continue;
            if (map.Add(c.Name)) result.Add(c);
        }
        return result;
    }

    public void ComputeGlobalIndices(List<Channel> channels)
    {
        if (channels == null) return;
        var distinct = BuildDistinctByNamePreserveOrder(channels);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < distinct.Count; i++)
        {
            var key = distinct[i].Name ?? "";
            map[key] = i + 1;
        }
        foreach (var ch in channels)
        {
            if (ch?.Name == null) continue;
            if (map.TryGetValue(ch.Name, out var idx)) ch.GlobalIndex = idx;
        }
    }

    private List<Channel> DistinctByName(IEnumerable<Channel> list)
    {
        var map = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in list)
        {
            if (c == null) continue;
            if (!map.TryGetValue(c.Name, out var exist))
            {
                map[c.Name] = c;
            }
            else
            {
                var scoreExist = ScoreChannel(exist);
                var scoreNew = ScoreChannel(c);
                if (scoreNew > scoreExist) map[c.Name] = c;
                if (string.IsNullOrWhiteSpace(map[c.Name].Logo) && !string.IsNullOrWhiteSpace(c.Logo)) map[c.Name].Logo = c.Logo;
                // Merge sources from duplicate channel
                if (c.Sources != null)
                {
                    foreach (var src in c.Sources)
                    {
                        if (src == null || string.IsNullOrWhiteSpace(src.Url)) continue;
                        if (!map[c.Name].Sources.Any(s => string.Equals(s.Url, src.Url, StringComparison.OrdinalIgnoreCase)))
                        {
                            map[c.Name].Sources.Add(src);
                        }
                    }
                }
            }
        }
        return new List<Channel>(map.Values);
    }

    private int ScoreChannel(Channel c)
    {
        int score = 0;
        if (!string.IsNullOrWhiteSpace(c.Logo)) score += 1;
        if (!string.IsNullOrWhiteSpace(c.Group) && c.Group.Contains("4K", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }
}
