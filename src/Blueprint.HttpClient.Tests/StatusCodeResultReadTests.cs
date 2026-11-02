using System.Net;
using System.Reflection;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A body-less success asked for as the base <see cref="StatusCodeResult"/> is the nested singleton of its status, for every
/// nested status result <see cref="StatusCodeResult"/> declares, including one added later.
/// </summary>
public sealed class StatusCodeResultReadTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public StatusCodeResultReadTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    public static TheoryData<string> SuccessStatusCodeResults()
    {
        var data = new TheoryData<string>();

        foreach (var nested in typeof(StatusCodeResult).GetNestedTypes(BindingFlags.Public))
        {
            if (Instance(nested) is { StatusCode: >= 200 and < 300 })
            {
                data.Add(nested.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SuccessStatusCodeResults))]
    public async Task A_bodiless_success_read_as_the_base_type_is_the_nested_singleton_of_its_status(string nestedName)
    {
        var expected = Instance(typeof(StatusCodeResult).GetNestedType(nestedName)!)!;
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)expected.StatusCode)));

        var result = await client.SendAsync<StatusCodeResult>(new ArchiveTemplateCommand { Id = "t-1" });

        result.Should().BeSameAs(expected);
    }

    private static StatusCodeResult? Instance(Type nested)
    {
        return nested.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as StatusCodeResult;
    }
}
