namespace Ordena.Backend.Models;

public class Partner
{
    public int Id { get; set; }

    // As typed: "Telefónica España, S.A.U."
    public string Name { get; set; } = string.Empty;

    // "telefonica espana": lowercase, no accents, no legal suffix. Used for search and similar names.
    public string NameNorm { get; set; } = string.Empty;

    // "Telefonica-Espana": fixed at creation, used for the folder and the file name.
    public string FolderName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<Document> Documents { get; set; } = [];
}
