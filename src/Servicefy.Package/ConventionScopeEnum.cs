namespace Servicefy.Package;

public static class ConventionScopeEnum
{
    public static string Value(string namespacedName)
        => $$"""
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Which assemblies <c>AddServicefyConventions().ByNamespace(...)</c> /
                 /// <c>.ByTypeName(...)</c> look for candidate types in.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-namespace#scope">ByNamespace scope — Servicefy docs</seealso>
                 internal enum ConventionScope
                 {
                     /// <summary>
                     /// The project being compiled and its <c>&lt;ProjectReference&gt;</c>s. Default.
                     /// </summary>
                     Compilation = 0,

                     /// <summary>
                     /// Also every other referenced assembly, NuGet packages and their transitive
                     /// dependencies included (the .NET SDK's own assemblies are never scanned). Opt-in:
                     /// slower on large dependency graphs, and a broad predicate picks up types from
                     /// packages you did not mean to register.
                     /// </summary>
                     AllReferences
                 }
             }
             """;
}
