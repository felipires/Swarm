using System.Collections.Concurrent;

namespace Swarm.Node.Configuration;

/// <summary>
/// Holds the Node's tag layers (roadmap D6) and exposes a merged view.
/// Static tags are loaded once at startup from <see cref="TagDiscovery"/>.
/// Overlay tags are refreshed from the heartbeat response on every tick.
/// On key conflict static wins — operational overlays cannot override the
/// Node's deploy-time identity.
/// </summary>
public class NodeTagState
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string> _static = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _overlay = new Dictionary<string, string>();

    public void SetStatic(IReadOnlyDictionary<string, string> staticTags)
    {
        lock (_gate)
        {
            _static = new Dictionary<string, string>(staticTags, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetOverlay(IReadOnlyDictionary<string, string> overlayTags)
    {
        lock (_gate)
        {
            _overlay = new Dictionary<string, string>(overlayTags, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, string> Static
    {
        get { lock (_gate) return _static; }
    }

    public IReadOnlyDictionary<string, string> Overlay
    {
        get { lock (_gate) return _overlay; }
    }

    /// <summary>
    /// Effective tag set used for capability matching. Static layer wins
    /// when both layers carry the same key (D6).
    /// </summary>
    public IReadOnlyDictionary<string, string> Effective
    {
        get
        {
            lock (_gate)
            {
                var merged = new Dictionary<string, string>(_overlay, StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in _static) merged[k] = v;
                return merged;
            }
        }
    }
}
