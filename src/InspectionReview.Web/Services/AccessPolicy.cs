using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Services;

/// <summary>An inspector's relationship to one inspection. All false for non-inspectors.</summary>
public sealed record InspectionAccess(bool IsInspector, bool IsAssigned, bool IsRecused, bool HasConfirmedIndependence)
{
    public static readonly InspectionAccess None = new(false, false, false, false);

    public bool CanCreateFinding => IsInspector && IsAssigned && HasConfirmedIndependence && !IsRecused;

    public bool NeedsIndependenceConfirmation => IsInspector && IsAssigned && !IsRecused && !HasConfirmedIndependence;

    public bool CanConfirmIndependence => IsInspector && IsAssigned && !IsRecused;

    public bool CanRecuse => IsInspector && !IsRecused;
}

/// <summary>The single place that decides what the signed-in user may see and do.</summary>
public sealed class AccessPolicy(ICurrentUser currentUser, IDbContextFactory<AppDbContext> dbFactory)
{
    // The Visible* methods take the caller's context because they return queries that run later.
    // The yes/no checks below use their own short-lived context.

    /// <summary>Inspectors see all, firm users see their own firm's, everyone else sees none.</summary>
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

        return db.Inspections.Where(_ => false);
    }

    /// <summary>
    /// Unconfirmed uploads are hidden from everyone. Firm users see all their firm's documents,
    /// inspectors only Accepted ones, everyone else none.
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

    public async Task<bool> CanSeeDocumentStatusAsync() =>
        (await currentUser.GetAsync())?.IsInRole(AppRoles.FirmUser) == true;

    public async Task<bool> IsInspectorAsync() =>
        (await currentUser.GetAsync())?.IsInRole(AppRoles.Inspector) == true;

    public async Task<bool> IsReviewerAsync() =>
        (await currentUser.GetAsync())?.IsInRole(AppRoles.Reviewer) == true;

    /// <summary>Inspectors only, for now. Lets pages hide the findings section entirely.</summary>
    public Task<bool> CanSeeFindingsAsync() => IsInspectorAsync();

    /// <summary>Inspectors see every finding on inspections they can view; everyone else none (for now).</summary>
    public async Task<IQueryable<Finding>> VisibleFindingsAsync(AppDbContext db)
    {
        if (!await CanSeeFindingsAsync())
        {
            return db.Findings.Where(_ => false);
        }

        var inspections = await VisibleInspectionsAsync(db);
        return db.Findings.Where(f => inspections.Any(i => i.Id == f.InspectionId));
    }

    public async Task<bool> CanEditFindingAsync(int findingId) =>
        (await GetFindingAccessAsync(findingId)).CanEdit;

    public async Task<bool> CanReviseFindingAsync(int findingId) =>
        (await GetFindingAccessAsync(findingId)).CanRevise;

    public async Task<FindingAccess> GetFindingAccessAsync(int findingId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var finding = await db.Findings
            .Where(f => f.Id == findingId)
            .Select(f => new { f.InspectionId, f.State })
            .SingleOrDefaultAsync();

        if (finding is null)
        {
            return FindingAccess.None;
        }

        var inspectionAccess = await GetInspectionAccessAsync(finding.InspectionId);
        return new FindingAccess(inspectionAccess, finding.State);
    }
}

/// <summary>Edit and revise reuse CanCreateFinding; the finding's state decides which applies.</summary>
public sealed record FindingAccess(InspectionAccess Inspection, FindingState? State)
{
    public static readonly FindingAccess None = new(InspectionAccess.None, null);

    public bool CanEdit => Inspection.CanCreateFinding && State == FindingState.Draft;

    public bool CanRevise => Inspection.CanCreateFinding && State == FindingState.Returned;
}
