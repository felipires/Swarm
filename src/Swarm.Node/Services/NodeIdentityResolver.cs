using Microsoft.Data.Sqlite;
using Swarm.Node.Data;

namespace Swarm.Node.Services;

/// <summary>
/// Looks up the Node's persistent identity from SQLite, generating and
/// persisting a fresh GUID on first startup. Called once during
/// <c>StartupService.InitializeNodeAsync</c>, after the schema migrations
/// have run, and before <c>RegistrationService.RegisterWithClusterAsync</c>.
/// </summary>
public static class NodeIdentityResolver
{
    public static async Task<Guid> ResolveAsync(
        AppDbConnection dbConnection,
        ILogger logger,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(dbConnection.GetConnectionString());
        await conn.OpenAsync(ct);

        await using var read = conn.CreateCommand();
        read.CommandText = "SELECT NodeId FROM Configuration LIMIT 1";
        var existing = await read.ExecuteScalarAsync(ct) as string;

        if (Guid.TryParse(existing, out var stored))
        {
            logger.LogInformation("Resolved NodeId {NodeId} from local Configuration", stored);
            return stored;
        }

        var fresh = Guid.NewGuid();
        await using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO Configuration (NodeId, NodeName, Registered, Online)
            VALUES ($id, '', 0, 0)
            """;
        insert.Parameters.AddWithValue("$id", fresh.ToString());
        await insert.ExecuteNonQueryAsync(ct);

        logger.LogInformation(
            "Generated new NodeId {NodeId}; persisted to local Configuration", fresh);
        return fresh;
    }
}
