using verii_wms_api_v2.Modules.Location.Application;
using Xunit;

namespace QueryTests;

public sealed class LocationCodePolicyTests
{
    [Theory]
    [InlineData("A/01-B")]
    [InlineData("A.B_C-1")]
    [InlineData("1/SEVK-RAF.02_A")]
    public void Valid_location_codes_accept_supported_separators(string code)
    {
        Assert.True(LocationCodePolicy.IsValid(LocationCodePolicy.Normalize(code)));
    }

    [Theory]
    [InlineData("/A01")]
    [InlineData("-A01")]
    [InlineData("A 01")]
    [InlineData("A\\01")]
    public void Invalid_location_codes_are_rejected(string code)
    {
        Assert.False(LocationCodePolicy.IsValid(LocationCodePolicy.Normalize(code)));
    }

    [Fact]
    public void Location_code_cannot_exceed_fifty_characters()
    {
        Assert.False(LocationCodePolicy.IsValid(new string('A', LocationCodePolicy.MaxLength + 1)));
    }
}
