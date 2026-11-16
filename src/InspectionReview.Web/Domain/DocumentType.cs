using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>Kind of document a firm uploads (seeded: Financial Statement, License, Policy, Other).</summary>
public class DocumentType
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }
}
