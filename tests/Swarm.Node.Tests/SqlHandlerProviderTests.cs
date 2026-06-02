using FluentAssertions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Swarm.Node.Handlers;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// Multi-flavor SQL handler: the connection-type switch must produce the
/// correct provider for each alias, and reject unknown values.
/// </summary>
public class SqlHandlerProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("postgres")]
    [InlineData("PostgreSQL")]
    public void OpenConnection_DefaultsToNpgsql(string? provider)
    {
        var conn = SqlHandlerV1.OpenConnection(provider, "Host=localhost");
        conn.Should().BeOfType<NpgsqlConnection>();
    }

    [Theory]
    [InlineData("mssql")]
    [InlineData("sqlserver")]
    [InlineData("SqlServer")]
    public void OpenConnection_SqlServer(string provider)
    {
        var conn = SqlHandlerV1.OpenConnection(provider, "Server=localhost");
        conn.Should().BeOfType<SqlConnection>();
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("mariadb")]
    [InlineData("MySQL")]
    public void OpenConnection_MySql(string provider)
    {
        var conn = SqlHandlerV1.OpenConnection(provider, "Server=localhost;Database=test");
        conn.Should().BeOfType<MySqlConnection>();
    }

    [Fact]
    public void OpenConnection_UnknownProvider_Throws()
    {
        var act = () => SqlHandlerV1.OpenConnection("oracle", "anything");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unsupported SQL provider 'oracle'*");
    }
}
