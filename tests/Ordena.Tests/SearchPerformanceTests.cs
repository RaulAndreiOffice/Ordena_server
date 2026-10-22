using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests;

// Plan step 3: searching 10,000 documents must feel instant. The benchmark measured about 10 ms
// at 9,000 rows on a database file; the limit leaves room for a busy machine.
public sealed class SearchPerformanceTests(SearchPerformanceTests.SeededBackend backend)
    : IClassFixture<SearchPerformanceTests.SeededBackend>
{
    private const double LimitMs = 50;
    private const int Runs = 10;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("telef")]
    [InlineData("repsol 2025")]
    [InlineData("0123")]
    [InlineData("zzzz")] // no match: the worst case, every row is scanned
    [InlineData("")] // newest 500 plus the count
    public async Task A_search_over_10000_documents_takes_under_50_ms(string text)
    {
        var query = new SearchQuery { Text = text };
        await backend.Search.SearchAsync(query, Ct); // warm-up

        var times = new double[Runs];
        for (var i = 0; i < Runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await backend.Search.SearchAsync(query, Ct);
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        var median = times.Order().ElementAt(Runs / 2);
        Assert.True(median < LimitMs, $"\"{text}\": median {median:0.0} ms over {Runs} runs");
    }

    // Otherwise the timings above could be measuring searches that find nothing.
    [Fact]
    public async Task The_seeded_data_gives_real_matches()
    {
        var telef = await backend.Search.SearchAsync(new SearchQuery { Text = "telef" }, Ct);
        var all = await backend.Search.SearchAsync(new SearchQuery(), Ct);

        Assert.NotEmpty(telef.Items);
        Assert.True(all.IsTruncated);
        Assert.InRange(all.TotalCount, 9_000, 10_000); // about 5% are in the trash
    }

    // One database file, with the real pragmas, seeded once for the whole class.
    public sealed class SeededBackend : IAsyncLifetime
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "Ordena.Tests", Guid.NewGuid().ToString("N"));
        private ServiceProvider? _provider;

        public ISearchService Search => _provider!.GetRequiredService<ISearchService>();

        public async ValueTask InitializeAsync()
        {
            _provider = new ServiceCollection().AddBackend(_folder).BuildServiceProvider();
            await _provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(CancellationToken.None);

            var contextFactory = _provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await contextFactory.CreateDbContextAsync();
            DocumentSeeder.Seed(db, partnerCount: 400, documentCount: 10_000);
        }

        public async ValueTask DisposeAsync()
        {
            if (_provider is not null)
                await _provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
    }
}
