using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Xunit.Abstractions;

namespace InspectionReview.Tests;

/// <summary>
/// Inspection detail, independence confirmation and recusal rules, against a real SQLite in-memory database.
///
/// Seed data from HasData, present in every test:
///   Firm 1 Harbourview Property Group: INS-001 (Id 1), INS-002 (Id 2)
///   Firm 2 Northgate Facilities Ltd:   INS-003 (Id 3)
/// </summary>
public sealed class InspectionDetailTests(ITestOutputHelper output) : IDisposable
{
    private const int Harbourview = 1;
    private const int Northgate = 2;
    private const string Inspector = "inspector";
    private const string FirmUser = "firm-user";

    private readonly TestDatabase database = new(output.WriteLine);

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Recused_inspector_can_still_view_the_inspection_but_cannot_create_findings()
    {
        // Assigned AND confirmed, so the recusal is the only thing that should block findings.
        await database.AddUserAsync(Inspector);
        await database.AssignAsync(inspectionId: 3, Inspector, independenceConfirmed: true);
        await database.RecuseAsync(Inspector, Northgate);
        var (service, policy) = CreateServices(TestUsers.Inspector(Inspector));

        var detail = await service.GetInspectionDetailAsync(3);

        Assert.NotNull(detail);
        Assert.Equal("INS-003", detail.Reference);
        Assert.True(detail.IsRecused);
        Assert.False(detail.CanCreateFinding);
        Assert.False(detail.NeedsIndependenceConfirmation);
        Assert.False(await policy.CanCreateFindingAsync(3));
    }

    [Fact]
    public async Task Inspector_who_has_not_confirmed_independence_cannot_create_findings()
    {
        await database.AddUserAsync(Inspector);
        await database.AssignAsync(inspectionId: 1, Inspector, independenceConfirmed: false);
        var (service, policy) = CreateServices(TestUsers.Inspector(Inspector));

        var before = await service.GetInspectionDetailAsync(1);

        Assert.NotNull(before);
        Assert.False(before.CanCreateFinding);
        Assert.True(before.NeedsIndependenceConfirmation);
        Assert.False(await policy.CanCreateFindingAsync(1));

        // Confirming is the only missing piece: afterwards findings are allowed.
        await service.ConfirmIndependenceAsync(1);
        var after = await service.GetInspectionDetailAsync(1);

        Assert.NotNull(after);
        Assert.True(after.CanCreateFinding);
        Assert.False(after.NeedsIndependenceConfirmation);
        Assert.NotNull(await database.IndependenceConfirmedAtAsync(1, Inspector));
    }

    [Fact]
    public async Task Inspector_who_is_not_assigned_cannot_confirm_independence()
    {
        await database.AddUserAsync(Inspector);
        await database.AssignAsync(inspectionId: 1, Inspector);   // assigned elsewhere, not to INS-002
        var (service, _) = CreateServices(TestUsers.Inspector(Inspector));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmIndependenceAsync(2));

        Assert.Null(await database.IndependenceConfirmedAtAsync(2, Inspector));   // nothing was written
    }

    [Fact]
    public async Task Inspectors_only_see_accepted_documents()
    {
        await AddMixedDocumentsToInspection2Async();
        await database.AddUserAsync(Inspector);
        var (service, _) = CreateServices(TestUsers.Inspector(Inspector));

        var detail = await service.GetInspectionDetailAsync(2);

        Assert.NotNull(detail);
        var document = Assert.Single(detail.Documents);
        Assert.Equal("accepted.pdf", document.FileName);
        // Inspectors aren't sent the check status or rejection reason at all.
        Assert.False(detail.ShowDocumentStatus);
        Assert.Null(document.Status);
        Assert.Null(document.RejectionReason);
    }

    [Fact]
    public async Task Firm_users_see_all_their_confirmed_documents_with_status()
    {
        await AddMixedDocumentsToInspection2Async();
        var (service, _) = CreateServices(TestUsers.FirmUser(Harbourview));

        var detail = await service.GetInspectionDetailAsync(2);

        Assert.NotNull(detail);
        Assert.True(detail.ShowDocumentStatus);
        Assert.Equal(
            new[] { "accepted.pdf", "pending.pdf", "rejected.pdf" },
            detail.Documents.Select(d => d.FileName).Order());
        var rejected = detail.Documents.Single(d => d.FileName == "rejected.pdf");
        Assert.Equal(DocumentStatus.Rejected, rejected.Status);
        Assert.Equal("Test rejection reason", rejected.RejectionReason);
    }

    /// <summary>
    /// INS-002 (Harbourview) gets one confirmed document of each status, plus an Accepted one whose
    /// upload never completed: it must be hidden from everyone.
    /// </summary>
    private async Task AddMixedDocumentsToInspection2Async()
    {
        await database.AddUserAsync(FirmUser, Harbourview);
        await database.AddDocumentAsync(2, FirmUser, "accepted.pdf", DocumentStatus.Accepted);
        await database.AddDocumentAsync(2, FirmUser, "rejected.pdf", DocumentStatus.Rejected);
        await database.AddDocumentAsync(2, FirmUser, "pending.pdf", DocumentStatus.PendingCheck);
        await database.AddDocumentAsync(2, FirmUser, "unfinished-upload.pdf", DocumentStatus.Accepted, uploadConfirmed: false);
    }

    private (InspectionService Service, AccessPolicy Policy) CreateServices(CurrentUserInfo user)
    {
        ICurrentUser currentUser = TestUsers.As(user);
        var policy = new AccessPolicy(currentUser, database.Factory);
        return (new InspectionService(database.Factory, policy, currentUser), policy);
    }
}
