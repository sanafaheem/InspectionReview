using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace InspectionReview.Web.Domain;

public class AppUser : IdentityUser
{
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    // Set only for firm users.
    public int? FirmId { get; set; }
    public Firm? Firm { get; set; }
}
