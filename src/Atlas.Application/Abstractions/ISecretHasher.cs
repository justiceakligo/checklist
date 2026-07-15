namespace Atlas.Application.Abstractions;

public interface ISecretHasher
{
    byte[] HashSecret(string secret);
    bool VerifySecret(string secret, byte[] expectedHash);
}
