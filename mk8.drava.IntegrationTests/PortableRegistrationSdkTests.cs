using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class PortableRegistrationSdkTests(ITestOutputHelper output)
{
    private const string Token = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public async Task ProtectedSdkChildRegistersRoutesAndDrainsWithoutAmbientConfigurationAsync()
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        output.WriteLine("sdk_fixture: {0}", proxy.EvidenceId);
        var directory = Directory.CreateTempSubdirectory("drava_sdk_collection_").FullName;
        try
        {
            using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
            var options = new DravaRegistrationOptions
            {
                Site = new RegistrationSiteTrust { SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256),
                    RootCertificate = new SecretBytes(root.RawData), NodeCertificate = new SecretBytes(await File.ReadAllBytesAsync(proxy.NodeCertificatePath, CancellationToken.None).ConfigureAwait(true)),
                    NodeAgentDescriptorPath = proxy.NodeAgentDescriptorPath, },
                NodeAgentToken = new SecretText(Token), NodeId = "local", OwnerId = "development", ServiceId = "svc", ReadinessPath = "/protected-ready",
                MulticastDiscovery = false, GatewaySeeds = [new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)],
            };
            var path = Path.Combine(directory, ".env");
            await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(options)).ConfigureAwait(true);
            var child = new DevelopmentProcess(SdkAssemblyPath(), path, "--development-registration-sdk",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Drava__DravaRegistrationOptions"] = "invalid-synthetic-ambient-sdk",
                    ["ASPNETCORE_URLS"] = "http://127.0.0.1:1", ["DOTNET_ENVIRONMENT"] = "Development",
                });
            await using var childLifetime = child.ConfigureAwait(true);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var port = await ReadPortAsync(child, timeout.Token).ConfigureAwait(true);
                using var control = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
                await WaitForReadyAsync(child, control, timeout.Token).ConfigureAwait(true);
                using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
                using var response = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("protected-sdk-body", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
                using var stopped = await control.PostAsync(new Uri("/stop", UriKind.Relative), content: null, timeout.Token).ConfigureAwait(true);
                stopped.EnsureSuccessStatusCode();
                Assert.Equal(0, await child.WaitForExitAsync(timeout.Token).ConfigureAwait(true));
                using var drained = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, drained.StatusCode);
                var protectedDocument = await File.ReadAllTextAsync(child.PortableDocumentPath, timeout.Token).ConfigureAwait(true);
                await child.DisposeAsync().ConfigureAwait(true);
                Assert.DoesNotContain(Token, child.CapturedLog, StringComparison.Ordinal);
                Assert.DoesNotContain(Token, protectedDocument, StringComparison.Ordinal);
            }
            finally { await PreserveChildAsync(child, directory).ConfigureAwait(true); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("wrong-role")]
    [InlineData("template-only")]
    [InlineData("raw-json")]
    [InlineData("provider-pointer")]
    [InlineData("unknown-field")]
    [InlineData("null-token")]
    [InlineData("invalid-readiness")]
    [InlineData("invalid-trust")]
    public async Task InvalidProtectedSdkInputExitsBeforeListenerAdmissionAsync(string fault)
    {
        var directory = Directory.CreateTempSubdirectory("drava_sdk_refusal_").FullName;
        try
        {
            using var authority = LocalSiteCertificateAuthority.Create("synthetic-site", TimeProvider.System);
            using var root = authority.PublicCertificate;
            using var node = authority.IssueNode("node", ["127.0.0.1"]);
            var options = new DravaRegistrationOptions
            {
                Site = new RegistrationSiteTrust { SiteId = "synthetic-site", Domain = "site.test", RootFingerprint = authority.Fingerprint,
                    RootCertificate = new SecretBytes(root.RawData), NodeCertificate = PortableCertificate.ExportPrivate(node), },
                NodeAgentToken = new SecretText(Token), NodeId = "node", OwnerId = "owner", ServiceId = "svc", ReadinessPath = "/ready",
            };
            var path = Path.Combine(directory, ".env");
            await PortableFixtureFile.WriteAsync(path, BrokenDocument(options, fault)).ConfigureAwait(true);
            var child = new DevelopmentProcess(SdkAssemblyPath(), path,
                "--development-registration-sdk", templateOnly: string.Equals(fault, "template-only", StringComparison.Ordinal));
            await using var childLifetime = child.ConfigureAwait(true);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Assert.Equal(2, await child.WaitForExitAsync(timeout.Token).ConfigureAwait(true));
                await child.DisposeAsync().ConfigureAwait(true);
                Assert.Contains("SDK-CONFIG-REFUSED", child.CapturedLog, StringComparison.Ordinal);
                Assert.DoesNotContain("SDK-READY", child.CapturedLog, StringComparison.Ordinal);
                Assert.DoesNotContain(Token, child.CapturedLog, StringComparison.Ordinal);
            }
            finally { await PreserveChildAsync(child, directory).ConfigureAwait(true); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task PreserveChildAsync(DevelopmentProcess child, string directory)
    {
        await child.DisposeAsync().ConfigureAwait(false);
        var evidence = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "sdk-portable-process", Path.GetFileName(directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "child.log"), child.CapturedLog, CancellationToken.None).ConfigureAwait(false);
    }

    // The same process assertions also qualify a separately compiled NuGet consumer.
    private static string SdkAssemblyPath()
    {
        var consumer = Environment.GetEnvironmentVariable("MK8_DRAVA_DEVELOPMENT_SDK_ASSEMBLY");
        if (consumer is null) return DevelopmentBinaryPaths.ForProject("mk8.drava.CompatibilityTests");
        if (!Path.IsPathFullyQualified(consumer) || !File.Exists(consumer)) throw new InvalidDataException("The development SDK consumer assembly is unavailable.");
        return consumer;
    }

    private static string BrokenDocument(DravaRegistrationOptions options, string fault)
    {
        if (string.Equals(fault, "wrong-role", StringComparison.Ordinal)) return PortableConfiguration.Serialize(new GatewayBootstrap());
        if (string.Equals(fault, "raw-json", StringComparison.Ordinal)) return BootstrapFile.Serialize(options);
        if (string.Equals(fault, "provider-pointer", StringComparison.Ordinal)) return "SUPPROCOM_SECRET_SOURCE=unavailable://reserved.invalid/sdk\n";
        var root = JsonNode.Parse(BootstrapFile.Serialize(options))?.AsObject() ?? throw new InvalidOperationException("Synthetic SDK options are absent.");
        switch (fault)
        {
            case "template-only": break;
            case "unknown-field": root["unrecognized"] = "synthetic-private-canary"; break;
            case "null-token": root["nodeAgentToken"] = null; break;
            case "invalid-readiness": root["readinessPath"] = ""; break;
            case "invalid-trust": (root["site"]?.AsObject() ?? throw new InvalidOperationException("Synthetic site is absent."))["rootFingerprint"] = new string('0', 64); break;
            default: throw new InvalidOperationException("Unknown synthetic SDK fault.");
        }
        return PortableConfiguration.SerializeDocument<DravaRegistrationOptions>(root.ToJsonString());
    }

    private static async Task<int> ReadPortAsync(DevelopmentProcess child, CancellationToken cancellationToken)
    {
        while (true)
        {
            child.ThrowIfExited();
            var captured = child.CapturedLog;
            var complete = captured.LastIndexOf('\n');
            foreach (var line in (complete < 0 ? "" : captured[..complete]).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("SDK-READY ", StringComparison.Ordinal)) continue;
                using var message = JsonDocument.Parse(line[10..]);
                return message.RootElement.GetProperty("port").GetInt32();
            }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForReadyAsync(DevelopmentProcess child, HttpClient control, CancellationToken cancellationToken)
    {
        while (true)
        {
            child.ThrowIfExited();
            using var response = await control.GetAsync(new Uri("/state", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (string.Equals(message.RootElement.GetProperty("phase").GetString(), "Ready", StringComparison.Ordinal)) return;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }
}
