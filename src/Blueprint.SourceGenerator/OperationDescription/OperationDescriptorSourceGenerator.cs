using System.Text;
using Blueprint.SourceGenerator.ApiResources;
using Blueprint.SourceGenerator.AppDataModel;
using Blueprint.SourceGenerator.AppDataModel.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using CodeGenHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.OperationDescription;

[Generator(LanguageNames.CSharp)]
public sealed class OperationDescriptorSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
        id: "BP005",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be partial",
        category: nameof(OperationDescriptorSourceGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var config = new BlueprintSourceGenerationConfig([new LinkGenerationModule()]);

        context.RegisterPostInitializationOutput(i =>
            {
                foreach (var module in config.Modules)
                {
                    module.RegisterMarkerAttributes(i);
                }
            });

        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m != null)
            .Select(static (m, _) => m!)
            .WithTrackingName(Stages.InitialExtraction);

        var combinedProvider = classProvider.Combine(context.CompilationProvider)
            .WithComparer(CombinedProviderComparer<Result<OperationClassToGenerate>>.Instance)
            .WithTrackingName(Stages.CombineWithCompilation);

        var configuredModels = combinedProvider
            .Select((x, _) => Configure(x, config))
            .WithTrackingName(Stages.Configuration);

        context.RegisterSourceOutput(
            configuredModels,
            (spc, result) =>
            {
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } classToGenerate)
                {
                    return;
                }

                spc.AddSource(
                    $"{classToGenerate.TypeName}_Descriptor.g.cs",
                    SourceText.From(GenerateDescriptorClass(classToGenerate, config), Encoding.UTF8));
            });
    }

    private static Result<OperationClassToGenerate>? Transform(GeneratorSyntaxContext ctx)
    {
        const string apiResourceSymbol = "Blueprint.Model.IOperation";

        var classNode = (ClassDeclarationSyntax)ctx.Node;
        var model = ctx.SemanticModel.GetDeclaredSymbol(classNode);

        if (model is null || !model.ImplementsInterface(apiResourceSymbol) || model.IsAbstract)
        {
            return null;
        }

        var isPartial = classNode.Modifiers.Any(SyntaxKind.PartialKeyword);

        if (!isPartial)
        {
            return Result<OperationClassToGenerate>.Failure(
                new DiagnosticInfo(_missingPartialModifierRule, classNode.Identifier.GetLocation(), [model.Name]));
        }

        return Result.Success(
            new OperationClassToGenerate(
                model.ToString(),
                model.ContainingNamespace.ToDisplayString(),
                model.Name));
    }

    private static Result<OperationToRegisterModel> Configure((Result<OperationClassToGenerate> Result, Compilation Compilation) context, BlueprintSourceGenerationConfig config)
    {
        var (result, compilation) = context;

        if (result.Value is null)
        {
            return Result<OperationToRegisterModel>.Failure(result.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(result.Value.FullClassName);

        var operationToRegisterModel = new OperationToRegisterModel(operationImplementationType!.Name, operationImplementationType.ContainingNamespace.ToDisplayString());
        var operationConfigurationModel = new OperationConfigurationModel(operationImplementationType, operationToRegisterModel);

        foreach (var module in config.Modules)
        {
            module.ConfigureModel(operationConfigurationModel);
        }

        var failures = operationConfigurationModel.Diagnostics.ToArray();

        if (failures.Length > 0)
        {
            return Result<OperationToRegisterModel>.Failure(failures);
        }

        return Result.Success(operationToRegisterModel);
    }

    private static string GenerateDescriptorClass(
        OperationToRegisterModel registration,
        BlueprintSourceGenerationConfig config)
    {
        var descriptorClassName = registration.TypeName + "Descriptor";

        var codeBuilder = CodeBuilder.Create(registration.Namespace);

        var wrapperClass = codeBuilder.AddClass(registration.TypeName)
            .WithAccessModifier(Accessibility.Public);

        wrapperClass.AddMethod("Configure")
            .WithAccessModifier(Accessibility.Public)
            .MakeStaticMethod()
            .WithReturnType("ApiOperationDescriptor")
            .WithBody(c =>
                {
                    c.AppendLine($"return new {descriptorClassName}();");
                });

        // Note that we have to hack around CodeGenHelpers a little to get a base call without constructor arguments. We
        // pass in an empty dictionary (as it *immediately* uses that to configure the parameters), and THEN after we
        // populate the dictionary because it uses callbacks to grab those when creating the base call.
        var baseParameters = new Dictionary<string, string>();

        wrapperClass.AddNestedClass(descriptorClassName)
            .WithAccessModifier(Accessibility.Private)
            .SetBaseClass("global::Blueprint.Model.ApiOperationDescriptor")
            .AddConstructor(Accessibility.Public)
            .WithBaseCall(baseParameters)
            .WithBody(b =>
                {
                    foreach (var module in config.Modules)
                    {
                        module.ConfigureDescriptor(b, registration);
                    }
                });

        // The keys do not matter, they are not used when generating the base call
        baseParameters.Add("type", "typeof(" + registration.TypeName + ")");
        baseParameters.Add("name", "\"" + registration.TypeName + "\"");
        baseParameters.Add("where", "\"Source Generated\"");

        return codeBuilder.Build();
    }

    private record OperationClassToGenerate(string FullClassName, string Namespace, string ClassName);

    /// <summary>
    /// The names of the <see cref="ApiResourceSourceGenerator"/> stages, used in particular to test for the
    /// cacheability of the generator.
    /// </summary>
    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
        public const string CombineWithCompilation = nameof(CombineWithCompilation);
        public const string Configuration = nameof(Configuration);
    }
}
