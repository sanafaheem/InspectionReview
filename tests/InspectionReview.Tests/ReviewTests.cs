using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace InspectionReview.Tests;

// Two inspectors share INS-001 (shared drafts), plus one reviewer.
public sealed class ReviewTests : IDisposable
{
    private const string Author = "author";
    private const string CoAuthor = "co-author";

    private readonly TestDatabase database;
    private readonly FindingService author;
    private readonly FindingService coAuthor;
    private readonly FindingService reviewer;

    public ReviewTests(ITestOutputHelper output)
    {
        database = new TestDatabase(output.WriteLine);
        author = CreateService(TestUsers.Inspector(Author));
        coAuthor = CreateService(TestUsers.Inspector(CoAuthor));
        reviewer = CreateService(TestUsers.Reviewer());
    }

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Reviewer_cannot_see_a_draft()
    {
        await ArrangeInspectorsAsync();
        var draftId = await author.CreateAsync(1, "Draft", "Unfinished", []);
        var submittedId = await SubmitNewAsync(author, "Submitted");

        Assert.Null(await reviewer.GetFindingAsync(draftId));
        Assert.NotNull(await reviewer.GetFindingAsync(submittedId));
    }

    [Fact]
    public async Task Inspector_cannot_approve_or_return_a_finding()
    {
        await ArrangeInspectorsAsync();
        var findingId = await SubmitNewAsync(author, "Finding");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => author.ApproveAsync(findingId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => author.ReturnAsync(findingId, "Looks fine to me"));

        Assert.Equal(FindingState.Submitted, await ReadStateAsync(findingId));
    }

    [Fact]
    public async Task Approving_a_submitted_finding_makes_it_read_only_for_everyone()
    {
        await ArrangeInspectorsAsync();
        var findingId = await SubmitNewAsync(author, "Finding");

        var beforeApproval = await reviewer.GetFindingAsync(findingId);
        Assert.NotNull(beforeApproval);
        Assert.True(beforeApproval.CanApprove);
        Assert.True(beforeApproval.CanReturn);

        await reviewer.ApproveAsync(findingId);
        Assert.Equal(FindingState.Approved, await ReadStateAsync(findingId));

        var asInspector = await author.GetFindingAsync(findingId);
        Assert.NotNull(asInspector);
        Assert.False(asInspector.CanEdit);
        Assert.False(asInspector.CanRevise);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => author.UpdateDraftAsync(findingId, "Changed", "Changed", []));

        var asReviewer = await reviewer.GetFindingAsync(findingId);
        Assert.NotNull(asReviewer);
        Assert.False(asReviewer.CanApprove);
        Assert.False(asReviewer.CanReturn);
    }

    [Fact]
    public async Task Approving_a_finding_twice_fails_with_a_clear_error()
    {
        await ArrangeInspectorsAsync();
        var findingId = await SubmitNewAsync(author, "Finding");
        await reviewer.ApproveAsync(findingId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reviewer.ApproveAsync(findingId));

        Assert.Contains("Cannot Approve a finding that is Approved", error.Message);
    }

    [Fact]
    public async Task Review_queue_contains_only_submitted_findings_oldest_first()
    {
        await ArrangeInspectorsAsync();
        await author.CreateAsync(1, "Still a draft", "Text", []);
        var first = await SubmitNewAsync(author, "Submitted first");

        // Created by the author but submitted by the co-author: the queue shows the submitter.
        var second = await author.CreateAsync(1, "Submitted second", "Text", []);
        await coAuthor.SubmitAsync(second);

        var returned = await SubmitNewAsync(author, "Returned");
        await reviewer.ReturnAsync(returned, "Needs more detail");
        var approved = await SubmitNewAsync(author, "Approved");
        await reviewer.ApproveAsync(approved);

        var queue = await reviewer.GetReviewQueueAsync();

        Assert.Equal(new[] { first, second }, queue.Select(q => q.Id));
        Assert.Equal(Author, queue[0].SubmittedBy);
        Assert.Equal(CoAuthor, queue[1].SubmittedBy);
        Assert.All(queue, q => Assert.Equal("INS-001", q.InspectionReference));
        Assert.All(queue, q => Assert.Equal("Harbourview Property Group", q.FirmName));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => author.GetReviewQueueAsync());
    }

    // Both assigned to INS-001 with independence confirmed. DisplayName = user Id.
    private async Task ArrangeInspectorsAsync()
    {
        foreach (var inspector in new[] { Author, CoAuthor })
        {
            await database.AddUserAsync(inspector);
            await database.AssignAsync(inspectionId: 1, inspector, independenceConfirmed: true);
        }
    }

    private static async Task<int> SubmitNewAsync(FindingService service, string title)
    {
        var id = await service.CreateAsync(1, title, "Text", []);
        await service.SubmitAsync(id);
        return id;
    }

    private FindingService CreateService(CurrentUserInfo user)
    {
        ICurrentUser currentUser = TestUsers.As(user);
        var factory = database.FactoryFor(currentUser);
        return new FindingService(factory, new AccessPolicy(currentUser, factory), currentUser);
    }

    private async Task<FindingState> ReadStateAsync(int findingId)
    {
        await using var db = database.CreateContext();
        return await db.Findings.Where(f => f.Id == findingId).Select(f => f.State).SingleAsync();
    }
}
