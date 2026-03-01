using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using SiadFrotaDesktop.Models;

namespace SiadFrotaDesktop.Services;

/// <summary>
/// Porta o fluxo principal do seu script Python (requests + BeautifulSoup) para C#.
/// A ideia é manter a lógica, mas com HttpClient (cookies) + AngleSharp (HTML DOM).
/// </summary>
public sealed class SiadClient : IDisposable
{
    private const string Base = "https://wwws.siad.mg.gov.br/siad/";
    private const string LoginUrl = Base + "login.jsp";
    private const string GxUrl = Base + "gxproj.jsp";

    private readonly HttpClient _http;
    private readonly HtmlParser _parser;



private static string CleanField(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    s = s.Replace('\u00A0', ' ');
    s = Regex.Replace(s, @"gx_WriteNbsp\(\d+\)", string.Empty, RegexOptions.IgnoreCase);
    s = Regex.Replace(s, @"\s+", " ").Trim();
    return s;
}

    public SiadClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123 Safari/537.36");
        _http.DefaultRequestHeaders.Referrer = new Uri(LoginUrl);
        _http.DefaultRequestHeaders.Add("Origin", "https://wwws.siad.mg.gov.br");

        _parser = new HtmlParser();
    }

    public void Dispose() => _http.Dispose();

    // ===========================
    // Básico (GET/POST)
    // ===========================

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PostFormAsync(IEnumerable<KeyValuePair<string, string>> payload, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, GxUrl)
        {
            Content = content
        };

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PostDictAsync(Dictionary<string, string> payload, CancellationToken ct)
    {
        var list = payload.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value));
        return await PostFormAsync(list, ct);
    }

    // ===========================
    // Login
    // ===========================

    public async Task<(bool ok, string html, string? seq)> LoginAsync(string usuario, string senha, string unidade, CancellationToken ct)
    {
        // 1) abre login.jsp (pega cookies)
        _ = await GetAsync(LoginUrl, ct);

        // 2) posta no gxproj.jsp
        var payload = new Dictionary<string, string>
        {
            ["user"] = usuario,
            ["password"] = senha,
            ["unProcessadora"] = unidade,
            ["GX_Path"] = "br.gov.sefaz.business.geral.Usuario.logon()",
            ["GX_ActionKey"] = ""
        };

        var html = await PostDictAsync(payload, ct);

        if (html.Contains("Usuário ou senha inválido", StringComparison.OrdinalIgnoreCase))
            return (false, html, null);

        var seq = HtmlFormHelper.ExtrairSeq(html);
        return (seq is not null, html, seq);
    }

    public async Task<bool> TestLoginAsync(string usuario, string senha, string unidade, CancellationToken ct)
    {
        var (ok, _, _) = await LoginAsync(usuario, senha, unidade, ct);
        return ok;
    }

    // ===========================
    // Navegação (equivalentes ao Python)
    // ===========================

    private async Task<string> SelecionarModuloFrotaAsync(string seq, CancellationToken ct)
    {
        // Equivalente ao payload_frota do Python (11 inputs "Modulos", com X no item 7)
        var payload = new List<KeyValuePair<string, string>>
        {
            new("GX_Path", ""),
            new("GX_ActionKey", "[enter]"),
            new("GX_SeqScreenNumber", seq),
            new("GX_CursorPos", "Modulos")
        };

        for (var i = 0; i < 11; i++)
            payload.Add(new KeyValuePair<string, string>("Modulos", i == 7 ? "X" : ""));

        return await PostFormAsync(payload, ct);
    }

    private async Task<string> EnviarValorAsync(string valor, string htmlAtual, CancellationToken ct)
    {
        var doc = await _parser.ParseDocumentAsync(htmlAtual);

        var seq = doc.QuerySelector("input[name='GX_SeqScreenNumber']")?.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(seq))
            return htmlAtual;

        var cursor = doc.QuerySelector("input[name='GX_CursorPos']")?.GetAttribute("value") ?? string.Empty;

        var payload = new List<KeyValuePair<string, string>>
        {
            new("GX_Path", ""),
            new("GX_ActionKey", "[enter]"),
            new("GX_SeqScreenNumber", seq),
            new("GX_CursorPos", cursor)
        };

        if (!string.IsNullOrWhiteSpace(cursor))
            payload.Add(new KeyValuePair<string, string>(cursor, valor));

        return await PostFormAsync(payload, ct);
    }

    private async Task<string> NavegarPassoAsync(string valorMenu, string htmlAtual, CancellationToken ct)
    {
        var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, htmlAtual);

        payload.TryGetValue("GX_CursorPos", out var cursor);
        cursor ??= string.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
            payload["opcaoMenu"] = valorMenu;
        else
            payload[cursor] = valorMenu;

        payload["GX_ActionKey"] = "[enter]";

        return await PostDictAsync(payload, ct);
    }

    private async Task<string> EnviarPlacaGenericoAsync(string htmlAtual, string placa, CancellationToken ct)
    {
        var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, htmlAtual);

        payload["GX_CursorPos"] = "847";
        payload["POS847"] = placa;
        payload["GX_ActionKey"] = "[enter]";

        if (!payload.ContainsKey("opcaoMenu"))
            payload["opcaoMenu"] = string.Empty;

        return await PostDictAsync(payload, ct);
    }

    private async Task<string> RealizarConsultaPlacaAsync(string htmlTelaInput, string placa, CancellationToken ct)
    {
        var doc = await _parser.ParseDocumentAsync(htmlTelaInput);

        var seqAtual = doc.QuerySelector("input[name='GX_SeqScreenNumber']")?.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(seqAtual))
            return htmlTelaInput;

        const string CampoPlaca = "POS988";

        // step 1
        var payload1 = new List<KeyValuePair<string, string>>
        {
            new("GX_Path", ""),
            new("GX_ActionKey", ""),
            new("GX_SeqScreenNumber", seqAtual),
            new("GX_CursorPos", CampoPlaca),
            new(CampoPlaca, placa)
        };

        var html1 = await PostFormAsync(payload1, ct);

        // step 2 (com novo seq)
        var seq2 = HtmlFormHelper.ExtrairSeq(html1);
        if (string.IsNullOrWhiteSpace(seq2))
            return html1;

        var payload2 = new List<KeyValuePair<string, string>>
        {
            new("GX_Path", ""),
            new("GX_ActionKey", "[enter]"),
            new("GX_SeqScreenNumber", seq2),
            new("GX_CursorPos", CampoPlaca),
            new(CampoPlaca, placa)
        };

        return await PostFormAsync(payload2, ct);
    }

    private async Task<string> VoltarTelaAsync(string htmlAtual, CancellationToken ct)
    {
        var doc = await _parser.ParseDocumentAsync(htmlAtual);
        var seq = doc.QuerySelector("input[name='GX_SeqScreenNumber']")?.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(seq))
            return htmlAtual;

        var payload = new List<KeyValuePair<string, string>>
        {
            new("GX_Path", ""),
            new("GX_ActionKey", "[PF9]"),
            new("GX_SeqScreenNumber", seq),
            new("GX_CursorPos", "")
        };

        return await PostFormAsync(payload, ct);
    }


    // ===========================
    // Deletar
    // ===========================

    public async Task DeleteUltimoLancamentoAsync(
        string usuario,
        string senha,
        string unidade,
        string placa,
        CancellationToken ct,
        IProgress<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(placa))
            throw new ArgumentException("Placa inválida.", nameof(placa));

        // normaliza placa (remove hífen/espaço)
        placa = Regex.Replace(placa.ToUpperInvariant(), @"[^A-Z0-9]", "");

        log?.Report("Conectando ao SIAD...");
        var (ok, _, seq) = await LoginAsync(usuario, senha, unidade, ct);
        if (!ok || string.IsNullOrWhiteSpace(seq))
            throw new Exception("Login inválido (usuário/senha/unidade) ou falha ao capturar GX_SeqScreenNumber.");

        log?.Report("Login OK. Abrindo módulo Frota...");
        var html = await SelecionarModuloFrotaAsync(seq, ct);

        // Caminho: 6 -> 2 -> 3
        log?.Report("Navegando: 6 → 2 → 3 ...");
        html = await EnviarValorAsync("6", html, ct);
        html = await EnviarValorAsync("2", html, ct);
        html = await EnviarValorAsync("3", html, ct);

        // Placa
        log?.Report($"Informando placa {placa} ...");
        html = await EnviarPlacaGenericoAsync(html, placa, ct);

        // Próximo: "apenas Enter" (último atendimento já selecionado)
        log?.Report("Selecionando último atendimento (ENTER) ...");
        html = await PressEnterAsync(html, ct);

        // 1) Preferir confirmação via POS39 (muito comum no SIAD)
        if (html.Contains("POS39", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Confirmando exclusão via POS39 = S ...");
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS39"] = "S";
            payload["GX_CursorPos"] = "39";
            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }
        else
        {
            // 2) fallback: enviar "S" como opção/entrada padrão
            log?.Report("Confirmando exclusão via 'S' + ENTER ...");
            html = await EnviarValorAsync("S", html, ct);
        }

        // Confirmação extra (quando existir), igual você já faz no lançamento
        if (html.Contains("POS91", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Confirmação extra detectada (POS91). Respondendo N ...");
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS91"] = "N";
            payload["GX_CursorPos"] = "91";
            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }

        // Confirma exclusão: "S" + Enter
        log?.Report("Confirmando exclusão (S + ENTER) ...");
        

        // Alguns fluxos do SIAD perguntam POS91 (tipo confirmação extra).
        // Se aparecer, responda N (igual você já faz na saída).
        if (html.Contains("POS91", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Confirmação extra detectada (POS91). Respondendo N ...");
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS91"] = "N";
            payload["GX_CursorPos"] = "91";
            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }

        log?.Report("Exclusão enviada ao SIAD.");
    }

    private async Task<string> PressEnterAsync(string htmlAtual, CancellationToken ct)
    {
        var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, htmlAtual);

        // Não altera nenhum campo: só ENTER
        payload["GX_ActionKey"] = "[enter]";

        // Se não existir opcaoMenu no payload, cria vazio (seu EnviarPlacaGenerico faz isso)
        if (!payload.ContainsKey("opcaoMenu"))
            payload["opcaoMenu"] = string.Empty;

        return await PostDictAsync(payload, ct);
    }


    // ===========================
    // Consulta (Relatório)
    // ===========================



    public async Task<IReadOnlyList<ConsultaResultItem>> ConsultarAsync(
        string usuario,
        string senha,
        string unidade,
        IReadOnlyList<FrotaItem> viaturas,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (viaturas.Count == 0)
            return Array.Empty<ConsultaResultItem>();

        log?.Report("Conectando ao SIAD...");
        var (ok, htmlLogin, seq) = await LoginAsync(usuario, senha, unidade, ct);




        if (!ok || string.IsNullOrWhiteSpace(seq))
            throw new InvalidOperationException("Login inválido (usuário/senha/unidade) ou falha ao capturar GX_SeqScreenNumber.");

        log?.Report("Login OK. Navegando até o módulo Frota...");
        var html = await SelecionarModuloFrotaAsync(seq, ct);

        // Mesmo caminho do Python: "6" -> "2" -> "4"
        html = await EnviarValorAsync("6", html, ct);
        html = await EnviarValorAsync("2", html, ct);
        html = await EnviarValorAsync("4", html, ct);

        var resultados = new List<ConsultaResultItem>(capacity: viaturas.Count);

        foreach (var v in viaturas)
        {
            ct.ThrowIfCancellationRequested();

            log?.Report($"Processando {v.Codinome} ({v.Placa})...");

            // Seleciona modo de busca por placa (no Python isso ocorre a cada iteração)
            html = await EnviarValorAsync("1", html, ct);

            var htmlResultado = await RealizarConsultaPlacaAsync(html, v.Placa, ct);

            

// Placa: se não achar no HTML, usa a da frota
var placaTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Placa");
placaTxt = CleanField(placaTxt);
if (string.IsNullOrWhiteSpace(placaTxt) || placaTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase))
    placaTxt = v.Placa;

// Detecta se existe bloco de retorno
bool temRetorno = htmlResultado.Contains("Dados do Retorno do Atendimento", StringComparison.OrdinalIgnoreCase);

// Hora/Hodômetro: preferir Retorno quando existir, senão Atendimento
string horaTxt;
string odoTxt;

if (temRetorno)
{
    horaTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hora\\s*Retorno");
    odoTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hodometro\\s*Retorno");
}
else
{
    horaTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hora\\s*Atendimento");
    odoTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hodometro\\s*Atendimento");
}

// Fallback (GeneXus às vezes usa só "Hora"/"Hodometro")
if (string.IsNullOrWhiteSpace(horaTxt) || horaTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase))
    horaTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hora", ocorrencia: -1);

if (string.IsNullOrWhiteSpace(odoTxt) || odoTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase))
    odoTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Hodometro", ocorrencia: -1);

horaTxt = CleanField(horaTxt);
odoTxt = CleanField(odoTxt);

// REDS / Ocorrências: tentar várias labels
var redsTxt = await HtmlFormHelper.BuscarTextoProximaCelulaAsync(_parser, htmlResultado, "Numero\\s*REDS|REDS|Ocorr");
redsTxt = CleanField(redsTxt);

// CPF/Nome
var (cpf, nome) = await HtmlFormHelper.ExtrairCpfNomeAsync(_parser, htmlResultado);
cpf = CleanField(cpf);
nome = CleanField(nome);

// Se ainda estiver "Não encontrado", deixa vazio (melhor UX)
if (horaTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase)) horaTxt = "";
if (odoTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase)) odoTxt = "";
if (redsTxt.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase)) redsTxt = "";
if (cpf.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase)) cpf = "";
if (nome.Equals("Não encontrado", StringComparison.OrdinalIgnoreCase)) nome = "";

resultados.Add(new ConsultaResultItem
            {
                Codinome = v.Codinome,
                Placa = placaTxt,
                Hodometro = odoTxt,
                Hora = horaTxt,
                Ocorrencias = redsTxt,
                Cpf = cpf,
                Nome = nome
            });

            // volta para a tela anterior
            html = await VoltarTelaAsync(htmlResultado, ct);

            // pequena pausa (igual ao time.sleep(0.5) no Python)
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        log?.Report("Consulta finalizada.");
        return resultados;
    }

    // ===========================
    // Lançamento
    // ===========================

    public async Task<LancamentoResult> LancarSaidaAsync(
        string usuario,
        string senha,
        string unidade,
        string placa,
        Motorista motorista,
        IProgress<string>? log,
        CancellationToken ct)
    {
        log?.Report("Acessando SIAD...");
        var (ok, _, seq) = await LoginAsync(usuario, senha, unidade, ct);

        if (!ok || string.IsNullOrWhiteSpace(seq))
            return new LancamentoResult { Sucesso = false, Mensagem = "Login inválido." };

        // Equivalente ao Python: módulo Frota -> Movimentação
        var html = await SelecionarModuloFrotaAsync(seq, ct);
        html = await NavegarPassoAsync("6", html, ct); // Frota
        var htmlMov = await NavegarPassoAsync("2", html, ct); // Movimentação

        log?.Report($"Iniciando Lançamento de SAÍDA: {placa}");

        // No Python: navegar_passo("1", html_mov)
        html = await NavegarPassoAsync("1", htmlMov, ct);

        html = await EnviarPlacaGenericoAsync(html, placa, ct);

        // PF1
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["GX_CursorPos"] = "1215";
            payload["GX_ActionKey"] = "[PF1]";
            html = await PostDictAsync(payload, ct);
        }

        // Busca pelo nome (primeiros 15 chars)
        var nomeBusca = (motorista.Nome ?? string.Empty);
        if (nomeBusca.Length > 15) nomeBusca = nomeBusca.Substring(0, 15);

        {
            var payloadBusca = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payloadBusca["POS51"] = nomeBusca;
            payloadBusca["GX_CursorPos"] = "51";
            payloadBusca["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payloadBusca, ct);
        }

        // Lista de candidatos - localizar CPF e marcar X
        var cpfAlvo = HtmlFormHelper.ApenasDigitos(motorista.Cpf);

        string? inputSelecao = null;
        {
            var docLista = await _parser.ParseDocumentAsync(html);

            var tdCpf = docLista.QuerySelectorAll("td")
                .FirstOrDefault(td => HtmlFormHelper.ApenasDigitos(td.TextContent) == cpfAlvo);

            if (tdCpf is not null)
            {
                var tr = tdCpf.ParentElement;
                while (tr is not null && !tr.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase))
                    tr = tr.ParentElement;

                var inp = tr?.QuerySelector("input[type='text']");
                inputSelecao = inp?.GetAttribute("name");
            }
        }

        if (string.IsNullOrWhiteSpace(inputSelecao))
            return new LancamentoResult { Sucesso = false, Mensagem = "CPF não encontrado na lista do SIAD." };

        {
            var payloadSel = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payloadSel[inputSelecao] = "X";
            payloadSel["GX_CursorPos"] = inputSelecao;
            payloadSel["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payloadSel, ct);
        }

        // Preenche data/hora
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);

            var agora = DateTime.Now;
            payload["POS902"] = agora.ToString("dd");
            payload["POS907"] = agora.ToString("MM");
            payload["POS912"] = agora.ToString("yyyy");
            payload["POS951"] = agora.ToString("HH");
            payload["POS956"] = agora.ToString("mm");
            payload["POS982"] = "1";

            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }

        // Confirmação POS39 = S
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS39"] = "S";
            payload["GX_CursorPos"] = "39";
            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }

        // Confirmação POS91 = N
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS91"] = "N";
            payload["GX_CursorPos"] = "91";
            payload["GX_ActionKey"] = "[enter]";
            _ = await PostDictAsync(payload, ct);
        }

        return new LancamentoResult { Sucesso = true, Mensagem = "SAÍDA REGISTRADA COM SUCESSO!" };
    }

    public async Task<LancamentoResult> LancarRetornoAsync(
        string usuario,
        string senha,
        string unidade,
        string placa,
        string hodometroChegada,
        string reds,
        IProgress<string>? log,
        CancellationToken ct)
    {
        log?.Report("Acessando SIAD...");
        var (ok, _, seq) = await LoginAsync(usuario, senha, unidade, ct);

        if (!ok || string.IsNullOrWhiteSpace(seq))
            return new LancamentoResult { Sucesso = false, Mensagem = "Login inválido." };

        // Equivalente ao Python: módulo Frota -> Movimentação
        var html = await SelecionarModuloFrotaAsync(seq, ct);
        html = await NavegarPassoAsync("6", html, ct); // Frota
        var htmlMov = await NavegarPassoAsync("2", html, ct); // Movimentação

        log?.Report($"Iniciando Lançamento de RETORNO: {placa}");

        // No Python: navegar_passo("2", html_mov)
        html = await NavegarPassoAsync("2", htmlMov, ct);

        html = await EnviarPlacaGenericoAsync(html, placa, ct);

        // Preenche hodômetro + REDS
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS1455"] = hodometroChegada;
            payload["POS1537"] = reds;

            payload["GX_CursorPos"] = "1537";
            payload["GX_ActionKey"] = "[enter]";
            html = await PostDictAsync(payload, ct);
        }

        // Confirmação POS39 = S
        {
            var payload = await HtmlFormHelper.MontarPayloadCompletoAsync(_parser, html);
            payload["POS39"] = "S";
            payload["GX_CursorPos"] = "39";
            payload["GX_ActionKey"] = "[enter]";
            _ = await PostDictAsync(payload, ct);
        }

        return new LancamentoResult { Sucesso = true, Mensagem = "RETORNO REGISTRADO COM SUCESSO!" };
    }
}