# mk8.drava

Application, Gateway, node agent and Registration SDK settings come from the pinned
Supprocom.Secrets 0.1.4 local store at
`AppContext.BaseDirectory/Environment/.env`. Each installation has its own
protected document and `.installation.key`. Production configuration has no
command-line locator, process-environment override, development overlay or raw
JSON/certificate-file fallback.

Current Windows support is incomplete. Site initialization refuses Windows;
protected policy storage and durable recovery require Linux. Creating Windows
publish outputs does not enable these operations or establish Windows runtime
compatibility.

Publish Application and Gateway into separate output roots:

```sh
dotnet publish mk8.drava.Application/mk8.drava.Application.csproj --configuration Release --self-contained false -p:UseAppHost=false --output artifacts/portable/application
dotnet publish mk8.drava.Gateway/mk8.drava.Gateway.csproj --configuration Release --self-contained false -p:UseAppHost=false --output artifacts/portable/gateway
```

The published `Environment/.env.template` contains no credentials or usable site
defaults. Templates are never activated automatically. Prepare the required role
using owner-approved identities, addresses, certificates, credentials and policy.
Keep the installation directory private to its service owner, with private
`Environment`, `.env` and `.installation.key` access. On Linux the directories must
be mode 0700 and files mode 0400 or 0600. First load protects a prepared plaintext
dotenv document before returning values. Preserve its installation key with the
document; do not copy another role's key or put active documents/keys into packages.
Only the nonsecret base template is copied from the project's `Environment`
directory during build/publish. Publishing does not erase existing installation
state in an output directory.

For source-based execution, use separate artifact roots for independent roles:

```sh
dotnet run --project mk8.drava.Application/mk8.drava.Application.csproj --configuration Release --artifacts-path artifacts/run/application --no-launch-profile
dotnet run --project mk8.drava.Gateway/mk8.drava.Gateway.csproj --configuration Release --artifacts-path artifacts/run/gateway --no-launch-profile
dotnet run --project mk8.drava.Application/mk8.drava.Application.csproj --configuration Release --artifacts-path artifacts/run/node --no-launch-profile -- --node-agent
```

`dotnet run` uses its build output, rather than the directory passed to
`dotnet publish`. Prepare the corresponding protected role beside those actual
binaries. With these commands, Application uses
`artifacts/run/application/bin/mk8.drava.Application/release/Environment/.env`;
Gateway and node use the equivalent paths under their separate artifact roots.
Node-agent mode reads `Drava:NodeAgentBootstrap`; normal Application and Gateway read
`Drava:ApplicationBootstrap` and `Drava:GatewayBootstrap` respectively.

The Application `--initialize-site` mode reads `Drava:SiteInitializationOptions`
from its own fixed collection. It prepares independent encrypted Application,
Gateway and node-enrollment role roots in the approved destination. The enrollment
role contains a `NodeEnrollmentProfile`, rather than the node agent's listener and
admission settings. Prepare the agent's `NodeAgentBootstrap` separately with the
approved enrollment identity, local token, state/listener paths and relay scope.
Recovery modes likewise
read their typed requests from the fixed collection and require the existing
explicit inventory/credential approvals. These preparation modes do not take
service configuration values or alternate collection paths on the command line.

For a service using the Registration SDK, store the complete
`DravaRegistrationOptions` value under `Drava:DravaRegistrationOptions` in that
service's fixed collection. The dotenv spelling is
`Drava__DravaRegistrationOptions`. `PortableConfiguration.Serialize(options)` uses
the package's canonical encoding. Include the approved site trust, node/service
identity, node-agent token when applicable, readiness path, discovery/seeds and
retry/shutdown settings in that protected value. An omitted `instanceId` generates
a fresh runtime instance identity. The readiness path must be served by the actual
bound application before registration can become ready.

With multiple listeners, the SDK selects an endpoint matching the configured
advertised address and upstream protocol. Wildcard bindings require an address
in the same IP family; an IPv6 wildcard alone does not prove IPv4 availability.
Declare an IPv4 listener when advertising an IPv4 address. HTTP/2 upstreams require
a bound HTTPS endpoint.

Await configuration and validation before building or starting the host:

```csharp
await builder.Services.AddDravaRegistrationAsync(cancellationToken);
var app = builder.Build();
```

The existing typed `AddDravaRegistration(options)` and `FromEnrollment` APIs accept
explicit prepared snapshots. They are not fallback providers for the awaited
protected loader. SDK configuration is captured at startup; changing accepted
settings requires an owned restart. Registration readiness, route publication,
certificate trust, DNS verification, leases and graceful drain still apply.

The host's stopping signal immediately clears the SDK's ready observation. Concurrent stop calls
join one renewal/drain/cleanup operation; cancelling a caller's wait leaves that
operation owned by the service. The shutdown deadline limits new drain work, while
already-started operations must settle before their transports can be disposed.
Await host shutdown or asynchronous disposal to join that cleanup.

The repository remains under development. Checkpoint tests do not establish
Review acceptance, platform/native compatibility, peer adoption or authority to
change a live service, DNS or PKI deployment.
