using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.App.Model;

/// <summary>
/// The compilation model for the <see cref="AppDataModelSourceGenerator"/>. This represents a single
/// instance of a BlueprintAppDataModel that needs to be populated with API operations, along with all found
/// operations.
/// </summary>
/// <param name="Class">Details of the class itself.</param>
/// <param name="Operations">All "registrations", representing BlueprintAppScan attribute definitions.</param>
/// <param name="LinkCheckerTypes">Discovered StateBasedLinkChecker subclasses with their checker and resource type names.</param>
/// <param name="ApiAuthoriserTypes">Discovered IApiAuthoriser implementations (fully-qualified type names).</param>
/// <param name="ResourceExpanderTypes">Discovered IResourceExpander implementations (fully-qualified type names).</param>
/// <param name="HasServiceBuilder">Whether the compilation references BlueprintServiceBuilder (i.e., Blueprint.Runtime).</param>
/// <param name="Diagnostics">App-level diagnostics discovered while validating the declared serializer context against the scanned operations.</param>
internal record BlueprintAppDataModelCompilationModel(
    ParseOnlyBlueprintAppClassModel Class,
    EquatableList<string> Operations,
    EquatableList<LinkCheckerRegistrationInfo> LinkCheckerTypes,
    EquatableList<string> ApiAuthoriserTypes,
    EquatableList<string> ResourceExpanderTypes,
    bool HasServiceBuilder,
    EquatableList<DiagnosticInfo> Diagnostics);

/// <summary>
/// Captures a discovered <c>StateBasedLinkChecker&lt;TOperation, TResource&gt;</c> implementation
/// with the fully-qualified names needed for DI registration and resource descriptor wiring.
/// </summary>
/// <param name="CheckerTypeName">Fully-qualified name of the checker class.</param>
/// <param name="ResourceTypeName">Fully-qualified name of TResource (second type argument).</param>
internal record LinkCheckerRegistrationInfo(string CheckerTypeName, string ResourceTypeName);
