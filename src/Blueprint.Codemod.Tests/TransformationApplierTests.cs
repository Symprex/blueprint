using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.Codemod.Tests;

public sealed class TransformationApplierTests
{
    [Xunit.Fact]
    public void When_a_supported_code_fix_returns_an_unchanged_solution_then_apply_mode_throws_instead_of_looping()
    {
        var projectId = ProjectId.CreateNewId("TransformationApplierTests");
        var documentId = DocumentId.CreateNewId(projectId, "Guard.cs");
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TransformationApplierTests", "TransformationApplierTests", LanguageNames.CSharp)
            .AddDocument(documentId, "Guard.cs", SourceText.From("public sealed class Guard { }"));
        var guardMethod = typeof(TransformationApplier).GetMethod(
            "EnsureCodeFixMadeProgress",
            BindingFlags.NonPublic | BindingFlags.Static);

        guardMethod.Should().NotBeNull();

        var act = () => guardMethod!.Invoke(null, [solution, solution, "BPMIG001"]);

        var exception = act.Should().Throw<TargetInvocationException>().Which.InnerException;
        exception.Should().BeOfType<InvalidOperationException>();
        exception!.Message.Should().Contain("BPMIG001").And.Contain("did not change the solution");
    }
}
