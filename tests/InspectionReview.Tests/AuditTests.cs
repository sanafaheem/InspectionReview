using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace InspectionReview.Tests;

// The services contain no audit code: every row checked here is written by AuditInterceptor.
public sealed class AuditTests : IDisposable
{
    private const string Author = "author";
    private const string CoAuthor = "co-author";
    private const string FirmUser = "firm-user";

    private readonly TestDatabase database;
    private readonly FindingService author;
    private readonly FindingService coAuthor;

    public AuditTests(ITestOutputHelper output)
    {
        database = new TestDatabase(output.WriteLine);
        author = CreateService(TestUsers.Inspector(Author));
        coAuthor = CreateService(TestUsers.Inspector(CoAuthor));
    }

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Editing_a_findings_title_writes_an_audit_entry_with_old_and_new_values()
    {
        await ArrangeInspectorsAsync();
        var findingId = await author.CreateAsync(1, "Old title", "Text", []);

        await author.UpdateDraftAsync(findingId, await database.TakeLockAsync(findingId, Author), "New title", "Text", []);

        var titleChanges = (await FindingAuditAsync(findingId)).Where(a => a.FieldName == nameof(Finding.Title)).ToList();
        Assert.Contains(titleChanges, a => a.OldValue == null && a.NewValue == "Old title");                    // creation
        Assert.Contains(titleChanges, a => a.OldValue == "Old title" && a.NewValue == "New title" && a.UserId == Author);
        // Text didn't change, so it's only logged once (on creation).
        Assert.Single(await FindingAuditAsync(findingId), a => a.FieldName == nameof(Finding.Text));
    }

    [Fact]
    public async Task Submitting_writes_a_state_change_from_draft_to_submitted_with_the_submitter()
    {
        await ArrangeInspectorsAsync();
        var findingId = await author.CreateAsync(1, "Title", "Text", []);

        await coAuthor.SubmitAsync(findingId, await database.TakeLockAsync(findingId, CoAuthor));   // a different inspector than the creator

        var stateChange = Assert.Single(
            await FindingAuditAsync(findingId), a => a.FieldName == nameof(Finding.State) && a.OldValue != null);
        Assert.Equal("Draft", stateChange.OldValue);
        Assert.Equal("Submitted", stateChange.NewValue);
        Assert.Equal(CoAuthor, stateChange.UserId);
    }

    [Fact]
    public async Task Linking_and_unlinking_a_document_is_logged_on_the_finding()
    {
        await ArrangeInspectorsAsync();
        await database.AddUserAsync(FirmUser, firmId: 1);
        var documentId = await database.AddDocumentAsync(1, FirmUser, "Fire Safety Policy.pdf", DocumentStatus.Accepted);
        var findingId = await author.CreateAsync(1, "Title", "Text", []);

        var version = await author.UpdateDraftAsync(findingId, await database.TakeLockAsync(findingId, Author), "Title", "Text", [documentId]);
        await author.UpdateDraftAsync(findingId, version, "Title", "Text", []);

        var links = (await FindingAuditAsync(findingId))
            .Where(a => a.FieldName == AuditInterceptor.LinkedDocumentsField)
            .OrderBy(a => a.Id)
            .ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal((null, "Fire Safety Policy.pdf"), (links[0].OldValue, links[0].NewValue));
        Assert.Equal(("Fire Safety Policy.pdf", null), (links[1].OldValue, links[1].NewValue));
        Assert.All(links, a => Assert.Equal(Author, a.UserId));
    }

    [Fact]
    public async Task Modifying_or_deleting_an_audit_entry_throws()
    {
        await ArrangeInspectorsAsync();
        var findingId = await author.CreateAsync(1, "Title", "Text", []);
        var original = (await FindingAuditAsync(findingId)).First();

        await using (var db = database.CreateContext())
        {
            var entry = await db.AuditEntries.SingleAsync(a => a.Id == original.Id);
            entry.NewValue = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using (var db = database.CreateContext())
        {
            db.AuditEntries.Remove(await db.AuditEntries.SingleAsync(a => a.Id == original.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        var unchanged = Assert.Single(await FindingAuditAsync(findingId), a => a.Id == original.Id);
        Assert.Equal(original.NewValue, unchanged.NewValue);
    }

    [Fact]
    public async Task Skipped_fields_are_not_audited()
    {
        await ArrangeInspectorsAsync();
        var findingId = await author.CreateAsync(1, "Title", "Text", []);
        await author.UpdateDraftAsync(findingId, await database.TakeLockAsync(findingId, Author), "Changed", "Text", []);   // also changes UpdatedAt

        var fields = (await FindingAuditAsync(findingId)).Select(a => a.FieldName).ToHashSet();

        Assert.Contains(nameof(Finding.Title), fields);
        Assert.DoesNotContain(nameof(Finding.UpdatedAt), fields);
        // Not on any entity yet; kept in the skip list for the edit lock.
        Assert.DoesNotContain("LockedById", fields);
        Assert.DoesNotContain("LockedAt", fields);
        Assert.DoesNotContain("Version", fields);
    }

    [Fact]
    public async Task If_writing_the_audit_rows_fails_the_change_is_rolled_back()
    {
        await ArrangeInspectorsAsync();
        ICurrentUser currentUser = TestUsers.As(TestUsers.Inspector(Author));
        var factory = database.FactoryFor(currentUser, new FailWhenWritingAuditRows());
        var service = new FindingService(factory, new AccessPolicy(currentUser, factory, database.Clock), currentUser, database.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(1, "Title", "Text", []));

        await using var db = database.CreateContext();
        Assert.Equal(0, await db.Findings.CountAsync());
        Assert.False(await db.AuditEntries.AnyAsync(a => a.EntityName == nameof(Finding)));
    }

    // Both assigned to INS-001 with independence confirmed.
    private async Task ArrangeInspectorsAsync()
    {
        foreach (var inspector in new[] { Author, CoAuthor })
        {
            await database.AddUserAsync(inspector);
            await database.AssignAsync(inspectionId: 1, inspector, independenceConfirmed: true);
        }
    }

    private FindingService CreateService(CurrentUserInfo user)
    {
        ICurrentUser currentUser = TestUsers.As(user);
        var factory = database.FactoryFor(currentUser);
        return new FindingService(factory, new AccessPolicy(currentUser, factory, database.Clock), currentUser, database.Clock);
    }

    private async Task<List<AuditEntry>> FindingAuditAsync(int findingId)
    {
        await using var db = database.CreateContext();
        var entityId = findingId.ToString();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityName == nameof(Finding) && a.EntityId == entityId)
            .ToListAsync();
    }

    // Lets the real change save, then fails the audit rows' save that follows it.
    private sealed class FailWhenWritingAuditRows : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEntry>().Any(e => e.State == EntityState.Added))
            {
                throw new InvalidOperationException("Simulated failure while writing audit rows.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
