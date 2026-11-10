namespace InspectionReview.Web.Domain;

/// <summary>
/// Role names. Use these constants instead of string literals, e.g. [Authorize(Roles = AppRoles.Reviewer)].
/// </summary>
public static class AppRoles
{
    public const string FirmUser = "FirmUser";
    public const string Inspector = "Inspector";
    public const string Reviewer = "Reviewer";
}
