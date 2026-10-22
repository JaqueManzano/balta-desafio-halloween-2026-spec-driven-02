using System.Security.Cryptography;

namespace LinkShortener.Api.Application;

public interface ILinkCodeGenerator
{
    string Generate(int length = 7);
}

public sealed class LinkCodeGenerator : ILinkCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ123456789";

    public string Generate(int length = 7)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return string.Create(length, Alphabet, static (code, alphabet) =>
        {
            for (var index = 0; index < code.Length; index++)
            {
                code[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}
