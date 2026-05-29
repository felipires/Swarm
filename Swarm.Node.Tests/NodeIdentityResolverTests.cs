using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarm.Node.Data;
using Swarm.Node.Services;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// Covers P2-1: on first startup the resolver generates a fresh GUID and
/// persists it; on subsequent startups it reads the same GUID back. Uses
/// SQLite's shared-cache in-memory mode so each test gets an isolated DB
/// without writing to disk.
/// </summary>
public class NodeIdentityResolverTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public NodeIdentityResolverTests()
    {
        var name = $"resolver-test-{Guid.NewGuid():N}";
        _connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Configuration (
                NodeId TEXT NOT NULL PRIMARY KEY,
                NodeName TEXT NOT NULL,
                Registered BOOLEAN NOT NULL DEFAULT 0,
                Online BOOLEAN NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ResolveAsync_FirstCall_GeneratesAndPersistsFreshGuid()
    {
        var db = BuildDb();

        var id = await NodeIdentityResolver.ResolveAsync(db, NullLogger.Instance);

        id.Should().NotBe(Guid.Empty);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT NodeId FROM Configuration";
        var stored = (string?)cmd.ExecuteScalar();
        Guid.Parse(stored!).Should().Be(id);
    }

    [Fact]
    public async Task ResolveAsync_SubsequentCall_ReturnsPreviouslyPersistedId()
    {
        var db = BuildDb();

        var first = await NodeIdentityResolver.ResolveAsync(db, NullLogger.Instance);
        var second = await NodeIdentityResolver.ResolveAsync(db, NullLogger.Instance);

        second.Should().Be(first);

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Configuration";
        var count = (long)cmd.ExecuteScalar()!;
        count.Should().Be(1, "exactly one Configuration row should exist after either call");
    }

    [Fact]
    public async Task ResolveAsync_PreInsertedNodeName_PreservesOnRead()
    {
        var preset = Guid.NewGuid();
        using (var seed = _keepAlive.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO Configuration (NodeId, NodeName, Registered, Online)
                VALUES ($id, 'preexisting-node', 1, 1)
                """;
            seed.Parameters.AddWithValue("$id", preset.ToString());
            seed.ExecuteNonQuery();
        }

        var resolved = await NodeIdentityResolver.ResolveAsync(BuildDb(), NullLogger.Instance);

        resolved.Should().Be(preset);
    }

    private AppDbConnection BuildDb()
    {
        var options = Options.Create(new DataConfiguration { ConnectionString = _connectionString });
        return new AppDbConnection(options, NullLogger<AppDbConnection>.Instance);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
        SqliteConnection.ClearAllPools();
        GC.SuppressFinalize(this);
    }
}
