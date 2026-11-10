using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public class Inspection
{
    public int Id { get; set; }

    /// <summary>Human-readable, unique reference, e.g. "INS-001".</summary>
    [MaxLength(20)]
    public required string Reference { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public int FirmId { get; set; }
    public Firm? Firm { get; set; }

    public ICollection<InspectionAssignment> Assignments { get; set; } = [];
}
