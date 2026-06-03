namespace Swarm.Cluster.Models;

/// <summary>
/// How the delay between successive retry attempts scales with the attempt
/// count (P1-2). Lives as an enum + switch in <c>RetryDelayCalculator</c>;
/// no <c>IRetryBackoffStrategy</c> interface — the set is closed and small,
/// and a plugin model would be a YAGNI violation here.
/// </summary>
public enum RetryBackoffStrategy
{
    /// <summary>Always <c>RetryDelaySeconds</c>.</summary>
    Fixed = 0,

    /// <summary><c>RetryDelaySeconds * attempt</c>.</summary>
    Linear = 1,

    /// <summary><c>RetryDelaySeconds * 2^(attempt - 1)</c>.</summary>
    Exponential = 2,
}

public static class RetryDelayCalculator
{
    /// <param name="attempt">1 for the first retry, 2 for the second, etc.</param>
    public static TimeSpan ComputeDelay(RetryBackoffStrategy strategy, int baseSeconds, int attempt)
    {
        if (attempt < 1) attempt = 1;
        if (baseSeconds < 0) baseSeconds = 0;

        var seconds = strategy switch
        {
            RetryBackoffStrategy.Fixed => baseSeconds,
            RetryBackoffStrategy.Linear => baseSeconds * attempt,
            RetryBackoffStrategy.Exponential => baseSeconds * (long)Math.Pow(2, attempt - 1),
            _ => baseSeconds,
        };

        // Cap at one day to keep a runaway exponential from scheduling decades out.
        var capped = Math.Min(seconds, 86_400L);
        return TimeSpan.FromSeconds(capped);
    }
}
