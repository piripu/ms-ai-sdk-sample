using Ovi.Sdk.Nodes;
using Xunit;

namespace Ovi.Sdk.Tests;

public class ResultTests
{
    [Fact]
    public void Success_carries_a_value_and_guards_the_error()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_an_error_and_guards_the_value()
    {
        var result = Result.Failure<int>(new ValidationError("bad input"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("bad input", result.Error.Message);
    }

    [Fact]
    public void Values_and_errors_convert_implicitly()
    {
        Result<string> success = "hello";
        Result<string> failure = new ResolutionError("missing dependency");

        Assert.True(success.IsSuccess);
        Assert.Equal("hello", success.Value);
        Assert.True(failure.IsFailure);
        Assert.Equal(ResolutionError.ErrorCode, failure.Error.Code);
    }

    [Fact]
    public void Null_success_values_are_allowed()
    {
        var result = Result<string?>.Success(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Match_branches_exhaustively()
    {
        Result<int> success = 2;
        Result<int> failure = new ExecutionError("boom");

        Assert.Equal("value:2", success.Match(v => $"value:{v}", e => $"error:{e.Code}"));
        Assert.Equal("error:execution", failure.Match(v => $"value:{v}", e => $"error:{e.Code}"));
    }

    [Fact]
    public void Map_and_bind_short_circuit_failures()
    {
        Result<int> success = 2;
        Result<int> failure = new ValidationError("nope");

        Assert.Equal(4, success.Map(v => v * 2).Value);
        Assert.Equal("nope", failure.Map(v => v * 2).Error.Message);

        Assert.Equal(1, success.Bind(v => Result.Success(v - 1)).Value);
        Assert.True(success.Bind(_ => Result.Failure<int>(new ValidationError("later"))).IsFailure);
        Assert.Equal("nope", failure.Bind(v => Result.Success(v)).Error.Message);
    }

    [Fact]
    public void TryGetValue_and_GetValueOrDefault_behave()
    {
        Result<string> success = "x";
        Result<string> failure = new ValidationError("no");

        Assert.True(success.TryGetValue(out var value));
        Assert.Equal("x", value);
        Assert.False(failure.TryGetValue(out _));
        Assert.Equal("fallback", failure.GetValueOrDefault("fallback"));
    }

    [Fact]
    public void Error_records_carry_stable_codes_and_details()
    {
        var validation = new ValidationError("bad", detail: "line 3");
        var resolution = new ResolutionError("missing", hint: "register it");
        var execution = ExecutionError.FromException(new InvalidOperationException("boom"));

        Assert.Equal("validation", validation.Code);
        Assert.Equal("line 3", validation.Detail);
        Assert.Equal("resolution", resolution.Code);
        Assert.Equal("register it", resolution.Hint);
        Assert.Equal("execution", execution.Code);
        Assert.Contains("boom", execution.Message);
        Assert.IsType<InvalidOperationException>(execution.Exception);

        Assert.Equal("validation: bad", validation.ToString());
    }
}
