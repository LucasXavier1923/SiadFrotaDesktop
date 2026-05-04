using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace SiadFrotaDesktop.Services;

public static class HtmlFormHelper
{
    private static readonly Regex SeqRegex = new(
        @"name=[""']GX_SeqScreenNumber[""']\s+value=[""'](\d+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OcorrenciaNumeroRegex = new(
        @"\b\d{4}-\d{6,10}-\d{1,3}\b",
        RegexOptions.Compiled);

    private static readonly Regex OcorrenciaLabelRegex = new(
        @"Ocorr[eÃª]ncias?|N[uÃº]mero\s*REDS|REDS",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extrai o nÃºmero de sequÃªncia (GX_SeqScreenNumber) do HTML.
    /// </summary>
    public static string? ExtrairSeq(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        try
        {
            var parser = new HtmlParser();
            using var document = parser.ParseDocument(html);
            var input = document.QuerySelector("input[name='GX_SeqScreenNumber']");
            var valor = input?.GetAttribute("value");
            if (!string.IsNullOrWhiteSpace(valor)) return valor;
        }
        catch
        {
            // fallback abaixo
        }

        var m = SeqRegex.Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static async System.Threading.Tasks.Task<Dictionary<string, string>> MontarPayloadCompletoAsync(HtmlParser parser, string html)
    {
        var doc = await parser.ParseDocumentAsync(html);
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var input in doc.QuerySelectorAll("input"))
        {
            var name = input.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var type = (input.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (type is "submit" or "image" or "button" or "reset") continue;

            payload[name] = input.GetAttribute("value") ?? string.Empty;
        }

        foreach (var select in doc.QuerySelectorAll("select"))
        {
            var name = select.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var selected = select.QuerySelector("option[selected]");
            var val = selected?.GetAttribute("value") ?? select.QuerySelector("option")?.GetAttribute("value");
            payload[name] = val ?? string.Empty;
        }

        return payload;
    }

    public static async System.Threading.Tasks.Task<string> BuscarTextoProximaCelulaAsync(HtmlParser parser, string html, string labelRegex, int ocorrencia = -1)
    {
        var doc = await parser.ParseDocumentAsync(html);

        var regex = new Regex(labelRegex, RegexOptions.IgnoreCase);

        var labelCells = doc.QuerySelectorAll("td,th")
            .OfType<IElement>()
            .Where(td => regex.IsMatch(NormalizeCellText(td)))
            .ToList();

        if (labelCells.Count == 0) return "NÃ£o encontrado";

        IElement? label = (ocorrencia < 0) ? labelCells.LastOrDefault()
            : (ocorrencia < labelCells.Count ? labelCells[ocorrencia] : null);

        if (label is null) return "NÃ£o encontrado";

        var row = label.ParentElement;
        while (row != null && !row.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase))
            row = row.ParentElement;

        if (row is null) return "NÃ£o encontrado";

        var cells = row.QuerySelectorAll("td,th").OfType<IElement>().ToList();
        var idx = cells.IndexOf(label);
        if (idx < 0) return "NÃ£o encontrado";

        for (int i = idx + 1; i < cells.Count; i++)
        {
            var cell = cells[i];
            RemoveScripts(cell);
            var txt = CleanField(cell.TextContent);

            if (string.IsNullOrWhiteSpace(txt)) continue;
            if (txt is ":" or "-") continue;
            if (txt.EndsWith(":", StringComparison.Ordinal)) continue;

            return txt;
        }

        return "NÃ£o encontrado";
    }

    public static async System.Threading.Tasks.Task<string> ExtrairOcorrenciaAsync(HtmlParser parser, string html)
    {
        var doc = await parser.ParseDocumentAsync(html);
        var rows = doc.QuerySelectorAll("tr").OfType<IElement>().ToList();

        // Prioriza linha de "Ocorrencias", que Ã© onde o valor correto aparece nos dois formatos.
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td,th").OfType<IElement>().ToList();
            if (cells.Count == 0) continue;

            var labelIndex = cells.FindIndex(c => Regex.IsMatch(NormalizeCellText(c), @"Ocorr[eÃª]ncias?", RegexOptions.IgnoreCase));
            if (labelIndex < 0) continue;

            var valor = ExtractOcorrenciaFromCells(cells, labelIndex);
            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
        }

        // Fallback para linhas com REDS/Numero REDS.
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td,th").OfType<IElement>().ToList();
            if (cells.Count == 0) continue;

            var labelIndex = cells.FindIndex(c => OcorrenciaLabelRegex.IsMatch(NormalizeCellText(c)));
            if (labelIndex < 0) continue;

            var valor = ExtractOcorrenciaFromCells(cells, labelIndex);
            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
        }

        var text = CleanField(doc.Body?.TextContent ?? doc.DocumentElement?.TextContent ?? string.Empty);
        var match = OcorrenciaNumeroRegex.Match(text);
        return match.Success ? match.Value : "NÃ£o encontrado";
    }

    public static async System.Threading.Tasks.Task<(string cpf, string nome)> ExtrairCpfNomeAsync(HtmlParser parser, string html)
    {
        var doc = await parser.ParseDocumentAsync(html);

        var label = doc.QuerySelectorAll("td,th")
            .OfType<IElement>()
            .FirstOrDefault(td => Regex.IsMatch(NormalizeCellText(td), @"^CPF\s*/\s*Nome\s*:?\s*$", RegexOptions.IgnoreCase));

        if (label is null) return ("NÃ£o encontrado", "NÃ£o encontrado");

        var row = label.ParentElement;
        while (row != null && !row.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase))
            row = row.ParentElement;

        if (row is null) return ("NÃ£o encontrado", "NÃ£o encontrado");

        var cells = row.QuerySelectorAll("td,th").OfType<IElement>().ToList();
        var idx = cells.IndexOf(label);
        if (idx < 0) return ("NÃ£o encontrado", "NÃ£o encontrado");

        string cpf = string.Empty;
        string nome = string.Empty;

        for (int i = idx + 1; i < cells.Count; i++)
        {
            var cell = cells[i];
            RemoveScripts(cell);

            var txt = CleanField(cell.TextContent);
            if (string.IsNullOrWhiteSpace(txt) || txt is ":" or "-") continue;
            if (txt.EndsWith(":", StringComparison.Ordinal)) continue;

            if (string.IsNullOrWhiteSpace(cpf))
            {
                cpf = txt;
                continue;
            }

            nome = txt;
            break;
        }

        if (string.IsNullOrWhiteSpace(cpf)) cpf = "NÃ£o encontrado";
        if (string.IsNullOrWhiteSpace(nome)) nome = "NÃ£o encontrado";

        return (cpf, nome);
    }

    private static string ExtractOcorrenciaFromCells(IReadOnlyList<IElement> cells, int startIndex)
    {
        for (int i = startIndex; i < cells.Count; i++)
        {
            var cell = cells[i];
            RemoveScripts(cell);
            var txt = CleanField(cell.TextContent);
            if (string.IsNullOrWhiteSpace(txt)) continue;

            var direto = OcorrenciaNumeroRegex.Match(txt);
            if (direto.Success) return direto.Value;

            var semLabel = Regex.Replace(
                txt,
                @"^\s*(Ocorr[eÃª]ncias?|N[uÃº]mero\s*REDS|REDS)\s*:?\s*",
                string.Empty,
                RegexOptions.IgnoreCase);

            semLabel = CleanField(semLabel);
            if (string.IsNullOrWhiteSpace(semLabel)) continue;

            var aposLabel = OcorrenciaNumeroRegex.Match(semLabel);
            if (aposLabel.Success) return aposLabel.Value;
        }

        return string.Empty;
    }

    private static void RemoveScripts(IElement el)
    {
        foreach (var s in el.QuerySelectorAll("script").ToArray())
            s.Remove();
    }

    private static string NormalizeCellText(IElement el)
    {
        RemoveScripts(el);
        return CleanField(el.TextContent).Trim();
    }

    private static string CleanField(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Replace('\u00A0', ' ');
        s = Regex.Replace(s, @"gx_WriteNbsp\(\d+\)", string.Empty, RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }

    public static string ApenasDigitos(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s, @"\D", "");
}

