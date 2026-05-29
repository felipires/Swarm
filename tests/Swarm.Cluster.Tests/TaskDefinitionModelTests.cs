using FluentAssertions;
using Swarm.Cluster.Models;
using Xunit;

namespace Swarm.Cluster.Tests;

public class TaskDefinitionModelTests
{
    [Fact]
    public void NewTaskDefinition_DefaultsTaskTypeToDefaultAt1()
    {
        // Model-level default ensures both rows created via EF (which reads
        // the .HasDefaultValue in OnModelCreating and applies it server-side)
        // and rows created via `new TaskDefinition()` in code carry a valid
        // TaskType string. This pairs with the DB column default added in the
        // AddTaskTypeToTaskDefinition migration.
        var definition = new TaskDefinition();

        definition.TaskType.Should().Be("default@1");
    }
}
