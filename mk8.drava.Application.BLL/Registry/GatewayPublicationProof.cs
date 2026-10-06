namespace Mk8.Drava.Application.BLL.Registry;

public sealed record GatewayPublicationProof(long Generation, DateTimeOffset ValidUntilUtc, string Domain, int HttpPort, int HttpsPort);
