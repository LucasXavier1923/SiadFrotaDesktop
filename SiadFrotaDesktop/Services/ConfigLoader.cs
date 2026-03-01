using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SiadFrotaDesktop.Models;

namespace SiadFrotaDesktop.Services;

public static class ConfigLoader
{
    public static IReadOnlyList<FrotaItem> LoadFrota(string filePath)
    {
        if (!File.Exists(filePath))
            return Array.Empty<FrotaItem>();

        var json = File.ReadAllText(filePath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

        // Ordena pelo codinome para a UI ficar agradável
        return dict
            .Select(kvp => new FrotaItem { Placa = kvp.Key.Trim(), Codinome = kvp.Value.Trim() })
            .OrderBy(x => x.Codinome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<Motorista> LoadMotoristas(string filePath)
    {
        if (!File.Exists(filePath))
            return Array.Empty<Motorista>();

        var json = File.ReadAllText(filePath);

        // Estrutura igual ao seu Python: { "matricula": { "Nome": "...", "CPF": "..." } }
        var dict = JsonSerializer.Deserialize<Dictionary<string, MotoristaRaw>>(json) ?? new Dictionary<string, MotoristaRaw>();

        return dict
            .Select(kvp => new Motorista
            {
                Matricula = kvp.Key.Trim(),
                Nome = kvp.Value.Nome?.Trim() ?? string.Empty,
                Cpf = kvp.Value.Cpf?.Trim() ?? string.Empty
            })
            .OrderBy(x => x.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class MotoristaRaw
    {
        public string? Nome { get; set; }
        public string? CPF { get; set; }

        // Compatibilidade: alguns JSONs podem usar "Cpf"
        public string? Cpf => CPF;
    }
}
