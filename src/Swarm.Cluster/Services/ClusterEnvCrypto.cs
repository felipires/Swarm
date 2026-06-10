using System.Security.Cryptography;
using System.Text;

namespace Swarm.Cluster.Services;

/// <summary>
/// AES-256-GCM encryption for pending <c>NodeEnvOp</c> values at rest on the
/// Cluster. Only used for the transit window between enqueue and Node ack —
/// once acked the value is nulled. The key lives in <c>Env:EncryptionKey</c>
/// (32-byte base-64); if absent, operations run in plaintext-passthrough mode
/// and a startup warning is logged.
/// </summary>
public class ClusterEnvCrypto
{
    private const int NonceSize = 12;
    private const int TagSize   = 16;

    private readonly byte[]? _key;
    private readonly ILogger<ClusterEnvCrypto> _logger;

    public ClusterEnvCrypto(IConfiguration configuration, ILogger<ClusterEnvCrypto> logger)
    {
        _logger = logger;
        var raw = configuration["Env:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning(
                "Env:EncryptionKey is not configured — pending env op values will be stored " +
                "plaintext in Postgres until the Node acks them. Set a 32-byte base-64 key " +
                "to encrypt them at rest.");
            return;
        }
        try
        {
            var decoded = Convert.FromBase64String(raw);
            if (decoded.Length != 32)
                throw new InvalidOperationException($"Env:EncryptionKey must be 32 bytes, got {decoded.Length}");
            _key = decoded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid Env:EncryptionKey — falling back to plaintext storage");
        }
    }

    public bool IsConfigured => _key is not null;

    /// <summary>
    /// Encrypt <paramref name="plaintext"/> and return a base-64 encoded blob
    /// (nonce || ciphertext || tag). Returns the original string unchanged when
    /// no key is configured.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (_key is null) return plaintext;

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var packed = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, packed, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, packed, NonceSize + ciphertext.Length, TagSize);
        return Convert.ToBase64String(packed);
    }

    /// <summary>
    /// Decrypt a blob produced by <see cref="Encrypt"/>. Returns the original
    /// string unchanged when no key is configured (passthrough mode).
    /// </summary>
    public string Decrypt(string blob)
    {
        if (_key is null) return blob;

        var packed = Convert.FromBase64String(blob);
        var nonce      = new byte[NonceSize];
        var ciphertext = new byte[packed.Length - NonceSize - TagSize];
        var tag        = new byte[TagSize];

        Buffer.BlockCopy(packed, 0,                             nonce,      0, NonceSize);
        Buffer.BlockCopy(packed, NonceSize,                     ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(packed, NonceSize + ciphertext.Length, tag,        0, TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
