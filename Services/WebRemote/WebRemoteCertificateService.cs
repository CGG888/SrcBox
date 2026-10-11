using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services.WebRemote
{
    /// <summary>
    /// Owns the self-signed certificate authority behind the LAN web remote's HTTPS endpoint.
    /// Android Chrome only allows PWA installation (and service workers) inside a secure context,
    /// so a phone on the local network has to trust a certificate issued here.
    /// </summary>
    public static class WebRemoteCertificateService
    {
        private const int RootCaValidityDays = 3650;
        private const int ServerCertificateValidityDays = 397;
        private const int RenewBeforeDays = 30;

        private static readonly object Gate = new object();
        private static X509Certificate2? _serverCertificate;
        private static X509Certificate2? _rootCertificate;
        private static byte[]? _rootCertificateDer;

        /// <summary>
        /// Folder that holds the CA and server certificate (next to the other local app data).
        /// </summary>
        public static string CertificateDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SrcBox",
            "webremote-certs");

        /// <summary>
        /// Path of the DER encoded CA certificate offered to phones via /cert.
        /// </summary>
        public static string RootCertificatePath => Path.Combine(CertificateDirectory, "srcbox-ca.crt");

        /// <summary>
        /// Expiry of the current server certificate, or null when none could be prepared.
        /// </summary>
        public static DateTime? ServerCertificateNotAfter
        {
            get
            {
                if (!TryGetServerCertificate(out var certificate)) return null;
                return certificate!.NotAfter;
            }
        }

        /// <summary>
        /// Server certificate for the HTTPS listener, generating it on first use.
        /// </summary>
        public static X509Certificate2 GetServerCertificate()
        {
            EnsureLoaded();
            lock (Gate) return _serverCertificate!;
        }

        /// <summary>
        /// Non throwing variant used by the server startup path.
        /// </summary>
        public static bool TryGetServerCertificate(out X509Certificate2? certificate)
        {
            try
            {
                certificate = GetServerCertificate();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] HTTPS certificate unavailable: {ex.Message}");
                certificate = null;
                return false;
            }
        }

        /// <summary>
        /// DER encoded CA certificate handed out to phones, or null when it is not ready.
        /// </summary>
        public static byte[]? GetRootCertificateDer()
        {
            try
            {
                EnsureLoaded();
                lock (Gate) return _rootCertificateDer;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] CA certificate unavailable: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Discards the cached material and issues a brand new CA plus server certificate.
        /// </summary>
        public static void Regenerate()
        {
            lock (Gate)
            {
                _serverCertificate = null;
                _rootCertificate = null;
                _rootCertificateDer = null;
                try
                {
                    if (Directory.Exists(CertificateDirectory)) Directory.Delete(CertificateDirectory, true);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] Failed to clear old certificates: {ex.Message}");
                }
            }

            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_serverCertificate != null) return;

            lock (Gate)
            {
                if (_serverCertificate != null) return;

                var material = EnsureCore(CertificateDirectory);
                _rootCertificate = material.Root;
                _serverCertificate = material.Server;
                _rootCertificateDer = material.RootDer;

                try
                {
                    File.WriteAllBytes(RootCertificatePath, material.RootDer);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] Failed to write CA certificate: {ex.Message}");
                }

                Logger.Info($"[WebRemote] HTTPS certificate ready ({material.SubjectCount} SAN entries, expires {material.Server.NotAfter:yyyy-MM-dd})");
            }
        }

        /// <summary>
        /// Loads the material from <paramref name="directory"/>, (re)issuing what is missing, expired
        /// or no longer covers the current host addresses.
        /// </summary>
        internal static (X509Certificate2 Server, X509Certificate2 Root, byte[] RootDer, int SubjectCount) EnsureCore(string directory)
        {
            Directory.CreateDirectory(directory);

            var subjects = CollectSubjects();
            var fingerprint = string.Join("|", subjects);
            var fingerprintPath = Path.Combine(directory, "subjects.txt");
            var storedFingerprint = ReadText(fingerprintPath);

            var rootPath = Path.Combine(directory, "srcbox-ca.pfx");
            var root = LoadPfx(rootPath);
            if (root == null || !root.HasPrivateKey || root.NotAfter <= DateTime.Now.AddDays(RenewBeforeDays))
            {
                root?.Dispose();
                root = CreateRootCertificate();
                SavePfx(root, rootPath);
                storedFingerprint = null; // new issuer, so the server certificate must be reissued as well
            }

            var serverPath = Path.Combine(directory, "srcbox-server.pfx");
            var server = LoadPfx(serverPath);
            var reusable = server != null
                && server.HasPrivateKey
                && server.NotAfter > DateTime.Now.AddDays(RenewBeforeDays)
                && string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal)
                && string.Equals(server.Issuer, root.Subject, StringComparison.Ordinal);

            if (!reusable)
            {
                server?.Dispose();
                server = CreateServerCertificate(root, subjects);
                SavePfx(server, serverPath);
                WriteText(fingerprintPath, fingerprint);
            }

            return (server!, root, root.Export(X509ContentType.Cert), subjects.Count);
        }

        /// <summary>
        /// Every name a phone may use to reach this machine: LAN IPv4 addresses, host name and loopback.
        /// </summary>
        internal static List<string> CollectSubjects()
        {
            var subjects = new List<string> { "localhost", "127.0.0.1", "srcbox.local" };

            var machineName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(machineName) && machineName.All(c => c < 128))
            {
                subjects.Add(machineName);
                subjects.Add(machineName + ".local");
            }

            foreach (var address in WebRemoteAddressHelper.GetLocalIPv4Addresses())
            {
                subjects.Add(address);
            }

            return subjects
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static X509Certificate2 CreateRootCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=SrcBox Local CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var now = DateTimeOffset.Now;
            using var selfSigned = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(RootCaValidityDays));
            return RoundTrip(selfSigned);
        }

        private static X509Certificate2 CreateServerCertificate(X509Certificate2 issuer, IReadOnlyList<string> subjects)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=SrcBox Web Remote", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // server authentication
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var alternativeNames = new SubjectAlternativeNameBuilder();
            foreach (var subject in subjects)
            {
                if (IPAddress.TryParse(subject, out var address)) alternativeNames.AddIpAddress(address);
                else alternativeNames.AddDnsName(subject);
            }
            request.CertificateExtensions.Add(alternativeNames.Build());

            var serial = new byte[16];
            RandomNumberGenerator.Fill(serial);

            var now = DateTimeOffset.Now;
            using var issued = request.Create(issuer, now.AddDays(-1), now.AddDays(ServerCertificateValidityDays), serial);
            using var withKey = issued.CopyWithPrivateKey(key);
            return RoundTrip(withKey);
        }

        /// <summary>
        /// SChannel needs the private key in a key container, so every certificate is round tripped
        /// through PKCS#12 before it is handed out.
        /// </summary>
        private static X509Certificate2 RoundTrip(X509Certificate2 certificate)
        {
            var pfx = certificate.Export(X509ContentType.Pfx, "");
            return new X509Certificate2(pfx, "", X509KeyStorageFlags.Exportable);
        }

        private static X509Certificate2? LoadPfx(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return new X509Certificate2(path, "", X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to load certificate {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        private static void SavePfx(X509Certificate2 certificate, string path)
        {
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, ""));
        }

        private static string? ReadText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteText(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to store certificate fingerprint: {ex.Message}");
            }
        }
    }
}
