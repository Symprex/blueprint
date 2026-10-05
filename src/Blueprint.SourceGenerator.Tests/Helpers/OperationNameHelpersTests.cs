using Blueprint.SourceGenerator.Helpers;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Helpers;

/// <summary>
/// Pins the descriptor name the generator bakes for an operation type to legacy Blueprint's
/// <c>apiOperationType.Name.Replace("Query", string.Empty).Replace("Command", string.Empty).ToPascalCase()</c>
/// (S365 <c>8b40ecc961</c>, <c>backend/src/Blueprint/Blueprint/ApiOperationDescriptor.cs:50</c>), and its casing rule to
/// legacy <c>StringExtensions.ToPascalCase</c> (<c>Blueprint/Utilities/StringExtensions.cs:65-120</c>).
/// </summary>
public class OperationNameHelpersTests
{
    // Every case legacy's own suite pinned, verbatim (S365 8b40ecc961,
    // backend/src/Blueprint/tests/Blueprint.Tests/Utilities/StringExtensions_Tests/Given_StringUtilities.cs:10-23).
    [Theory]
    [InlineData("imAString", "ImAString")]
    [InlineData("imAlsoString", "ImAlsoString")]
    [InlineData("ImAlsoString", "ImAlsoString")]
    [InlineData("im_a_string", "ImAString")]
    [InlineData("im a string", "ImAString")]
    [InlineData("ABCAcronym", "AbcAcronym")]
    [InlineData("im_a_ABCAcronym", "ImAAbcAcronym")]
    [InlineData("im a ABCAcronym", "ImAAbcAcronym")]
    [InlineData("8ball", "8Ball")]
    [InlineData("im a 8ball", "ImA8Ball")]
    [InlineData("IM_ALL_CAPS", "ImAllCaps")]
    [InlineData("IM ALSO ALL CAPS", "ImAlsoAllCaps")]
    [InlineData("i-have-dashes", "IHaveDashes")]
    [InlineData("a8word_another_word", "A8WordAnotherWord")]
    public void When_legacy_suite_case_is_cased_then_matches_legacy(string input, string expected)
    {
        OperationNameHelpers.ToLegacyPascalCase(input).Should().Be(expected);
    }

    // Worked by hand through legacy StringExtensions.cs:100-117: after the first character a capital is kept only when
    // the next INPUT character is lower-case, or when it is A or I and the previous INPUT character is lower-case; every
    // other capital is lower-cased, and a digit upper-cases the character after it.
    // - GetVCardConfiguration: V's next 'C' is not lower and V is not A/I -> 'v'.
    // - GetInvoicesV2: V's next '2' is not lower -> 'v'.
    // - GetItemsETag: E's next 'T' is not lower -> 'e'.
    // - GetUserID: I is kept after lower-case 'r'; D's previous 'I' is not lower -> 'd'.
    // - GetAIModel: A is kept after lower-case 't'; I's previous 'A' is not lower -> 'i'.
    // - GetXAFoo: A's previous INPUT character 'X' is not lower (the output's 'x' would be) -> 'a'.
    // - Get2faStatus: the digit upper-cases 'f'.
    // - GetHTMLPreview: H's next 'T' is not lower and H is not A/I, so it is lowered too, as are T, M and L -> "html".
    [Theory]
    [InlineData("GetVCardConfiguration", "GetvCardConfiguration")]
    [InlineData("GetInvoicesV2", "GetInvoicesv2")]
    [InlineData("GetItemsETag", "GetItemseTag")]
    [InlineData("GetUserID", "GetUserId")]
    [InlineData("GetAIModel", "GetAiModel")]
    [InlineData("GetXAFoo", "GetxaFoo")]
    [InlineData("Get2faStatus", "Get2FaStatus")]
    [InlineData("GetHTMLPreview", "GethtmlPreview")]
    [InlineData("GetSentEmail", "GetSentEmail")]
    [InlineData("", "")]
    public void When_operation_name_is_cased_then_matches_legacy_rule(string input, string expected)
    {
        OperationNameHelpers.ToLegacyPascalCase(input).Should().Be(expected);
    }

    // Legacy ApiOperationDescriptor.cs:50: Replace("Query") anywhere, then Replace("Command") anywhere, then ToPascalCase.
    // - GetQueryResultsQuery / ExportCommandSample: replaced anywhere, not only as a suffix.
    // - XComQuerymandY: "Query" goes first, exposing "Command"; the other order leaves "XCommandY" -> "XCommandy".
    // - GetXQueryAB: casing runs after the replacement; casing first gives "GetxQueryAb" and then "GetxAb".
    // - Get_Widget_Query: the casing drops separators, as legacy's did.
    [Theory]
    [InlineData("GetSentEmailQuery", "GetSentEmail")]
    [InlineData("GenerateVCardQuery", "GeneratevCard")]
    [InlineData("GetInvoicesV2Query", "GetInvoicesv2")]
    [InlineData("GetItemsETagQuery", "GetItemseTag")]
    [InlineData("FooBackgroundTask", "FooBackgroundTask")]
    [InlineData("GetQueryResultsQuery", "GetResults")]
    [InlineData("ExportCommandSample", "ExportSample")]
    [InlineData("XComQuerymandY", "Xy")]
    [InlineData("GetXQueryAB", "Getxab")]
    [InlineData("Get_Widget_Query", "GetWidget")]
    public void When_operation_type_is_named_then_descriptor_name_matches_legacy(string typeName, string expected)
    {
        OperationNameHelpers.GetDescriptorName(typeName).Should().Be(expected);
    }

    // The one deliberate departure from legacy: legacy's replacement left a type named only Query, Command or
    // QueryCommand with an empty name, which is no usable OpenAPI operationId. Such a type keeps its own name, cased, as
    // the framework's suffix guard already did for Query and Command.
    [Theory]
    [InlineData("Query", "Query")]
    [InlineData("Command", "Command")]
    [InlineData("QueryCommand", "QueryCommand")]
    public void When_replacement_would_empty_the_name_then_type_name_is_kept(string typeName, string expected)
    {
        OperationNameHelpers.GetDescriptorName(typeName).Should().Be(expected);
    }
}
