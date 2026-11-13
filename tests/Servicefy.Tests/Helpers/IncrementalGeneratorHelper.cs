using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

/// <summary>
/// Runs both Servicefy generators through one <see cref="CSharpGeneratorDriver"/> created with
/// <c>trackIncrementalGeneratorSteps: true</c>: once over an initial compilation, then again over the
/// same compilation after an edit — the way the IDE / a warm compiler server re-runs generators.
/// The returned <see cref="IncrementalRun"/> exposes each run's
/// <see cref="GeneratorRunResult.TrackedSteps"/> / <see cref="GeneratorRunResult.TrackedOutputSteps"/>
/// and their <see cref="IncrementalStepRunReason"/>s, so tests can document which pipeline steps
/// (named with <c>WithTrackingName</c>, see <c>TrackingNames</c>) re-ran and which were cached.
/// </summary>
internal static class IncrementalGeneratorHelper
{
    public const string AssemblyName = "TestAssembly";

    /// <summary>
    /// Builds a compilation from <paramref name="sources"/> (one syntax tree per entry, keyed by its
    /// path), runs the generators, applies <paramref name="edit"/> to that compilation and runs the
    /// <em>same</em> driver again over the result.
    /// </summary>
    public static IncrementalRun RunWithEdit(
        IEnumerable<(string Path, string Source)> sources,
        Func<Compilation, Compilation> edit,
        LanguageVersion langVersion = LanguageVersion.CSharp13)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);

        Compilation compilation = CSharpCompilation.Create(
            assemblyName: AssemblyName,
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, parseOptions, path: s.Path)),
            references: CompilationHelper.BaseReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: generators.Select(g => g.AsSourceGenerator()),
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var initial = driver.GetRunResult();

        var editedCompilation = edit(compilation);
        driver = driver.RunGenerators(editedCompilation);
        var edited = driver.GetRunResult();

        return new IncrementalRun(initial, edited);
    }
}

/// <summary>Ready-made edits for <see cref="IncrementalGeneratorHelper.RunWithEdit"/>.</summary>
internal static class CompilationEdits
{
    /// <summary>Re-runs over the very same compilation object (nothing changed at all).</summary>
    public static Func<Compilation, Compilation> None => static c => c;

    /// <summary>Replaces the whole text of the syntax tree at <paramref name="path"/>.</summary>
    public static Func<Compilation, Compilation> ReplaceSource(string path, string newSource) => c =>
    {
        var oldTree = FindTree(c, path);
        var newTree = oldTree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(newSource));
        return c.ReplaceSyntaxTree(oldTree, newTree);
    };

    /// <summary>Replaces the single occurrence of <paramref name="oldText"/> in the tree at <paramref name="path"/>.</summary>
    public static Func<Compilation, Compilation> ReplaceText(string path, string oldText, string newText) => c =>
    {
        var text = FindTree(c, path).GetText().ToString();
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"'{oldText}' must occur exactly once in '{path}'.");

        return ReplaceSource(path, text.Remove(index, oldText.Length).Insert(index, newText))(c);
    };

    /// <summary>Adds a new syntax tree (a new file in the project).</summary>
    public static Func<Compilation, Compilation> AddSource(string path, string source) => c =>
    {
        var parseOptions = (CSharpParseOptions)c.SyntaxTrees.First().Options;
        return c.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, parseOptions, path: path));
    };

    private static SyntaxTree FindTree(Compilation c, string path) =>
        c.SyntaxTrees.SingleOrDefault(t => t.FilePath == path)
        ?? throw new InvalidOperationException($"No syntax tree with path '{path}' in the compilation.");
}

/// <summary>The two run results of <see cref="IncrementalGeneratorHelper.RunWithEdit"/>.</summary>
internal sealed class IncrementalRun(GeneratorDriverRunResult initial, GeneratorDriverRunResult edited)
{
    /// <summary>Key of <see cref="GeneratorRunResult.TrackedOutputSteps"/> holding every <c>RegisterSourceOutput</c> step.</summary>
    public const string SourceOutputStepName = "SourceOutput";

    public GeneratorDriverRunResult Initial { get; } = initial;

    public GeneratorDriverRunResult Edited { get; } = edited;

    public GeneratorRunResult InitialResult<TGenerator>() where TGenerator : IIncrementalGenerator =>
        ResultOf<TGenerator>(Initial);

    public GeneratorRunResult EditedResult<TGenerator>() where TGenerator : IIncrementalGenerator =>
        ResultOf<TGenerator>(Edited);

    /// <summary>Tracked steps named <paramref name="trackingName"/> in the run after the edit.</summary>
    public ImmutableArray<IncrementalGeneratorRunStep> EditedSteps<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        StepsOf(EditedResult<TGenerator>(), trackingName);

    /// <summary>Tracked steps named <paramref name="trackingName"/> in the initial run.</summary>
    public ImmutableArray<IncrementalGeneratorRunStep> InitialSteps<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        StepsOf(InitialResult<TGenerator>(), trackingName);

    /// <summary>Run reasons of every output of the steps named <paramref name="trackingName"/>, after the edit.</summary>
    public IReadOnlyList<IncrementalStepRunReason> EditedReasons<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Reasons(EditedSteps<TGenerator>(trackingName));

    /// <summary>Run reasons of every output of the steps named <paramref name="trackingName"/>, in the initial run.</summary>
    public IReadOnlyList<IncrementalStepRunReason> InitialReasons<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Reasons(InitialSteps<TGenerator>(trackingName));

    /// <summary>Every <c>RegisterSourceOutput</c> step of the generator in the run after the edit.</summary>
    public ImmutableArray<IncrementalGeneratorRunStep> EditedSourceOutputSteps<TGenerator>()
        where TGenerator : IIncrementalGenerator =>
        EditedResult<TGenerator>().TrackedOutputSteps.TryGetValue(SourceOutputStepName, out var steps)
            ? steps
            : ImmutableArray<IncrementalGeneratorRunStep>.Empty;

    /// <summary>
    /// Run reasons, after the edit, of the <c>RegisterSourceOutput</c> step(s) whose direct input is the
    /// step named <paramref name="inputTrackingName"/>.
    /// </summary>
    public IReadOnlyList<IncrementalStepRunReason> EditedSourceOutputReasonsFedBy<TGenerator>(string inputTrackingName)
        where TGenerator : IIncrementalGenerator
    {
        var outputs = EditedSourceOutputSteps<TGenerator>()
            .Where(step => step.Inputs.Any(input => input.Source.Name == inputTrackingName))
            .ToList();

        if (outputs.Count == 0)
            throw new InvalidOperationException(
                $"No source output of {typeof(TGenerator).Name} is fed directly by the step '{inputTrackingName}'.");

        return Reasons(outputs);
    }

    /// <summary>Generated source text by hint name (file name), initial run; <c>null</c> if not generated.</summary>
    public string? InitialGeneratedSource<TGenerator>(string hintName) where TGenerator : IIncrementalGenerator =>
        GeneratedSource(InitialResult<TGenerator>(), hintName);

    /// <summary>Generated source text by hint name (file name), after the edit; <c>null</c> if not generated.</summary>
    public string? EditedGeneratedSource<TGenerator>(string hintName) where TGenerator : IIncrementalGenerator =>
        GeneratedSource(EditedResult<TGenerator>(), hintName);

    /// <summary>All generated sources of the generator, hint name → text.</summary>
    public static IReadOnlyDictionary<string, string> GeneratedSources(GeneratorRunResult result) =>
        result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    public static IReadOnlyList<IncrementalStepRunReason> Reasons(IEnumerable<IncrementalGeneratorRunStep> steps) =>
        steps.SelectMany(step => step.Outputs).Select(output => output.Reason).ToList();

    private static GeneratorRunResult ResultOf<TGenerator>(GeneratorDriverRunResult run) =>
        run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TGenerator));

    private static ImmutableArray<IncrementalGeneratorRunStep> StepsOf(GeneratorRunResult result, string trackingName) =>
        result.TrackedSteps.TryGetValue(trackingName, out var steps)
            ? steps
            : throw new InvalidOperationException(
                $"Step '{trackingName}' was not tracked by {result.Generator.GetGeneratorType().Name}. " +
                $"Tracked steps: {string.Join(", ", result.TrackedSteps.Keys.OrderBy(k => k, StringComparer.Ordinal))}");

    private static string? GeneratedSource(GeneratorRunResult result, string hintName) =>
        result.GeneratedSources
            .Where(s => s.HintName == hintName)
            .Select(s => s.SourceText.ToString())
            .FirstOrDefault();
}
