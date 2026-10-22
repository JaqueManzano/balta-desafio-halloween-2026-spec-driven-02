using LinkShortener.Api.Application;

namespace LinkShortener.Tests;

public sealed class LinkCodeGeneratorTests
{
    [Fact]
    public void Generate_returns_seven_characters_from_unambiguous_alphabet()
    {
        var code = new LinkCodeGenerator().Generate();

        Assert.Equal(7, code.Length);
        Assert.All(code, character =>
            Assert.Contains(character, "ABCDEFGHJKMNPQRSTUVWXYZ123456789"));
    }
}
