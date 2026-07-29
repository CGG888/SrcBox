using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services
{
    public class UserDataStore
    {
        private const int CurrentVersion = 1;
        private const int HistoryLimit = 200;
        private readonly string _path;
        private UserData _data = new UserData();

        public UserDataStore(string? baseDir = null)
        {
            var dir = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            _path = Path.Combine(dir, "user_data.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _data = JsonSerializer.Deserialize<UserData>(json, opt) ?? new UserData();
                }
                else
                {
                    _data = new UserData();
                }
                if (_data.Version <= 0) _data.Version = CurrentVersion;
                if (_data.Favorites == null) _data.Favorites = new List<string>();
                if (_data.History == null) _data.History = new List<HistoryItem>();
                Logger.Debug($"[UserDataStore.Load] favorites={_data.Favorites.Count} history={_data.History.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error("[UserDataStore.Load] " + ex.ToString());
                _data = new UserData();
            }
        }

        public void Save()
        {
            try
            {
                _data.Version = CurrentVersion;
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
                Logger.Trace($"[UserDataStore.Save] favorites={_data.Favorites.Count} history={_data.History.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error("[UserDataStore.Save] " + ex.ToString());
            }
        }

        public static string ComputeKey(Channel ch, string? url = null)
        {
            if (!string.IsNullOrWhiteSpace(ch.TvgId)) return ch.TvgId.Trim();
            var u = url ?? ch.Tag?.Url ?? (ch.Sources?.FirstOrDefault()?.Url ?? "");
            return $"{(ch.Name ?? "").Trim()}|{(u ?? "").Trim()}";
        }

        public bool IsFavorite(string key) => _data.Favorites.Contains(key, StringComparer.OrdinalIgnoreCase);

        public void SetFavorite(string key, bool on)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var exists = _data.Favorites.FindIndex(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
            if (on)
            {
                if (exists < 0)
                {
                    _data.Favorites.Add(key);
                }
            }
            else
            {
                if (exists >= 0) _data.Favorites.RemoveAt(exists);
            }
            Save();
        }

        public IReadOnlyList<string> GetFavorites() => _data.Favorites;

        public IReadOnlyList<HistoryItem> GetHistory() => _data.History.OrderByDescending(h => h.LastPlayedAtUtc).ToList();

        public void AddOrUpdateHistory(HistoryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Key)) return;
            // Remove existing with same key AND same play type (允许同一频道的直播/回放/时移并存)
            _data.History.RemoveAll(h =>
                string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(h.PlayType ?? "", item.PlayType ?? "", StringComparison.OrdinalIgnoreCase));
            item.LastPlayedAtUtc = DateTime.UtcNow;
            _data.History.Insert(0, item);
            // Trim
            if (_data.History.Count > HistoryLimit) _data.History.RemoveRange(HistoryLimit, _data.History.Count - HistoryLimit);
            Save();
        }

        public void RemoveHistory(string key)
        {
            _data.History.RemoveAll(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public void ClearHistory()
        {
            _data.History.Clear();
            Save();
        }
    }

    public class UserData
    {
        public int Version { get; set; } = 1;
        public List<string> Favorites { get; set; } = new List<string>();
        public List<HistoryItem> History { get; set; } = new List<HistoryItem>();
    }

    public class HistoryItem
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Logo { get; set; } = "";
        public string Group { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public string PlayType { get; set; } = "live"; // live|catchup|timeshift
        public double PositionSec { get; set; } // optional
        public double DurationSec { get; set; } // optional
        public DateTime LastPlayedAtUtc { get; set; } = DateTime.UtcNow;
        public string PlayTypeLabel
        {
            get
            {
                var t = (PlayType ?? "").ToLowerInvariant();
                if (t == "catchup") return "回放";
                if (t == "timeshift") return "时移";
                return "直播";
            }
        }
    }
}
