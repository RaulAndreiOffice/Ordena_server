using Bogus;
using Ordena.Backend.Data;
using Ordena.Backend.Models;
using Ordena.Backend.Rules;

namespace Ordena.Tests.TestSupport;

// Realistic fake rows for performance tests: Spanish company names, a few big suppliers with most
// of the invoices (like the utilities), dates over three years, 5% in the trash. No files on disk.
// The fixed seed gives the same data on every run.
internal static class DocumentSeeder
{
    private static readonly string[] BigPartners =
    [
        "Telefónica España, S.A.U.",
        "Repsol S.A.",
        "Iberdrola Clientes S.A.U.",
        "Endesa Energía S.A.",
        "Mercadona S.A.",
    ];

    public static void Seed(AppDbContext db, int partnerCount, int documentCount)
    {
        var faker = new Faker("es") { Random = new Randomizer(42) };
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var partners = Enumerable.Range(0, partnerCount).Select(i =>
        {
            var name = i < BigPartners.Length ? BigPartners[i] : faker.Company.CompanyName();
            return new Partner
            {
                Name = name,
                NameNorm = TextNormalizer.NormalizeName(name),
                // The number keeps folder names unique; search never looks at them.
                FolderName = PathSanitizer.Sanitize($"{i} {name}", PathSanitizer.PartnerMaxLength),
                CreatedAt = now,
            };
        }).ToList();
        db.Partners.AddRange(partners);

        for (var i = 0; i < documentCount; i++)
        {
            // Two random factors multiplied favour the first partners, like a few suppliers sending most invoices.
            var partner = partners[(int)(faker.Random.Double() * faker.Random.Double() * partners.Count)];
            var number = faker.Random.Replace("?-####/####");
            db.Documents.Add(new Document
            {
                Partner = partner,
                Sha256 = i.ToString("x64"),
                DocNumber = number,
                DocNumberNorm = TextNormalizer.NormalizeNumber(number),
                IssueDate = DateOnly.FromDateTime(faker.Date.Between(new DateTime(2024, 1, 1), new DateTime(2026, 12, 31))),
                Type = faker.Random.Enum<DocType>(),
                Direction = faker.Random.Bool(0.8f) ? Direction.Recibida : Direction.Emitida,
                RelativePath = $"seed/{i}.pdf",
                OriginalName = $"scan{i}.pdf",
                FileSizeBytes = faker.Random.Long(50_000, 2_000_000),
                ImportedAt = now,
                UpdatedAt = now,
                DeletedAt = faker.Random.Bool(0.05f) ? now : null,
            });
        }

        db.SaveChanges();
    }
}
