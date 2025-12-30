using EcobeeCLISharp;
using Xunit;

namespace EcobeeCLISharp.Tests;

public class TemperatureTests
{
    [Theory]
    [InlineData(720, 72.0)]
    [InlineData(680, 68.0)]
    [InlineData(755, 75.5)]
    [InlineData(0, 0.0)]
    [InlineData(-100, -10.0)]
    public void ConvertTemperatureFromApi_ReturnsCorrectValue(int apiValue, decimal expected)
    {
        var result = Program.ConvertTemperatureFromApi(apiValue);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(72.0, 720)]
    [InlineData(68.0, 680)]
    [InlineData(75.5, 755)]
    [InlineData(0.0, 0)]
    [InlineData(-10.0, -100)]
    public void ConvertTemperatureToApi_ReturnsCorrectValue(decimal degrees, int expected)
    {
        var result = Program.ConvertTemperatureToApi(degrees);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(720)]
    [InlineData(685)]
    [InlineData(0)]
    [InlineData(-50)]
    public void ConvertTemperature_RoundTrip_ReturnsOriginalValue(int original)
    {
        var asDecimal = Program.ConvertTemperatureFromApi(original);
        var backToInt = Program.ConvertTemperatureToApi(asDecimal);
        Assert.Equal(original, backToInt);
    }

    [Theory]
    [InlineData("72", 72.0)]
    [InlineData("68.5", 68.5)]
    [InlineData("0", 0.0)]
    [InlineData("-10", -10.0)]
    public void ParseTemperature_ReturnsCorrectValue(string input, decimal expected)
    {
        var result = Program.ParseTemperature(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("72", 70.0, 72.0)]
    [InlineData("+2", 70.0, 72.0)]
    [InlineData("-2", 70.0, 68.0)]
    [InlineData("+0.5", 70.0, 70.5)]
    [InlineData("-0.5", 70.0, 69.5)]
    [InlineData("+10", 65.0, 75.0)]
    [InlineData("-10", 80.0, 70.0)]
    public void ParsePossiblyRelativeTemperature_ReturnsCorrectValue(string input, decimal current, decimal expected)
    {
        var result = Program.ParsePossiblyRelativeTemperature(input, current);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParsePossiblyRelativeTemperature_AbsoluteValue_IgnoresCurrentTemperature()
    {
        var result1 = Program.ParsePossiblyRelativeTemperature("75", 60.0m);
        var result2 = Program.ParsePossiblyRelativeTemperature("75", 80.0m);
        Assert.Equal(75.0m, result1);
        Assert.Equal(75.0m, result2);
    }

    [Fact]
    public void ParsePossiblyRelativeTemperature_RelativePlus_AddsToCurrentTemperature()
    {
        var result = Program.ParsePossiblyRelativeTemperature("+5", 70.0m);
        Assert.Equal(75.0m, result);
    }

    [Fact]
    public void ParsePossiblyRelativeTemperature_RelativeMinus_SubtractsFromCurrentTemperature()
    {
        var result = Program.ParsePossiblyRelativeTemperature("-5", 70.0m);
        Assert.Equal(65.0m, result);
    }
}
