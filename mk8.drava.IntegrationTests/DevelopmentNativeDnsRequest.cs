using Mk8.Drava.Application.INF.Dns.Management;

namespace Mk8.Drava.IntegrationTests;

internal sealed record DevelopmentNativeDnsRequest(string Action, Guid Operation, ManagementApiRequest? Body, string Target);
