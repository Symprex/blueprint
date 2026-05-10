using Blueprint.SourceGenerator.Planning;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.Validation;

/// <summary>
/// Contributes <c>validation.enabled</c>, <c>validation.mode</c>, and <c>validation.properties</c>
/// to the per-operation debug manifest, reflecting the compile-time validation plan computed by
/// <see cref="ValidationPropertyPlanner"/>.
/// </summary>
/// <remarks>
/// Unlike other manifest modules that read metadata from the descriptor plan, this module
/// independently computes the validation plan from the operation's type symbol because
/// <see cref="ValidationModule"/> stores its plan only in the pipeline phase.
/// </remarks>
internal sealed class ValidationManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ValidationManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ValidationManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var plan = ValidationPropertyPlanner.Create(operationType);

        context.AddEntry("validation.enabled", plan.RequiresValidation.ToString());

        if (!plan.RequiresValidation)
        {
            context.AddEntry("validation.mode", "none");
            return;
        }

        if (plan.RequiresWholeObjectFallback)
        {
            context.AddEntry("validation.mode", "whole-object-fallback");
            return;
        }

        context.AddEntry("validation.mode", plan.ImplementsIValidatableObject ? "per-property+ivalidatable" : "per-property");

        if (plan.Properties.Count == 0)
        {
            return;
        }

        var propertiesArray = new JArray(plan.Properties.Select(static p =>
        {
            var entry = new JObject
            {
                ["name"] = p.MemberName,
            };

            switch (p)
            {
                case FastPathPropertyValidationPlan fastPath:
                    entry["strategy"] = "fast-path";
                    var rules = new JArray();

                    if (fastPath.RequiredRule is not null)
                    {
                        rules.Add("Required");
                    }

                    foreach (var rule in fastPath.OtherRules)
                    {
                        rules.Add(ExtractRuleName(rule));
                    }

                    entry["rules"] = rules;
                    break;

                case RuntimeFallbackPropertyValidationPlan:
                    entry["strategy"] = "runtime-fallback";
                    break;
            }

            return entry;
        }));

        context.AddEntry("validation.properties", propertiesArray);
    }

    /// <summary>
    /// Extracts a human-readable rule name from the generated validation attribute expression.
    /// Falls back to the raw expression when the attribute name cannot be determined.
    /// </summary>
    private static string ExtractRuleName(ValidationFastPathRule rule)
    {
        var expression = rule.ValidationAttributeExpression;

        // Generated expressions follow patterns like "new MaxLengthAttribute(...)" or
        // "new Blueprint.Validation.RequiredIfAttribute(...)". Extract the attribute name.
        var newIndex = expression.IndexOf("new ", StringComparison.Ordinal);
        if (newIndex < 0)
        {
            return expression;
        }

        var start = newIndex + 4;
        var parenIndex = expression.IndexOf('(', start);
        if (parenIndex < 0)
        {
            return expression[start..];
        }

        var fullName = expression[start..parenIndex];

        // Strip "Attribute" suffix and namespace prefix for readability
        var lastDot = fullName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;

        return shortName.EndsWith("Attribute", StringComparison.Ordinal)
            ? shortName[..^"Attribute".Length]
            : shortName;
    }
}
