using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Data;

/// <summary>
/// EF Core database context. Services use this directly (no repository layer).
/// Entities from the Domain folder get added as DbSet properties later.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
