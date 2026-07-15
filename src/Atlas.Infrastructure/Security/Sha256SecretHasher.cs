using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Security;

public sealed class Sha256SecretHasher(IOptions<SecurityOptions> options) : ISecretHasher
{
    private readonly byte[]? _pepper = string.IsNullOrWhiteSpace(options.Value.HashPepper)
        ? null
        : Encoding.UTF8.GetBytes(options.Value.HashPepper);

    public byte[] HashSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (_pepper is null)
        {
            return SHA256.HashData(secretBytes);
        }

        using var hmac = new HMACSHA256(_pepper);
        return hmac.ComputeHash(secretBytes);
    }

    public bool VerifySecret(string secret, byte[] expectedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(expectedHash);

        var actual = HashSecret(secret);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}
