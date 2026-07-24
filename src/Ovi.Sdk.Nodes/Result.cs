using System.Diagnostics.CodeAnalysis;

namespace Ovi.Sdk.Nodes;

/// <summary>
/// The outcome of a public SDK execution API: exactly one of a success carrying
/// <see cref="Value"/> or a failure carrying <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>This is a deliberately <b>naive, migration-ready</b> result type: two states, factory
/// methods, exhaustive <see cref="Match"/>, and monadic <see cref="Map"/>/<see cref="Bind"/> —
/// the same surface CSharpFunctionalExtensions offers and the same shape a future C# discriminated
/// union (<c>Result&lt;T&gt; = Success(T) | Failure(Error)</c>) will have, so call sites written
/// against it survive the migration unchanged.</para>
/// <para>Reading <see cref="Value"/> on a failure (or <see cref="Error"/> on a success) throws —
/// check <see cref="IsSuccess"/> or use <see cref="Match"/>/<see cref="TryGetValue"/>.</para>
/// </remarks>
public sealed class Result<T>
{
    private readonly T _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        _value = default!;
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    /// <summary>The success value; throws when the result is a failure.</summary>
    public T Value => _error is null
        ? _value
        : throw new InvalidOperationException($"The result is a failure ({_error}); check IsSuccess before reading Value.");

    /// <summary>The failure error; throws when the result is a success.</summary>
    public Error Error => _error
        ?? throw new InvalidOperationException("The result is a success; check IsFailure before reading Error.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }

    /// <summary>Values convert implicitly, so node bodies can simply <c>return output;</c>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Errors convert implicitly, so failure paths can simply <c>return error;</c>.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Exhaustive two-way branch — the pattern-match of the future union.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return _error is null ? onSuccess(_value) : onFailure(_error);
    }

    /// <summary>Transforms the success value; failures pass through unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return _error is null ? Result<TOut>.Success(map(_value)) : Result<TOut>.Failure(_error);
    }

    /// <summary>Chains a result-producing continuation; failures short-circuit.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return _error is null ? bind(_value) : Result<TOut>.Failure(_error);
    }

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _error is null ? _value : default;
        return _error is null;
    }

    public T? GetValueOrDefault(T? defaultValue = default) => _error is null ? _value : defaultValue;

    public override string ToString() => _error is null ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>Factory helpers that let type inference do the work: <c>Result.Success(value)</c>.</summary>
public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}
