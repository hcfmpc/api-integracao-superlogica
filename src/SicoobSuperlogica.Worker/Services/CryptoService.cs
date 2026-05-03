using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SicoobSuperlogica.Worker.Services;

public interface ICryptoService
{
    byte[] Encrypt(object value);
    T Decrypt<T>(byte[] cipherBlob);
}

/// <summary>
/// AES-256-GCM encryption with PBKDF2-SHA256 key derivation.
/// Blob layout: [16 salt][12 nonce][16 tag][ciphertext]
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;   // AES-GCM standard nonce
    private const int TagSize = 16;     // AES-GCM 128-bit tag
    private const int KeySize = 32;     // AES-256
    private const int Iterations = 100_000;

    private readonly byte[] _masterKey;

    public CryptoService(string masterPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        // Derive a stable in-memory key from the password with a fixed application salt
        // so we can use it directly without per-blob KDF at construction time.
        // Per-blob KDF still runs inside Encrypt/Decrypt with a random salt.
        _masterKey = Encoding.UTF8.GetBytes(masterPassword);
    }

    public byte[] Encrypt(object value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(salt);

        var ciphertext = new byte[json.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, json, ciphertext, tag);

        // blob: salt | nonce | tag | ciphertext
        var blob = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
        salt.CopyTo(blob, 0);
        nonce.CopyTo(blob, SaltSize);
        tag.CopyTo(blob, SaltSize + NonceSize);
        ciphertext.CopyTo(blob, SaltSize + NonceSize + TagSize);

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(json);
        return blob;
    }

    public T Decrypt<T>(byte[] cipherBlob)
    {
        if (cipherBlob.Length < SaltSize + NonceSize + TagSize + 1)
            throw new CryptographicException("Blob too short.");

        var salt = cipherBlob[..SaltSize];
        var nonce = cipherBlob[SaltSize..(SaltSize + NonceSize)];
        var tag = cipherBlob[(SaltSize + NonceSize)..(SaltSize + NonceSize + TagSize)];
        var ciphertext = cipherBlob[(SaltSize + NonceSize + TagSize)..];

        var key = DeriveKey(salt);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return JsonSerializer.Deserialize<T>(plaintext)
                ?? throw new InvalidOperationException("Deserialized value is null.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] DeriveKey(byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(_masterKey, salt, Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeySize);
    }
}
