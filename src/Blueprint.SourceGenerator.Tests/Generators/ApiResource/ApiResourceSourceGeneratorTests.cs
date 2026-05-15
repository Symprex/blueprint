using Blueprint.SourceGenerator.Generators.ApiResources;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.ApiResource
{
    public class ApiResourceSourceGeneratorTests
    {
        private const string SnapshotsFolder = "Generators/ApiResource/Snapshots/";

        [Fact]
        public async Task When_not_partial_then_reports_diagnostic()
        {
            var source = """
                         public class Test : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessWithDiagnosticsAsync(SnapshotsFolder, "BP001");
        }

        [Fact]
        public async Task When_partial_and_no_suffix_then_implements_correctly()
        {
            var source = """
                         public partial class Test : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_and_ends_Resource_then_implements_correctly()
        {
            var source = """
                         public partial class TestResource : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_and_ends_DTO_then_implements_correctly()
        {
            var source = """
                         public partial class TestDTO : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_and_ends_ApiResource_then_implements_correctly()
        {
            var source = """
                         public partial class TestApiResource : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_and_compound_word_then_implements_correctly()
        {
            var source = """
                         public partial class UserMembershipApiResource : ApiResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_and_inheritance_chain_then_implements_correctly()
        {
            var source = """
                         public abstract class BaseResource : ApiResource
                         {
                         }
                     
                         public partial class UserMembershipApiResource : BaseResource
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_partial_list_then_implements_correctly()
        {
            var source = """
                         public partial class TestApiResource : ApiResource, IApiResourceCollection
                         {
                            public System.Collections.Generic.IEnumerable<object> GetEnumerable() 
                            {
                                throw new System.NotImplementedException();
                            }
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public void When_non_partial_but_inherits_concrete_Object_then_no_diagnostic()
        {
            // TenantPagedApiResource<T> extends PagedApiResource<T> which provides concrete Object override.
            // The generator should skip this class entirely — no diagnostic, no source output.
            var source = """
                         public class TenantPagedApiResource<T> : PagedApiResource<T> where T : ApiResource
                         {
                             public string TenantId { get; set; }
                         }
                         """;

            var result = Compile(source, assertCacheability: false);

            result.Diagnostics.Where(d => d.Id == "BP001").Should().BeEmpty(
                "a non-partial class that inherits a concrete Object from its base should not report BP001");
            result.Output.Should().BeEmpty(
                "no Object override should be generated when the base chain already provides one");
        }

        private static GenerationResult Compile(string source, bool assertCacheability = true)
        {
            return TestHelpers.Compile<ApiResourceSourceGenerator>("using Blueprint.Model;\n\nnamespace TestNamespace;\n\n" + source, assertCacheability);
        }
    }
}
