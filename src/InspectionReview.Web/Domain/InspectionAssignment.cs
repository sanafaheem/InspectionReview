using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>
/// Links an inspector (a user in the Inspector role) to an inspection they are assigned to.
/// The primary key is (InspectionId, InspectorId), so an inspector is assigned to an inspection at most once.
/// </summary>
public class InspectionAssignment
{
    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }

    // Matches the length of AspNetUsers.Id, which this references.
    [MaxLength(450)]
    public required string InspectorId { get; set; }
    public AppUser? Inspector { get; set; }

    /// <summary>When the inspector confirmed their independence (UTC). Null = not confirmed yet.</summary>
    public DateTime? IndependenceConfirmedAt { get; set; }
}
