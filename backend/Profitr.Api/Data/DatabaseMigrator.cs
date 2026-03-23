using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Profitr.Api.Data;

/// <summary>
/// Lightweight migration runner for SQLite.
/// - New databases: EnsureCreated builds the full schema from EF model.
/// - Existing databases: incremental migrations add new tables/columns.
/// 
/// To add a migration:
///   1. Add a new entry to the Migrations list with a unique name and SQL.
///   2. Use "CREATE TABLE IF NOT EXISTS" / "ALTER TABLE ... ADD COLUMN" as needed.
///   3. Never modify or reorder existing migrations.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>
    /// Ordered list of migrations. Each runs at most once per database.
    /// Names must be unique and must never change once shipped.
    /// </summary>
    private static readonly List<(string Name, string Sql)> Migrations =
    [
        ("001_AddCashTransactions", """
            CREATE TABLE IF NOT EXISTS CashTransactions (
                Id TEXT NOT NULL PRIMARY KEY,
                PortfolioId TEXT NOT NULL,
                Type TEXT NOT NULL DEFAULT 'Deposit',
                Amount TEXT NOT NULL DEFAULT '0',
                Currency TEXT NOT NULL DEFAULT 'EUR',
                TransactionDate TEXT NOT NULL,
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (PortfolioId) REFERENCES Portfolios(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_CashTransactions_PortfolioId ON CashTransactions(PortfolioId);
        """),
    ];

    public static async Task MigrateAsync(ProfitrDbContext db)
    {
        // For brand new databases, EnsureCreated builds the full schema (including CashTransactions).
        // For existing databases, it's a no-op — then the migrations below handle incremental changes.
        await db.Database.EnsureCreatedAsync();

        var conn = (SqliteConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();

        // Create migrations tracking table if it doesn't exist
        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS __Migrations (
                Name TEXT NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
        """);

        // Get already-applied migrations
        var applied = new HashSet<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Name FROM __Migrations";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                applied.Add(reader.GetString(0));
            }
        }

        // Apply pending migrations in order
        foreach (var (name, sql) in Migrations)
        {
            if (applied.Contains(name)) continue;

            await using var transaction = await conn.BeginTransactionAsync();
            try
            {
                // Split on semicolons to execute multiple statements
                foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(statement))
                        await ExecuteNonQueryAsync(conn, statement);
                }

                // Record migration
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO __Migrations (Name, AppliedAt) VALUES (@name, @now)";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
                Console.WriteLine($"[Migration] Applied: {name}");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
