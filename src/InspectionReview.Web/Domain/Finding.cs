using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public enum FindingState
{
    Draft,
    Submitted,
    Returned,
    Approved,
}

public class Finding
{
    public int Id { get; set; }

    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public required string Text { get; set; }

    [MaxLength(20)]
    public FindingState State { get; set; }

    /// <summary>From the last Return. Kept after Revise, replaced on the next Return.</summary>
    public string? ReturnComment { get; set; }

    // Same length as AspNetUsers.Id.
    [MaxLength(450)]
    public required string CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }

    // Both UTC. UpdatedAt changes on every edit and state change.
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Set on every submit; null = never submitted. With shared drafts the submitter may not be the creator.
    public DateTime? SubmittedAt { get; set; }

    [MaxLength(450)]
    public string? SubmittedById { get; set; }
    public AppUser? SubmittedBy { get; set; }

    public ICollection<FindingDocument> Documents { get; set; } = [];
}
