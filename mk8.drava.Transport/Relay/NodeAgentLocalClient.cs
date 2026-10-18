using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Transport.Relay;

public sealed class NodeAgentLocalClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, MaxDepth = 8,
    };
    private readonly ApplicationChannel _channel;
    private readonly NodeAgentLocal.NodeAgentLocalClient _client;
    public NodeAgentDescriptor Descriptor { get; }

    public NodeAgentLocalClient(NodeAgentDescriptor descriptor, SecretText identityToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Validate();
        Descriptor = descriptor;
        _channel = new ApplicationChannel(descriptor.LocalEndpoint, identityToken);
        _client = new NodeAgentLocal.NodeAgentLocalClient(_channel.Invoker);
    }

    public static async ValueTask<NodeAgentDescriptor?> ReadDescriptorAsync(RegistrationSiteTrust trust, string nodeId, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trust);
        cancellationToken.ThrowIfCancellationRequested();
        var path = trust.NodeAgentDescriptorPath;
        if (path.Length == 0) return null;
        var admitted = false;
        try
        {
            var input = InstallationDocumentGuard.OpenRead(path, 1, 16 * 1024);
            await using var inputLifetime = input.ConfigureAwait(false);
            admitted = true;
            var length = input.Length;
            if (length is < 1 or > 16 * 1024) throw new InvalidDataException("Local node descriptor exceeds its bound.");
            var bytes = new byte[checked((int)length)];
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (await input.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Local node descriptor changed while reading.");
            var descriptor = Decode(bytes);
            if (!string.Equals(descriptor.SiteId, trust.SiteId, StringComparison.Ordinal) || !string.Equals(descriptor.NodeId, nodeId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.CertificateFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Local agent does not match node enrollment.");
            }
            return descriptor;
        }
        // Only failure to open a missing file permits direct registration.
        catch (IOException exception) when (!admitted && IsMissingFile(exception)) { return null; }
    }

    public async ValueTask<NodeRelayEndpoint> ApplyAsync(RegistrationCommand command, CancellationToken cancellationToken)
    {
        using var call = _client.ApplyAsync(new NodeLocalRequest { Version = 1, CommandJson = ByteString.CopyFrom(RegistrationJson.Encode(command)) },
            _channel.Credentials, DateTime.UtcNow.AddSeconds(3), cancellationToken);
        var reply = await call.ResponseAsync.ConfigureAwait(false);
        if (reply.StatusCode != 200) throw new InvalidDataException("Private node mapping was rejected.");
        var descriptor = Decode(reply.DescriptorJson.Memory);
        if (descriptor != Descriptor) throw new InvalidDataException("Local agent changed while the mapping was being applied.");
        return new NodeRelayEndpoint { AgentBootId = descriptor.AgentBootId, Address = descriptor.RelayAddress, Port = descriptor.RelayPort, CertificateFingerprint = descriptor.CertificateFingerprint };
    }

    private static NodeAgentDescriptor Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > 16 * 1024) throw new InvalidDataException("Invalid local agent descriptor size.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        RequireUnique(document.RootElement);
        var descriptor = document.RootElement.Deserialize<NodeAgentDescriptor>(Json) ?? throw new InvalidDataException("Missing local agent descriptor.");
        descriptor.Validate();
        return descriptor;
    }

    private static void RequireUnique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate agent descriptor property.");
            RequireUnique(field.Value);
        }
    }

    private static bool IsMissingFile(IOException exception) => exception.InnerException is Win32Exception error &&
        (error.NativeErrorCode == 2 || (OperatingSystem.IsWindows() && error.NativeErrorCode == 3));

    public void Dispose() => _channel.Dispose();
}
