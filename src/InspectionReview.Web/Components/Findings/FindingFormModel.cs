namespace InspectionReview.Web.Components.Findings;

public sealed class FindingFormModel
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public HashSet<int> DocumentIds { get; set; } = [];
}
