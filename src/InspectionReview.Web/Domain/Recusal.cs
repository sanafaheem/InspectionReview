using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>
/// An inspector has stepped away from a firm because they can't judge it fairly
/// (for example, they used to work there). It applies to every inspection of that firm,
/// now and in the future, including ones the inspector is assigned to: they can view, but not edit.
/// At most one recusal per inspector and firm.
/// </summary>
public class Recusal
{
    public int Id { get; set; }

    // Matches the length of AspNetUsers.Id, which this references.
    [MaxLength(450)]
    public required string InspectorId { get; set; }
    public AppUser? Inspector { get; set; }

    public int FirmId { get; set; }
    public Firm? Firm { get; set; }

    /// <summary>When the inspector recused themselves (UTC).</summary>
    public DateTime RecusedAt { get; set; }
}
