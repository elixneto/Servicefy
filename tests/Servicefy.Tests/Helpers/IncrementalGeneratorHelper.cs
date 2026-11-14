using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

internal static class IncrementalGeneratorHelper
{
    public const string AssemblyName = "TestAssembly";

    public static IncrementalRun RunWithEdit(
        IEnumerable<(string Path, string Source)> sources,
        Func<Compilation, Compilation> edit,
        LanguageVersion langVersion = LanguageVersion.CSharp13,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var steps = RunSequence(sources, [edit], langVersion, additionalReferences);
        return new IncrementalRun(steps[0].Result, steps[1].Result, steps[0].Compilation, steps[1].Compilation);
    }

    public static IReadOnlyList<(Compilation Compilation, GeneratorDriverRunResult Result)> RunSequence(
        IEnumerable<(string Path, string Source)> sources,
        IEnumerable<Func<Compilation, Compilation>> edits,
        LanguageVersion langVersion = LanguageVersion.CSharp13,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);

        Compilation compilation = CreateCompilation(sources, parseOptions, additionalReferences);
        var driver = CreateDriver(parseOptions);

        var runs = new List<(Compilation, GeneratorDriverRunResult)>();

        driver = driver.RunGenerators(compilation);
        runs.Add((compilation, driver.GetRunResult()));

        foreach (var edit in edits)
        {
            compilation = edit(compilation);
            driver = driver.RunGenerators(compilation);
            runs.Add((compilation, driver.GetRunResult()));
        }

        return runs;
    }

    public static GeneratorDriverRunResult RunFresh(Compilation compilation)
    {
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        return CreateDriver(parseOptions).RunGenerators(compilation).GetRunResult();
    }

    public static CSharpCompilation CreateCompilation(
        IEnumerable<(string Path, string Source)> sources,
        CSharpParseOptions parseOptions,
        IEnumerable<MetadataReference>? additionalReferences = null) =>
        CSharpCompilation.Create(
            assemblyName: AssemblyName,
            syntaxTrees: sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, parseOptions, path: s.Path)),
            references: CompilationHelper.BaseReferences.Concat(additionalReferences ?? []),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static GeneratorDriver CreateDriver(CSharpParseOptions parseOptions)
    {
        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        return CSharpGeneratorDriver.Create(
            generators: generators.Select(g => g.AsSourceGenerator()),
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }
}

internal static class CompilationEdits
{
    public static Func<Compilation, Compilation> None => static c => c;

    public static Func<Compilation, Compilation> ReplaceSource(string path, string newSource) => c =>
    {
        var oldTree = FindTree(c, path);
        var newTree = oldTree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(newSource));
        return c.ReplaceSyntaxTree(oldTree, newTree);
    };

    public static Func<Compilation, Compilation> ReplaceText(string path, string oldText, string newText) => c =>
    {
        var text = FindTree(c, path).GetText().ToString();
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"'{oldText}' must occur exactly once in '{path}'.");

        return ReplaceSource(path, text.Remove(index, oldText.Length).Insert(index, newText))(c);
    };

    public static Func<Compilation, Compilation> AddSource(string path, string source) => c =>
    {
        var parseOptions = (CSharpParseOptions)c.SyntaxTrees.First().Options;
        return c.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, parseOptions, path: path));
    };

    public static Func<Compilation, Compilation> InsertAfter(string path, string anchor, string newText) =>
        ReplaceText(path, anchor, anchor + newText);

    public static Func<Compilation, Compilation> Combine(params Func<Compilation, Compilation>[] edits) =>
        c => edits.Aggregate(c, (current, edit) => edit(current));

    public static Func<Compilation, Compilation> ReplaceReference(MetadataReference oldReference, MetadataReference newReference) =>
        c => c.ReplaceReference(oldReference, newReference);

    public static Func<Compilation, Compilation> RemoveReference(MetadataReference reference) =>
        c => c.RemoveReferences(reference);

    public static Func<Compilation, Compilation> RemoveSource(string path) =>
        c => c.RemoveSyntaxTrees(FindTree(c, path));

    private static SyntaxTree FindTree(Compilation c, string path) =>
        c.SyntaxTrees.SingleOrDefault(t => t.FilePath == path)
        ?? throw new InvalidOperationException($"No syntax tree with path '{path}' in the compilation.");
}

internal sealed class IncrementalRun(
    GeneratorDriverRunResult initial,
    GeneratorDriverRunResult edited,
    Compilation initialCompilation,
    Compilation editedCompilation)
{
    public Compilation InitialCompilation { get; } = initialCompilation;

    public Compilation EditedCompilation { get; } = editedCompilation;

    public const string SourceOutputStepName = "SourceOutput";

    public GeneratorDriverRunResult Initial { get; } = initial;

    public GeneratorDriverRunResult Edited { get; } = edited;

    public GeneratorRunResult InitialResult<TGenerator>() where TGenerator : IIncrementalGenerator =>
        ResultOf<TGenerator>(Initial);

    public GeneratorRunResult EditedResult<TGenerator>() where TGenerator : IIncrementalGenerator =>
        ResultOf<TGenerator>(Edited);

    public ImmutableArray<IncrementalGeneratorRunStep> EditedSteps<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        StepsOf(EditedResult<TGenerator>(), trackingName);

    public ImmutableArray<IncrementalGeneratorRunStep> InitialSteps<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        StepsOf(InitialResult<TGenerator>(), trackingName);

    public IReadOnlyList<IncrementalStepRunReason> EditedReasons<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Reasons(EditedSteps<TGenerator>(trackingName));

    public IReadOnlyList<IncrementalStepRunReason> InitialReasons<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Reasons(InitialSteps<TGenerator>(trackingName));

    public IReadOnlyList<object> InitialValues<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Values(InitialSteps<TGenerator>(trackingName));

    public IReadOnlyList<object> EditedValues<TGenerator>(string trackingName)
        where TGenerator : IIncrementalGenerator =>
        Values(EditedSteps<TGenerator>(trackingName));

    public ImmutableArray<IncrementalGeneratorRunStep> EditedSourceOutputSteps<TGenerator>()
        where TGenerator : IIncrementalGenerator =>
        EditedResult<TGenerator>().TrackedOutputSteps.TryGetValue(SourceOutputStepName, out var steps)
            ? steps
            : ImmutableArray<IncrementalGeneratorRunStep>.Empty;

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

    public string? InitialGeneratedSource<TGenerator>(string hintName) where TGenerator : IIncrementalGenerator =>
        GeneratedSource(InitialResult<TGenerator>(), hintName);

    public string? EditedGeneratedSource<TGenerator>(string hintName) where TGenerator : IIncrementalGenerator =>
        GeneratedSource(EditedResult<TGenerator>(), hintName);

    public static IReadOnlyDictionary<string, string> GeneratedSources(GeneratorRunResult result) =>
        result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    public static IReadOnlyList<IncrementalStepRunReason> Reasons(IEnumerable<IncrementalGeneratorRunStep> steps) =>
        steps.SelectMany(step => step.Outputs).Select(output => output.Reason).ToList();

    public static IReadOnlyList<object> Values(IEnumerable<IncrementalGeneratorRunStep> steps) =>
        steps.SelectMany(step => step.Outputs).Select(output => output.Value).ToList();

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
