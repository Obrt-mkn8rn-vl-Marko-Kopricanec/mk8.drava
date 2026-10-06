namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeSniCertificateFacts
{
    public static void Validate(string hostName, string certificateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ValidateCertificateId(certificateId);
    }

    public static void ValidateOptionalCertificateId(string? certificateId)
    {
        if (certificateId is not null)
        {
            ValidateCertificateId(certificateId);
        }
    }

    private static void ValidateCertificateId(string certificateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateId);
    }
}
