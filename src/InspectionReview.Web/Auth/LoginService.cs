using InspectionReview.Web.Data;
using InspectionReview.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Auth;

public sealed record LoginOption(string UserId, string DisplayName, string Email, string Role);

/// <summary>
/// Role switcher: sign in as any seeded user without a password (Development only), and sign out.
/// Sign-in and sign-out write the auth cookie, so they must run during an HTTP request:
/// the static SSR Login page and the /auth/logout endpoint.
/// </summary>
public sealed class LoginService(
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IWebHostEnvironment environment)
{
    public async Task<IReadOnlyList<LoginOption>> GetLoginOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (
                from user in db.Users
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                orderby role.Name, user.DisplayName
                select new LoginOption(user.Id, user.DisplayName, user.Email!, role.Name!))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Signs in as the given user. Returns false if the user doesn't exist.</summary>
    public async Task<bool> SignInAsync(string userId)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Password-less role switching is only allowed in Development.");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        // Writes the auth cookie (user Id + role claims) onto the HTTP response.
        await signInManager.SignInAsync(user, isPersistent: false);
        return true;
    }

    /// <summary>Removes the auth cookie.</summary>
    public Task LogoutAsync() => signInManager.SignOutAsync();

    /// <summary>Only redirect back into this app, never to another site (open-redirect protection).</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
        && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
