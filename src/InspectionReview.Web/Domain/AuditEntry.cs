using System.ComponentModel.DataAnnotations;

namespace InspectionReview.Web.Domain;

/// <summary>One changed field. Written only by AuditInterceptor; append-only.</summary>
public class AuditEntry
{
    public long Id { get; set; }

    [MaxLength(100)]
    public required string EntityName { get; set; }

    // Text so composite keys fit too, e.g. "1|<inspector id>" for an assignment.
    [MaxLength(500)]
    public required string EntityId { get; set; }

    [MaxLength(100)]
    public required string FieldName { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    // An AspNetUsers.Id or "System". Not a foreign key, because "System" isn't a user.
    [MaxLength(450)]
    public required string UserId { get; set; }

    // UTC.
    public DateTime ChangedAt { get; set; }
}
