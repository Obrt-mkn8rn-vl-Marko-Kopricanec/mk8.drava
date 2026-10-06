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

    public NodeAgentLocalClient(NodeAgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Validate();
        Descriptor = descriptor;
        _channel = new ApplicationChannel(descriptor.LocalEndpoint);
        _client = new NodeAgentLocal.NodeAgentLocalClient(_channel.Invoker);
    }

    public static async ValueTask<NodeAgentDescriptor?> ReadDescriptorAsync(RegistrationSiteTrust trust, string nodeId, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trust);
        var path = trust.NodeCertificatePath + ".agent.json";
        if (!File.Exists(path)) return null;
        RequirePrivateFile(path);
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);
        await using var inputLifetime = input.ConfigureAwait(false);
        if (input.Length is < 1 or > 16 * 1024) throw new InvalidDataException("Local node descriptor exceeds its bound.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var descriptor = Decode(bytes);
        if (!string.Equals(descriptor.SiteId, trust.SiteId, StringComparison.Ordinal) || !string.Equals(descriptor.NodeId, nodeId, StringComparison.Ordinal) ||
            !string.Equals(descriptor.CertificateFingerprint, fingerprint, StringComparison.Ordinal)) throw new InvalidDataException("Local agent does not match node enrollment.");
        RequirePrivateFile(descriptor.LocalEndpoint.IdentityTokenPath);
        return descriptor;
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

    private static void RequirePrivateFile(string path)
    {
        var file = new FileInfo(path);
        if (!Path.IsPathFullyQualified(path) || !file.Exists || file.LinkTarget is not null || file.Length is < 1 or > 16 * 1024) throw new InvalidDataException("Invalid private local agent file.");
        var directory = file.Directory;
        while (directory is not null)
        {
            if (directory.LinkTarget is not null) throw new InvalidDataException("Agent file cannot traverse a symbolic link.");
            directory = directory.Parent;
        }
        if (!OperatingSystem.IsWindows())
        {
            var denied = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((File.GetUnixFileMode(path) & denied) != UnixFileMode.None || (File.GetUnixFileMode(file.DirectoryName!) & denied) != UnixFileMode.None)
                throw new InvalidDataException("Local agent file and directory must be private.");
        }
    }

    public void Dispose() => _channel.Dispose();
}
