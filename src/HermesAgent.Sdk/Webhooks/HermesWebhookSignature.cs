using System.Security.Cryptography;
using System.Text;

namespace HermesAgent.Sdk;

public static class HermesWebhookSignature
{
    public static string ComputeHmacSha256(string body, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyGitHubSignature(string body, string secret, string signatureHeader)
    {
        var expected = "sha256=" + ComputeHmacSha256(body, secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader));
    }

    public static bool VerifyGitLabToken(string token, string secret)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret));
    }
}
