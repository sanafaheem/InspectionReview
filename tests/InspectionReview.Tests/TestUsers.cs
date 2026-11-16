using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using NSubstitute;

namespace InspectionReview.Tests;

/// <summary>
/// Builds a substitute ICurrentUser. Always go through this helper: an unconfigured substitute
/// returns null ("signed out"), which could make a test pass for the wrong reason.
/// </summary>
public static class TestUsers
{
    public static ICurrentUser As(CurrentUserInfo? user)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.GetAsync(Arg.Any<CancellationToken>()).Returns(user);
        return currentUser;
    }

    public static CurrentUserInfo FirmUser(int firmId) =>
        new("firm-user", "Test Firm User", [AppRoles.FirmUser], firmId);

    public static CurrentUserInfo Inspector(string userId) =>
        new(userId, "Test Inspector", [AppRoles.Inspector], FirmId: null);
}
