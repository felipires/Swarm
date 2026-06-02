using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Swarm.Node.Data;

/// <summary>
/// Tier-2 task-env store (P1-5a): SQLite-backed, AES-256-GCM encrypted at
/// rest. The encryption key is derived from a machine-local secret so the
/// SQLite db file alone is not enough to decrypt — an attacker who exfils
/// <c>app.db</c> still needs the machine key.
///
/// Key derivation: HKDF-SHA256 from the contents of <c>SWARM_NODE_MACHINE_KEY</c>
/// (or a per-Node random key persisted in <c>{db-dir}/.machinekey</c> on first
/// run if no env override is supplied) mixed with the resolved NodeId.
/// </summary>
public class EnvSecretsStore
{
    private const int NonceSize = 12;       // AES-GCM standard
    private const int TagSize = 16;
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("swarm/envsecrets/aes256gcm/v1");

    private readonly AppDbConnection _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvSecretsStore> _logger;
    private readonly object _keyGate = new();
    private byte[]? _cachedKey;

    public EnvSecretsStore(AppDbConnection db, IConfiguration configuration, ILogger<EnvSecretsStore> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var (ciphertext, nonce) = Encrypt(value);

        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO EnvSecrets (Key, Ciphertext, Nonce, UpdatedAt)
            VALUES ($key, $cipher, $nonce, datetime('now'))
            ON CONFLICT (Key) DO UPDATE SET
                Ciphertext = excluded.Ciphertext,
                Nonce = excluded.Nonce,
                UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$cipher", ciphertext);
        cmd.Parameters.AddWithValue("$nonce", nonce);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Ciphertext, Nonce FROM EnvSecrets WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var ciphertext = (byte[])reader["Ciphertext"];
        var nonce = (byte[])reader["Nonce"];
        return Decrypt(ciphertext, nonce);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EnvSecrets WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key FROM EnvSecrets ORDER BY Key";

        var keys = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            keys.Add(reader.GetString(0));
        return keys;
    }

    private (byte[] Ciphertext, byte[] Nonce) Encrypt(string plaintext)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack ciphertext || tag so both fit in the Ciphertext column.
        var packed = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, packed, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, packed, ciphertext.Length, tag.Length);
        return (packed, nonce);
    }

    private string Decrypt(byte[] packed, byte[] nonce)
    {
        var key = GetKey();
        var ciphertext = new byte[packed.Length - TagSize];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(packed, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(packed, ciphertext.Length, tag, 0, TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetKey()
    {
        if (_cachedKey is not null) return _cachedKey;
        lock (_keyGate)
        {
            if (_cachedKey is not null) return _cachedKey;

            var nodeId = _configuration["NodeId"] ?? "unresolved-node";
            var machineKey = ResolveMachineKey();
            _cachedKey = DeriveAesKey(machineKey, nodeId);
            return _cachedKey;
        }
    }

    private byte[] ResolveMachineKey()
    {
        var envValue = Environment.GetEnvironmentVariable("SWARM_NODE_MACHINE_KEY");
        if (!string.IsNullOrEmpty(envValue))
            return Encoding.UTF8.GetBytes(envValue);

        // Per-Node random key persisted next to the SQLite db on first start.
        // Not portable between containers — that's by design.
        var dbPath = ExtractSqliteFilePath(_db.GetConnectionString());
        var keyFile = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", ".machinekey");

        if (File.Exists(keyFile))
            return File.ReadAllBytes(keyFile);

        var fresh = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile) ?? ".");
        File.WriteAllBytes(keyFile, fresh);
        _logger.LogInformation(
            "Generated new EnvSecrets machine key at {Path} — back this up to keep encrypted secrets recoverable",
            keyFile);
        return fresh;
    }

    private static byte[] DeriveAesKey(byte[] machineKey, string nodeId)
    {
        var salt = Encoding.UTF8.GetBytes(nodeId);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, machineKey, outputLength: 32, salt: salt, info: KeyInfo);
    }

    private static string ExtractSqliteFilePath(string connectionString)
    {
        // Microsoft.Data.Sqlite connection strings look like
        // "DataSource=path/to/app.db;Cache=Shared". Pull the DataSource value.
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = part[..eq].Trim();
            if (key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim();
            }
        }
        return "app.db";
    }
}
