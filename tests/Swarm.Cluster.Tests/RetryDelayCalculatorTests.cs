using FluentAssertions;
using Swarm.Cluster.Models;
using Xunit;

namespace Swarm.Cluster.Tests;

public class RetryDelayCalculatorTests
{
    [Theory]
    [InlineData(60, 1, 60)]
    [InlineData(60, 2, 60)]
    [InlineData(60, 5, 60)]
    public void Fixed_AlwaysReturnsBase(int baseSeconds, int attempt, int expected)
    {
        RetryDelayCalculator.ComputeDelay(RetryBackoffStrategy.Fixed, baseSeconds, attempt)
            .Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Theory]
    [InlineData(30, 1, 30)]
    [InlineData(30, 2, 60)]
    [InlineData(30, 3, 90)]
    public void Linear_ScalesWithAttemptCount(int baseSeconds, int attempt, int expected)
    {
        RetryDelayCalculator.ComputeDelay(RetryBackoffStrategy.Linear, baseSeconds, attempt)
            .Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Theory]
    [InlineData(10, 1, 10)]   // 10 * 2^0
    [InlineData(10, 2, 20)]   // 10 * 2^1
    [InlineData(10, 3, 40)]   // 10 * 2^2
    [InlineData(10, 4, 80)]
    public void Exponential_DoublesEachAttempt(int baseSeconds, int attempt, int expected)
    {
        RetryDelayCalculator.ComputeDelay(RetryBackoffStrategy.Exponential, baseSeconds, attempt)
            .Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Fact]
    public void Exponential_CapsAtOneDay()
    {
        // base=3600, attempt=30 → 3600 * 2^29 ≈ 1.9e12 seconds. Must clamp.
        var delay = RetryDelayCalculator.ComputeDelay(RetryBackoffStrategy.Exponential, 3600, attempt: 30);

        delay.Should().Be(TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveAttempt_TreatedAsFirstAttempt(int attempt)
    {
        RetryDelayCalculator.ComputeDelay(RetryBackoffStrategy.Linear, 30, attempt)
            .Should().Be(TimeSpan.FromSeconds(30));
    }
}
