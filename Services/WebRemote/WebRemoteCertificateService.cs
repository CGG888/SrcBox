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

        /// <summary>How long a failing certificate store is left alone before it is tried again.</summary>
        private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(30);

        private static readonly object Gate = new object();
        private static X509Certificate2? _serverCertificate;
        private static X509Certificate2? _rootCertificate;
        private static byte[]? _rootCertificateDer;
        private static string? _loadedFingerprint;
        private static DateTime _nextLoadAttemptUtc;
        private static string? _lastLoadError;

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
                _loadedFingerprint = null;
                _nextLoadAttemptUtc = DateTime.MinValue;
                _lastLoadError = null;
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

        /// <summary>
        /// Asks the store for the CA and the server certificate, (re)issuing them when anything is
        /// missing, expired, unusable or no longer covers the current host addresses. The addresses are
        /// re-read on every call, so a phone that follows a new DHCP lease gets a certificate for it
        /// without restarting the app. A failing store is retried at most every
        /// <see cref="FailureRetryDelay"/> instead of on every handshake.
        /// </summary>
        private static void EnsureLoaded()
        {
            lock (Gate)
            {
                var fingerprint = CertificateFingerprint();
                if (_serverCertificate != null && string.Equals(_loadedFingerprint, fingerprint, StringComparison.Ordinal)) return;

                if (DateTime.UtcNow < _nextLoadAttemptUtc)
                {
                    throw new InvalidOperationException(_lastLoadError ?? "The https certificate is unavailable");
                }

                try
                {
                    var material = EnsureCore(CertificateDirectory);
                    _rootCertificate = material.Root;
                    _serverCertificate = material.Server;
                    _rootCertificateDer = material.RootDer;
                    _loadedFingerprint = fingerprint;

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
                catch (Exception ex)
                {
                    // Without the backoff a refused key store reissued on every handshake and wrote an
                    // error line each time; the reason is kept for the next caller to see.
                    _lastLoadError = ex.Message;
                    _nextLoadAttemptUtc = DateTime.UtcNow.Add(FailureRetryDelay);
                    throw;
                }
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
            var fingerprint = CertificateFingerprint(subjects);
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

        /// <summary>
        /// Stable identity of the names the server certificate has to cover. Sorted so that the order the
        /// adapters happen to report their addresses in cannot look like an address change.
        /// </summary>
        private static string CertificateFingerprint()
            => CertificateFingerprint(CollectSubjects());

        private static string CertificateFingerprint(IEnumerable<string> subjects)
            => string.Join("|", subjects.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

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
        /// profile behaves exactly as before. The container backed ones cover a profile whose key container
        /// store rejects the default import with "the specified network password is not correct".
        /// The last one imports the key without touching the user key container at all, which is the only
        /// strategy left for a process that cannot create a key container ("access denied" from the
        /// container store); it is last because an imported ephemeral key can serve TLS, while the pristine
        /// in-memory key of a freshly created certificate cannot ("the platform does not support ephemeral
        /// keys"), and because the freshly issued certificates are exported before they are imported.
        /// </summary>
        private static readonly (string Name, string? Password, X509KeyStorageFlags Flags)[] ImportLadder =
        {
            ("Exportable", string.Empty, X509KeyStorageFlags.Exportable),
            ("null password", null, X509KeyStorageFlags.Exportable),
            ("Exportable|PersistKeySet", string.Empty, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet),
            ("DefaultKeySet", string.Empty, X509KeyStorageFlags.DefaultKeySet),
            ("EphemeralKeySet", string.Empty, X509KeyStorageFlags.EphemeralKeySet),
        };

        /// <summary>
        /// The only place PKCS#12 material is turned into a certificate. Every strategy is attempted and
        /// the failure is logged with the native error code, so a machine specific problem names the file
        /// and the reason instead of surfacing as a bare "certificate unavailable".
        /// </summary>
        private static X509Certificate2 ImportPkcs12(byte[] pkcs12, string source)
        {
            Exception? first = null;
            foreach (var (name, password, flags) in ImportLadder)
            {
                try
                {
                    var loaded = new X509Certificate2(pkcs12, password, flags);
                    if (first != null)
                    {
                        Logger.Warn($"[WebRemote] Loading {source} needed the '{name}' fallback after {first.GetType().Name}");
                    }

                    return loaded;
                }
                catch (Exception ex)
                {
                    first ??= ex;
                    Logger.Warn($"[WebRemote] PKCS#12 import ({name}) failed for {source}: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
                }
            }

            try
            {
                var collection = new X509Certificate2Collection();
                collection.Import(pkcs12, string.Empty, X509KeyStorageFlags.Exportable);
                if (collection.Count > 0 && collection[0].HasPrivateKey)
                {
                    Logger.Warn($"[WebRemote] Loading {source} needed the certificate collection fallback");
                    return collection[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebRemote] PKCS#12 collection import failed for {source}: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }

            DescribeImportFailure(pkcs12, source, first);
            throw new CryptographicException($"Unable to import {source} with any key storage flag: {first?.Message}", first);
        }

        /// <summary>
        /// Records what the failing process can and cannot do with PKCS#12 and with the user key store.
        /// A machine specific failure is otherwise indistinguishable from a corrupted file.
        /// </summary>
        private static void DescribeImportFailure(byte[] pkcs12, string source, Exception? first)
        {
            try
            {
                Logger.Error(
                    $"[WebRemote] Import diagnostics for {source}: length={pkcs12.Length} " +
                    $"sha256={Convert.ToHexString(SHA256.HashData(pkcs12))} " +
                    $"fips={CryptoConfig.AllowOnlyFipsAlgorithms} identity={SafeIdentity()} " +
                    $"profile={Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)} " +
                    $"cwd={Environment.CurrentDirectory}");
            }
            catch
            {
                // Diagnostics must never mask the original failure.
            }

            try
            {
                var certificateOnly = new X509Certificate2(pkcs12);
                Logger.Error($"[WebRemote] Certificate part parses: subject={certificateOnly.Subject}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Certificate part does not parse: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }

            ProbeKeyStore();
            ProbePasswordProtection();

            if (first != null)
            {
                var text = first.ToString();
                Logger.Error($"[WebRemote] First failure: {(text.Length > 1200 ? text[..1200] : text)}");
            }
        }

        /// <summary>Checks whether this process may create a persistent RSA key container.</summary>
        private static void ProbeKeyStore()
        {
            var name = "SrcBoxDiagnostics-" + Guid.NewGuid().ToString("N");
            try
            {
                var parameters = new CngKeyCreationParameters
                {
                    ExportPolicy = CngExportPolicies.AllowPlaintextExport,
                    KeyUsage = CngKeyUsages.AllUsages,
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                };

                using var key = CngKey.Create(CngAlgorithm.Rsa, name, parameters);
                var persisted = CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
                Logger.Error($"[WebRemote] Key container can be created: name={name} persisted={persisted}");
                key.Delete();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Key container cannot be created: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }

            var keysDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Crypto",
                "Keys");
            try
            {
                var probe = Path.Combine(keysDirectory, "srcbox-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                Logger.Error($"[WebRemote] Key store directory is writable: {keysDirectory}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Key store directory is not writable: {keysDirectory}: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }

            try
            {
                var parameters = new CspParameters(1) { KeyContainerName = name };
                using var rsa = new RSACryptoServiceProvider(2048, parameters);
                Logger.Error($"[WebRemote] CAPI key container can be created: {rsa.CspKeyContainerInfo.KeyContainerName}");
                rsa.PersistKeyInCsp = false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] CAPI key container cannot be created: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
            }
        }

        /// <summary>
        /// Separates "this process cannot handle a passwordless PKCS#12" from "this process cannot import
        /// PKCS#12 at all" by round tripping a freshly created certificate in both flavours.
        /// </summary>
        private static void ProbePasswordProtection()
        {
            try
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=SrcBox Diagnostics", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));

                foreach (var password in new[] { string.Empty, "srcbox" })
                {
                    var flavour = password.Length == 0 ? "empty" : "non-empty";
                    try
                    {
                        var bytes = certificate.Export(X509ContentType.Pfx, password);
                        using var reloaded = new X509Certificate2(bytes, password, X509KeyStorageFlags.Exportable);
                        Logger.Error($"[WebRemote] Round trip with a {flavour} password works (hasKey={reloaded.HasPrivateKey})");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[WebRemote] Round trip with a {flavour} password failed: {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Password probe could not run: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string SafeIdentity()
        {
            try
            {
                using var thread = System.Security.Principal.WindowsIdentity.GetCurrent();
                using var process = System.Security.Principal.WindowsIdentity.GetCurrent(false);
                var marks = new List<string>();
                foreach (var group in thread.Groups)
                {
                    var value = group?.Value ?? "";
                    if (value.StartsWith("S-1-16-", StringComparison.Ordinal) ||
                        value.StartsWith("S-1-15-2-", StringComparison.Ordinal))
                    {
                        marks.Add(value);
                    }
                }

                return $"{thread.Name} impersonating={thread.Token != process.Token} level={thread.ImpersonationLevel} " +
                       $"process={process.Name} marks={string.Join(",", marks)}";
            }
            catch (Exception ex)
            {
                return $"{Environment.UserName} (token unavailable: {ex.GetType().Name}: {ex.Message})";
            }
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
