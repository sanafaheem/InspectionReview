using System.Security.Claims;
using InspectionReview.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Auth;

/// <summary>
/// Reads the user Id and roles from the auth cookie's claims, and loads DisplayName and FirmId
/// from the database. Cached for the lifetime of the scope (one request, or one Blazor circuit).
/// </summary>
public sealed class CurrentUser(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<AppDbContext> dbFactory) : ICurrentUser
{
    private CurrentUserInfo? _userInfo;

    public async Task<CurrentUserInfo?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_userInfo is not null)
        {
            return _userInfo;
        }

        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

        // Identity puts the user Id in the NameIdentifier claim and each role in a Role claim.
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.FirmId })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null; // the cookie is for a user that no longer exists
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        _userInfo = new CurrentUserInfo(userId, user.DisplayName, roles, user.FirmId);
        return _userInfo;
    }
}
