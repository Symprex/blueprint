using Xunit;

namespace Blueprint.SourceGenerator.CodeWriter.Tests;

/// <summary>
/// Tests for low-level <see cref="CodeWriter"/> rendering and extension helpers.
/// </summary>
public class CodeWriterTests
{
    private static readonly string NewLine = Environment.NewLine;

    /// <summary>
    /// Verifies that appended text is emitted without additional formatting.
    /// </summary>
    [Fact]
    public void GenerateCodeWriter()
    {
        var sut = new CodeWriter(IndentStyle.Tabs);

        sut.Append("test");

        Assert.Equal("test", sut.Render());
    }

    /// <summary>
    /// Verifies that nested block writers render balanced braces and indentation.
    /// </summary>
    [Fact]
    public void GenerateCodeSubBlock()
    {
        var sut = new CodeWriter(IndentStyle.Tabs);

        var ifBlock = sut.BlockWriter("if (true)");
        var elseBlock = sut.BlockWriter("else");

        ifBlock.AppendLine("CrashTheSystem();");
        elseBlock.AppendLine("DontCrashTheSystem();");

        var expected = "if (true)" + NewLine +
                       "{" + NewLine +
                       "\tCrashTheSystem();" + NewLine +
                       "}" + NewLine +
                       "else" + NewLine +
                       "{" + NewLine +
                       "\tDontCrashTheSystem();" + NewLine +
                       "}" + NewLine;
        var r = sut.Render();
        Assert.Equal(expected, r);
    }

    /// <summary>
    /// Verifies fluent <c>if/else</c> helpers render the expected conditional blocks.
    /// </summary>
    [Fact]
    public void GenerateIfElse()
    {
        var writer = new CodeWriter(IndentStyle.Tabs);

        writer.If("true")
            .WithBody(w => w.AppendLine("CrashTheSystem();"))
            .Else()
            .WithBody(w => w.AppendLine("DontCrashTheSystem();"))
            .EndIf();

        var expected = "if (true)" + NewLine +
                       "{" + NewLine +
                       "\tCrashTheSystem();" + NewLine +
                       "}" + NewLine +
                       "else" + NewLine +
                       "{" + NewLine +
                       "\tDontCrashTheSystem();" + NewLine +
                       "}" + NewLine;
        var r = writer.Render();
        Assert.Equal(expected, r);
    }

    /// <summary>
    /// Verifies callback-based block helpers render statements in the expected scope.
    /// </summary>
    [Fact]
    public void GenerateBlockWithCallback()
    {
        var writer = new CodeWriter(IndentStyle.Tabs);

        writer.Block(
            "try",
            w =>
            {
                w.DeclareVar("value", "42");
                w.Return("value");
            });

        writer.Catch(
            "System.Exception ex",
            w =>
            {
                w.Assign("_lastError", "ex");
            });

        var expected = "try" + NewLine +
                       "{" + NewLine +
                       "\tvar value = 42;" + NewLine +
                       "\treturn value;" + NewLine +
                       "}" + NewLine +
                       "catch (System.Exception ex)" + NewLine +
                       "{" + NewLine +
                       "\t_lastError = ex;" + NewLine +
                       "}" + NewLine;

        var rendered = writer.Render();
        Assert.Equal(expected, rendered);
    }

    /// <summary>
    /// Verifies that the switch statement builder renders classic switch/case/break.
    /// </summary>
    [Fact]
    public void SwitchStatement_renders_classic_cases()
    {
        var writer = new CodeWriter(IndentStyle.Tabs);

        writer.Switch("status")
            .AddCase("\"active\"").WithContent(w => w.AppendLine("HandleActive();"))
            .AddCase("\"inactive\"").WithContent(w => w.AppendLine("HandleInactive();"))
            .Close();

        var expected = "switch(status)" + NewLine +
                       "{" + NewLine +
                       "\tcase \"active\":" + NewLine +
                       "\t\tHandleActive();" + NewLine +
                       "\t\tbreak;" + NewLine +
                       "\tcase \"inactive\":" + NewLine +
                       "\t\tHandleInactive();" + NewLine +
                       "\t\tbreak;" + NewLine +
                       "}" + NewLine;

        Assert.Equal(expected, writer.Render());
    }

    /// <summary>
    /// Verifies that the switch expression builder renders C# 8+ switch expression arms.
    /// </summary>
    [Fact]
    public void SwitchExpression_renders_expression_arms()
    {
        var writer = new CodeWriter(IndentStyle.Tabs);

        writer.SwitchExpression("status")
            .AddCase("\"active\"").WithExpression("1")
            .AddCase("\"inactive\"").WithExpression("2")
            .AddCase("_").WithExpression("0")
            .Close();

        var expected = "status switch" + NewLine +
                       "{" + NewLine +
                       "\t\"active\" => 1," + NewLine +
                       "\t\"inactive\" => 2," + NewLine +
                       "\t_ => 0" + NewLine +
                       "}" + NewLine;

        Assert.Equal(expected, writer.Render());
    }
}
