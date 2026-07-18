using Atlas.Infrastructure.Security;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Tests.Infrastructure;

public sealed class Sha256SecretHasherTests
{
    [Fact]
    public void Verify_secret_uses_fixed_hash_and_pepper()
    {
        var hasher = new Sha256SecretHasher(Options.Create(new SecurityOptions
        {
            HashPepper = "test-pepper"
        }));

        var hash = hasher.HashSecret("atl_live_secret");

        Assert.True(hasher.VerifySecret("atl_live_secret", hash));
        Assert.False(hasher.VerifySecret("atl_live_other", hash));
    }

    [Fact]
    public void Verify_secret_accepts_legacy_sha256_hash_when_pepper_is_configured()
    {
        var legacyHash = SHA256.HashData(Encoding.UTF8.GetBytes("atl_live_secret"));
        var hasher = new Sha256SecretHasher(Options.Create(new SecurityOptions
        {
            HashPepper = "test-pepper"
        }));

        Assert.True(hasher.VerifySecret("atl_live_secret", legacyHash));
        Assert.False(hasher.VerifySecret("atl_live_other", legacyHash));
    }
}
