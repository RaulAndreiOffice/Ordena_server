using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class PathSanitizerTests
{
    [Theory]
    [InlineData("Telefónica España, S.A.U.", "Telefonica-Espana")]
    [InlineData("Repsol S.A.", "Repsol")]
    [InlineData("Ñandú & Cía", "Nandu-Cia")]
    public void ToFolderName(string name, string expected) =>
        Assert.Equal(expected, PathSanitizer.ToFolderName(name));

    [Theory]
    [InlineData("A-2026/0123", "A-2026-0123")]
    [InlineData(@"A\2026", "A-2026")]
    [InlineData("a<b>c:d\"e|f?g*h", "a-b-c-d-e-f-g-h")]
    [InlineData("Factura. ", "Factura")]
    [InlineData("tab\there", "tab-here")]
    [InlineData("CON", "CON_")]
    [InlineData("com1", "com1_")]
    [InlineData("%%%", "")]
    public void Sanitize(string text, string expected) =>
        Assert.Equal(expected, PathSanitizer.Sanitize(text, PathSanitizer.NumberMaxLength));

    [Fact]
    public void Sanitize_cuts_to_the_limit()
    {
        const string name = "Asociación de Comerciantes del Centro Histórico de Valencia";

        Assert.Equal("Asociacion-de-Comerciantes-del-Centro-Hi", PathSanitizer.Sanitize(name, 40));
    }

    [Fact]
    public void Sanitize_never_ends_with_a_dash_after_the_cut()
    {
        // Character 38 is the "-" before "Historico".
        const string name = "Asociación de Comerciantes del Centro Histórico de Valencia";

        Assert.Equal("Asociacion-de-Comerciantes-del-Centro", PathSanitizer.Sanitize(name, 38));
    }
}
