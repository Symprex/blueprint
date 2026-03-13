using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    public class ClassTests : TestBase
    {
        public ClassTests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        [Fact]
        public void GenerateAbstractClass()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SampleAbstractClass")
                .Abstract();

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateSimpleGenericClass()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SimpleGenericClass")
                .AddGeneric("T");

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateGenericClassWithConstraint()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("GenericClassWithConstraint")
                .AddGeneric("T", b => b.AddConstraint("IFoo"));

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateInternalClass()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("InternalClass")
                .WithAccessModifier(Accessibility.Internal);

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateInternalClassTurkish()
        {
            var currentCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");

            try
            {
                var builder = CodeBuilder.Create(Namespace)
                    .AddClass("InternalClass")
                    .WithAccessModifier(Accessibility.Internal);

                this.MakeAssertion(builder);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = currentCulture;
            }
        }

        [Fact]
        public void GenerateGenericClassWithComplexConstraint()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("GenericClassWithComplexConstraints")
                .AddGeneric("T", b =>
                {
                    b.Class()
                     .AddConstraint("IFoo")
                     .New();
                });

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GeneratesTopLevelNamespace()
        {
            var builder = CodeBuilder.Create(Namespace)
                .TopLevelNamespace()
                .AddClass("TopLevelNamespace");

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GeneratesNullableCodeFile()
        {
            var builder = CodeBuilder.Create(Namespace)
                .Nullable(NullableState.Enable)
                .AddClass("NullableClass");

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GeneratesClassInGlobalNamespace()
        {
            var builder = CodeBuilder.CreateInGlobalNamespace()
                .AddClass("ClassInGlobalNamespace");

            this.MakeAssertion(builder);
        }

        [Fact]
        public void SampleClassWithPropertiesSorted()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SampleClassWithPropertiesSorted");

            builder.AddProperty("PropC", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropB", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropA", Accessibility.Public).SetType<string>();

            this.MakeAssertion(builder);
        }

        [Fact]
        public void SampleClassWithPropertiesNotSorted()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SampleClassWithPropertiesNotSorted");

            builder.AddProperty("PropZ", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropC", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropB", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropA", Accessibility.Public).SetType<string>();
            builder.AddProperty("PropD", Accessibility.Public).SetType<string>();

            builder.DontSortPropertiesByName();

            this.MakeAssertion(builder);
        }
    }
}
