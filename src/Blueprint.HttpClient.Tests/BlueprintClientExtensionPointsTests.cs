using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves the extension points of a generated typed client: the readonly field its constructor takes, the hand-written method
/// copied onto its interface, its per-operation <c>OnConfigure</c> hook, and the clones <c>WithRequestModifier</c> (which
/// only its hand-written methods call) and <c>WithCredential</c> make. Every step that shapes a request appends its name to
/// one header, so the order they ran in is read off the request the handler received.
/// </summary>
public sealed class BlueprintClientExtensionPointsTests
{
    private static readonly Uri _baseAddress = new("https://default.example.test");
    private static readonly Uri _euAddress = new("https://eu.example.test");

    [Fact]
    public async Task A_request_is_shaped_by_the_caller_then_the_hook_then_each_modifier_in_the_order_added_then_the_credential()
    {
        var (api, sent) = CreateApi();
        var configured = api.WithStep("first").WithStep("second").WithCredential(new StepCredential("only"));

        await configured.SignupTenantAsync(
            new SignupTenantCommand { IdempotencyKey = "key-1" },
            new CallOptions { ModifyRequest = static request => request.Headers.Add(RequestSteps.HeaderName, "caller") });

        var request = sent.Should().ContainSingle().Subject;
        request.Steps.Should().Equal("caller", "hook", "first", "second", "credential:only");
        request.IdempotencyKey.Should().Be("key-1");
    }

    [Fact]
    public async Task The_hook_sees_the_operation_and_its_modifier_runs_in_the_callers_place_when_the_call_gave_none()
    {
        var (api, sent) = CreateApi();

        await api.WithStep("after").SignupTenantAsync(new SignupTenantCommand { IdempotencyKey = "key-2" });

        var request = sent.Should().ContainSingle().Subject;
        request.Steps.Should().Equal("hook", "after");
        request.IdempotencyKey.Should().Be("key-2");
    }

    [Fact]
    public async Task An_operation_with_no_hook_is_sent_with_the_callers_options_untouched()
    {
        var (api, sent) = CreateApi();

        await api.FindTemplateCopiesAsync(
            new FindTemplateCopiesQuery { Id = "t-1" },
            new CallOptions { ModifyRequest = static request => request.Headers.Add(RequestSteps.HeaderName, "caller") });

        sent.Should().ContainSingle().Which.Steps.Should().Equal("caller");
    }

    [Fact]
    public async Task The_credential_wins_over_a_modifier_that_sets_the_same_header()
    {
        var (api, sent) = CreateApi();
        var configured = api
            .WithCredential(new BearerTokenCredential("from-credential"))
            .WithAuthorizationHeader("Bearer from-modifier");

        await configured.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Authorization.Should().Be("Bearer from-credential");
    }

    [Fact]
    public async Task A_second_credential_replaces_the_first_rather_than_adding_to_it()
    {
        var (api, sent) = CreateApi();
        var configured = api.WithCredential(new StepCredential("first")).WithCredential(new StepCredential("second"));

        await configured.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Steps.Should().Equal("credential:second");
    }

    [Fact]
    public async Task A_clone_leaves_the_client_it_was_made_from_as_it_was()
    {
        var (api, sent) = CreateApi();

        _ = api.WithStep("modifier").WithCredential(new StepCredential("clone"));
        await api.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Clones_of_one_client_do_not_see_each_others_additions()
    {
        var (api, sent) = CreateApi();
        var withCredential = api.WithCredential(new StepCredential("a"));
        var withModifier = api.WithStep("b");
        var withBoth = withCredential.WithStep("c");

        await withCredential.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });
        await withModifier.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });
        await withBoth.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Select(static s => s.Steps).Should().SatisfyRespectively(
            first => first.Should().Equal("credential:a"),
            second => second.Should().Equal("b"),
            third => third.Should().Equal("c", "credential:a"));
    }

    [Fact]
    public async Task The_hand_written_method_returns_a_client_that_sends_to_the_region_it_names()
    {
        var (api, sent) = CreateApi();

        await api.ForRegion("eu").FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });
        await api.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Select(static s => s.Host).Should().Equal("eu.example.test", "default.example.test");
        sent[0].PathAndQuery.Should().Be(sent[1].PathAndQuery);
    }

    [Fact]
    public async Task A_region_chosen_after_another_replaces_it_and_the_path_under_the_base_address_is_kept()
    {
        var (api, sent) = CreateApi(new Uri("https://default.example.test/api/"), new Uri("https://eu.example.test/api/"), new Uri("https://us.example.test/api/"));

        // The first modifier makes the request's URI absolute, so the second must swap that URI's host, not resolve it
        // against its own base, which an absolute URI ignores.
        await api.ForRegion("eu").ForRegion("us").FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });
        await api.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Select(static s => s.Host).Should().Equal("us.example.test", "default.example.test");
        sent[0].PathAndQuery.Should().StartWith("/api/").And.Be(sent[1].PathAndQuery);
    }

    [Fact]
    public async Task A_clone_carries_the_readonly_fields_the_hand_written_methods_read()
    {
        var (api, sent) = CreateApi();

        // ForRegion reads the endpoints field, so it only works on a clone that was given the same one.
        var regional = api.WithCredential(new StepCredential("kept")).WithStep("kept").ForRegion("eu");
        await regional.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        var request = sent.Should().ContainSingle().Subject;
        request.Host.Should().Be("eu.example.test");
        request.Steps.Should().Equal("kept", "credential:kept");
    }

    [Fact]
    public void The_hand_written_method_is_copied_onto_the_interface_returning_the_interface()
    {
        var method = typeof(IRegionalOperationsApi).GetMethod("ForRegion");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(IRegionalOperationsApi));
        method.GetParameters().Select(static p => (p.ParameterType, p.Name)).Should().Equal([(typeof(string), "region")]);
    }

    [Fact]
    public void WithRequestModifier_is_for_the_hand_written_methods_and_is_not_on_the_interface()
    {
        typeof(IRegionalOperationsApi).GetMethod("WithRequestModifier").Should().BeNull();

        var method = typeof(RegionalOperationsApi).GetMethod("WithRequestModifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.IsFamily.Should().BeTrue();
    }

    [Fact]
    public void The_constructor_takes_each_readonly_field_as_a_parameter_and_refuses_it_missing()
    {
        var client = new BlueprintHttpClient(new System.Net.Http.HttpClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK))) { BaseAddress = _baseAddress });

        var act = () => new RegionalOperationsApi(client, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("endpoints");
    }

    [Fact]
    public void A_clone_refuses_a_missing_modifier_and_a_missing_credential()
    {
        var (api, _) = CreateApi();

        api.Invoking(static a => a.WithModifier(null!)).Should().Throw<ArgumentNullException>().WithParameterName("modify");
        api.Invoking(static a => a.WithCredential(null!)).Should().Throw<ArgumentNullException>().WithParameterName("credential");
    }

    private static (RegionalOperationsApi Api, List<SentRequest> Sent) CreateApi(Uri? baseAddress = null, Uri? eu = null, Uri? us = null)
    {
        var sent = new List<SentRequest>();
        var handler = new FakeHttpHandler(request =>
        {
            sent.Add(new SentRequest(
                request.RequestUri!.Host,
                request.RequestUri.PathAndQuery,
                request.Headers.TryGetValues(RequestSteps.HeaderName, out var steps) ? steps.ToArray() : [],
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post ? "\"ok\"" : "[]", new MediaTypeHeaderValue("application/json")),
            };
        });
        var endpoints = new RegionEndpoints();
        endpoints.Regions["eu"] = eu ?? _euAddress;
        endpoints.Regions["us"] = us ?? new Uri("https://us.example.test");

        return (new RegionalOperationsApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = baseAddress ?? _baseAddress }), endpoints), sent);
    }

    private sealed record SentRequest(string Host, string PathAndQuery, string[] Steps, string? Authorization, string? IdempotencyKey);

    private sealed class StepCredential : IUpstreamCredential
    {
        private readonly string _name;

        public StepCredential(string name)
        {
            this._name = name;
        }

        public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(RequestSteps.HeaderName, "credential:" + this._name);

            return ValueTask.CompletedTask;
        }
    }
}
