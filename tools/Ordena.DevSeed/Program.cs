using System.Diagnostics;
using System.Globalization;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.DevSeed;

// Fills a demo archive with fake documents through the real import, so every document has its PDF and
// search, open, edit, delete and restore can be tried on thousands of rows. Never on the user's own data:
//
//   dotnet run --project tools/Ordena.DevSeed -- --data D:\OrdenaDemo\data --archive D:\OrdenaDemo\Facturas --count 10000
//   $env:ORDENA_DATA_DIR = 'D:\OrdenaDemo\data'; dotnet run --project src/Ordena.Frontend
//
// The fixed seed gives the same partners, numbers and dates on every run. Running it again adds more
// documents (the same numbers again, confirmed as duplicates).

if (!TryParse(args, out var data, out var archive, out var count, out var seed))
{
    Console.Error.WriteLine("Usage: Ordena.DevSeed --data <folder> --archive <folder> [--count 10000] [--seed 42]");
    return 2;
}

var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ordena");
if (string.Equals(data, userData, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Refusing to seed {data}: that is the real Ordena data. Use a demo folder.");
    return 1;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};
var ct = cancel.Token;

await using var services = new ServiceCollection().AddBackend(data).BuildServiceProvider();
await services.GetRequiredService<IMaintenanceService>().RunStartupAsync(ct);

var settings = services.GetRequiredService<ISettingsService>();
var current = (await settings.GetAsync(ct)).ArchiveRoot;
if (current is null)
{
    var created = await settings.SetArchiveRootAsync(archive, ct);
    if (!created.IsSuccess)
    {
        Console.Error.WriteLine($"Can't use {archive} as the archive: {created.Error} {created.Detail}");
        return 1;
    }
}
else if (!string.Equals(Path.GetFullPath(current), archive, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"{data} already files into {current}, not {archive}.");
    return 1;
}

var faker = new Faker("es") { Random = new Randomizer(seed) };
string[] bigSuppliers =
[
    "Telefónica España, S.A.U.",
    "Repsol S.A.",
    "Iberdrola Clientes S.A.U.",
    "Endesa Energía S.A.",
    "Mercadona S.A.",
];
var partners = bigSuppliers
    .Concat(Enumerable.Range(0, 145).Select(_ => faker.Company.CompanyName()))
    .Distinct()
    .ToList();
DocType[] types = [DocType.Factura, DocType.FacturaSimplificada, DocType.FacturaRectificativa, DocType.Recibo, DocType.Proforma, DocType.Otro];
float[] typeWeights = [0.6f, 0.15f, 0.05f, 0.1f, 0.05f, 0.05f];
var firstDate = new DateTime(2024, 1, 1);

var import = services.GetRequiredService<IImportService>();
var inbox = Path.Combine(data, "seed-inbox");
Directory.CreateDirectory(inbox);
var run = DateTime.UtcNow.Ticks;
var failed = 0;
var clock = Stopwatch.StartNew();
Console.WriteLine($"Seeding {count:N0} documents into {archive}…");

for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
{
    // Two random factors multiplied favour the first partners: a few suppliers send most invoices.
    var partner = partners[(int)(faker.Random.Double() * faker.Random.Double() * partners.Count)];
    var number = faker.Random.Replace(faker.PickRandom("?-####/####", "F####", "####/##", "??######"));
    var date = DateOnly.FromDateTime(faker.Date.Between(firstDate, DateTime.Today));
    var type = faker.Random.WeightedRandom(types, typeWeights);
    var direction = faker.Random.Bool(0.8f) ? Direction.Recibida : Direction.Emitida;
    var concept = faker.Commerce.ProductName();
    var amount = faker.Finance.Amount(5, 2500);

    var source = Path.Combine(inbox, $"scan-{i:D5}.pdf");
    await File.WriteAllBytesAsync(source, DemoPdf.Create(type, partner, number, date, concept, amount, $"{run}-{i}"), ct);

    var result = await import.ImportAsync(new ImportRequest
    {
        SourcePath = source,
        PartnerName = partner,
        DocNumber = number,
        IssueDate = date,
        Type = type,
        Direction = direction,
        ConfirmDuplicate = true,
    }, ct);
    if (!result.IsSuccess)
    {
        failed++;
        File.Delete(source);
        if (failed <= 5)
            Console.WriteLine($"  {partner} {number}: {result.Error} {result.Detail}");
    }

    if ((i + 1) % 500 == 0)
        Console.WriteLine($"  {i + 1,7:N0}  {clock.Elapsed:mm\\:ss}");
}

Console.WriteLine(ct.IsCancellationRequested ? "Stopped." : "Done.");
Console.WriteLine($"{clock.Elapsed:mm\\:ss}, {failed:N0} failed.");
Console.WriteLine($"Open it with:  $env:ORDENA_DATA_DIR = '{data}'; dotnet run --project src/Ordena.Frontend");
return 0;

static bool TryParse(string[] args, out string data, out string archive, out int count, out int seed)
{
    data = archive = "";
    count = 10_000;
    seed = 42;
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        var value = args[i + 1];
        switch (args[i])
        {
            case "--data": data = Path.GetFullPath(value).TrimEnd('\\'); break;
            case "--archive": archive = Path.GetFullPath(value).TrimEnd('\\'); break;
            case "--count" when int.TryParse(value, CultureInfo.InvariantCulture, out var n) && n > 0: count = n; break;
            case "--seed" when int.TryParse(value, CultureInfo.InvariantCulture, out var s): seed = s; break;
            default: return false;
        }
    }

    return data.Length > 0 && archive.Length > 0;
}
