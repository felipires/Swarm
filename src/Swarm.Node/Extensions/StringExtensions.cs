using System;

namespace Swarm.Node.Extensions;

public static class StringExtensions
{
    public static bool IsNullOrEmpty(this string value)
    {
        return value == null || value.Trim().Length == 0;
    }
}
