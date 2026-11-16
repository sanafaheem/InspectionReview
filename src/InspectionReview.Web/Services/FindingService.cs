using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Services;

public sealed record FindingDocumentOption(int Id, string FileName, string DocumentType);

public sealed record FindingListItem(int Id, string Title, FindingState State, DateTime UpdatedAt);

public sealed record NewFindingForm(
    int InspectionId,
    string InspectionReference,
    bool CanCreate,
    IReadOnlyList<FindingDocumentOption> AvailableDocuments);

/// <summary>The page uses CanEdit and CanRevise, never the user's role. AvailableDocuments is empty unless CanEdit.</summary>
public sealed record FindingDetail(
    int Id,
    int InspectionId,
    string InspectionReference,
    string Title,
    string Text,
    FindingState State,
    string? ReturnComment,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<FindingDocumentOption> LinkedDocuments,
    IReadOnlyList<FindingDocumentOption> AvailableDocuments,
    bool CanEdit,
    bool CanRevise,
    bool CanApprove,
    bool CanReturn);

public sealed record ReviewQueueItem(
    int Id,
    string Title,
    string InspectionReference,
    string FirmName,
    string SubmittedBy,
    DateTime SubmittedAt);

/// <summary>
/// Each action checks access (UnauthorizedAccessException), then input (ArgumentException),
/// then the state change (InvalidOperationException), and only then saves.
/// </summary>
public sealed class FindingService(
    IDbContextFactory<AppDbContext> dbFactory,
    AccessPolicy accessPolicy,
    ICurrentUser currentUser)
{
    public const int TitleMaxLength = 200;

    /// <summary>Null if the user may not see findings at all.</summary>
    public async Task<IReadOnlyList<FindingListItem>?> GetFindingsForInspectionAsync(
        int inspectionId, CancellationToken cancellationToken = default)
    {
        if (!await accessPolicy.CanSeeFindingsAsync())
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (await accessPolicy.VisibleFindingsAsync(db))
            .AsNoTracking()
            .Where(f => f.InspectionId == inspectionId)
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FindingListItem(f.Id, f.Title, f.State, f.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reviewers only: every Submitted finding, longest waiting first.</summary>
    public async Task<IReadOnlyList<ReviewQueueItem>> GetReviewQueueAsync(CancellationToken cancellationToken = default)
    {
        if (!await accessPolicy.CanReviewAsync())
        {
            throw new UnauthorizedAccessException("Only reviewers can see the review queue.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings
            .AsNoTracking()
            .Where(f => f.State == FindingState.Submitted)
            .OrderBy(f => f.SubmittedAt)
            .Select(f => new ReviewQueueItem(
                f.Id,
                f.Title,
                f.Inspection!.Reference,
                f.Inspection.Firm!.Name,
                f.SubmittedBy!.DisplayName,
                f.SubmittedAt!.Value))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Null if the finding doesn't exist or isn't visible.</summary>
    public async Task<FindingDetail?> GetFindingAsync(int findingId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var finding = await (await accessPolicy.VisibleFindingsAsync(db))
            .AsNoTracking()
            .Where(f => f.Id == findingId)
            .Select(f => new
            {
                f.Id,
                f.InspectionId,
                InspectionReference = f.Inspection!.Reference,
                f.Title,
                f.Text,
                f.State,
                f.ReturnComment,
                CreatedBy = f.CreatedBy!.DisplayName,
                f.CreatedAt,
                f.UpdatedAt,
                LinkedDocuments = f.Documents
                    .OrderBy(fd => fd.Document!.FileName)
                    .Select(fd => new FindingDocumentOption(fd.DocumentId, fd.Document!.FileName, fd.Document.DocumentType!.Name))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (finding is null)
        {
            return null;
        }

        var access = await accessPolicy.GetFindingAccessAsync(findingId);

        var available = access.CanEdit
            ? await DocumentOptionsAsync(db, finding.InspectionId, cancellationToken)
            : [];

        return new FindingDetail(
            finding.Id,
            finding.InspectionId,
            finding.InspectionReference,
            finding.Title,
            finding.Text,
            finding.State,
            finding.ReturnComment,
            finding.CreatedBy,
            finding.CreatedAt,
            finding.UpdatedAt,
            finding.LinkedDocuments,
            available,
            access.CanEdit,
            access.CanRevise,
            access.CanApprove,
            access.CanReturn);
    }

    /// <summary>Null if the inspection isn't visible.</summary>
    public async Task<NewFindingForm?> GetNewFindingFormAsync(int inspectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var reference = await (await accessPolicy.VisibleInspectionsAsync(db))
            .Where(i => i.Id == inspectionId)
            .Select(i => i.Reference)
            .SingleOrDefaultAsync(cancellationToken);

        if (reference is null)
        {
            return null;
        }

        var canCreate = await accessPolicy.CanCreateFindingAsync(inspectionId);
        var documents = canCreate ? await DocumentOptionsAsync(db, inspectionId, cancellationToken) : [];

        return new NewFindingForm(inspectionId, reference, canCreate, documents);
    }

    /// <summary>Creates a Draft and returns its Id.</summary>
    public async Task<int> CreateAsync(
        int inspectionId, string? title, string? text, IEnumerable<int>? documentIds,
        CancellationToken cancellationToken = default)
    {
        if (!await accessPolicy.CanCreateFindingAsync(inspectionId))
        {
            throw new UnauthorizedAccessException(
                "You can only create findings on inspections you're assigned to, have confirmed independence for, and aren't recused from.");
        }

        var cleanTitle = ValidateTitle(title);
        var user = (await currentUser.GetAsync(cancellationToken))!;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var linkIds = await EnsureLinkableDocumentsAsync(db, inspectionId, documentIds, cancellationToken);

        var now = DateTime.UtcNow;
        var finding = new Finding
        {
            InspectionId = inspectionId,
            Title = cleanTitle,
            Text = text ?? string.Empty,
            State = FindingState.Draft,
            CreatedById = user.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            Documents = linkIds.Select(id => new FindingDocument { DocumentId = id }).ToList(),
        };

        db.Findings.Add(finding);
        await db.SaveChangesAsync(cancellationToken);
        return finding.Id;
    }

    public async Task UpdateDraftAsync(
        int findingId, string? title, string? text, IEnumerable<int>? documentIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanWorkOnAsync(findingId);
        var cleanTitle = ValidateTitle(title);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var finding = await LoadAsync(db, findingId, includeDocuments: true, cancellationToken);

        if (finding.State != FindingState.Draft)
        {
            throw new InvalidOperationException($"Only Draft findings can be edited. This finding is {finding.State}.");
        }

        var linkIds = await EnsureLinkableDocumentsAsync(db, finding.InspectionId, documentIds, cancellationToken);

        finding.Title = cleanTitle;
        finding.Text = text ?? string.Empty;

        // Update links in place: clearing and re-adding would track two links with the same key.
        finding.Documents.Where(fd => !linkIds.Contains(fd.DocumentId)).ToList()
            .ForEach(fd => finding.Documents.Remove(fd));
        foreach (var id in linkIds.Except(finding.Documents.Select(fd => fd.DocumentId)))
        {
            finding.Documents.Add(new FindingDocument { DocumentId = id });
        }

        finding.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAsync(int findingId, CancellationToken cancellationToken = default)
    {
        await EnsureCanWorkOnAsync(findingId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var finding = await LoadAsync(db, findingId, includeDocuments: false, cancellationToken);

        if (string.IsNullOrWhiteSpace(finding.Title) || string.IsNullOrWhiteSpace(finding.Text))
        {
            throw new ArgumentException("A finding needs a title and text before it can be submitted.");
        }

        finding.SubmittedAt = DateTime.UtcNow;
        finding.SubmittedById = (await currentUser.GetAsync(cancellationToken))!.UserId;
        await TransitionAsync(db, finding, FindingAction.Submit, cancellationToken);
    }

    /// <summary>Returned → Draft. The reviewer's comment is kept.</summary>
    public async Task ReviseAsync(int findingId, CancellationToken cancellationToken = default)
    {
        await EnsureCanWorkOnAsync(findingId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var finding = await LoadAsync(db, findingId, includeDocuments: false, cancellationToken);

        await TransitionAsync(db, finding, FindingAction.Revise, cancellationToken);
    }

    public async Task ApproveAsync(int findingId, CancellationToken cancellationToken = default)
    {
        await EnsureReviewerAsync();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var finding = await LoadAsync(db, findingId, includeDocuments: false, cancellationToken);

        await TransitionAsync(db, finding, FindingAction.Approve, cancellationToken);
    }

    public async Task ReturnAsync(int findingId, string? comment, CancellationToken cancellationToken = default)
    {
        await EnsureReviewerAsync();
        var cleanComment = FindingWorkflow.EnsureValidReturnComment(comment);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var finding = await LoadAsync(db, findingId, includeDocuments: false, cancellationToken);

        finding.ReturnComment = cleanComment;
        await TransitionAsync(db, finding, FindingAction.Return, cancellationToken);
    }

    /// <summary>
    /// The "who" half of CanEdit / CanRevise. The state half is checked afterwards, so a wrong
    /// state gives a clear InvalidOperationException rather than an access error.
    /// </summary>
    private async Task EnsureCanWorkOnAsync(int findingId)
    {
        var access = await accessPolicy.GetFindingAccessAsync(findingId);
        if (access.State is null)
        {
            throw new ArgumentException($"Finding {findingId} does not exist.", nameof(findingId));
        }

        if (!access.Inspection.CanCreateFinding)
        {
            throw new UnauthorizedAccessException(
                "You can only work on findings for inspections you're assigned to, have confirmed independence for, and aren't recused from.");
        }
    }

    private async Task EnsureReviewerAsync()
    {
        if (!await accessPolicy.IsReviewerAsync())
        {
            throw new UnauthorizedAccessException("Only reviewers can approve or return findings.");
        }
    }

    private static async Task TransitionAsync(
        AppDbContext db, Finding finding, FindingAction action, CancellationToken cancellationToken)
    {
        finding.State = FindingWorkflow.EnsureCanTransition(finding.State, action);
        finding.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Finding> LoadAsync(
        AppDbContext db, int findingId, bool includeDocuments, CancellationToken cancellationToken)
    {
        IQueryable<Finding> query = includeDocuments ? db.Findings.Include(f => f.Documents) : db.Findings;
        return await query.SingleOrDefaultAsync(f => f.Id == findingId, cancellationToken)
            ?? throw new ArgumentException($"Finding {findingId} does not exist.", nameof(findingId));
    }

    private static string ValidateTitle(string? title)
    {
        var clean = (title ?? string.Empty).Trim();
        if (clean.Length > TitleMaxLength)
        {
            throw new ArgumentException($"The title can be at most {TitleMaxLength} characters.", nameof(title));
        }
        return clean;
    }

    // Accepted, fully uploaded documents of the finding's own inspection.
    private static IQueryable<Document> LinkableDocuments(AppDbContext db, int inspectionId) =>
        db.Documents.Where(d =>
            d.InspectionId == inspectionId && d.UploadConfirmed && d.Status == DocumentStatus.Accepted);

    private static async Task<List<FindingDocumentOption>> DocumentOptionsAsync(
        AppDbContext db, int inspectionId, CancellationToken cancellationToken) =>
        await LinkableDocuments(db, inspectionId)
            .AsNoTracking()
            .OrderBy(d => d.FileName)
            .Select(d => new FindingDocumentOption(d.Id, d.FileName, d.DocumentType!.Name))
            .ToListAsync(cancellationToken);

    // The error doesn't say which Id failed, so it can't reveal documents from other inspections.
    private static async Task<List<int>> EnsureLinkableDocumentsAsync(
        AppDbContext db, int inspectionId, IEnumerable<int>? documentIds, CancellationToken cancellationToken)
    {
        var ids = (documentIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return ids;
        }

        var valid = await LinkableDocuments(db, inspectionId).CountAsync(d => ids.Contains(d.Id), cancellationToken);
        if (valid != ids.Count)
        {
            throw new ArgumentException(
                "Findings can only link accepted documents of the same inspection.", nameof(documentIds));
        }

        return ids;
    }
}
