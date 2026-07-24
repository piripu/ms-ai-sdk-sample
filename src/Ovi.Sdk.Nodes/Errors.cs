namespace Ovi.Sdk.Nodes;

/// <summary>
/// The base of the SDK's closed error set. Errors describe <b>expected</b> runtime failures of
/// public execution APIs (see <see cref="Result{T}"/>); programmer errors (bad constructor
/// arguments, misuse) remain exceptions.
/// </summary>
/// <remarks>
/// The set is deliberately closed — <see cref="ValidationError"/>, <see cref="ResolutionError"/>,
/// <see cref="ExecutionError"/> — which is exactly the shape of a future C# discriminated union:
/// when unions land (or the SDK adopts a union library), <c>Error</c> becomes the union and the
/// derived records become its cases with no consumer-visible surface change. Match on the derived
/// type (<c>error is ResolutionError r</c>) or on the stable <see cref="Code"/> string.
/// </remarks>
public abstract record Error(string Code, string Message)
{
    public sealed override string ToString() => $"{Code}: {Message}";
}

/// <summary>An input, request, or definition failed validation.</summary>
public sealed record ValidationError : Error
{
    public const string ErrorCode = "validation";

    public ValidationError(string message, string? detail = null)
        : base(ErrorCode, message) => Detail = detail;

    /// <summary>Optional machine-usable detail (offending property, parse position, …).</summary>
    public string? Detail { get; }
}

/// <summary>A required collaborator (chat client, script engine, tool, …) could not be resolved.</summary>
public sealed record ResolutionError : Error
{
    public const string ErrorCode = "resolution";

    public ResolutionError(string message, string? hint = null)
        : base(ErrorCode, message) => Hint = hint;

    /// <summary>How to make the collaborator available (e.g. "register an IChatClient in RuntimeServices").</summary>
    public string? Hint { get; }
}

/// <summary>Execution itself failed — typically an exception from a collaborator or node body.</summary>
public sealed record ExecutionError : Error
{
    public const string ErrorCode = "execution";

    public ExecutionError(string message, Exception? exception = null)
        : base(ErrorCode, message) => Exception = exception;

    /// <summary>The underlying exception, when one caused the failure.</summary>
    public Exception? Exception { get; }

    public static ExecutionError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ExecutionError($"{exception.GetType().Name}: {exception.Message}", exception);
    }
}
