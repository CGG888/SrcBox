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
        /// PEM encoded CA certificate, served as /cert.pem for phone installers that only accept a
        /// text certificate (some Android builds refuse the DER file).
        /// </summary>
        public static string? GetRootCertificatePem()
        {
            var der = GetRootCertificateDer();
            return der == null || der.Length == 0 ? null : ToPem(der);
        }

        /// <summary>PEM encoding of one DER certificate, wrapped at the customary 64 characters.</summary>
        internal static string ToPem(byte[] der)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("-----BEGIN CERTIFICATE-----\n");
            var base64 = Convert.ToBase64String(der);
            for (var i = 0; i < base64.Length; i += 64)
            {
                builder.Append(base64, i, Math.Min(64, base64.Length - i));
                builder.Append('\n');
            }
            builder.Append("-----END CERTIFICATE-----\n");
            return builder.ToString();
        }

        /// <summary>
        /// Apple configuration profile that installs the CA as a trusted root request on iOS in one
        /// step. iOS still needs the user to switch on full trust under Certificate Trust Settings.
        /// </summary>
        public static string? BuildRootCaProfile()
        {
            var der = GetRootCertificateDer();
            return der == null || der.Length == 0 ? null : BuildRootCaProfile(der);
        }

        /// <summary>Builds the same Apple profile from an explicit DER certificate (kept pure for tests).</summary>
        internal static string BuildRootCaProfile(byte[] der)
        {
            var payloadUuid = DeriveUuid(der);
            var profileUuid = DeriveUuid(der, 1);
            var encoded = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>PayloadContent</key>
    <array>
        <dict>
            <key>PayloadCertificateFileName</key>
            <string>srcbox-ca.crt</string>
            <key>PayloadContent</key>
            <data>
{encoded}
            </data>
            <key>PayloadDescription</key>
            <string>SrcBox Local CA</string>
            <key>PayloadDisplayName</key>
            <string>SrcBox Local CA</string>
            <key>PayloadIdentifier</key>
            <string>local.srcbox.webremote.ca.{payloadUuid}</string>
            <key>PayloadType</key>
            <string>com.apple.security.root</string>
            <key>PayloadUUID</key>
            <string>{payloadUuid}</string>
            <key>PayloadVersion</key>
            <integer>1</integer>
        </dict>
    </array>
    <key>PayloadDescription</key>
    <string>Trusts the SrcBox web remote on this network.</string>
    <key>PayloadDisplayName</key>
    <string>SrcBox Web Remote</string>
    <key>PayloadIdentifier</key>
    <string>local.srcbox.webremote.{profileUuid}</string>
    <key>PayloadOrganization</key>
    <string>SrcBox</string>
    <key>PayloadRemovalDisallowed</key>
    <false/>
    <key>PayloadType</key>
    <string>Configuration</string>
    <key>PayloadUUID</key>
    <string>{profileUuid}</string>
    <key>PayloadVersion</key>
    <integer>1</integer>
</dict>
</plist>
";
        }

        /// <summary>
        /// Stable UUID for a certificate so that reinstalling the same profile is treated as an update.
        /// </summary>
        private static string DeriveUuid(byte[] der, byte salt = 0)
        {
            var hash = SHA1.HashData(der);
            hash[15] = (byte)(hash[15] ^ salt);
            return new Guid(hash[..16]).ToString().ToUpperInvariant();
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
                if (File.Exists(rootPath) && root == null)
                {
                    // Silence here used to hide the reason a phone suddenly rejected the remote: the old CA
                    // was still on disk, but unusable, so a different one was issued.
                    Logger.Warn("[WebRemote] The stored CA could not be loaded, a new one is issued: phones must trust the new certificate once");
                }

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
        /// PKCS#12 import strategies, tried in order. The plain exportable import stays first so a healthy
        /// profile behaves exactly as before; the other two are container backed as well, because SChannel
        /// refuses an ephemeral key ("the platform does not support ephemeral keys"), and they cover a
        /// profile whose key container store rejects the default import with
        /// "the specified network password is not correct".
        /// </summary>
        private static readonly X509KeyStorageFlags[] ImportLadder =
        {
            X509KeyStorageFlags.Exportable,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet,
            X509KeyStorageFlags.DefaultKeySet,
        };

        /// <summary>
        /// The only place PKCS#12 material is turned into a certificate. Every strategy is attempted and
        /// the full path is logged, so a failure on a user machine names the file and the reason instead
        /// of surfacing as a bare "certificate unavailable".
        /// </summary>
        private static X509Certificate2 ImportPkcs12(byte[] pkcs12, string source)
        {
            Exception? first = null;
            foreach (var flags in ImportLadder)
            {
                try
                {
                    return new X509Certificate2(pkcs12, "", flags);
                }
                catch (Exception ex)
                {
                    first ??= ex;
                    Logger.Debug($"[WebRemote] PKCS#12 import ({flags}) failed for {source}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            throw new CryptographicException($"Unable to import {source} with any key storage flag: {first?.Message}", first);
        }

        /// <summary>
        /// SChannel needs the private key in a key container, so every certificate is round tripped
        /// through PKCS#12 before it is handed out.
        /// </summary>
        private static X509Certificate2 RoundTrip(X509Certificate2 certificate)
        {
            var pfx = certificate.Export(X509ContentType.Pfx, "");
            return ImportPkcs12(pfx, $"the '{certificate.Subject}' certificate");
        }

        private static X509Certificate2? LoadPfx(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return ImportPkcs12(File.ReadAllBytes(path), path);
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to load certificate {path}: {ex.GetType().Name}: {ex.Message}");
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
