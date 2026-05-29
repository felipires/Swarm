namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// Standard error envelope returned by every API endpoint (roadmap P3-2).
/// <see cref="Code"/> is a machine-readable error class (e.g.
/// <c>UNSUPPORTED_TASK_TYPE</c>, <c>NODE_NOT_FOUND</c>). <see cref="Message"/>
/// is human-readable. <see cref="Details"/> carries structured context the
/// caller can inspect (lists of missing keys, conflicting IDs, etc.).
/// </summary>
public record ApiError(string Code, string Message, object? Details = null);
