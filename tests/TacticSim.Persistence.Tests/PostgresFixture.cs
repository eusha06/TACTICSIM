using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TacticSim.Web.Data;

namespace TacticSim.Persistence.Tests;

// All writes stay inside a uniquely named test schema, which is removed afterwards.
// The application's public tables are neither read nor changed by these tests.
public sealed class PostgresFixture : IDisposable
{
    private readonly string _baseConnection;
    private readonly string _schema = "tacticsim_test_" + Guid.NewGuid().ToString("N");
    private readonly string _testConnection;

    public PostgresFixture() : this(null) { }

    internal PostgresFixture(Action<NpgsqlConnection>? beforeMigration)
    {
        _baseConnection = Environment.GetEnvironmentVariable("TACTICSIM_TEST_CONNECTION") ??
            new NpgsqlConnectionStringBuilder { Host = "/tmp", Database = "tacticsim", Username = Environment.UserName }.ConnectionString;
        _testConnection = new NpgsqlConnectionStringBuilder(_baseConnection) { SearchPath = _schema }.ConnectionString;
        using var connection = new NpgsqlConnection(_baseConnection);
        connection.Open();
        using var create = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", connection);
        create.ExecuteNonQuery();
        try
        {
            if (beforeMigration is not null)
            {
                using var seedConnection = new NpgsqlConnection(_testConnection);
                seedConnection.Open();
                beforeMigration(seedConnection);
            }
            using var db = Open();
            db.Database.Migrate();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public MatchDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<MatchDbContext>()
        .UseNpgsql(_testConnection, options => options.MigrationsHistoryTable("__EFMigrationsHistory", _schema))
        .AddInterceptors(interceptors).Options);

    public async Task Sql(string sql)
    {
        await using var connection = new NpgsqlConnection(_testConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        using var connection = new NpgsqlConnection(_baseConnection);
        connection.Open();
        using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
        drop.ExecuteNonQuery();
    }
}
