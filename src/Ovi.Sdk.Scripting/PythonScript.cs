using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Ovi.Sdk.Scripting;

/// <summary>
/// A Python script destined for a <see cref="PythonScriptNode"/>. The script must define the
/// <see cref="EntryPoint"/> function with the signature <c>def run(input, context)</c>: it receives
/// the node's input and a read-only context snapshot (both as plain Python values decoded from
/// JSON) and returns the node's output (encoded back to JSON).
/// </summary>
public sealed partial record PythonScript
{
    public const string DefaultEntryPoint = "run";

    public PythonScript()
    {
    }

    [SetsRequiredMembers]
    public PythonScript(string code, string entryPoint = DefaultEntryPoint, string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        if (!PythonIdentifier().IsMatch(entryPoint))
        {
            throw new ArgumentException($"'{entryPoint}' is not a valid Python identifier.", nameof(entryPoint));
        }

        Code = code;
        EntryPoint = entryPoint;
        SourcePath = sourcePath;
    }

    /// <summary>The Python source code.</summary>
    public required string Code { get; init; }

    /// <summary>The name of the function the engine calls; <c>run</c> by default.</summary>
    public string EntryPoint { get; init; } = DefaultEntryPoint;

    /// <summary>Where the script was loaded from, when file-based (diagnostics only).</summary>
    public string? SourcePath { get; init; }

    public static PythonScript FromCode(string code, string entryPoint = DefaultEntryPoint) =>
        new(code, entryPoint);

    /// <summary>Loads a script from a <c>.py</c> file, remembering the path for diagnostics.</summary>
    public static PythonScript FromFile(string path, string entryPoint = DefaultEntryPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PythonScript(File.ReadAllText(path), entryPoint, sourcePath: path);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PythonIdentifier();
}
