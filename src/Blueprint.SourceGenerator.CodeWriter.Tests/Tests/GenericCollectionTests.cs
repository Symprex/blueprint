using Xunit;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    public class GenericCollectionTests
    {
        [Fact]
        public void GeneratesNoOutput()
        {
            var collection = new GenericCollection();

            Assert.Equal(string.Empty, collection.ToString());
        }

        [Fact]
        public void GeneratesSingleGenericOutput()
        {
            var collection = new GenericCollection
            {
                new GenericBuilder("T"),
            };

            Assert.Equal("<T>", collection.ToString());
        }

        [Fact]
        public void GeneratesGenericListOutput()
        {
            var collection = new GenericCollection
            {
                new GenericBuilder("TKey"),
                new GenericBuilder("TValue"),
            };

            Assert.Equal("<TKey, TValue>", collection.ToString());
        }
    }
}
