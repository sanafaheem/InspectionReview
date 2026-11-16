using InspectionReview.Web.Domain;
using InspectionReview.Web.Services;

namespace InspectionReview.Tests;

public sealed class FindingWorkflowTests
{
    [Theory]
    [InlineData(FindingState.Draft, FindingAction.Submit, FindingState.Submitted)]
    [InlineData(FindingState.Submitted, FindingAction.Approve, FindingState.Approved)]
    [InlineData(FindingState.Submitted, FindingAction.Return, FindingState.Returned)]
    [InlineData(FindingState.Returned, FindingAction.Revise, FindingState.Draft)]
    public void Allowed_transitions_return_the_new_state(FindingState from, FindingAction action, FindingState expected)
    {
        Assert.Equal(expected, FindingWorkflow.EnsureCanTransition(from, action));
    }

    // Every (state, action) pair except the four allowed ones.
    public static TheoryData<FindingState, FindingAction> InvalidTransitions()
    {
        var allowed = new HashSet<(FindingState, FindingAction)>
        {
            (FindingState.Draft, FindingAction.Submit),
            (FindingState.Submitted, FindingAction.Approve),
            (FindingState.Submitted, FindingAction.Return),
            (FindingState.Returned, FindingAction.Revise),
        };

        var data = new TheoryData<FindingState, FindingAction>();
        foreach (var state in Enum.GetValues<FindingState>())
        {
            foreach (var action in Enum.GetValues<FindingAction>())
            {
                if (!allowed.Contains((state, action)))
                {
                    data.Add(state, action);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Every_other_transition_is_rejected(FindingState from, FindingAction action)
    {
        var error = Assert.Throws<InvalidOperationException>(() => FindingWorkflow.EnsureCanTransition(from, action));
        Assert.Contains($"Cannot {action} a finding that is {from}", error.Message);
    }

    [Fact]
    public void Approved_is_final()
    {
        Assert.All(Enum.GetValues<FindingAction>(),
            action => Assert.False(FindingWorkflow.CanTransition(FindingState.Approved, action)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Return_comment_is_required(string? comment)
    {
        Assert.Throws<ArgumentException>(() => FindingWorkflow.EnsureValidReturnComment(comment));
    }

    [Fact]
    public void Return_comment_is_trimmed()
    {
        Assert.Equal("Add the license number.", FindingWorkflow.EnsureValidReturnComment("  Add the license number.  "));
    }
}
