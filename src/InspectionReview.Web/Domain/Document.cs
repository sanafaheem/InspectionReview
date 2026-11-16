using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

public enum DocumentStatus
{
    PendingCheck,
    Accepted,
    Rejected,
}

public class Document
{
    public int Id { get; set; }

    // Unguessable id, e.g. for the blob name.
    public Guid DocumentKey { get; set; }

    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }

    public int DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    [MaxLength(255)]
    public required string FileName { get; set; }

    public long SizeBytes { get; set; }

    [MaxLength(20)]
    public DocumentStatus Status { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    // False until the file has reached storage; unconfirmed rows are hidden from everyone.
    public bool UploadConfirmed { get; set; }

    // Same length as AspNetUsers.Id.
    [MaxLength(450)]
    public required string UploadedById { get; set; }
    public AppUser? UploadedBy { get; set; }

    // UTC.
    public DateTime UploadedAt { get; set; }
}
