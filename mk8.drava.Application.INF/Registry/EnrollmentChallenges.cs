using System.Security.Cryptography;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.INF.Registry;

public sealed class EnrollmentChallenges(TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _nonces = new(StringComparer.Ordinal);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public byte[] Create(string fingerprint)
    {
        RegistryNames.RequireFingerprint(fingerprint);
        lock (_gate)
        {
            PurgeExpired();
            if (_nonces.Count >= 4096) throw new InvalidOperationException("Enrollment challenge capacity exceeded.");
            var pending = 0;
            foreach (var entry in _nonces.Values)
                if (string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)) pending++;
            if (pending >= 16) throw new InvalidOperationException("Enrollment challenge quota exceeded.");
            var nonce = RandomNumberGenerator.GetBytes(32);
            _nonces.Add(Convert.ToHexString(nonce), new Entry(fingerprint, clock.GetTimestamp()));
            return nonce;
        }
    }

    public bool Consume(string fingerprint, ReadOnlySpan<byte> nonce)
    {
        RegistryNames.RequireFingerprint(fingerprint);
        if (nonce.Length != 32) return false;
        lock (_gate)
        {
            var key = Convert.ToHexString(nonce);
            if (!_nonces.TryGetValue(key, out var entry) || !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)) return false;
            _nonces.Remove(key);
            var age = clock.GetElapsedTime(entry.CreatedAt);
            return age >= TimeSpan.Zero && age < Lifetime;
        }
    }

    private void PurgeExpired()
    {
        List<string> expired = [];
        foreach (var entry in _nonces)
        {
            var age = clock.GetElapsedTime(entry.Value.CreatedAt);
            if (age < TimeSpan.Zero || age >= Lifetime) expired.Add(entry.Key);
        }
        for (var index = 0; index < expired.Count; index++) _nonces.Remove(expired[index]);
    }

    private sealed record Entry(string Fingerprint, long CreatedAt);
}
