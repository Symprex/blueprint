using Blueprint.SourceGenerator.Generators.ApiResources;

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
                .VerifyCompilationFailedAsync(SnapshotsFolder);
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

        private static GenerationResult Compile(string source)
        {
            return TestHelpers.Compile<ApiResourceSourceGenerator>("using Blueprint.Model;\n\nnamespace TestNamespace;\n\n" + source);
        }
    }
}
