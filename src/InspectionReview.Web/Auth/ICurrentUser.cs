namespace InspectionReview.Web.Auth;

/// <summary>
/// The signed-in user, as seen by services. The actual sign-in mechanism
/// (e.g. Entra ID, cookies) lives behind an implementation of this interface.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Returns null when nobody is signed in.</summary>
    Task<CurrentUserInfo?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Snapshot of the signed-in user. FirmId is set only for firm users.</summary>
public sealed record CurrentUserInfo(string UserId, string DisplayName, IReadOnlyList<string> Roles, int? FirmId)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}
