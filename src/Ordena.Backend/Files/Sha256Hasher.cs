using System.Security.Cryptography;

namespace Ordena.Backend.Files;

// 64 lowercase hex characters, the format stored in Documents.Sha256.
internal static class Sha256Hasher
{
    public static string Compute(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    public static string ComputeFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
