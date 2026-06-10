using Microsoft.Data.Sqlite;

namespace Swarm.Node.Data;

/// <summary>
/// Tier-3 task-config store: SQLite-backed, plaintext. Used for non-secret
/// operational config (feature flags, base URLs) pushed by the Cluster
/// via heartbeat when <c>IsSecret = false</c> on the <c>EnvOp</c>.
/// </summary>
public class PlaintextConfigStore
{
    private readonly AppDbConnection _db;

    public PlaintextConfigStore(AppDbConnection db)
    {
        _db = db;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PlaintextConfig (Key, Value, UpdatedAt)
            VALUES ($key, $value, datetime('now'))
            ON CONFLICT (Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM PlaintextConfig WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaintextConfig WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_db.GetConnectionString());
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key FROM PlaintextConfig ORDER BY Key";

        var keys = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            keys.Add(reader.GetString(0));
        return keys;
    }
}
