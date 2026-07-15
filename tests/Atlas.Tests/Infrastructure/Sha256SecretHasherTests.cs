using Atlas.Infrastructure.Security;
using Microsoft.Extensions.Options;

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
}
