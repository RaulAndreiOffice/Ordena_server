using Ordena.Backend.Files;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Files;

public sealed class Sha256HasherTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Matches_the_standard_test_vector_in_lowercase_hex()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Sha256Hasher.Compute("abc"u8));
    }

    [Fact]
    public void A_file_hashes_the_same_as_its_bytes()
    {
        var content = TestPdf.Bytes("hash");
        var path = _folder.WriteFile("a.pdf", content);

        Assert.Equal(Sha256Hasher.Compute(content), Sha256Hasher.ComputeFile(path));
    }
}
