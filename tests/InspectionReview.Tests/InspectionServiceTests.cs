using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;
using Xunit.Abstractions;

namespace InspectionReview.Tests;

// Seed: firm 1 has INS-001 and INS-002, firm 2 has INS-003. Each test gets a fresh database.
public sealed class InspectionServiceTests(ITestOutputHelper output) : IDisposable
{
    private const int Harbourview = 1;
    private const int Northgate = 2;

    // To see the SQL: dotnet test --logger "console;verbosity=detailed"
    private readonly TestDatabase database = new(output.WriteLine);

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Firm_user_sees_only_their_own_firms_inspections()
    {
        var service = CreateService(TestUsers.FirmUser(Harbourview));

        var items = await service.GetInspectionsAsync();

        Assert.Equal(new[] { "INS-001", "INS-002" }, items.Select(i => i.Reference));
        Assert.All(items, i => Assert.Equal("Harbourview Property Group", i.FirmName));
        Assert.All(items, i => Assert.Null(i.Status));
    }

    [Fact]
    public async Task Firm_user_gets_null_for_another_firms_inspection()
    {
        var service = CreateService(TestUsers.FirmUser(Harbourview));

        var otherFirms = await service.GetInspectionDetailAsync(3);   // INS-003 belongs to Northgate
        var ownFirms = await service.GetInspectionDetailAsync(1);     // proves the null isn't a broken setup

        Assert.Null(otherFirms);
        Assert.NotNull(ownFirms);
        Assert.Equal("INS-001", ownFirms.Reference);
    }

    [Fact]
    public async Task Inspector_sees_all_inspections_with_assigned_and_recused_set()
    {
        const string me = "inspector-me";
        const string colleague = "inspector-colleague";

        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(
                new AppUser { Id = me, UserName = "me@test.example", DisplayName = "Me" },
                new AppUser { Id = colleague, UserName = "colleague@test.example", DisplayName = "Colleague" });

            db.InspectionAssignments.AddRange(
                new InspectionAssignment { InspectionId = 1, InspectorId = me },
                new InspectionAssignment { InspectionId = 3, InspectorId = me },          // assigned AND recused
                new InspectionAssignment { InspectionId = 2, InspectorId = colleague });  // not mine

            db.Recusals.AddRange(
                new Recusal { InspectorId = me, FirmId = Northgate, RecusedAt = DateTime.UtcNow },
                new Recusal { InspectorId = colleague, FirmId = Harbourview, RecusedAt = DateTime.UtcNow }); // not mine

            await db.SaveChangesAsync();
        }

        var service = CreateService(TestUsers.Inspector(me));

        var items = (await service.GetInspectionsAsync()).ToDictionary(i => i.Reference);

        Assert.Equal(new[] { "INS-001", "INS-002", "INS-003" }, items.Keys.Order());

        Assert.True(items["INS-001"].IsAssigned);
        Assert.False(items["INS-001"].IsRecused);
        Assert.Equal(AssignmentStatus.Assigned, items["INS-001"].Status);

        // Only my colleague is assigned and recused here.
        Assert.False(items["INS-002"].IsAssigned);
        Assert.False(items["INS-002"].IsRecused);
        Assert.Equal(AssignmentStatus.NotAssigned, items["INS-002"].Status);

        // Assigned, but recused: Recused wins.
        Assert.True(items["INS-003"].IsAssigned);
        Assert.True(items["INS-003"].IsRecused);
        Assert.Equal(AssignmentStatus.Recused, items["INS-003"].Status);
    }

    private InspectionService CreateService(CurrentUserInfo user)
    {
        ICurrentUser currentUser = TestUsers.As(user);
        return new InspectionService(database.Factory, new AccessPolicy(currentUser, database.Factory), currentUser);
    }
}
