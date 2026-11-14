using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Servicefy.Tests.Helpers;

internal static class GeneratorOutputAssert
{
    public static void EqualToFreshRun(GeneratorDriverRunResult incremental, Compilation compilation, string what)
    {
        var fresh = IncrementalGeneratorHelper.RunFresh(compilation);

        Assert.All(fresh.Results, r => Assert.Null(r.Exception));
        Assert.All(incremental.Results, r => Assert.True(
            r.Exception is null,
            $"{what}: {r.Generator.GetGeneratorType().Name} threw: {r.Exception}"));

        foreach (var freshResult in fresh.Results)
        {
            var generatorType = freshResult.Generator.GetGeneratorType();
            var incrementalResult = incremental.Results.Single(r => r.Generator.GetGeneratorType() == generatorType);

            var expectedSources = Sources(freshResult);
            var actualSources = Sources(incrementalResult);

            Assert.True(
                expectedSources.Keys.SequenceEqual(actualSources.Keys),
                $"{what}: {generatorType.Name} generated files differ from a fresh run.\n" +
                $"  fresh:       [{string.Join(", ", expectedSources.Keys)}]\n" +
                $"  incremental: [{string.Join(", ", actualSources.Keys)}]");

            foreach (var (hint, expected) in expectedSources)
                Assert.True(
                    expected == actualSources[hint],
                    $"{what}: {generatorType.Name} '{hint}' differs from a fresh run.\n" +
                    $"--- fresh ---\n{expected}\n--- incremental ---\n{actualSources[hint]}");

            var expectedDiagnostics = Describe(freshResult.Diagnostics);
            var actualDiagnostics = Describe(incrementalResult.Diagnostics);
            Assert.True(
                expectedDiagnostics.SequenceEqual(actualDiagnostics),
                $"{what}: {generatorType.Name} diagnostics differ from a fresh run.\n" +
                $"  fresh:       [{string.Join("; ", expectedDiagnostics)}]\n" +
                $"  incremental: [{string.Join("; ", actualDiagnostics)}]");

            AssertLocatedInCompilation(incrementalResult.Diagnostics, compilation, $"{what}: {generatorType.Name}");
        }
    }

    public static void AssertLocatedInCompilation(IEnumerable<Diagnostic> diagnostics, Compilation compilation, string what)
    {
        foreach (var diagnostic in diagnostics.Where(d => d.Location.IsInSource))
            Assert.True(
                compilation.SyntaxTrees.Any(t => ReferenceEquals(t, diagnostic.Location.SourceTree)),
                $"{what}: {diagnostic.Id} points into a syntax tree ('{diagnostic.Location.SourceTree!.FilePath}') " +
                "that is not part of the compilation it was reported for.");
    }

    public static IReadOnlyList<string> Describe(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics
            .Select(d =>
            {
                var line = d.Location.GetLineSpan();
                return $"{d.Id} {d.Severity} {line.Path}{d.Location.SourceSpan} " +
                       $"({line.StartLinePosition.Line + 1},{line.StartLinePosition.Character + 1}) {d.GetMessage()}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyDictionary<string, string> Sources(GeneratorRunResult result) =>
        result.GeneratedSources
            .OrderBy(s => s.HintName, StringComparer.Ordinal)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

    public static string AllGeneratedText(GeneratorDriverRunResult run) =>
        string.Join("\n", run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

    public static IReadOnlyList<string> DecoratorChain(string generated, string serviceFqn)
    {
        var svc = Regex.Escape(serviceFqn);

        var resolvers = Regex.Matches(
            generated,
            $@"\.Add(?:Scoped|Singleton|Transient)<{svc}>\(sp =>\s*sp\.GetRequiredKeyedService<{svc}>\(""(?<key>[^""]+)""\)\);");
        Assert.True(resolvers.Count == 1, $"Expected exactly one decorated resolver for {serviceFqn}, found {resolvers.Count}.\n{generated}");

        var chain = new List<string>();
        var key = resolvers[0].Groups["key"].Value;

        while (key != "__BASE__")
        {
            Assert.True(chain.Count < 32, $"Decorator chain for {serviceFqn} does not terminate.");
            chain.Add(key);

            var layers = Regex.Matches(
                generated,
                $@"\.AddKeyed(?:Scoped|Singleton|Transient)<{svc}>\(\s*""{Regex.Escape(key)}"",\s*\(sp, _\) => new global::{Regex.Escape(key)}\((?<args>[^;]*)\);");
            Assert.True(layers.Count == 1, $"Expected exactly one keyed layer '{key}' for {serviceFqn}, found {layers.Count}.\n{generated}");

            var inner = Regex.Match(layers[0].Groups["args"].Value, $@"sp\.GetRequiredKeyedService<{svc}>\(""(?<key>[^""]+)""\)");
            Assert.True(inner.Success, $"Layer '{key}' of {serviceFqn} does not receive the inner layer.\n{generated}");
            key = inner.Groups["key"].Value;
        }

        Assert.Matches($@"\.AddKeyed(?:Scoped|Singleton|Transient)<{svc}, [^>]+(?:<[^>]*>)?>\(""__BASE__""\);", generated);
        return chain;
    }
}
