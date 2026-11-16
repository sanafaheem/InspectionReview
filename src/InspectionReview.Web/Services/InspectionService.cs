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

public sealed record InspectionDetail(int Id, string Reference, string Title, string FirmName);

/// <summary>
/// Read-only inspection queries. Every query starts from AccessPolicy.VisibleInspectionsAsync,
/// so a user can never load an inspection they aren't allowed to see.
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

    /// <summary>Returns the inspection, or null if it doesn't exist or the user may not see it.</summary>
    public async Task<InspectionDetail?> GetInspectionAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var visible = await accessPolicy.VisibleInspectionsAsync(db);

        return await visible
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new InspectionDetail(i.Id, i.Reference, i.Title, i.Firm!.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
