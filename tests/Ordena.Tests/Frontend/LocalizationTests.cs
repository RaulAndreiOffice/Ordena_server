using System.Collections;
using System.Globalization;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Resources;
using Ordena.Frontend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

[Collection(CultureCollection.Name)]
public sealed class LocalizationTests
{
    [Fact]
    public void English_and_Spanish_have_the_same_keys_and_no_empty_text()
    {
        var english = Texts(CultureInfo.InvariantCulture);
        var spanish = Texts(CultureInfo.GetCultureInfo("es"));

        Assert.Empty(english.Keys.Except(spanish.Keys));
        Assert.Empty(spanish.Keys.Except(english.Keys));
        Assert.DoesNotContain(english.Concat(spanish), e => string.IsNullOrWhiteSpace(e.Value));
    }

    // A new ErrorCode, DocType or Direction without its text fails here, not in front of the user.
    [Fact]
    public void Every_error_and_enum_value_has_a_text_in_both_languages()
    {
        var keys = Enum.GetValues<ErrorCode>().Select(e => $"Error_{e}")
            .Concat(Enum.GetValues<DocType>().Select(t => $"DocType_{t}"))
            .Concat(Enum.GetValues<Direction>().Select(d => $"Direction_{d}"));

        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("es") })
            Assert.Empty(keys.Except(Texts(culture).Keys));
    }

    [Fact]
    public void The_localizer_follows_the_language_of_the_session()
    {
        var localizer = new Localizer();

        using (new CultureScope("es-ES"))
        {
            Assert.Equal("Buscar", localizer.Get("Page_Search"));
            Assert.Equal("El archivo está abierto en otro programa. Ciérralo y vuelve a intentarlo.",
                localizer.Error(ErrorCode.FileLocked));
            Assert.Equal("Recibo / justificante", localizer.Enum(DocType.Recibo));
            Assert.Equal(@"Carpeta de facturas: D:\Facturas", localizer.Format("Footer_Archive", @"D:\Facturas"));
        }

        using (new CultureScope("en-GB"))
        {
            Assert.Equal("Search", localizer.Get("Page_Search"));
            Assert.Equal("Issued", localizer.Enum(Direction.Emitida));
        }
    }

    // On the UI thread the culture goes back to Windows' own after the async startup; the app's
    // language must hold anyway (App.ApplyCulture puts it in Strings.Culture).
    [Fact]
    public void The_apps_language_wins_over_the_threads_culture()
    {
        using var windows = new CultureScope("en-GB");
        Strings.Culture = CultureInfo.GetCultureInfo("es-ES");
        try
        {
            Assert.Equal("Buscar", new Localizer().Get("Page_Search"));
            Assert.Equal("Buscar", Strings.Page_Search); // what {x:Static} reads
        }
        finally
        {
            Strings.Culture = null;
        }
    }

    [Fact]
    public void A_missing_key_shows_as_itself()
    {
        Assert.Equal("No_Such_Key", new Localizer().Get("No_Such_Key"));
    }

    // Only the texts of that exact resource file: no falling back to English.
    private static Dictionary<string, string> Texts(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
}
