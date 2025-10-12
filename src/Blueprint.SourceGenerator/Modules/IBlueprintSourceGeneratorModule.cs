using Blueprint.SourceGenerator.AppDataModel;
using Blueprint.SourceGenerator.OperationDescription;
using CodeGenHelpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// A module used to extend the functionality of the <see cref="AppDataModelSourceGenerator"/>.
/// </summary>
public interface IBlueprintSourceGeneratorModule
{
    /// <summary>
    /// Allows a module to register any marker attributes or other static code that needs to be added to the
    /// generated source.
    /// </summary>
    /// <param name="initializationContext">The context used to register the source.</param>
    void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext);

    /// <summary>
    /// Configures the given <see cref="OperationConfigurationModel"/>, allowing the module to inspect the
    /// compilation and the given <See cref="INamedTypeSymbol"/> (representing the operation being configured) to introduce
    /// new diagnostics or metadata, used when generating registrations in the <see cref="ConfigureDescriptor" />
    /// method.
    /// </summary>
    /// <param name="registration">The registration to configure.</param>
    void ConfigureModel(OperationConfigurationModel registration);

    /// <summary>
    /// Writes code to configure the ApiOperationDescriptor for the given <see cref="OperationToRegisterModel"/>. Typically
    /// a module will use the <see cref="OperationToRegisterModel.Metadata"/> to find metadata that it has
    /// added in the <see cref="ConfigureModel"/> method to determine what code is necessary to configure the descriptor.
    /// </summary>
    /// <param name="codeWriter">The writer to append code to.</param>
    /// <param name="registration">The registration model to write code for.</param>
    void ConfigureDescriptor(ICodeWriter codeWriter, OperationToRegisterModel registration);
}
