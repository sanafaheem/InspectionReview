using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Services;

/// <summary>An inspector's relationship to an inspection, as shown in the list.</summary>
public enum AssignmentStatus
{
    Assigned,
    NotAssigned,
    Recused,
}

/// <summary>
/// One row of the inspection list. IsAssigned and IsRecused are only set for inspectors
/// (null for everyone else), so Status is null for non-inspectors too.
/// </summary>
public sealed record InspectionListItem(
    int Id,
    string Reference,
    string Title,
    string FirmName,
    bool? IsAssigned,
    bool? IsRecused)
{
    /// <summary>The tag to show. Recused takes priority over Assigned.</summary>
    public AssignmentStatus? Status =>
        IsRecused == true ? AssignmentStatus.Recused
        : IsAssigned == true ? AssignmentStatus.Assigned
        : IsAssigned == false ? AssignmentStatus.NotAssigned
        : null;
}

/// <summary>
/// One document on the detail page. Status and RejectionReason are only filled in for users
/// allowed to see them (firm users); for everyone else they're null and never leave the server.
/// </summary>
public sealed record DocumentItem(
    int Id,
    string FileName,
    string DocumentType,
    string UploadedBy,
    DateTime UploadedAt,
    DocumentStatus? Status,
    string? RejectionReason);

/// <summary>
/// Everything the /inspections/{id} page shows. The page decides what to display from these
/// flags only, never from the user's role.
/// </summary>
public sealed record InspectionDetail(
    int Id,
    string Reference,
    string Title,
    int FirmId,
    string FirmName,
    IReadOnlyList<DocumentItem> Documents,
    bool ShowDocumentStatus,
    bool CanCreateFinding,
    bool NeedsIndependenceConfirmation,
    bool IsRecused,
    bool CanRecuse);

/// <summary>
/// Inspection queries and inspector actions. Every query starts from AccessPolicy, so a user
/// can never load an inspection or document they aren't allowed to see. Every action checks
/// access on the server and throws UnauthorizedAccessException if it isn't allowed.
/// </summary>
public sealed class InspectionService(
    IDbContextFactory<AppDbContext> dbFactory,
    AccessPolicy accessPolicy,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<InspectionListItem>> GetInspectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var visible = (await accessPolicy.VisibleInspectionsAsync(db))
            .AsNoTracking()
            .OrderBy(i => i.Reference);

        var user = await currentUser.GetAsync(cancellationToken);

        if (user is not null && user.IsInRole(AppRoles.Inspector))
        {
            var inspectorId = user.UserId;

            // Assigned and Recused are worked out inside the same SQL query (EXISTS subqueries),
            // not with a second query per row.
            return await visible
                .Select(i => new InspectionListItem(
                    i.Id,
                    i.Reference,
                    i.Title,
                    i.Firm!.Name,
                    i.Assignments.Any(a => a.InspectorId == inspectorId),
                    db.Recusals.Any(r => r.InspectorId == inspectorId && r.FirmId == i.FirmId)))
                .ToListAsync(cancellationToken);
        }

        return await visible
            .Select(i => new InspectionListItem(i.Id, i.Reference, i.Title, i.Firm!.Name, null, null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the inspection with its visible documents and the page's flags, or null if it
    /// doesn't exist or the user may not see it.
    /// </summary>
    public async Task<InspectionDetail?> GetInspectionDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var inspection = await (await accessPolicy.VisibleInspectionsAsync(db))
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new { i.Id, i.Reference, i.Title, i.FirmId, FirmName = i.Firm!.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (inspection is null)
        {
            return null;
        }

        var showStatus = await accessPolicy.CanSeeDocumentStatusAsync();

        var documents = await (await accessPolicy.VisibleDocumentsAsync(db))
            .AsNoTracking()
            .Where(d => d.InspectionId == id)
            .OrderBy(d => d.UploadedAt)
            .Select(d => new DocumentItem(
                d.Id,
                d.FileName,
                d.DocumentType!.Name,
                d.UploadedBy!.DisplayName,
                d.UploadedAt,
                showStatus ? d.Status : null,
                showStatus ? d.RejectionReason : null))
            .ToListAsync(cancellationToken);

        var access = await accessPolicy.GetInspectionAccessAsync(id);

        return new InspectionDetail(
            inspection.Id,
            inspection.Reference,
            inspection.Title,
            inspection.FirmId,
            inspection.FirmName,
            documents,
            showStatus,
            access.CanCreateFinding,
            access.NeedsIndependenceConfirmation,
            access.IsRecused,
            access.CanRecuse);
    }

    /// <summary>
    /// Records that the current inspector has confirmed their independence for this inspection.
    /// Allowed only if they're assigned and not recused. Confirming again keeps the first time.
    /// </summary>
    public async Task ConfirmIndependenceAsync(int inspectionId, CancellationToken cancellationToken = default)
    {
        var access = await accessPolicy.GetInspectionAccessAsync(inspectionId);
        if (!access.CanConfirmIndependence)
        {
            throw new UnauthorizedAccessException(
                "You can only confirm independence for an inspection you're assigned to and not recused from.");
        }

        var user = (await currentUser.GetAsync(cancellationToken))!;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var assignment = await db.InspectionAssignments
            .SingleAsync(a => a.InspectionId == inspectionId && a.InspectorId == user.UserId, cancellationToken);

        if (assignment.IndependenceConfirmedAt is null)
        {
            assignment.IndependenceConfirmedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Recuses the current inspector from a firm (all its inspections, now and in future).
    /// Inspectors only. Does nothing if they're already recused.
    /// </summary>
    public async Task RecuseAsync(int firmId, CancellationToken cancellationToken = default)
    {
        if (!await accessPolicy.IsInspectorAsync())
        {
            throw new UnauthorizedAccessException("Only inspectors can recuse themselves from a firm.");
        }

        var user = (await currentUser.GetAsync(cancellationToken))!;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.Firms.AnyAsync(f => f.Id == firmId, cancellationToken))
        {
            throw new ArgumentException($"Firm {firmId} does not exist.", nameof(firmId));
        }

        var alreadyRecused = await db.Recusals
            .AnyAsync(r => r.InspectorId == user.UserId && r.FirmId == firmId, cancellationToken);
        if (alreadyRecused)
        {
            return;
        }

        db.Recusals.Add(new Recusal { InspectorId = user.UserId, FirmId = firmId, RecusedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }
}
