using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.KnowledgeGraph.Domain;
using Neura.Modules.Memory.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Infrastructure.Persistence;

public class ApplicationUser : IdentityUser<Guid> { }

/// <summary>
/// Single EF Core context spanning module DbSets. Modules keep separate
/// Domain/Application boundaries; only Infrastructure knows about EF.
/// A future microservice extraction would split this per bounded context.
/// </summary>
public class NeuraDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public NeuraDbContext(DbContextOptions<NeuraDbContext> options) : base(options) { }

    public DbSet<AIProviderAccount> ProviderAccounts => Set<AIProviderAccount>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<AgentTask> Tasks => Set<AgentTask>();
    public DbSet<MemoryRecord> Memories => Set<MemoryRecord>();
    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeEdge> KnowledgeEdges => Set<KnowledgeEdge>();
    public DbSet<HandoffRecord> Handoffs => Set<HandoffRecord>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<ExecutionEvent> ExecutionEvents => Set<ExecutionEvent>();
    public DbSet<CostRecord> CostRecords => Set<CostRecord>();
    public DbSet<ContextHandoffPackage> ContextPackages => Set<ContextHandoffPackage>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Agent>(b =>
        {
            b.Property(a => a.Capabilities)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0 ? new List<AgentCapability>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => Enum.Parse<AgentCapability>(x)).ToList())
                .Metadata.SetValueComparer(ListComparer<AgentCapability>());
        });

        builder.Entity<Mission>(b => b.Ignore(m => m.Tasks));

        builder.Entity<MemoryRecord>(b =>
        {
            // MemoryRecord uses MemoryId rather than EF Core's conventional
            // Id/MemoryRecordId property name. Configure it explicitly so
            // EF Core treats it as the primary key during model validation
            // and when EnsureCreated/Migrations build the Memories table.
            b.HasKey(m => m.MemoryId);

            b.Property(m => m.Tags)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0 ? new List<string>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Metadata.SetValueComparer(ListComparer<string>());
        });

        builder.Entity<AgentTask>(b =>
        {
            var referenceMaterialConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
                List<TrustLabeledContent>, string>(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<TrustLabeledContent>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<TrustLabeledContent>());

            // This one matters more than the others: TaskReferenceController
            // mutates this list in place (task.ReferenceMaterial.Add(...))
            // before calling SaveChangesAsync. Without an explicit value
            // comparer, EF Core's default reference-equality comparer sees
            // the SAME List<T> instance before and after the mutation and
            // concludes nothing changed — silently dropping the update.
            // The comparer below forces an actual element-by-element check.
            b.Property(t => t.ReferenceMaterial)
                .HasConversion(referenceMaterialConverter)
                .Metadata.SetValueComparer(ListComparer<TrustLabeledContent>());

            b.Property(t => t.DependsOn)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0 ? new List<Guid>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList())
                .Metadata.SetValueComparer(ListComparer<Guid>());
        });

        // ContextHandoffPackage's list fields are stored as simple
        // newline-joined text — sufficient for the Context Explorer
        // screen; move to a JSONB column if richer querying is needed.
        builder.Entity<ContextHandoffPackage>(b =>
        {
            var listConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>(
                v => string.Join('\u001F', v),
                v => v.Length == 0 ? new List<string>() : v.Split('\u001F', StringSplitOptions.RemoveEmptyEntries).ToList());
            var comparer = ListComparer<string>();

            b.Property(p => p.CompletedWork).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.RemainingWork).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.Decisions).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.Constraints).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.FilesChanged).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.Errors).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.Tests).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.Dependencies).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.OpenQuestions).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
            b.Property(p => p.RelevantMemory).HasConversion(listConverter).Metadata.SetValueComparer(comparer);
        });
    }

    /// <summary>
    /// Builds a proper element-by-element ValueComparer for a converted
    /// List&lt;T&gt; property. Without this, EF Core's change tracker
    /// defaults to reference equality for the snapshot comparison, which
    /// means in-place mutations (list.Add(...) without reassigning the
    /// property) can go undetected and silently fail to persist.
    /// </summary>
    private static Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<T>> ListComparer<T>()
        => new(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item!.GetHashCode())),
            v => v.ToList());
}
