namespace Swarm.Sdk.ValueResolution;

/// <summary>
/// One placeholder occurrence parsed out of a template. The parser does not
/// resolve modifier semantics — it only splits the syntax and reports the
/// position so the pipeline can replace the slice in the source string.
/// </summary>
public record Placeholder(
    int Start,
    int Length,
    string Source,
    string Key,
    IReadOnlyList<string> Modifiers);
