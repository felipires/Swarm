namespace Swarm.Cluster.Validation;

/// <summary>
/// Thrown by <see cref="DispatchValidator"/> when a dispatch request fails
/// validation. <see cref="Code"/> is an error class (e.g.
/// <c>UNSUPPORTED_TASK_TYPE</c>, <c>MISSING_REQUIRED_PARAMS</c>) the API
/// translates into an HTTP 400 with a structured body.
/// </summary>
public class DispatchValidationException : Exception
{
    public string Code { get; }
    public object? Details { get; }

    public DispatchValidationException(string code, string message, object? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }
}
