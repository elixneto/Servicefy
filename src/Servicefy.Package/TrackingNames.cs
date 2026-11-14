namespace Servicefy.Package;

/// <summary>
/// Names given (via <c>WithTrackingName</c>) to the incremental pipeline steps of
/// <see cref="ByConfiguration.Generators.ServicefyByConfigurationGenerator"/> and
/// <see cref="Conventions.Generators.ServicefyConventionsGenerator"/>.
/// <para>
/// They have no effect on generated code. They exist so a <c>GeneratorDriver</c> created with
/// <c>trackIncrementalGeneratorSteps: true</c> can report, per step, whether a run was
/// <c>Cached</c>, <c>Unchanged</c>, <c>Modified</c> or <c>New</c> — the baseline used to measure
/// incremental-build performance (issue #4) before and after the caching work in #5 / #6.
/// </para>
/// </summary>
internal static class TrackingNames
{
    // ── ByConfiguration generator ────────────────────────────────────────────────────────────

    /// <summary>Compilation fed to the attribute-source emitter (<c>AttributeEmitter</c>).</summary>
    public const string ByConfigurationAttributesInput = "ByConfiguration.Attributes.Input";

    /// <summary>Class declarations carrying at least one attribute list (syntax filter).</summary>
    public const string ByConfigurationAnnotatedClasses = "ByConfiguration.AnnotatedClasses";

    /// <summary>Compilation + collected annotated classes fed to <c>DiagnosticsByConfigurationEmitter</c>.</summary>
    public const string ByConfigurationDiagnosticsInput = "ByConfiguration.Diagnostics.Input";

    /// <summary>Compilation fed to <c>RegistrationByConfigurationEmitter</c>.</summary>
    public const string ByConfigurationRegistrationsInput = "ByConfiguration.Registrations.Input";

    // ── Conventions generator ────────────────────────────────────────────────────────────────

    /// <summary>Compilation fed to <c>ConventionsBuilderEmitter</c> (runtime API source).</summary>
    public const string ConventionsBuilderInput = "Conventions.Builder.Input";

    /// <summary><c>.ByNamespace(...)</c> call sites: parsed rule and/or diagnostic per call.</summary>
    public const string ByNamespaceResults = "Conventions.ByNamespace.Results";

    /// <summary><c>.ByNamespace(...)</c> rules that parsed successfully.</summary>
    public const string ByNamespaceRules = "Conventions.ByNamespace.Rules";

    /// <summary>Compilation + collected <c>.ByNamespace(...)</c> rules fed to the registration emitter.</summary>
    public const string ByNamespaceEmitInput = "Conventions.ByNamespace.EmitInput";

    /// <summary><c>.ByBaseType(...)</c> call sites: parsed rule and/or diagnostic per call.</summary>
    public const string ByBaseTypeResults = "Conventions.ByBaseType.Results";

    /// <summary><c>.ByBaseType(...)</c> rules that parsed successfully.</summary>
    public const string ByBaseTypeRules = "Conventions.ByBaseType.Rules";

    /// <summary>Compilation + collected <c>.ByBaseType(...)</c> rules fed to the registration emitter.</summary>
    public const string ByBaseTypeEmitInput = "Conventions.ByBaseType.EmitInput";

    /// <summary><c>.ByNamespaceOf&lt;TMarker&gt;(...)</c> call sites: parsed rule and/or diagnostic per call.</summary>
    public const string ByNamespaceOfResults = "Conventions.ByNamespaceOf.Results";

    /// <summary><c>.ByNamespaceOf&lt;TMarker&gt;(...)</c> rules that parsed successfully.</summary>
    public const string ByNamespaceOfRules = "Conventions.ByNamespaceOf.Rules";

    /// <summary>Compilation + collected <c>.ByNamespaceOf&lt;TMarker&gt;(...)</c> rules fed to the registration emitter.</summary>
    public const string ByNamespaceOfEmitInput = "Conventions.ByNamespaceOf.EmitInput";

    /// <summary><c>.ByTypeName(...)</c> call sites: parsed rule and/or diagnostic per call.</summary>
    public const string ByTypeNameResults = "Conventions.ByTypeName.Results";

    /// <summary><c>.ByTypeName(...)</c> rules that parsed successfully.</summary>
    public const string ByTypeNameRules = "Conventions.ByTypeName.Rules";

    /// <summary>Compilation + collected <c>.ByTypeName(...)</c> rules fed to the registration emitter.</summary>
    public const string ByTypeNameEmitInput = "Conventions.ByTypeName.EmitInput";
}
