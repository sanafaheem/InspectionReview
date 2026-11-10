using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public class Firm
{
    public int Id { get; set; }
    [MaxLength(200)]
    public required string Name { get; set; }

    public ICollection<Inspection> Inspections { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Recusal> Recusals { get; set; } = [];
}
