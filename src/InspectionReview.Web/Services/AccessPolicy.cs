using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Services;

/// <summary>
/// The current inspector's relationship to one inspection, and the rules derived from it.
/// Everything is false for users who aren't inspectors.
/// </summary>
public sealed record InspectionAccess(bool IsInspector, bool IsAssigned, bool IsRecused, bool HasConfirmedIndependence)
{
    public static readonly InspectionAccess None = new(false, false, false, false);

    /// <summary>Assigned, independence confirmed, and not recused from the inspection's firm.</summary>
    public bool CanCreateFinding => IsInspector && IsAssigned && HasConfirmedIndependence && !IsRecused;

    /// <summary>Assigned and not recused, but hasn't confirmed independence yet.</summary>
    public bool NeedsIndependenceConfirmation => IsInspector && IsAssigned && !IsRecused && !HasConfirmedIndependence;

    /// <summary>Assigned and not recused (confirming again is allowed and keeps the first time).</summary>
    public bool CanConfirmIndependence => IsInspector && IsAssigned && !IsRecused;

    /// <summary>Any inspector who isn't already recused from the inspection's firm.</summary>
    public bool CanRecuse => IsInspector && !IsRecused;
}

/// <summary>
/// The single place that decides what the signed-in user may see and do.
/// Every inspection or document query should start from VisibleInspectionsAsync or VisibleDocumentsAsync,
/// and every permission check should come from here.
/// </summary>
public sealed class AccessPolicy(ICurrentUser currentUser, IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Returns a query (not yet run) over the inspections the current user may see:
    /// inspectors see all, firm users see their own firm's, everyone else sees none.
    /// Takes the caller's context because the query runs later, on that context.
    /// </summary>
    public async Task<IQueryable<Inspection>> VisibleInspectionsAsync(AppDbContext db)
    {
        var user = await currentUser.GetAsync();

        if (user is null)
        {
            return db.Inspections.Where(_ => false);
        }

        if (user.IsInRole(AppRoles.Inspector))
        {
            return db.Inspections;
        }

        if (user.IsInRole(AppRoles.FirmUser) && user.FirmId is int firmId)
        {
            return db.Inspections.Where(i => i.FirmId == firmId);
        }

        // Reviewers, firm users without a firm, and any other role.
        return db.Inspections.Where(_ => false);
    }

    /// <summary>
    /// Returns a query (not yet run) over the documents the current user may see. Incomplete uploads
    /// (UploadConfirmed = false) are hidden from everyone. Firm users see all of their own firm's
    /// documents, whatever their status; inspectors see only Accepted ones; everyone else sees none.
    /// </summary>
    public async Task<IQueryable<Document>> VisibleDocumentsAsync(AppDbContext db)
    {
        var user = await currentUser.GetAsync();
        var confirmed = db.Documents.Where(d => d.UploadConfirmed);

        if (user is null)
        {
            return confirmed.Where(_ => false);
        }

        if (user.IsInRole(AppRoles.Inspector))
        {
            return confirmed.Where(d => d.Status == DocumentStatus.Accepted);
        }

        if (user.IsInRole(AppRoles.FirmUser) && user.FirmId is int firmId)
        {
            return confirmed.Where(d => d.Inspection!.FirmId == firmId);
        }

        return confirmed.Where(_ => false);
    }

    public async Task<bool> CanCreateFindingAsync(int inspectionId) =>
        (await GetInspectionAccessAsync(inspectionId)).CanCreateFinding;

    public async Task<bool> NeedsIndependenceConfirmationAsync(int inspectionId) =>
        (await GetInspectionAccessAsync(inspectionId)).NeedsIndependenceConfirmation;

    /// <summary>
    /// Looks up the current inspector's assignment, confirmation and recusal for one inspection
    /// in a single query. Returns <see cref="InspectionAccess.None"/> for non-inspectors or an
    /// unknown inspection. Uses its own short-lived context because it returns a finished result.
    /// </summary>
    public async Task<InspectionAccess> GetInspectionAccessAsync(int inspectionId)
    {
        var user = await currentUser.GetAsync();
        if (user is null || !user.IsInRole(AppRoles.Inspector))
        {
            return InspectionAccess.None;
        }

        var inspectorId = user.UserId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var state = await db.Inspections
            .Where(i => i.Id == inspectionId)
            .Select(i => new
            {
                IsAssigned = i.Assignments.Any(a => a.InspectorId == inspectorId),
                HasConfirmed = i.Assignments.Any(a => a.InspectorId == inspectorId && a.IndependenceConfirmedAt != null),
                IsRecused = db.Recusals.Any(r => r.InspectorId == inspectorId && r.FirmId == i.FirmId),
            })
            .SingleOrDefaultAsync();

        return state is null
            ? InspectionAccess.None
            : new InspectionAccess(IsInspector: true, state.IsAssigned, state.IsRecused, state.HasConfirmed);
    }

    /// <summary>Firm users see each document's check status and rejection reason; nobody else does.</summary>
    public async Task<bool> CanSeeDocumentStatusAsync() =>
        (await currentUser.GetAsync())?.IsInRole(AppRoles.FirmUser) == true;

    /// <summary>Whether the current user is an inspector (the only role that may recuse).</summary>
    public async Task<bool> IsInspectorAsync() =>
        (await currentUser.GetAsync())?.IsInRole(AppRoles.Inspector) == true;
}
