using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Data;

public class ClusterDbContext(DbContextOptions<ClusterDbContext> options) : DbContext(options)
{
    public DbSet<Node> Nodes { get; set; } = null!;
    public DbSet<Log> Logs { get; set; } = null!;
    public DbSet<TaskDefinition> TaskDefinitions { get; set; } = null!;
    public DbSet<TaskInstance> TaskInstances { get; set; } = null!;
    public DbSet<PendingDispatch> PendingDispatches { get; set; } = null!;
    public DbSet<NodeOverlayTag> NodeOverlayTags { get; set; } = null!;
    public DbSet<NodeCapability> NodeCapabilities { get; set; } = null!;
    public DbSet<NodeEnvOp> NodeEnvOps { get; set; } = null!;
    public DbSet<TaggedRoute> TaggedRoutes { get; set; } = null!;
    public DbSet<Pipeline> Pipelines { get; set; } = null!;
    public DbSet<PipelineStep> PipelineSteps { get; set; } = null!;
    public DbSet<PipelineRun> PipelineRuns { get; set; } = null!;
    public DbSet<PipelineStepInstance> PipelineStepInstances { get; set; } = null!;
    public DbSet<Schedule> Schedules { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Node>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.EffectiveTagsJson).HasColumnType("jsonb");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastHeartbeatAt);
            // P3-3: GIN index backs the `EffectiveTags @> selector` containment
            // query used by tagged dispatch + tagged-subscription resolution.
            entity.HasIndex(e => e.EffectiveTagsJson).HasMethod("gin");
        });

        modelBuilder.Entity<Log>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Level).IsRequired().HasMaxLength(50);
            entity.Property(e => e.MessageTemplate).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => e.NodeId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Level);
        });

        modelBuilder.Entity<TaskDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TaskType).IsRequired().HasMaxLength(255).HasDefaultValue("default@1");
            entity.Property(e => e.ConfigJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").IsRequired();
        });

        modelBuilder.Entity<TaskInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => e.NodeId);
            entity.HasIndex(e => e.Status);
            entity.HasOne(e => e.TaskDefinition)
                  .WithMany(t => t.Instances)
                  .HasForeignKey(e => e.TaskDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PendingDispatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.QueueName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Payload).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.Attempts).HasDefaultValue(0).IsRequired();
            entity.HasIndex(e => e.PublishedAt)
                  .HasFilter("\"PublishedAt\" IS NULL");
            entity.HasOne<TaskInstance>()
                  .WithMany()
                  .HasForeignKey(e => e.InstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeOverlayTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => new { e.NodeId, e.Key }).IsUnique();
            entity.HasOne<Node>()
                  .WithMany()
                  .HasForeignKey(e => e.NodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeCapability>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.TaskType).IsRequired().HasMaxLength(255);
            entity.Property(e => e.JsonSchema).IsRequired().HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(e => e.RequiredEnvKeysJson).IsRequired().HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.RequiredParamsJson).IsRequired().HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.ReportedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => new { e.NodeId, e.TaskType }).IsUnique();
            entity.HasIndex(e => e.TaskType);
            entity.HasOne<Node>()
                  .WithMany()
                  .HasForeignKey(e => e.NodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeEnvOp>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Op).IsRequired();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => e.NodeId);
            entity.HasOne<Node>()
                  .WithMany()
                  .HasForeignKey(e => e.NodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaggedRoute>(entity =>
        {
            entity.HasKey(e => e.Hash);
            entity.Property(e => e.Hash).HasMaxLength(32);
            entity.Property(e => e.SelectorJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.FirstSeenAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.LastUsedAt).HasDefaultValueSql("now()").IsRequired();
            // Backs the TaggedRouteRetentionService sweep (range scan on LastUsedAt).
            entity.HasIndex(e => e.LastUsedAt);
        });

        modelBuilder.Entity<Pipeline>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Version).HasDefaultValue(1).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").IsRequired();
        });

        modelBuilder.Entity<PipelineStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DependsOnJson).IsRequired().HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.FailurePolicy).IsRequired();
            entity.HasIndex(e => new { e.PipelineId, e.Name }).IsUnique();
            entity.HasOne(e => e.Pipeline)
                  .WithMany(p => p.Steps)
                  .HasForeignKey(e => e.PipelineId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TaskDefinition>()
                  .WithMany()
                  .HasForeignKey(e => e.TaskDefinitionId)
                  .OnDelete(DeleteBehavior.Restrict);   // don't delete steps when a TaskDef is removed
        });

        modelBuilder.Entity<PipelineRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.StepsSnapshotJson).IsRequired().HasColumnType("jsonb").HasDefaultValue("[]");
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.StartedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => e.PipelineId);
            entity.HasIndex(e => e.Status);
            entity.HasOne<Pipeline>()
                  .WithMany()
                  .HasForeignKey(e => e.PipelineId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineStepInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.HasIndex(e => new { e.PipelineRunId, e.Status });
            entity.HasIndex(e => e.TaskInstanceId).IsUnique().HasFilter("\"TaskInstanceId\" IS NOT NULL");
            entity.HasOne<PipelineRun>()
                  .WithMany()
                  .HasForeignKey(e => e.PipelineRunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CronExpression).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TimeZoneId).IsRequired().HasMaxLength(64).HasDefaultValue("UTC");
            entity.Property(e => e.Enabled).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").IsRequired();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").IsRequired();
            // Sweep-query covering index: only Enabled rows are candidates,
            // ordered by NextFireAt.
            entity.HasIndex(e => new { e.Enabled, e.NextFireAt });
            entity.HasIndex(e => e.PipelineId);
            entity.HasOne<Pipeline>()
                  .WithMany()
                  .HasForeignKey(e => e.PipelineId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
