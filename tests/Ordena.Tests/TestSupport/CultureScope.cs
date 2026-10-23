using System.Globalization;

namespace Ordena.Tests.TestSupport;

// The tests that depend on the language run one at a time: one of them sets the process-wide
// Strings.Culture, which would change what the others read.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureCollection
{
    public const string Name = "Culture";
}

// Runs a test in a given language whatever the PC's own, and puts the old one back after.
// It flows through awaits, so an async test can open it at the start.
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
