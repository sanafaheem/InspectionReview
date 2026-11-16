using InspectionReview.Web.Domain;

namespace InspectionReview.Web.Services;

public enum FindingAction
{
    Submit,
    Approve,
    Return,
    Revise,
}

/// <summary>
/// Which state changes are valid. Who may make them is AccessPolicy's job.
///             Submit                 Approve
///   Draft ─────────────► Submitted ─────────────► Approved (final)
///     ▲                      │
///     │ Revise               │ Return (comment required)
///     │                      ▼
///     └─────────────────  Returned
/// </summary>
public static class FindingWorkflow
{
    // Anything not listed is invalid, so Approved is final without special code.
    private static readonly Dictionary<(FindingState From, FindingAction Action), FindingState> Transitions = new()
    {
        [(FindingState.Draft, FindingAction.Submit)] = FindingState.Submitted,
        [(FindingState.Submitted, FindingAction.Approve)] = FindingState.Approved,
        [(FindingState.Submitted, FindingAction.Return)] = FindingState.Returned,
        [(FindingState.Returned, FindingAction.Revise)] = FindingState.Draft,
    };

    public static bool CanTransition(FindingState from, FindingAction action) =>
        Transitions.ContainsKey((from, action));

    /// <summary>Returns the new state, or throws InvalidOperationException.</summary>
    public static FindingState EnsureCanTransition(FindingState from, FindingAction action)
    {
        if (Transitions.TryGetValue((from, action), out var to))
        {
            return to;
        }

        var allowed = Transitions.Keys.Where(k => k.From == from).Select(k => k.Action.ToString()).ToList();
        var hint = allowed.Count == 0
            ? $"{from} findings can't be changed."
            : $"From {from} you can only: {string.Join(", ", allowed)}.";

        throw new InvalidOperationException($"Cannot {action} a finding that is {from}. {hint}");
    }

    /// <summary>Returns the trimmed comment, or throws ArgumentException if it's blank.</summary>
    public static string EnsureValidReturnComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("A comment is required when returning a finding.", nameof(comment));
        }

        return comment.Trim();
    }
}
