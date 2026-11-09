namespace InspectionReview.Web.Auth;

/// <summary>
/// The signed-in user, as seen by services. The actual sign-in mechanism
/// (e.g. Entra ID, cookies) lives behind an implementation of this interface.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? DisplayName { get; }
}
