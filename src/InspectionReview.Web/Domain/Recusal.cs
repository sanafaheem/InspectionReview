using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>
/// An inspector can't judge this firm fairly. Applies to all its inspections, now and in future:
/// they can view but not edit.
/// </summary>
public class Recusal
{
    public int Id { get; set; }

    // Same length as AspNetUsers.Id.
    [MaxLength(450)]
    public required string InspectorId { get; set; }
    public AppUser? Inspector { get; set; }

    public int FirmId { get; set; }
    public Firm? Firm { get; set; }

    // UTC.
    public DateTime RecusedAt { get; set; }
}
