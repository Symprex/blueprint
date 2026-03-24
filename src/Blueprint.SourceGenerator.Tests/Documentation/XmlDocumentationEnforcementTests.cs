using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Tests.Documentation;

public sealed class XmlDocumentationEnforcementTests
{
    [Fact]
    public void When_reading_documentation_configuration_then_non_test_code_is_enforced_and_actual_tests_are_exempt()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var styleCopConfiguration = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "src", "Stylecop.json")));
        var documentationRules = styleCopConfiguration.RootElement
            .GetProperty("settings")
            .GetProperty("documentationRules");

        documentationRules.GetProperty("documentExposedElements").GetBoolean().Should().BeTrue("public and protected non-test code should explain why it exists and how callers should use it");
        documentationRules.GetProperty("documentInterfaces").GetBoolean().Should().BeTrue("contract seams are part of the supported Blueprint surface");
        documentationRules.GetProperty("documentInternalElements").GetBoolean().Should().BeTrue("internal generator and runtime seams also need rationale-focused documentation");
        documentationRules.GetProperty("documentPrivateElements").GetBoolean().Should().BeFalse("private implementation details are intentionally out of scope for repo-wide XML documentation enforcement");

        var editorConfig = File.ReadAllText(Path.Combine(repositoryRoot, "src", ".editorconfig"));
        var directoryBuildProps = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Directory.Build.props"));

        editorConfig.Should().Contain("dotnet_diagnostic.SA1600.severity = warning");
        editorConfig.Should().Contain("dotnet_diagnostic.SA1601.severity = none", "actual test partial declarations should stay exempt from XML-doc requirements just like their containing test files");
        editorConfig.Should().Contain("dotnet_diagnostic.SA1602.severity = warning");
        editorConfig.Should().Contain("[**/*Tests.cs]", "actual test files should stay exempt when useful test names already describe the behaviour");
        editorConfig.Should().Contain("[**/*Tests.*.cs]", "split partial test files should stay exempt too, otherwise large test fixtures start demanding boilerplate XML docs");
        editorConfig.Should().Contain("[**/*Test.cs]", "single-test files should receive the same exemption as plural test files");
        editorConfig.Should().Contain("[**/*Test.*.cs]", "split single-test files should receive the same exemption as other test declarations");
        directoryBuildProps.Should().Contain("<TreatWarningsAsErrors Condition=\"'$(IsTestProject)' != 'true' and '$(TreatWarningsAsErrors)' == ''\">true</TreatWarningsAsErrors>", "non-test projects should fail the build when documentation analyzers fire");
        File.ReadAllText(Path.Combine(repositoryRoot, "src", "Blueprint.SourceGenerator.Tests", "Blueprint.SourceGenerator.Tests.csproj"))
            .Should().Contain("<IsTestProject>true</IsTestProject>", "the main unit-test project should opt out of non-test warnings-as-errors enforcement");
        File.ReadAllText(Path.Combine(repositoryRoot, "src", "Blueprint.SourceGenerator.Tests.Integration", "Blueprint.SourceGenerator.Tests.Integration.csproj"))
            .Should().Contain("<IsTestProject>true</IsTestProject>", "the integration-test project should opt out of non-test warnings-as-errors enforcement");
    }

    [Theory]
    [InlineData(@"src\Blueprint.SourceGenerator.Tests\TestHelpers.cs")]
    [InlineData(@"src\Blueprint.SourceGenerator.Benchmarks\Scale\SyntheticBlueprintFixtureGenerator.cs")]
    [InlineData(@"src\Blueprint.SourceGenerator.CodeWriter.Tests\Tests\TestBase.cs")]
    public void When_reading_test_support_files_then_exposed_members_are_documented(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var filePath = Path.Combine([repositoryRoot, .. relativePath.Split('\\')]);
        var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath));
        var root = syntaxTree.GetRoot();

        var undocumentedDeclarations = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Select(TryCreateRequirement)
            .Where(static requirement => requirement is not null)
            .Select(static requirement => requirement!)
            .Where(static requirement => !HasXmlDocumentation(requirement.Declaration))
            .Select(static requirement => requirement.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        undocumentedDeclarations.Should().BeEmpty($"{relativePath} is reusable test infrastructure that should explain the rationale behind its public or protected surface");
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "Blueprint.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from the current test execution directory.");
    }

    private static bool HasXmlDocumentation(MemberDeclarationSyntax declaration)
    {
        return declaration.GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .Any();
    }

    private static DocumentationRequirement? TryCreateRequirement(MemberDeclarationSyntax declaration)
    {
        return declaration switch
        {
            ClassDeclarationSyntax classDeclaration when HasRequiredAccessibility(classDeclaration.Modifiers) => new DocumentationRequirement(classDeclaration, $"type {classDeclaration.Identifier.Text}"),
            RecordDeclarationSyntax recordDeclaration when HasRequiredAccessibility(recordDeclaration.Modifiers) => new DocumentationRequirement(recordDeclaration, $"type {recordDeclaration.Identifier.Text}"),
            InterfaceDeclarationSyntax interfaceDeclaration when HasRequiredAccessibility(interfaceDeclaration.Modifiers) => new DocumentationRequirement(interfaceDeclaration, $"type {interfaceDeclaration.Identifier.Text}"),
            StructDeclarationSyntax structDeclaration when HasRequiredAccessibility(structDeclaration.Modifiers) => new DocumentationRequirement(structDeclaration, $"type {structDeclaration.Identifier.Text}"),
            EnumDeclarationSyntax enumDeclaration when HasRequiredAccessibility(enumDeclaration.Modifiers) => new DocumentationRequirement(enumDeclaration, $"type {enumDeclaration.Identifier.Text}"),
            ConstructorDeclarationSyntax constructorDeclaration when HasRequiredAccessibility(constructorDeclaration.Modifiers) => new DocumentationRequirement(constructorDeclaration, $"constructor {constructorDeclaration.Identifier.Text}"),
            MethodDeclarationSyntax methodDeclaration when HasRequiredAccessibility(methodDeclaration.Modifiers) => new DocumentationRequirement(methodDeclaration, $"method {methodDeclaration.Identifier.Text}"),
            PropertyDeclarationSyntax propertyDeclaration when HasRequiredAccessibility(propertyDeclaration.Modifiers) => new DocumentationRequirement(propertyDeclaration, $"property {propertyDeclaration.Identifier.Text}"),
            _ => null,
        };
    }

    private static bool HasRequiredAccessibility(SyntaxTokenList modifiers)
    {
        return modifiers.Any(static modifier =>
            modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword) ||
            modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InternalKeyword) ||
            modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ProtectedKeyword));
    }

    private sealed record DocumentationRequirement(MemberDeclarationSyntax Declaration, string DisplayName);
}
