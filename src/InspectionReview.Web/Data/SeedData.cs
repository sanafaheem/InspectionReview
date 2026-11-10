using InspectionReview.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Data;

/// <summary>
/// Development seed data that needs UserManager (users, role membership) or depends on
/// user Ids (inspection assignments). Roles, firms and inspections are seeded with HasData
/// in <see cref="AppDbContext"/>. Safe to run on every startup: nothing is added twice.
/// </summary>
public static class SeedData
{
    public const string FirmUser1Id = "a1c4e8f2-3b6d-4f90-8e21-7d5c9b0a1f01";
    public const string FirmUser2Id = "a1c4e8f2-3b6d-4f90-8e21-7d5c9b0a1f02";
    public const string InspectorTwoInspectionsId = "a1c4e8f2-3b6d-4f90-8e21-7d5c9b0a1f03";
    public const string InspectorOneInspectionId = "a1c4e8f2-3b6d-4f90-8e21-7d5c9b0a1f04";
    public const string ReviewerId = "a1c4e8f2-3b6d-4f90-8e21-7d5c9b0a1f05";

    /// <summary>Configuration key for the password given to every seeded user.</summary>
    public const string PasswordConfigKey = "SeedUsers:Password";

    private sealed record SeedUser(string Id, string Email, string DisplayName, string Role, int? FirmId);

    private static readonly SeedUser[] Users =
    [
        new(FirmUser1Id, "olivia.grant@harbourview.example", "Olivia Grant", AppRoles.FirmUser, FirmId: 1),
        new(FirmUser2Id, "marcus.bell@northgate.example", "Marcus Bell", AppRoles.FirmUser, FirmId: 2),
        new(InspectorTwoInspectionsId, "priya.nair@inspections.example", "Priya Nair", AppRoles.Inspector, FirmId: null),
        new(InspectorOneInspectionId, "tom.reyes@inspections.example", "Tom Reyes", AppRoles.Inspector, FirmId: null),
        new(ReviewerId, "hannah.cole@inspections.example", "Hannah Cole", AppRoles.Reviewer, FirmId: null),
    ];

    // (InspectionId, InspectorId). Inspection Ids match the HasData seed in AppDbContext.
    private static readonly (int InspectionId, string InspectorId)[] Assignments =
    [
        (1, InspectorTwoInspectionsId),
        (3, InspectorTwoInspectionsId),
        (2, InspectorOneInspectionId),
    ];

    // (InspectorId, FirmId). Priya is recused from Northgate (firm 2), so she can edit INS-001
    // but only view INS-003, even though she is assigned to both.
    private static readonly (string InspectorId, int FirmId)[] Recusals =
    [
        (InspectorTwoInspectionsId, 2),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var password = configuration[PasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"'{PasswordConfigKey}' is not set. Set it with: " +
                $"dotnet user-secrets set \"{PasswordConfigKey}\" \"<value>\" --project src/InspectionReview.Web");
        }

        foreach (var seed in Users)
        {
            await EnsureUserAsync(userManager, seed, password);
        }

        await EnsureAssignmentsAsync(db, cancellationToken);
        await EnsureRecusalsAsync(db, cancellationToken);
    }

    private static async Task EnsureUserAsync(UserManager<AppUser> userManager, SeedUser seed, string password)
    {
        var user = await userManager.FindByIdAsync(seed.Id);

        if (user is null)
        {
            // Guard against duplicates: a different account may already use this email or
            // user name (for example one registered by hand). Never create a second one.
            var existing = await userManager.FindByEmailAsync(seed.Email)
                ?? await userManager.FindByNameAsync(seed.Email);
            if (existing is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot seed user '{seed.Email}' with Id {seed.Id}: a different user " +
                    $"(Id {existing.Id}) already has that email or user name.");
            }

            user = new AppUser
            {
                Id = seed.Id,
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                DisplayName = seed.DisplayName,
                FirmId = seed.FirmId,
            };
            ThrowIfFailed(await userManager.CreateAsync(user, password), $"create user '{seed.Email}'");
        }

        // Checked separately so a run that failed between the two steps is completed next time.
        if (!await userManager.IsInRoleAsync(user, seed.Role))
        {
            ThrowIfFailed(await userManager.AddToRoleAsync(user, seed.Role), $"add '{seed.Email}' to role {seed.Role}");
        }
    }

    private static async Task EnsureAssignmentsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.InspectionAssignments
            .Select(a => new { a.InspectionId, a.InspectorId })
            .ToListAsync(cancellationToken);

        var missing = Assignments
            .Where(a => !existing.Any(e => e.InspectionId == a.InspectionId && e.InspectorId == a.InspectorId))
            .Select(a => new InspectionAssignment { InspectionId = a.InspectionId, InspectorId = a.InspectorId })
            .ToList();

        if (missing.Count > 0)
        {
            db.InspectionAssignments.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureRecusalsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Recusals
            .Select(r => new { r.InspectorId, r.FirmId })
            .ToListAsync(cancellationToken);

        var missing = Recusals
            .Where(r => !existing.Any(e => e.InspectorId == r.InspectorId && e.FirmId == r.FirmId))
            .Select(r => new Recusal { InspectorId = r.InspectorId, FirmId = r.FirmId, RecusedAt = DateTime.UtcNow })
            .ToList();

        if (missing.Count > 0)
        {
            db.Recusals.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Seeding failed to {action}: {errors}");
        }
    }
}
