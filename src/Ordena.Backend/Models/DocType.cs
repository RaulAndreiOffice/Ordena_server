namespace Ordena.Backend.Models;

// Stored as INTEGER. Values are explicit and must never be renumbered.
public enum DocType
{
    Factura = 1,
    FacturaSimplificada = 2,
    FacturaRectificativa = 3,
    Recibo = 4,
    Proforma = 5,
    Otro = 9,
}
