using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace InspectionReview.Tests;

// One service per user, all sharing the database, so a test can switch between inspector and reviewer.
// Seed: firm 1 has INS-001 and INS-002, firm 2 has INS-003.
public sealed class FindingServiceTests : IDisposable
{
    private const string Author = "inspector";      // assigned to INS-001, independence confirmed
    private const string Outsider = "outsider";     // an inspector not assigned to INS-001
    private const string FirmUser1 = "firm-user-1";
    private const string FirmUser2 = "firm-user-2";

    private readonly TestDatabase database;
    private readonly FindingService author;
    private readonly FindingService outsider;
    private readonly FindingService reviewer;

    public FindingServiceTests(ITestOutputHelper output)
    {
        database = new TestDatabase(output.WriteLine);
        author = CreateService(TestUsers.Inspector(Author));
        outsider = CreateService(TestUsers.Inspector(Outsider));
        reviewer = CreateService(TestUsers.Reviewer());
    }

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Inspector_who_is_not_assigned_cannot_create_or_edit_a_finding()
    {
        await ArrangeAuthorAsync();
        await database.AddUserAsync(Outsider);
        var draftId = await author.CreateAsync(1, "Original title", "Original text", []);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => outsider.CreateAsync(1, "Sneaky", "Not my inspection", []));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => outsider.UpdateDraftAsync(draftId, "Changed", "Changed", []));

        // The outsider can view it but not edit it, and nothing changed.
        var asSeenByOutsider = await outsider.GetFindingAsync(draftId);
        Assert.NotNull(asSeenByOutsider);
        Assert.False(asSeenByOutsider.CanEdit);

        var (_, title, _) = await ReadFindingAsync(draftId);
        Assert.Equal("Original title", title);
        Assert.Equal(1, await CountFindingsAsync());
    }

    [Fact]
    public async Task Invalid_state_changes_are_rejected()
    {
        await ArrangeAuthorAsync();

        // Approving a Draft.
        var draftId = await author.CreateAsync(1, "Title", "Text", []);
        var approveDraft = await Assert.ThrowsAsync<InvalidOperationException>(() => reviewer.ApproveAsync(draftId));
        Assert.Contains("Cannot Approve a finding that is Draft", approveDraft.Message);
        Assert.Equal(FindingState.Draft, (await ReadFindingAsync(draftId)).State);

        // Approved to Draft: Approved is final.
        var approvedId = await author.CreateAsync(1, "Title", "Text", []);
        await author.SubmitAsync(approvedId);
        await reviewer.ApproveAsync(approvedId);

        var revise = await Assert.ThrowsAsync<InvalidOperationException>(() => author.ReviseAsync(approvedId));
        Assert.Contains("Cannot Revise a finding that is Approved", revise.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => author.UpdateDraftAsync(approvedId, "Edited", "Edited", []));
        Assert.Equal(FindingState.Approved, (await ReadFindingAsync(approvedId)).State);
    }

    [Fact]
    public async Task Returning_a_finding_without_a_comment_is_rejected()
    {
        await ArrangeAuthorAsync();
        var findingId = await author.CreateAsync(1, "Title", "Text", []);
        await author.SubmitAsync(findingId);

        await Assert.ThrowsAsync<ArgumentException>(() => reviewer.ReturnAsync(findingId, null));
        await Assert.ThrowsAsync<ArgumentException>(() => reviewer.ReturnAsync(findingId, "   "));

        var (state, _, comment) = await ReadFindingAsync(findingId);
        Assert.Equal(FindingState.Submitted, state);
        Assert.Null(comment);
    }

    [Fact]
    public async Task Linking_a_document_from_another_inspection_is_rejected()
    {
        await ArrangeAuthorAsync();
        await database.AddUserAsync(FirmUser1, firmId: 1);
        await database.AddUserAsync(FirmUser2, firmId: 2);
        var ownDocument = await database.AddDocumentAsync(1, FirmUser1, "ins-001.pdf", DocumentStatus.Accepted);
        var otherInspectionsDocument = await database.AddDocumentAsync(3, FirmUser2, "ins-003.pdf", DocumentStatus.Accepted);

        // One bad Id rejects the whole create.
        await Assert.ThrowsAsync<ArgumentException>(
            () => author.CreateAsync(1, "Title", "Text", [ownDocument, otherInspectionsDocument]));
        Assert.Equal(0, await CountFindingsAsync());

        var draftId = await author.CreateAsync(1, "Title", "Text", [ownDocument]);
        await Assert.ThrowsAsync<ArgumentException>(
            () => author.UpdateDraftAsync(draftId, "Title", "Text", [otherInspectionsDocument]));

        var draft = await author.GetFindingAsync(draftId);
        Assert.NotNull(draft);
        Assert.Equal([ownDocument], draft.LinkedDocuments.Select(d => d.Id));
    }

    [Fact]
    public async Task Submit_return_revise_and_submit_again_works()
    {
        await ArrangeAuthorAsync();
        var findingId = await author.CreateAsync(1, "Expired fire certificate", "The certificate expired.", []);

        await author.SubmitAsync(findingId);
        Assert.Equal(FindingState.Submitted, (await ReadFindingAsync(findingId)).State);

        await reviewer.ReturnAsync(findingId, "  Please give the expiry date.  ");
        var returned = await author.GetFindingAsync(findingId);
        Assert.NotNull(returned);
        Assert.Equal(FindingState.Returned, returned.State);
        Assert.Equal("Please give the expiry date.", returned.ReturnComment);
        Assert.True(returned.CanRevise);
        Assert.False(returned.CanEdit);

        // Revise: back to Draft, comment kept.
        await author.ReviseAsync(findingId);
        var revised = await author.GetFindingAsync(findingId);
        Assert.NotNull(revised);
        Assert.Equal(FindingState.Draft, revised.State);
        Assert.Equal("Please give the expiry date.", revised.ReturnComment);
        Assert.True(revised.CanEdit);
        Assert.False(revised.CanRevise);

        await author.UpdateDraftAsync(findingId, "Expired fire certificate", "The certificate expired on 31 March 2026.", []);
        await author.SubmitAsync(findingId);

        var (state, _, comment) = await ReadFindingAsync(findingId);
        Assert.Equal(FindingState.Submitted, state);
        Assert.Equal("Please give the expiry date.", comment);
    }

    // Assigned to INS-001 with independence confirmed.
    private async Task ArrangeAuthorAsync()
    {
        await database.AddUserAsync(Author);
        await database.AssignAsync(inspectionId: 1, Author, independenceConfirmed: true);
    }

    private FindingService CreateService(CurrentUserInfo user)
    {
        ICurrentUser currentUser = TestUsers.As(user);
        return new FindingService(database.Factory, new AccessPolicy(currentUser, database.Factory), currentUser);
    }

    private async Task<(FindingState State, string Title, string? ReturnComment)> ReadFindingAsync(int findingId)
    {
        await using var db = database.CreateContext();
        var finding = await db.Findings.AsNoTracking().SingleAsync(f => f.Id == findingId);
        return (finding.State, finding.Title, finding.ReturnComment);
    }

    private async Task<int> CountFindingsAsync()
    {
        await using var db = database.CreateContext();
        return await db.Findings.CountAsync();
    }
}
