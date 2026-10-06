namespace Mk8.Drava.Application.BLL.Registry;

public sealed record RegistryAudit(DateTimeOffset AtUtc, string Operation, string Actor, string Subject);
