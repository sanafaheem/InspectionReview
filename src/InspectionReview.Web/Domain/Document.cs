using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>Result of checking an uploaded document. Stored as text (e.g. "Accepted") in the database.</summary>
public enum DocumentStatus
{
    PendingCheck,
    Accepted,
    Rejected,
}

/// <summary>A file a firm user uploaded for an inspection.</summary>
public class Document
{
    public int Id { get; set; }

    /// <summary>Stable, unguessable identifier, e.g. for the file's name in blob storage. Unique.</summary>
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

    /// <summary>Why the document was rejected. Only set when Status is Rejected.</summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    /// <summary>
    /// True once the file has actually reached storage. Unconfirmed rows are incomplete uploads
    /// and are hidden from everyone.
    /// </summary>
    public bool UploadConfirmed { get; set; }

    // Matches the length of AspNetUsers.Id, which this references.
    [MaxLength(450)]
    public required string UploadedById { get; set; }
    public AppUser? UploadedBy { get; set; }

    /// <summary>When the file was uploaded (UTC).</summary>
    public DateTime UploadedAt { get; set; }
}
