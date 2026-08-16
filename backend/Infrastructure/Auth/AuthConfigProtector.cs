using System.Security.Cryptography;
using System.Text;

namespace IncidentManagement.Api.Infrastructure.Auth;

/// <summary>
/// Encrypts workflow step auth configs (tokens / credentials) at rest.
/// Uses AES-256-CBC with a key derived from config ("Workflow:AuthConfigEncryptionKey").
/// Payload format: "base64(iv):base64(ciphertext)". The key is per-app; rotate by
/// changing the config value (old runs keep their encrypted blobs, but the builder
/// can only decrypt with the current key — so change the key only when you also
/// re-save the workflows).
/// </summary>
public interface IAuthConfigProtector
{
    string Protect(string plaintext);
    string Unprotect(string payload);
}

public class AesAuthConfigProtector : IAuthConfigProtector
{
    private readonly byte[] _key;

    public AesAuthConfigProtector(IConfiguration cfg)
    {
        var secret = cfg["Workflow:AuthConfigEncryptionKey"]
            ?? "DEV-ONLY-workflow-auth-config-key-change-in-production";
        // Normalize to a fixed 32-byte key regardless of secret length.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(cipher);
    }

    public string Unprotect(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return "";
        var parts = payload.Split(':', 2);
        if (parts.Length != 2) return ""; // not encrypted (legacy/empty) — treat as plaintext-less
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.IV = Convert.FromBase64String(parts[0]);
        using var dec = aes.CreateDecryptor();
        var bytes = Convert.FromBase64String(parts[1]);
        var plain = dec.TransformFinalBlock(bytes, 0, bytes.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
