using InspectionReview.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Tests;

/// <summary>
/// A throwaway SQLite in-memory database with the app's full schema and HasData seed
/// (roles, 2 firms, INS-001 to INS-003). The database lives as long as this object:
/// an in-memory SQLite database is deleted when its connection closes.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<AppDbContext> options;

    /// <param name="log">Optional sink for EF's SQL logging, e.g. xUnit's ITestOutputHelper.WriteLine.</param>
    public TestDatabase(Action<string>? log = null)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
        if (log is not null)
        {
            builder.LogTo(log, [DbLoggerCategory.Database.Command.Name]);
        }
        options = builder.Options;

        // Builds the tables from the model (not from the SQL Server migrations), including HasData rows.
        using var db = CreateContext();
        db.Database.EnsureCreated();

        Factory = new ContextFactory(this);
    }

    /// <summary>Give this to services that take IDbContextFactory. Every context shares the same database.</summary>
    public IDbContextFactory<AppDbContext> Factory { get; }

    /// <summary>A new context on the shared connection, for arranging test data. Dispose it after use.</summary>
    public AppDbContext CreateContext() => new(options);

    public void Dispose() => connection.Dispose();

    private sealed class ContextFactory(TestDatabase database) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => database.CreateContext();
    }
}
