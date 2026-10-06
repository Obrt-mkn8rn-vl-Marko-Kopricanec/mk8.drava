namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeAcmeCertificateResponse
{
    public RuntimeAcmeCertificateResponse(string id, bool enabled, IReadOnlyList<string> domains, int renewBeforeDays)
    {
        Id = id;
        Enabled = enabled;
        Domains = ApiResponseList.Copy(domains);
        RenewBeforeDays = renewBeforeDays;
    }

    public string Id { get; }
    public bool Enabled { get; }
    public IReadOnlyList<string> Domains { get; }
    public int RenewBeforeDays { get; }
}
