namespace Ordena.Frontend.ViewModels;

// One choice of a search filter: a year, a quarter, a type, a direction. A null Value is "all".
public sealed record FilterOption<T>(T? Value, string Label) where T : struct
{
    // What screen readers announce for a dropdown entry, instead of the record's members.
    public override string ToString() => Label;
}
