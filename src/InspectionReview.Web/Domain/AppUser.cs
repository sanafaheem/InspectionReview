using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace InspectionReview.Web.Domain;

/// <summary>
/// Application user. Firm users belong to one firm; inspectors and reviewers have no firm.
/// </summary>
public class AppUser : IdentityUser
{
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    public int? FirmId { get; set; }
    public Firm? Firm { get; set; }
}
