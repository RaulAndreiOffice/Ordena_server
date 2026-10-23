using Ordena.Backend.Infrastructure;

namespace Ordena.Tests;

// Where the app keeps its data: %LocalAppData%\Ordena, or ORDENA_DATA_DIR for demo data and smoke tests.
public sealed class AppPathsTests
{
    [Fact]
    public void Without_the_variable_the_data_is_in_LocalAppData()
    {
        var root = AppPaths.ResolveDefaultRoot(_ => null);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ordena"), root);
    }

    [Fact]
    public void The_variable_moves_it_and_is_made_absolute()
    {
        var root = AppPaths.ResolveDefaultRoot(name => name == "ORDENA_DATA_DIR" ? "demo\\data" : null);

        Assert.Equal(Path.GetFullPath("demo\\data"), root);
    }

    [Fact]
    public void An_empty_variable_is_ignored()
    {
        Assert.EndsWith("Ordena", AppPaths.ResolveDefaultRoot(_ => ""));
    }

    [Fact]
    public void Every_folder_is_under_the_root()
    {
        var paths = new AppPaths(@"D:\Demo\data");

        Assert.Equal(@"D:\Demo\data\ordena.db", paths.Database);
        Assert.Equal(@"D:\Demo\data\WebView2", paths.WebView2);
    }
}
