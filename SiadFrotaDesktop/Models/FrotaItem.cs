namespace SiadFrotaDesktop.Models;

public sealed class FrotaItem
{
    public string Placa { get; init; } = string.Empty;
    public string Codinome { get; init; } = string.Empty;

    public string Display => $"{Codinome} ({Placa})";
}
