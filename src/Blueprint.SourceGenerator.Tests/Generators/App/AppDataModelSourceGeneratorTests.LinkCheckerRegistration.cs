namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public partial class AppDataModelSourceGeneratorTests
{
    public class LinkCheckerRegistration
    {
        [Fact]
        public async Task When_concrete_link_checker_exists_then_generates_RegisterLinkCheckers()
        {
            var source = """
                         using Blueprint.Authorization;
                         using Blueprint.Runtime.Exec;

                         public class TestResource { public bool IsActive { get; set; } }

                         public class DeactivateCommand : ICommand
                         {
                            public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                         }

                         public class TestDeactivateChecker : StateBasedLinkChecker<DeactivateCommand, TestResource>
                         {
                            protected override bool IsLinkAvailableForOperation(ApiOperationContext operationContext, TestResource resource)
                            {
                                return resource.IsActive;
                            }
                         }

                         [BlueprintAppScan]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_link_checker_is_private_nested_then_excluded()
        {
            var source = """
                         using Blueprint.Authorization;
                         using Blueprint.Runtime.Exec;

                         public class TestResource { public bool IsActive { get; set; } }

                         public class DeactivateCommand : ICommand
                         {
                            public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                         }

                         public class Container
                         {
                            private class TestDeactivateChecker : StateBasedLinkChecker<DeactivateCommand, TestResource>
                            {
                                protected override bool IsLinkAvailableForOperation(ApiOperationContext operationContext, TestResource resource)
                                {
                                    return resource.IsActive;
                                }
                            }
                         }

                         [BlueprintAppScan]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }

        [Fact]
        public async Task When_abstract_link_checker_then_excluded()
        {
            var source = """
                         using Blueprint.Authorization;
                         using Blueprint.Runtime.Exec;

                         public class TestResource { public bool IsActive { get; set; } }

                         public class DeactivateCommand : ICommand
                         {
                            public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                         }

                         public abstract class AbstractChecker : StateBasedLinkChecker<DeactivateCommand, TestResource>
                         {
                         }

                         [BlueprintAppScan]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                         """;

            await Compile(source)
                .VerifySuccessAsync(SnapshotsFolder);
        }
    }
}
