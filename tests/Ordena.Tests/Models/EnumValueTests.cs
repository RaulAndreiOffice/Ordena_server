using Ordena.Backend.Models;

namespace Ordena.Tests.Models;

// Enum numbers are stored in the database, so renumbering one would silently corrupt existing rows.
// These tests also fail when a member is added, as a reminder to pin its number here.
public sealed class EnumValueTests
{
    [Fact]
    public void DocType_values_never_change() => AssertValues(new Dictionary<DocType, int>
    {
        [DocType.Factura] = 1,
        [DocType.FacturaSimplificada] = 2,
        [DocType.FacturaRectificativa] = 3,
        [DocType.Recibo] = 4,
        [DocType.Proforma] = 5,
        [DocType.Otro] = 9,
    });

    [Fact]
    public void Direction_values_never_change() => AssertValues(new Dictionary<Direction, int>
    {
        [Direction.Recibida] = 1,
        [Direction.Emitida] = 2,
    });

    [Fact]
    public void FileOperationState_values_never_change() => AssertValues(new Dictionary<FileOperationState, int>
    {
        [FileOperationState.Pending] = 1,
        [FileOperationState.Done] = 2,
        [FileOperationState.Failed] = 3,
    });

    private static void AssertValues<T>(Dictionary<T, int> expected) where T : struct, Enum
    {
        var actual = Enum.GetValues<T>().ToDictionary(v => v, v => Convert.ToInt32(v));
        Assert.Equal(expected, actual);
    }
}
