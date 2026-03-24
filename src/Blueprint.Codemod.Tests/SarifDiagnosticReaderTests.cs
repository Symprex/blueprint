using System.Text;
using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class SarifDiagnosticReaderTests
{
    [Xunit.Fact]
    public async Task When_reading_valid_sarif_then_returns_sorted_unique_rule_ids()
    {
        const string sarif = """
            {
              "runs": [
                {
                  "results": [
                    { "ruleId": "BPMIG201" },
                    { "ruleId": "BPMIG001" },
                    { "ruleId": "BPMIG201" }
                  ]
                }
              ]
            }
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sarif));

        var diagnosticIds = await SarifDiagnosticReader.ReadDiagnosticIdsAsync(stream, CancellationToken.None);

        diagnosticIds.Should().Equal("BPMIG001", "BPMIG201");
    }

    [Xunit.Fact]
    public async Task When_reading_sarif_without_a_runs_array_then_throws_an_actionable_error()
    {
        const string sarif = """
            {
              "version": "2.1.0"
            }
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sarif));

        var act = async () => await SarifDiagnosticReader.ReadDiagnosticIdsAsync(stream, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*runs*array*");
    }

    [Xunit.Fact]
    public async Task When_reading_sarif_with_a_non_string_ruleid_then_throws_an_actionable_error()
    {
        const string sarif = """
            {
              "runs": [
                {
                  "results": [
                    { "ruleId": 42 }
                  ]
                }
              ]
            }
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sarif));

        var act = async () => await SarifDiagnosticReader.ReadDiagnosticIdsAsync(stream, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ruleId*JSON string*");
    }
}
