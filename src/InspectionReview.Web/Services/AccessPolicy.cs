using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;

namespace InspectionReview.Web.Services;

/// <summary>
/// The single place that decides which inspections the signed-in user may see.
/// Every inspection query in the app should start from <see cref="VisibleInspectionsAsync"/>.
/// </summary>
public sealed class AccessPolicy(ICurrentUser currentUser)
{
    /// <summary>
    /// Returns a query (not yet run) over the inspections the current user may see:
    /// inspectors see all, firm users see their own firm's, everyone else sees none.
    /// Callers add filters and projections, and EF runs it all as one SQL statement.
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
}
