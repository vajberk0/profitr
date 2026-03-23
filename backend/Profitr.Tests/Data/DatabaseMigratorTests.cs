using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;

namespace Profitr.Tests.Data;

public class DatabaseMigratorTests
{
    private static ProfitrDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ProfitrDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ProfitrDbContext(options);
    }

    [Fact]
    public async Task NewDatabase_CreatesAllTablesAndRecordsMigrations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        using var db = CreateContext(connection);
        await DatabaseMigrator.MigrateAsync(db);

        // CashTransactions table should exist
        var tables = await GetTables(connection);
        Assert.Contains("CashTransactions", tables);
        Assert.Contains("__Migrations", tables);

        // Migration should be recorded
        var migrations = await GetAppliedMigrations(connection);
        Assert.Contains("001_AddCashTransactions", migrations);
    }

    [Fact]
    public async Task ExistingDatabase_WithoutCashTransactions_AddsThem()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // Simulate an "old" database: create schema without CashTransactions
        using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            // Drop CashTransactions to simulate pre-migration state
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS CashTransactions";
            await cmd.ExecuteNonQueryAsync();
        }

        // Verify it's gone
        var tablesBefore = await GetTables(connection);
        Assert.DoesNotContain("CashTransactions", tablesBefore);

        // Now run the migrator on the existing DB
        using (var db = CreateContext(connection))
        {
            await DatabaseMigrator.MigrateAsync(db);
        }

        // CashTransactions should be recreated by migration
        var tablesAfter = await GetTables(connection);
        Assert.Contains("CashTransactions", tablesAfter);

        var migrations = await GetAppliedMigrations(connection);
        Assert.Contains("001_AddCashTransactions", migrations);
    }

    [Fact]
    public async Task RunningTwice_DoesNotDuplicateMigrations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        using var db = CreateContext(connection);
        await DatabaseMigrator.MigrateAsync(db);
        await DatabaseMigrator.MigrateAsync(db); // second run

        var migrations = await GetAppliedMigrations(connection);
        Assert.Single(migrations, m => m == "001_AddCashTransactions");
    }

    private static async Task<List<string>> GetTables(SqliteConnection connection)
    {
        var tables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<List<string>> GetAppliedMigrations(SqliteConnection connection)
    {
        var names = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM __Migrations ORDER BY Name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
