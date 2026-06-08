using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P1-10 pipeline versioning: edit bumps the version and records an immutable
/// history row; restore re-applies a prior version as a new version; soft delete
/// hides the pipeline while keeping its history.
/// </summary>
public class PipelineVersioningTests
{
    [Fact]
    public async Task Create_RecordsVersionOne()
    {
        using var db = BuildDb();
        var (svc, versions) = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", "first", Steps(taskId), CancellationToken.None);

        pipeline.Version.Should().Be(1);
        var history = await versions.ListAsync(VersionedEntityType.Pipeline, pipeline.Id, CancellationToken.None);
        history.Should().ContainSingle().Which.Version.Should().Be(1);
    }

    [Fact]
    public async Task Update_BumpsVersion_RecordsHistory_ReflectsNewContent()
    {
        using var db = BuildDb();
        var (svc, versions) = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", "v1 desc", Steps(taskId), CancellationToken.None);
        var updated = await svc.UpdateAsync(pipeline.Id, "p", "v2 desc", Steps(taskId, "renamed"),
            expectedVersion: 1, CancellationToken.None);

        updated.Version.Should().Be(2);
        updated.Description.Should().Be("v2 desc");
        updated.Steps.Should().ContainSingle().Which.Name.Should().Be("renamed");

        var history = await versions.ListAsync(VersionedEntityType.Pipeline, pipeline.Id, CancellationToken.None);
        history.Select(h => h.Version).Should().Equal(2, 1); // newest first
    }

    [Fact]
    public async Task Update_WithStaleExpectedVersion_Throws()
    {
        using var db = BuildDb();
        var (svc, _) = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", null, Steps(taskId), CancellationToken.None);

        var act = () => svc.UpdateAsync(pipeline.Id, "p", null, Steps(taskId),
            expectedVersion: 99, CancellationToken.None);

        await act.Should().ThrowAsync<PipelineService.VersionConflictException>();
    }

    [Fact]
    public async Task Restore_ReappliesPriorVersion_AsNewVersion()
    {
        using var db = BuildDb();
        var (svc, versions) = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", "v1 desc", Steps(taskId, "alpha"), CancellationToken.None);
        await svc.UpdateAsync(pipeline.Id, "p", "v2 desc", Steps(taskId, "beta"),
            expectedVersion: 1, CancellationToken.None);

        // Restore v1 the way the controller does: read snapshot, re-apply via update.
        var v1 = await versions.GetAsync(VersionedEntityType.Pipeline, pipeline.Id, 1, CancellationToken.None);
        var snap = EntityVersionService.Deserialize<PipelineService.PipelineSnapshot>(v1!.SnapshotJson);
        var restored = await svc.UpdateAsync(pipeline.Id, snap.Name, snap.Description, snap.Steps,
            expectedVersion: null, CancellationToken.None);

        restored.Version.Should().Be(3);                       // append-only, never reuses v1
        restored.Description.Should().Be("v1 desc");           // content matches v1
        restored.Steps.Should().ContainSingle().Which.Name.Should().Be("alpha");
    }

    [Fact]
    public async Task SoftDelete_HidesPipeline_ButKeepsHistory()
    {
        using var db = BuildDb();
        var (svc, versions) = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", null, Steps(taskId), CancellationToken.None);
        await svc.DeleteAsync(pipeline.Id, CancellationToken.None);

        (await svc.GetAsync(pipeline.Id, CancellationToken.None)).Should().BeNull();
        var (items, total) = await svc.ListAsync(0, 50, includeDeleted: false, CancellationToken.None);
        items.Should().BeEmpty();
        total.Should().Be(0);

        // History survives the soft delete.
        var history = await versions.ListAsync(VersionedEntityType.Pipeline, pipeline.Id, CancellationToken.None);
        history.Should().NotBeEmpty();

        // The row still exists, just filtered out.
        (await db.Pipelines.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ---- harness ----

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"pver-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }

    private static (PipelineService Svc, EntityVersionService Versions) Build(ClusterDbContext db)
    {
        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        var executor = new PipelineRunExecutor(db, dispatch, NullLogger<PipelineRunExecutor>.Instance);
        var versions = new EntityVersionService(db);
        return (new PipelineService(db, executor, versions, NullLogger<PipelineService>.Instance), versions);
    }

    private static async Task<Guid> SeedTaskAsync(ClusterDbContext db)
    {
        var t = new TaskDefinition { Id = Guid.NewGuid(), Name = "t", TaskType = "default@1", ConfigJson = "{}", Version = 1 };
        db.TaskDefinitions.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static List<PipelineService.StepDefinition> Steps(Guid taskId, string name = "only")
        => new() { new(name, taskId, DependsOnByName: Array.Empty<string>()) };
}
