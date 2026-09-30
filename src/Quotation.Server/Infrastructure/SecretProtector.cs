using System.Security.Cryptography;
using System.Text;

namespace Quotation.Server.Infrastructure;

/// <summary>Encrypts secrets (API keys, OAuth tokens) before they are written to the database.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

/// <summary>
/// Windows: DPAPI with LocalMachine scope, so only processes on the server PC can decrypt.
/// Other OS (development/CI only): AES-GCM with a key file in the data directory.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "TSQuotation.v1.secrets"u8.ToArray();
    private readonly byte[]? _devKey;

    public SecretProtector(string dataDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            var keyFile = Path.Combine(dataDirectory, "dev-secret.key");
            if (!File.Exists(keyFile))
            {
                File.WriteAllBytes(keyFile, RandomNumberGenerator.GetBytes(32));
            }
            _devKey = File.ReadAllBytes(keyFile);
        }
    }

    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        if (OperatingSystem.IsWindows())
        {
            return "dpapi:" + Convert.ToBase64String(
                ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine));
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_devKey!, 16);
        aes.Encrypt(nonce, data, cipher, tag, Entropy);
        return "aes:" + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText)
    {
        if (protectedText.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows()) throw new CryptographicException("DPAPI secrets can only be read on Windows.");
            var bytes = Convert.FromBase64String(protectedText[6..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine));
        }
        if (protectedText.StartsWith("aes:", StringComparison.Ordinal))
        {
            var all = Convert.FromBase64String(protectedText[4..]);
            var nonce = all[..12];
            var tag = all[12..28];
            var cipher = all[28..];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_devKey ?? throw new CryptographicException("No key available."), 16);
            aes.Decrypt(nonce, cipher, tag, plain, Entropy);
            return Encoding.UTF8.GetString(plain);
        }
        throw new CryptographicException("Unknown secret format.");
    }
}
