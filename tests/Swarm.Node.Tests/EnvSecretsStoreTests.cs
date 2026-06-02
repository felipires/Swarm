using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarm.Node.Data;
using Swarm.Node.ValueResolution;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// P1-5a Tier 2: ciphertext + nonce round-trip through the SQLite store.
/// Uses a shared-cache in-memory SQLite db so the test doesn't touch disk
/// and a deterministic machine key so encryption is reproducible.
/// </summary>
public class EnvSecretsStoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;
    private readonly EnvSecretsStore _store;

    public EnvSecretsStoreTests()
    {
        var name = $"envsecrets-{Guid.NewGuid():N}";
        _connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE EnvSecrets (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Ciphertext BLOB NOT NULL,
                    Nonce BLOB NOT NULL,
                    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                );
                """;
            cmd.ExecuteNonQuery();
        }

        Environment.SetEnvironmentVariable("SWARM_NODE_MACHINE_KEY", "test-machine-key-for-deterministic-derivation");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NodeId"] = "11111111-1111-1111-1111-111111111111",
            })
            .Build();

        var dataOpts = Options.Create(new DataConfiguration { ConnectionString = _connectionString });
        var db = new AppDbConnection(dataOpts, NullLogger<AppDbConnection>.Instance);
        _store = new EnvSecretsStore(db, config, NullLogger<EnvSecretsStore>.Instance);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsPlaintext()
    {
        await _store.SetAsync("API_TOKEN", "secret-value-123");

        var value = await _store.GetAsync("API_TOKEN");

        value.Should().Be("secret-value-123");
    }

    [Fact]
    public async Task SetAsync_CipherTextOnDisk_DoesNotMatchPlaintext()
    {
        await _store.SetAsync("API_TOKEN", "secret-value-123");

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = "SELECT Ciphertext FROM EnvSecrets WHERE Key = 'API_TOKEN'";
        var bytes = (byte[])cmd.ExecuteScalar()!;
        var hex = Convert.ToHexString(bytes);

        hex.Should().NotContain("736563726574",
            "ciphertext must not contain the plaintext 'secret' as bytes");
    }

    [Fact]
    public async Task SetAsync_SameKeyTwice_OverwritesValue()
    {
        await _store.SetAsync("K", "first");
        await _store.SetAsync("K", "second");

        (await _store.GetAsync("K")).Should().Be("second");
        (await _store.ListKeysAsync()).Should().ContainSingle().Which.Should().Be("K");
    }

    [Fact]
    public async Task DeleteAsync_RemovesKey()
    {
        await _store.SetAsync("K", "v");
        await _store.DeleteAsync("K");

        (await _store.GetAsync("K")).Should().BeNull();
        (await _store.ListKeysAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnvStoreResolver_FallsThroughToTier2()
    {
        // Tier 1 has no SWARM_TASKENV_X — resolver should hit Tier 2.
        Environment.SetEnvironmentVariable("SWARM_TASKENV_X", null);
        await _store.SetAsync("X", "tier2-value");

        var resolver = new EnvStoreResolver(_store);
        var resolved = await resolver.ResolveAsync("X", default);

        resolved.Should().NotBeNull();
        resolved!.Raw.Should().Be("tier2-value");
        resolved.IsSecret.Should().BeTrue(
            "Tier 2 values are marked secret even without an explicit :secret modifier");
    }

    [Fact]
    public async Task EnvStoreResolver_Tier1WinsOverTier2()
    {
        Environment.SetEnvironmentVariable("SWARM_TASKENV_Y", "tier1-override");
        try
        {
            await _store.SetAsync("Y", "tier2-value");

            var resolver = new EnvStoreResolver(_store);
            var resolved = await resolver.ResolveAsync("Y", default);

            resolved!.Raw.Should().Be("tier1-override");
            resolved.IsSecret.Should().BeFalse(
                "Tier 1 values are explicit env overrides; secrecy is opt-in via :secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWARM_TASKENV_Y", null);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SWARM_NODE_MACHINE_KEY", null);
        _keepAlive.Dispose();
        SqliteConnection.ClearAllPools();
        GC.SuppressFinalize(this);
    }
}
