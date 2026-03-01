namespace SiadFrotaDesktop.Models;

public sealed class Motorista
{
    public string Matricula { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string Cpf { get; init; } = string.Empty;

    public string Display => $"{Nome} ({Matricula})";
}
