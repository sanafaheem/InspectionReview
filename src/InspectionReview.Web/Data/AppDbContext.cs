using InspectionReview.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Data;

/// <summary>
/// EF Core database context, including the ASP.NET Core Identity tables.
/// Services use this directly (no repository layer).
/// Column lengths are set with attributes on the Domain classes; relationships and indexes are here.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Firm> Firms => Set<Firm>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionAssignment> InspectionAssignments => Set<InspectionAssignment>();
    public DbSet<Recusal> Recusals => Set<Recusal>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<Document> Documents => Set<Document>();

    public const string FirmUserRoleId = "6f1d2c3a-1b7e-4c55-9a0e-2f7b8d1e4a01";
    public const string InspectorRoleId = "6f1d2c3a-1b7e-4c55-9a0e-2f7b8d1e4a02";
    public const string ReviewerRoleId = "6f1d2c3a-1b7e-4c55-9a0e-2f7b8d1e4a03";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's own table configuration must run first.
        base.OnModelCreating(builder);

        builder.Entity<AppUser>()
            .HasOne(u => u.Firm)
            .WithMany(f => f.Users)
            .HasForeignKey(u => u.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Inspection>(inspection =>
        {
            inspection.HasOne(i => i.Firm)
                .WithMany(f => f.Inspections)
                .HasForeignKey(i => i.FirmId)
                .OnDelete(DeleteBehavior.Restrict);
            inspection.HasIndex(i => i.Reference).IsUnique();
        });

        builder.Entity<InspectionAssignment>(assignment =>
        {
            // Composite key: an inspector can be assigned to a given inspection only once.
            assignment.HasKey(a => new { a.InspectionId, a.InspectorId });
            assignment.HasOne(a => a.Inspection)
                .WithMany(i => i.Assignments)
                .HasForeignKey(a => a.InspectionId)
                .OnDelete(DeleteBehavior.Cascade);
            assignment.HasOne(a => a.Inspector)
                .WithMany()
                .HasForeignKey(a => a.InspectorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Recusal>(recusal =>
        {
            recusal.HasOne(r => r.Inspector)
                .WithMany()
                .HasForeignKey(r => r.InspectorId)
                .OnDelete(DeleteBehavior.Restrict);
            recusal.HasOne(r => r.Firm)
                .WithMany(f => f.Recusals)
                .HasForeignKey(r => r.FirmId)
                .OnDelete(DeleteBehavior.Restrict);
            // An inspector is recused from a given firm at most once.
            recusal.HasIndex(r => new { r.InspectorId, r.FirmId }).IsUnique();
        });

        builder.Entity<Document>(document =>
        {
            // Store the enum as its name ("Accepted") rather than a number, so the data reads
            // clearly and reordering the enum can't silently change stored values.
            document.Property(d => d.Status).HasConversion<string>();
            document.HasIndex(d => d.DocumentKey).IsUnique();

            // Documents are records: block deleting anything they still point to.
            document.HasOne(d => d.Inspection)
                .WithMany(i => i.Documents)
                .HasForeignKey(d => d.InspectionId)
                .OnDelete(DeleteBehavior.Restrict);
            document.HasOne(d => d.DocumentType)
                .WithMany()
                .HasForeignKey(d => d.DocumentTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            document.HasOne(d => d.UploadedBy)
                .WithMany()
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        SeedReferenceData(builder);
    }

    /// <summary>
    /// Fixed data that ships with the migrations: roles, firms, inspections and document types.
    /// Every value is hard-coded (including role ConcurrencyStamps) so the model never changes
    /// between builds and no empty migrations get generated. Users and inspection assignments
    /// are seeded at startup by <see cref="SeedData"/> instead, because they need UserManager.
    /// </summary>
    private static void SeedReferenceData(ModelBuilder builder)
    {
        builder.Entity<IdentityRole>().HasData(
            new IdentityRole
            {
                Id = FirmUserRoleId,
                Name = AppRoles.FirmUser,
                NormalizedName = "FIRMUSER",
                ConcurrencyStamp = "b3f0a9e2-0c1d-4e8a-9f61-5a2d7c4e1b01",
            },
            new IdentityRole
            {
                Id = InspectorRoleId,
                Name = AppRoles.Inspector,
                NormalizedName = "INSPECTOR",
                ConcurrencyStamp = "b3f0a9e2-0c1d-4e8a-9f61-5a2d7c4e1b02",
            },
            new IdentityRole
            {
                Id = ReviewerRoleId,
                Name = AppRoles.Reviewer,
                NormalizedName = "REVIEWER",
                ConcurrencyStamp = "b3f0a9e2-0c1d-4e8a-9f61-5a2d7c4e1b03",
            });

        builder.Entity<Firm>().HasData(
            new Firm { Id = 1, Name = "Harbourview Property Group" },
            new Firm { Id = 2, Name = "Northgate Facilities Ltd" });

        builder.Entity<Inspection>().HasData(
            new Inspection { Id = 1, FirmId = 1, Reference = "INS-001", Title = "Annual fire safety inspection" },
            new Inspection { Id = 2, FirmId = 1, Reference = "INS-002", Title = "Roof and gutter condition survey" },
            new Inspection { Id = 3, FirmId = 2, Reference = "INS-003", Title = "Electrical installation check" });

        builder.Entity<DocumentType>().HasData(
            new DocumentType { Id = 1, Name = "Financial Statement" },
            new DocumentType { Id = 2, Name = "License" },
            new DocumentType { Id = 3, Name = "Policy" },
            new DocumentType { Id = 4, Name = "Other" });
    }
}
