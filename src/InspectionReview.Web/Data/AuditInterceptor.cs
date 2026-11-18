using System.Globalization;
using System.Runtime.CompilerServices;
using InspectionReview.Web.Auth;
using InspectionReview.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace InspectionReview.Web.Data;

/// <summary>
/// Writes AuditEntry rows for every save, so services and pages need no audit code.
/// Changes are collected before the save; rows are written after it, once database-generated
/// keys exist, inside the same transaction.
/// </summary>
// Takes a function, not ICurrentUser itself: CurrentUser uses the DbContext factory, which needs this
// interceptor, so a constructor dependency would be circular.
public sealed class AuditInterceptor(Func<ICurrentUser> currentUser) : SaveChangesInterceptor
{
    public const string SystemUserId = "System";
    public const string LinkedDocumentsField = "Linked documents";

    private static readonly HashSet<Type> AuditedTypes =
        [typeof(Finding), typeof(Document), typeof(InspectionAssignment), typeof(Recusal)];

    // Change constantly and mean nothing in a history.
    private static readonly HashSet<string> SkippedFields = ["LockedById", "LockedAt", "Version", "UpdatedAt"];

    // One instance serves every context in the scope, so keep each save's state per context.
    private readonly ConditionalWeakTable<DbContext, SaveState> states = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        throw new InvalidOperationException("Use SaveChangesAsync: changes must be audited, and that needs the async user lookup.");

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        context.ChangeTracker.DetectChanges();

        EnsureAuditEntriesAreAppendOnly(context);

        // The nested save of the audit rows themselves.
        if (states.TryGetValue(context, out var current) && current.SavingAuditRows)
        {
            return result;
        }

        var changes = CollectChanges(context);
        changes.AddRange(await CollectDocumentLinksAsync(context, cancellationToken));

        if (changes.Count == 0)
        {
            return result;
        }

        var user = await currentUser().GetAsync(cancellationToken);
        var state = new SaveState(changes, user?.UserId ?? SystemUserId, DateTime.UtcNow);

        if (context.Database.CurrentTransaction is null)
        {
            state.OwnTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        }

        states.AddOrUpdate(context, state);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        if (!states.TryGetValue(context, out var state) || state.SavingAuditRows)
        {
            return result;
        }

        try
        {
            // Keys are real now, so EntityId can be read.
            context.Set<AuditEntry>().AddRange(state.Changes.Select(change => new AuditEntry
            {
                EntityName = change.EntityName,
                EntityId = change.EntityId(),
                FieldName = change.FieldName,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                UserId = state.UserId,
                ChangedAt = state.ChangedAt,
            }));

            state.SavingAuditRows = true;
            await context.SaveChangesAsync(cancellationToken);

            if (state.OwnTransaction is not null)
            {
                await state.OwnTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollBackAsync(context, state);
            throw;
        }

        await EndAsync(context, state);
        return result;
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        if (states.TryGetValue(context, out var state) && !state.SavingAuditRows)
        {
            await RollBackAsync(context, state);
        }
    }

    private static void EnsureAuditEntriesAreAppendOnly(DbContext context)
    {
        if (context.ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are append-only: they can't be modified or deleted.");
        }
    }

    private static List<PendingChange> CollectChanges(DbContext context)
    {
        var changes = new List<PendingChange>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!AuditedTypes.Contains(entry.Metadata.ClrType) || entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var entityName = entry.Metadata.ClrType.Name;

            foreach (var property in entry.Properties)
            {
                // A generated key is already the EntityId; keys the app sets (e.g. an assignment's) are data.
                var isGeneratedKey = property.Metadata.IsPrimaryKey() && property.Metadata.ValueGenerated != ValueGenerated.Never;
                if (SkippedFields.Contains(property.Metadata.Name) || isGeneratedKey)
                {
                    continue;
                }

                if (entry.State == EntityState.Added)
                {
                    var value = Format(property.CurrentValue);
                    if (!string.IsNullOrEmpty(value))
                    {
                        changes.Add(new PendingChange(entityName, () => KeyOf(entry), property.Metadata.Name, null, value));
                    }
                }
                else if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                {
                    changes.Add(new PendingChange(
                        entityName, () => KeyOf(entry), property.Metadata.Name,
                        Format(property.OriginalValue), Format(property.CurrentValue)));
                }
            }
        }

        return changes;
    }

    // Linking or unlinking a document is shown on the finding's own history.
    private static async Task<List<PendingChange>> CollectDocumentLinksAsync(DbContext context, CancellationToken cancellationToken)
    {
        var links = context.ChangeTracker.Entries<FindingDocument>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted)
            .ToList();

        if (links.Count == 0)
        {
            return [];
        }

        var documentIds = links.Select(l => l.Entity.DocumentId).Distinct().ToList();
        var fileNames = await context.Set<Document>()
            .AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.FileName, cancellationToken);

        return links.Select(link =>
        {
            var fileName = fileNames.GetValueOrDefault(link.Entity.DocumentId, $"Document {link.Entity.DocumentId}");
            var added = link.State == EntityState.Added;
            // FindingId is read after the save: a new finding only gets its real Id then.
            return new PendingChange(
                nameof(Finding), () => link.Entity.FindingId.ToString(CultureInfo.InvariantCulture), LinkedDocumentsField,
                added ? null : fileName, added ? fileName : null);
        }).ToList();
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join("|", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => Format(entry.Property(p.Name).CurrentValue)));

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private async Task RollBackAsync(DbContext context, SaveState state)
    {
        if (state.OwnTransaction is not null)
        {
            await state.OwnTransaction.RollbackAsync();
        }
        await EndAsync(context, state);
    }

    private async Task EndAsync(DbContext context, SaveState state)
    {
        if (state.OwnTransaction is not null)
        {
            await state.OwnTransaction.DisposeAsync();
        }
        states.Remove(context);
    }

    // EntityId is a function because a new entity's key only exists after the insert.
    private sealed record PendingChange(
        string EntityName, Func<string> EntityId, string FieldName, string? OldValue, string? NewValue);

    private sealed class SaveState(List<PendingChange> changes, string userId, DateTime changedAt)
    {
        public List<PendingChange> Changes { get; } = changes;
        public string UserId { get; } = userId;
        public DateTime ChangedAt { get; } = changedAt;
        public IDbContextTransaction? OwnTransaction { get; set; }
        public bool SavingAuditRows { get; set; }
    }
}
