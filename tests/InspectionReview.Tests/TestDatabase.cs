using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Tests;

/// <summary>SQLite in-memory database with the full schema and HasData seed. Lives as long as its connection.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<AppDbContext> options;

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

        // From the model, not the SQL Server migrations.
        using var db = CreateContext();
        db.Database.EnsureCreated();

        Factory = new ContextFactory(this);
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public AppDbContext CreateContext() => new(options);

    public void Dispose() => connection.Dispose();


    public async Task AddUserAsync(string userId, int? firmId = null)
    {
        await using var db = CreateContext();
        db.Users.Add(new AppUser { Id = userId, UserName = $"{userId}@test.example", DisplayName = userId, FirmId = firmId });
        await db.SaveChangesAsync();
    }

    public async Task AssignAsync(int inspectionId, string inspectorId, bool independenceConfirmed = false)
    {
        await using var db = CreateContext();
        db.InspectionAssignments.Add(new InspectionAssignment
        {
            InspectionId = inspectionId,
            InspectorId = inspectorId,
            IndependenceConfirmedAt = independenceConfirmed ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
    }

    public async Task RecuseAsync(string inspectorId, int firmId)
    {
        await using var db = CreateContext();
        db.Recusals.Add(new Recusal { InspectorId = inspectorId, FirmId = firmId, RecusedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task<int> AddDocumentAsync(
        int inspectionId, string uploadedById, string fileName, DocumentStatus status, bool uploadConfirmed = true)
    {
        await using var db = CreateContext();
        var document = new Document
        {
            DocumentKey = Guid.NewGuid(),
            InspectionId = inspectionId,
            DocumentTypeId = 4, // "Other", from HasData
            FileName = fileName,
            SizeBytes = 1_000,
            Status = status,
            RejectionReason = status == DocumentStatus.Rejected ? "Test rejection reason" : null,
            UploadConfirmed = uploadConfirmed,
            UploadedById = uploadedById,
            UploadedAt = DateTime.UtcNow,
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    public async Task<DateTime?> IndependenceConfirmedAtAsync(int inspectionId, string inspectorId)
    {
        await using var db = CreateContext();
        return await db.InspectionAssignments
            .Where(a => a.InspectionId == inspectionId && a.InspectorId == inspectorId)
            .Select(a => a.IndependenceConfirmedAt)
            .SingleOrDefaultAsync();
    }

    private sealed class ContextFactory(TestDatabase database) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => database.CreateContext();
    }
}
