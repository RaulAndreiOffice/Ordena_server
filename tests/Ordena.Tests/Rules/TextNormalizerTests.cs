using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Telefónica España, S.A.U.", "telefonica espana")]
    [InlineData("Repsol S.A.", "repsol")]
    [InlineData("MERCADONA, SA", "mercadona")]
    [InlineData("Construcciones Pérez S.L.U.", "construcciones perez")]
    [InlineData("Talleres Núñez, S.L.", "talleres nunez")]
    [InlineData("Cooperativa Agrícola S. Coop.", "cooperativa agricola")]
    [InlineData("Bar Paco C.B.", "bar paco")]
    [InlineData("Casa", "casa")] // "sa" at the end of a word is not a legal form
    [InlineData("Façade Müller", "facade muller")]
    [InlineData("  Ferretería   El-Clavo  ", "ferreteria el clavo")]
    [InlineData("S.L.", "")]
    public void NormalizeName(string name, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeName(name));

    [Theory]
    [InlineData("A-2026/0123", "A20260123")]
    [InlineData("a 2026.0123", "A20260123")]
    [InlineData("Nº 123", "123")]
    [InlineData("nº123", "123")]
    [InlineData("Núm. 45/2026", "452026")]
    [InlineData("Número 7", "7")]
    [InlineData("T.88812", "T88812")]
    [InlineData("NO123", "NO123")] // a number that really starts with NO keeps it
    [InlineData("--", "")]
    public void NormalizeNumber(string number, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeNumber(number));
}
