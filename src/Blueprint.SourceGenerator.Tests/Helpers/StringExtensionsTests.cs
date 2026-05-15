using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Tests.Helpers;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("ID", "id")]
    [InlineData("TenantId", "tenantId")]
    [InlineData("URL", "url")]
    [InlineData("HTMLContent", "htmlContent")]
    [InlineData("IPAddress", "ipAddress")]
    [InlineData("MyURL", "myURL")]
    [InlineData("ABCDef", "abcDef")]
    [InlineData("id", "id")]
    [InlineData("url", "url")]
    [InlineData("", "")]
    [InlineData("A", "a")]
    [InlineData("AB", "ab")]
    [InlineData("Name", "name")]
    public void Camelize_matches_System_Text_Json_CamelCase_policy(string input, string expected)
    {
        var result = input.Camelize();

        Assert.Equal(expected, result);
    }
}
