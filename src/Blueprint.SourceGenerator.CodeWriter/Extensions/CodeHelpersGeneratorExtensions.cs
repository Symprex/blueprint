using System.Linq;
using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Represents code helpers generator extensions.
/// </summary>
public static class CodeHelpersGeneratorExtensions
{
    /// <summary>
    /// This will add the source using the Fully Qualified Type name and will apply basic formatting based on the
    /// environment. If the Specified Language Version is 10+ it will automatically enable Top Level Namespaces. It
    /// will additionally attempt to normalize the white space based upon the user's preferences
    /// </summary>
    /// <param name="context">The <see cref="GeneratorExecutionContext"/></param>
    /// <param name="builder">The <see cref="ClassBuilder"/></param>
    public static void AddSource(this GeneratorExecutionContext context, CodeBuilder builder)
    {
        if (string.IsNullOrEmpty(builder.Namespace) || builder.Namespace is null)
            return;

        if(builder.Classes.Count == 1 && builder.Enums.Count == 0 && builder.Records.Count == 0)
        {
            context.AddSource(builder.Classes.First());
            return;
        }
        else if (builder.Classes.Count == 0 && builder.Enums.Count == 1 && builder.Records.Count == 0)
        {
            context.AddSource(builder.Enums.First());
            return;
        }
        else if (builder.Classes.Count == 0 && builder.Enums.Count == 0 && builder.Records.Count == 1)
        {
            context.AddSource(builder.Records.First());
            return;
        }

        foreach (var @class in builder.Classes)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddClass(@class));
        }

        foreach (var @enum in builder.Enums)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddEnum(@enum));
        }

        foreach (var @record in builder.Records)
        {
            context.AddSource(CodeBuilder.Create(builder.Namespace)
                .AddRecord(@record));
        }
    }

    /// <summary>
    /// This will add the source using the Fully Qualified Type name and will apply basic formatting based on the
    /// environment. If the Specified Language Version is 10+ it will automatically enable Top Level Namespaces. It
    /// will additionally attempt to normalize the white space based upon the user's preferences
    /// </summary>
    /// <param name="context">The <see cref="GeneratorExecutionContext"/></param>
    /// <param name="builder">The <see cref="ClassBuilder"/></param>
    public static void AddSource(this GeneratorExecutionContext context, ClassBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// This will add the source using the Fully Qualified Type name and will apply basic formatting based on the
    /// environment. If the Specified Language Version is 10+ it will automatically enable Top Level Namespaces. It
    /// will additionally attempt to normalize the white space based upon the user's preferences
    /// </summary>
    /// <param name="context">The <see cref="GeneratorExecutionContext"/></param>
    /// <param name="builder">The <see cref="EnumBuilder"/></param>
    public static void AddSource(this GeneratorExecutionContext context, EnumBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// This will add the source using the Fully Qualified Type name and will apply basic formatting based on the
    /// environment. If the Specified Language Version is 10+ it will automatically enable Top Level Namespaces. It
    /// will additionally attempt to normalize the white space based upon the user's preferences
    /// </summary>
    /// <param name="context">The <see cref="GeneratorExecutionContext"/></param>
    /// <param name="builder">The <see cref="RecordBuilder"/></param>
    public static void AddSource(this GeneratorExecutionContext context, RecordBuilder builder)
    {
        var source = SourceText(builder.Builder, context.ParseOptions);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Adds source.
    /// </summary>
    /// <param name="context">The context value.</param>
    /// <param name="builder">The builder value.</param>
    public static void AddSource(this SourceProductionContext context, ClassBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Adds source.
    /// </summary>
    /// <param name="context">The context value.</param>
    /// <param name="builder">The builder value.</param>
    public static void AddSource(this SourceProductionContext context, EnumBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Adds source.
    /// </summary>
    /// <param name="context">The context value.</param>
    /// <param name="builder">The builder value.</param>
    public static void AddSource(this SourceProductionContext context, RecordBuilder builder)
    {
        var source = SourceText(builder.Builder, null);
        context.AddSource($"{builder.FullyQualifiedName}.g.cs", source);
    }

    /// <summary>
    /// Builds build.
    /// </summary>
    /// <param name="builder">The builder value.</param>
    /// <param name="options">The options value.</param>
    /// <returns>The generated result.</returns>
    public static SourceText Build(this CodeBuilder builder, ParseOptions options)
    {
        return SourceText(builder, options);
    }

    /// <summary>
    /// Builds build.
    /// </summary>
    /// <param name="builder">The builder value.</param>
    /// <param name="options">The options value.</param>
    /// <returns>The generated result.</returns>
    public static SourceText Build(this ClassBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    /// <summary>
    /// Builds build.
    /// </summary>
    /// <param name="builder">The builder value.</param>
    /// <param name="options">The options value.</param>
    /// <returns>The generated result.</returns>
    public static SourceText Build(this EnumBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    /// <summary>
    /// Builds build.
    /// </summary>
    /// <param name="builder">The builder value.</param>
    /// <param name="options">The options value.</param>
    /// <returns>The generated result.</returns>
    public static SourceText Build(this RecordBuilder builder, ParseOptions options)
    {
        return SourceText(builder.Builder, options);
    }

    private static SourceText SourceText(CodeBuilder builder, ParseOptions? options)
    {
        if (options is CSharpParseOptions parseOptions)
        {
            ConfigureFromOptions(builder, parseOptions);

            var source = builder.Build();
            var syntaxTree = CSharpSyntaxTree.ParseText(Text.SourceText.From(source, Encoding.UTF8), parseOptions);
            var formattedRoot = (CSharpSyntaxNode)syntaxTree.GetRoot().NormalizeWhitespace();
            return Text.SourceText.From(CSharpSyntaxTree.Create(formattedRoot).ToString(), Encoding.UTF8);
        }

        return Text.SourceText.From(builder.Build(), Encoding.UTF8);
    }

    private static void ConfigureFromOptions(CodeBuilder builder, CSharpParseOptions options)
    {
        if (options.SpecifiedLanguageVersion >= LanguageVersion.CSharp10)
        {
            builder.TopLevelNamespace();
        }
    }
}
