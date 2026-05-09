using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class DateTimeExtensionsTests
{
    [Fact]
    public void EndOfDay_Returns_Last_Second()
    {
        var date = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        var endOfDay = date.EndOfDay();

        endOfDay.Hour.Should().Be(23);
        endOfDay.Minute.Should().Be(59);
        endOfDay.Second.Should().Be(59);
    }

    [Fact]
    public void FromUnixTimeSeconds_Converts_Correctly()
    {
        var epoch = 0L.FromUnixTimeSeconds();
        epoch.Should().Be(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ToUnixTimeSeconds_RoundTrips()
    {
        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var unix = date.ToUnixTimeSeconds();

        unix.FromUnixTimeSeconds().Should().Be(date);
    }

    [Fact]
    public void IsInFuture_DateTime_Returns_True_For_Future_Date()
    {
        var future = DateTime.UtcNow.AddDays(1);
        future.IsInFuture(TemporalCheck.DateTime).Should().BeTrue();
    }

    [Fact]
    public void IsInPast_DateTime_Returns_True_For_Past_Date()
    {
        var past = DateTime.UtcNow.AddDays(-1);
        past.IsInPast(TemporalCheck.DateTime).Should().BeTrue();
    }

    [Fact]
    public void GetPreviousDate_Returns_Previous_Matching_Day()
    {
        // Find a known Wednesday
        var wednesday = new DateTime(2024, 6, 19, 0, 0, 0, DateTimeKind.Utc);
        wednesday.DayOfWeek.Should().Be(DayOfWeek.Wednesday);

        var previousMonday = wednesday.GetPreviousDate([DayOfWeek.Monday]);
        previousMonday.DayOfWeek.Should().Be(DayOfWeek.Monday);
        previousMonday.Should().BeBefore(wednesday);
    }

    [Fact]
    public void GetPreviousDate_Throws_For_Empty_Days()
    {
        var act = () => DateTime.UtcNow.GetPreviousDate([]);
        act.Should().Throw<ArgumentException>();
    }
}
