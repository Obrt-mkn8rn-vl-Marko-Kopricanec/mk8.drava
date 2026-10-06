namespace Mk8.Drava.Contracts.Registration.V1;

public sealed record RegistrationCommand
{
    public int Version { get; init; } = 1;
    public RegistrationOperation Operation { get; init; }
    public RegistrationIdentity Identity { get; init; } = new();
    public ServiceAdvertisement? Advertisement { get; init; }
}
