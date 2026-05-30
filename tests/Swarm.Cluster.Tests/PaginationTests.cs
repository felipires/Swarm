using FluentAssertions;
using Swarm.Cluster.Models.Dto;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P3-1: normalization rules for paging query parameters. The controllers
/// rely on PageRequest to clamp invalid input rather than throwing — a
/// caller hitting <c>?pageSize=0</c> should get the default page, not a
/// 400.
/// </summary>
public class PaginationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void NormalizedPage_ClampsBelowOne(int input, int expected)
    {
        new PageRequest { Page = input }.NormalizedPage.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 50)]      // zero → default
    [InlineData(-1, 50)]     // negative → default
    [InlineData(50, 50)]     // sensible value preserved
    [InlineData(201, 200)]   // above max → clamp to max
    [InlineData(10_000, 200)]
    public void NormalizedPageSize_ClampsToWindow(int input, int expected)
    {
        new PageRequest { PageSize = input }.NormalizedPageSize.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 20, 40)]
    public void Skip_DerivesFromNormalizedPageAndSize(int page, int size, int expectedSkip)
    {
        new PageRequest { Page = page, PageSize = size }.Skip.Should().Be(expectedSkip);
    }
}
