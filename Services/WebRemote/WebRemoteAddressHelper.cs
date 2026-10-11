using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LibmpvIptvClient.Services.WebRemote
{
    /// <summary>
    /// Resolves the LAN IPv4 addresses used to build the Web Remote access URL (QR code / copyable link).
    /// </summary>
    public static class WebRemoteAddressHelper
    {
        private const string DefaultScheme = "http";

        /// <summary>
        /// All usable LAN IPv4 addresses, most likely one first (physical adapters on private ranges win).
        /// </summary>
        public static IReadOnlyList<string> GetLocalIPv4Addresses()
        {
            var candidates = new List<(int Score, string Address)>();

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var physical = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet3Megabit
                        || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                        || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT
                        || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx;

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = unicast.Address.ToString();
                        if (!IsUsable(ip)) continue;

                        var score = physical ? 2 : 0;
                        if (IsPrivateRange(ip)) score += 1;
                        candidates.Add((score, ip));
                    }
                }
            }
            catch
            {
                // Network enumeration can fail on exotic adapters; fall back to "no address".
            }

            return candidates
                .OrderByDescending(c => c.Score)
                .Select(c => c.Address)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Best guess for the address a phone on the same network should use, or null when none was found.
        /// </summary>
        public static string? GetPreferredLocalIPv4()
        {
            var all = GetLocalIPv4Addresses();
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Builds the Web Remote URL, e.g. http://192.168.1.20:8899/
        /// </summary>
        public static string BuildUrl(string? host, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0) return "";
            return $"{DefaultScheme}://{host}:{port}/";
        }

        internal static bool IsUsable(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (!IPAddress.TryParse(ip, out var address)) return false;
            if (IPAddress.IsLoopback(address)) return false;

            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;
            if (bytes[0] == 169 && bytes[1] == 254) return false; // APIPA / link-local
            if (address.Equals(IPAddress.Any)) return false;
            return true;
        }

        internal static bool IsPrivateRange(string ip)
        {
            if (!IPAddress.TryParse(ip, out var address)) return false;
            var b = address.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 10) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            return false;
        }
    }
}
