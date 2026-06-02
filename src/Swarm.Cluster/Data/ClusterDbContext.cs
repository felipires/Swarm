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
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastHeartbeatAt);
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
        });
    }
}
