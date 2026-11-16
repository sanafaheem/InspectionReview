using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public class InspectionAssignment
{
    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }

    // Same length as AspNetUsers.Id.
    [MaxLength(450)]
    public required string InspectorId { get; set; }
    public AppUser? Inspector { get; set; }

    /// <summary>UTC. Null = not confirmed yet.</summary>
    public DateTime? IndependenceConfirmedAt { get; set; }
}
