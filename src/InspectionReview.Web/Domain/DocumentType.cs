using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public class DocumentType
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }
}
