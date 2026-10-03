using System;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Diagnostics
{
    public static class LogRedactor
    {
        // Regex to identify URLs (HTTP/HTTPS/FTP/RTSP/UDP/MMS)
        // [^\s"<>]+ matches non-whitespace and non-quote characters, common delimiters in logs
        private static readonly Regex UrlRegex = new Regex(@"(https?|ftp|rtmp|rtsp|udp|mms)://[^\s""<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        // Regex for sensitive query parameters
        private static readonly Regex QueryParamRegex = new Regex(@"([?&])(token|key|pass|password|auth|sig|sign|access_token|secret)=([^&\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        // Regex for IPv4 addresses (standalone)
        private static readonly Regex IpRegex = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);

        // key: value / key=value / "key": "value" forms outside urls (the query rule only covers urls).
        // Without this a line like "Authentication failed with password: hunter2" was written as is.
        private static readonly Regex SecretValueRegex = new Regex(
            @"(?i)\b(password|passwd|pwd|secret|token|apikey|api_key|access_token)""?\s*[:=]\s*""?([^\s"";,}]+)""?",
            RegexOptions.Compiled);

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            try
            {
                // 1. Redact URLs
                string processed = UrlRegex.Replace(input, match => 
                {
                    string originalUrl = match.Value;
                    
                    // Trim common trailing punctuation that might be captured
                    string trailing = "";
                    if (originalUrl.EndsWith(".")) { originalUrl = originalUrl.TrimEnd('.'); trailing = "."; }
                    else if (originalUrl.EndsWith(",")) { originalUrl = originalUrl.TrimEnd(','); trailing = ","; }
                    else if (originalUrl.EndsWith(";")) { originalUrl = originalUrl.TrimEnd(';'); trailing = ";"; }
                    else if (originalUrl.EndsWith(")")) { originalUrl = originalUrl.TrimEnd(')'); trailing = ")"; }
                    else if (originalUrl.EndsWith("]")) { originalUrl = originalUrl.TrimEnd(']'); trailing = "]"; }

                    if (Uri.TryCreate(originalUrl, UriKind.Absolute, out Uri? uri))
                    {
                        try
                        {
                            var scheme = uri.Scheme;
                            var host = uri.Host;
                            var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
                            var path = uri.AbsolutePath;
                            var query = uri.Query;
                            var fragment = uri.Fragment;
                            var userInfo = uri.UserInfo;

                            // Redact Host (Domain or Public IP)
                            // Allow localhost to remain visible for debugging local issues
                            string redactedHost = "***";
                            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "::1") 
                            {
                                redactedHost = host;
                            }

                            // Redact UserInfo
                            string redactedUserInfo = "";
                            if (!string.IsNullOrEmpty(userInfo))
                            {
                                redactedUserInfo = "***:***@";
                            }

                            // Redact Query Params
                            if (!string.IsNullOrEmpty(query))
                            {
                                query = QueryParamRegex.Replace(query, "$1$2=***");
                            }

                            return $"{scheme}://{redactedUserInfo}{redactedHost}{port}{path}{query}{fragment}{trailing}";
                        }
                        catch
                        {
                            return RedactFallback(originalUrl) + trailing;
                        }
                    }
                    else
                    {
                        return RedactFallback(originalUrl) + trailing;
                    }
                });

                // 2. Redact standalone IPs (not part of URL)
                processed = IpRegex.Replace(processed, ipMatch => 
                {
                    string ip = ipMatch.Value;
                    if (ip == "127.0.0.1" || ip == "0.0.0.0") return ip;
                    // Check if this IP is inside an already redacted URL (simple heuristic check)
                    // If the input string was modified by UrlRegex, the IP might have been replaced by ***
                    // But IpRegex runs on the result. 
                    // If the URL was http://1.2.3.4, it is now http://***
                    // So IpRegex won't find it.
                    // If there is a standalone IP "Connected to 1.2.3.4", it will be found here.
                    return "***.***.***.***"; 
                });

                // 3. Redact key/value style secrets that are not part of a url
                processed = SecretValueRegex.Replace(processed, m => m.Groups[1].Value + "=***");

                return processed;
            }
            catch
            {
                return input; 
            }
        }

        private static string RedactFallback(string url)
        {
            // Simple regex fallback for malformed URLs or failure cases
            // Mask user info: ://user:pass@ -> ://***:***@
            url = Regex.Replace(url, @"://([^:/@]+):([^@]+)@", "://***:***@");
            
            // Mask host: try to find host part between :// and / or :
            // This is tricky with regex alone, so we just do best effort on query params
            url = QueryParamRegex.Replace(url, "$1$2=***");
            
            return url;
        }
    }
}
