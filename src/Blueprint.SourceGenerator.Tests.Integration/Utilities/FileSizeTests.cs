using System.Globalization;

using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class FileSizeTests
{
    [Fact]
    public void FromBytes_Returns_Correct_Byte_Count()
    {
        FileSize.FromBytes(1024).Bytes.Should().Be(1024);
    }

    [Fact]
    public void FromKilobytes_Converts_To_Bytes()
    {
        FileSize.FromKilobytes(1).Bytes.Should().Be(1024);
    }

    [Fact]
    public void FromMegabytes_Converts_To_Bytes()
    {
        FileSize.FromMegabytes(1).Bytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void FromGigabytes_Converts_To_Kilobytes()
    {
        FileSize.FromGigabytes(1).Kilobytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void ToString_FS_Selects_Appropriate_Unit()
    {
        FileSize.FromBytes(500).ToString().Should().Be("500B");
        FileSize.FromBytes(1024).ToString().Should().Be("1KB");
        FileSize.FromMegabytes(2.5).ToString().Should().Be("2.50MB");
    }

    [Fact]
    public void ToString_With_Explicit_Unit()
    {
        FileSize.FromMegabytes(1).ToString("KB", CultureInfo.InvariantCulture)
            .Should().Be("1,024KB");
    }

    [Fact]
    public void ToString_With_Custom_Format_And_Unit()
    {
        FileSize.FromGigabytes(1.5).ToString("N4:GB", CultureInfo.InvariantCulture)
            .Should().Be("1.5000GB");
    }

    [Fact]
    public void Equality_Compares_By_Bytes()
    {
        var a = FileSize.FromKilobytes(1);
        var b = FileSize.FromBytes(1024);

        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Inequality_Detects_Different_Sizes()
    {
        (FileSize.FromBytes(100) != FileSize.FromBytes(200)).Should().BeTrue();
    }

    [Fact]
    public void Negative_Value_Throws()
    {
        var act = () => FileSize.FromBytes(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
