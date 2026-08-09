using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibmpvIptvClient.Services
{
    public static class UrlTimeRewriter
    {
        public static string RewriteIfEnabled(LibmpvIptvClient.PlaybackSettings settings, string url, DateTime start, DateTime end, bool isTimeshift)
        {
            if (settings == null) return url;
            var cfg = settings.TimeOverride;

            if (isTimeshift)
            {
                return RewriteUrlWithDuration(url, start, end);
            }

            if (cfg == null || !cfg.Enabled) return url;
            var mode = (cfg.Mode ?? "time_only").ToLowerInvariant();
            if (mode != "time_only" && mode != "replace_all") return url;
            var layout = (cfg.Layout ?? "start_end").ToLowerInvariant();
            var encoding = (cfg.Encoding ?? "local").ToLowerInvariant();
            var startKey = string.IsNullOrWhiteSpace(cfg.StartKey) ? "start" : cfg.StartKey;
            var endKey = string.IsNullOrWhiteSpace(cfg.EndKey) ? "end" : cfg.EndKey;
            var durationKey = string.IsNullOrWhiteSpace(cfg.DurationKey) ? "duration" : cfg.DurationKey;
            var playseekKey = string.IsNullOrWhiteSpace(cfg.PlayseekKey) ? "playseek" : cfg.PlayseekKey;
            var urlEncode = cfg.UrlEncode;

            string baseUrl = url;
            string path = url;
            string query = "";
            int qIdx = url.IndexOf('?');
            if (qIdx >= 0)
            {
                path = url.Substring(0, qIdx);
                query = qIdx < url.Length - 1 ? url.Substring(qIdx + 1) : "";
            }

            var items = ParseQueryOrdered(query);
            RemoveTimeParamsOrdered(items, startKey, endKey, durationKey, playseekKey);

            var beginStr = FormatTime(start, encoding);
            var endStr = FormatTime(end, encoding);
            var dur = end > start ? (long)(end - start).TotalSeconds : 0L;
            var durStr = encoding == "unix_ms" ? (dur * 1000L).ToString() : dur.ToString();

            var appended = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>();
            if (layout == "playseek")
            {
                var val = beginStr + "-" + endStr;
                appended.Add(new System.Collections.Generic.KeyValuePair<string, string>(playseekKey, urlEncode ? Uri.EscapeDataString(val) : val));
            }
            else if (layout == "start_duration" || layout == "auto")
            {
                appended.Add(new System.Collections.Generic.KeyValuePair<string, string>(startKey, urlEncode ? Uri.EscapeDataString(beginStr) : beginStr));
                appended.Add(new System.Collections.Generic.KeyValuePair<string, string>(durationKey, urlEncode ? Uri.EscapeDataString(durStr) : durStr));
            }
            else
            {
                appended.Add(new System.Collections.Generic.KeyValuePair<string, string>(startKey, urlEncode ? Uri.EscapeDataString(beginStr) : beginStr));
                appended.Add(new System.Collections.Generic.KeyValuePair<string, string>(endKey, urlEncode ? Uri.EscapeDataString(endStr) : endStr));
            }

            var rebuilt = BuildQueryOrdered(items, appended);
            if (rebuilt.Length == 0) return path;
            return path + "?" + rebuilt;
        }

        private static string RewriteUrlWithDuration(string url, DateTime start, DateTime end)
        {
            string path = url;
            string query = "";
            int qIdx = url.IndexOf('?');
            if (qIdx >= 0)
            {
                path = url.Substring(0, qIdx);
                query = qIdx < url.Length - 1 ? url.Substring(qIdx + 1) : "";
            }

            var items = ParseQueryOrdered(query);
            RemoveTimeParamsOrdered(items, "start", "end", "duration", "playseek");

            var beginStr = start.ToString("yyyyMMddHHmmss");
            var endStr = end.ToString("yyyyMMddHHmmss");

            var appended = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>();
            appended.Add(new KeyValuePair<string, string>("starttime", beginStr));
            appended.Add(new KeyValuePair<string, string>("endtime", endStr));

            var rebuilt = BuildQueryOrdered(items, appended);
            if (rebuilt.Length == 0) return path;
            return path + "?" + rebuilt;
        }

        static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>> ParseQueryOrdered(string query)
        {
            if (string.IsNullOrEmpty(query)) return new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>(0);
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>(parts.Length);
            foreach (var p in parts)
            {
                var eq = p.IndexOf('=');
                if (eq > 0)
                {
                    var k = p.Substring(0, eq);
                    var v = eq < p.Length - 1 ? p.Substring(eq + 1) : "";
                    list.Add(new System.Collections.Generic.KeyValuePair<string, string>(k, v));
                }
                else
                {
                    list.Add(new System.Collections.Generic.KeyValuePair<string, string>(p, ""));
                }
            }
            return list;
        }

        static void RemoveTimeParamsOrdered(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>> items, string startKey, string endKey, string durationKey, string playseekKey)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "start","starttime","begin", startKey,
                "end","endtime","finish", endKey,
                "duration", durationKey,
                "playseek", playseekKey
            };
            // O(n) instead of O(n*m) - RemoveAll predicate traverses list once
            items.RemoveAll(kv => keys.Contains(kv.Key));
        }

        static string FormatTime(DateTime t, string encoding)
        {
            if (encoding == "unix")
            {
                var s = new DateTimeOffset(t).ToUnixTimeSeconds();
                return s.ToString();
            }
            if (encoding == "unix_ms")
            {
                var ms = new DateTimeOffset(t).ToUnixTimeMilliseconds();
                return ms.ToString();
            }
            if (encoding == "utc")
            {
                return t.ToUniversalTime().ToString("yyyyMMddHHmmss");
            }
            return t.ToString("yyyyMMddHHmmss");
        }

        static string BuildQueryOrdered(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>> items, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>> appended)
        {
            int ic = items?.Count ?? 0;
            int ac = appended?.Count ?? 0;
            if (ic == 0 && ac == 0) return "";
            var total = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>(ic + ac);
            if (ic > 0) total.AddRange(items);
            if (ac > 0) total.AddRange(appended);
            var sb = new StringBuilder();
            bool first = true;
            foreach (var e in total)
            {
                if (!first) sb.Append('&');
                first = false;
                sb.Append(e.Key);
                if (e.Value != null)
                {
                    sb.Append('=');
                    sb.Append(e.Value);
                }
            }
            return sb.ToString();
        }
    }
}
