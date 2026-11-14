using System.Text.RegularExpressions;

namespace Servicefy.Tests.Helpers;

internal sealed record DispatchBranch(string Lifetime, string Scope, string Predicate, IReadOnlyList<string> Lines)
{
    public IReadOnlyList<string> Registrations =>
        Lines.Where(l => l.StartsWith("Services.Add", StringComparison.Ordinal))
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
}

internal static partial class ConventionDispatch
{
    public const string ByNamespaceFile = "ServicefyConventionsBuilder.ByNamespace.g.cs";
    public const string ByTypeNameFile = "ServicefyConventionsBuilder.ByTypeName.g.cs";

    [GeneratedRegex("""^\s*if \(lifetime == Lifetime\.(?<lifetime>\w+) && scope == ConventionScope\.(?<scope>\w+) && predicateExpression == "(?<predicate>(?:[^"\\]|\\.)*)"\)\s*$""")]
    private static partial Regex HeaderRegex();

    public static IReadOnlyList<DispatchBranch> Branches(string? generated)
    {
        var branches = new List<DispatchBranch>();
        if (generated is null) return branches;

        var lines = generated.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var header = HeaderRegex().Match(lines[i]);
            if (!header.Success) continue;

            var body = new List<string>();
            for (i++; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line == "return;") break;
                if (line is "" or "{" or "}") continue;
                body.Add(line);
            }

            branches.Add(new DispatchBranch(
                header.Groups["lifetime"].Value,
                header.Groups["scope"].Value,
                Unescape(header.Groups["predicate"].Value),
                body));
        }

        return branches;
    }

    public static DispatchBranch? Branch(string? generated, string lifetime, string scope, string predicate) =>
        Branches(generated).SingleOrDefault(b => b.Lifetime == lifetime && b.Scope == scope && b.Predicate == predicate);

    private static string Unescape(string value) =>
        Regex.Replace(value, @"\\(.)", m => m.Groups[1].Value switch
        {
            "r" => "\r",
            "n" => "\n",
            var c => c,
        });
}
