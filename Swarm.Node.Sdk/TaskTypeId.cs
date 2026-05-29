using System.Diagnostics.CodeAnalysis;

namespace Swarm.Node.Sdk;

/// <summary>
/// Parsed form of a <c>name@version</c> task-type identifier. Name is a
/// lowercase identifier (<c>[a-z][a-z0-9_-]*</c>) and version is a positive
/// integer (<c>[1-9][0-9]*</c>). Examples: <c>http@1</c>, <c>sql@2</c>,
/// <c>default@1</c>.
/// </summary>
public readonly record struct TaskTypeId(string Name, int Version)
{
    public override string ToString() => $"{Name}@{Version}";

    public static TaskTypeId Parse(string s)
    {
        if (!TryParse(s, out var result))
            throw new FormatException($"Invalid TaskType identifier: '{s}'. Expected 'name@version'.");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, out TaskTypeId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var at = s.IndexOf('@');
        if (at <= 0 || at == s.Length - 1) return false;

        var name = s.AsSpan(0, at);
        var version = s.AsSpan(at + 1);

        if (!IsValidName(name)) return false;
        if (!IsValidVersion(version, out var versionNumber)) return false;

        result = new TaskTypeId(name.ToString(), versionNumber);
        return true;
    }

    private static bool IsValidName(ReadOnlySpan<char> name)
    {
        if (name.Length == 0) return false;
        var first = name[0];
        if (first < 'a' || first > 'z') return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    private static bool IsValidVersion(ReadOnlySpan<char> version, out int value)
    {
        value = 0;
        if (version.Length == 0) return false;
        if (version[0] == '0') return false;
        foreach (var c in version)
        {
            if (c < '0' || c > '9') return false;
        }
        return int.TryParse(version, out value) && value >= 1;
    }
}
