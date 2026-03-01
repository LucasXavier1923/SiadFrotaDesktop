using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SiadFrotaDesktop.Models;
using SiadFrotaDesktop.Services;

namespace SiadFrotaDesktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FrotaItem> _frota = new();
    private readonly ObservableCollection<Motorista> _motoristas = new();
    private readonly ObservableCollection<ConsultaResultItem> _consultaResultados = new();

    private readonly ObservableCollection<UnidadeOption> _unidades = new();

    private sealed class UnidadeOption
    {
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Display => string.IsNullOrWhiteSpace(Nome) ? Codigo : $"{Codigo} ({Nome})";
    }


    private CancellationTokenSource? _cts;

    private double _logFontSize = 13;

    public MainWindow()
    {
        InitializeComponent();

        CmbUnidade.ItemsSource = _unidades;
        _ = ReloadDataAsync();
        LoadUnidades();


        GridConsulta.ItemsSource = _consultaResultados;
        CmbConsultaViatura.ItemsSource = _frota;
        CmbLancViatura.ItemsSource = _frota;
        CmbMotorista.ItemsSource = _motoristas;

        Loaded += async (_, _) => await ReloadDataAsync();

        // Valor comum: usuário/ unidade ficam vazios por padrão (segurança).
        TxtUsuario.Text = "";
    }

    // ===========================
    // UI helpers
    // ===========================

    private void SetBusy(bool busy, string statusText)
    {
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BtnCancel.IsEnabled = busy;

        // Desabilita as áreas principais para evitar cliques durante operação
        Tabs.IsEnabled = !busy;
        TxtUsuario.IsEnabled = !busy;
        TxtSenha.IsEnabled = !busy;
        CmbUnidade.IsEnabled = !busy;

        TxtStatus.Text = statusText;
    }

    private async void MenuDeleteLancamento_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridConsulta.SelectedItem is not SiadFrotaDesktop.Models.ConsultaResultItem row)
            {
                MessageBox.Show("Selecione uma linha primeiro.", "Atenção",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var placa = row.Placa?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(placa))
            {
                MessageBox.Show("Linha sem placa.", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"Deseja excluir o ÚLTIMO lançamento da viatura {placa}?",
                "Confirmar exclusão",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var usuario = TxtUsuario.Text?.Trim() ?? "";
            var senha = TxtSenha.Password ?? "";

            // Se você usa ComboBox de unidade (CmbUnidade com SelectedValuePath="Codigo")
            var unidade = (CmbUnidade.SelectedValue as string)?.Trim() ?? "";

            SetBusy(true, "Excluindo lançamento...");

            var progress = new Progress<string>(AppendLog);
            using var client = new SiadClient();

            var ct = _cts?.Token ?? CancellationToken.None;

            await client.DeleteUltimoLancamentoAsync(usuario, senha, unidade, placa, ct, progress);

            AppendLog($"✅ Exclusão enviada: {placa}");
            TxtStatus.Text = "Exclusão concluída.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("⏹ Operação cancelada.");
            TxtStatus.Text = "Cancelado.";
        }
        catch (Exception ex)
        {
            AppendLog("❌ Falha ao excluir: " + ex.Message);
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "Erro.";
        }
        finally
        {
            SetBusy(false, "Pronto.");
        }
    }


    private void AppendLog(string msg)
    {
        var ts = DateTime.Now.ToString("[HH:mm:ss] ");
        TxtLog.AppendText(ts + msg + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }

    private void ClearLog()
    {
        TxtLog.Clear();
        TxtStatus.Text = "Pronto.";
    }

    private bool TryGetCredenciais(out string usuario, out string senha, out string unidade)
    {
        usuario = (TxtUsuario.Text ?? "").Trim();
        senha = (TxtSenha.Password ?? "").Trim();
        unidade = GetUnidadeSelecionada();
if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha) || string.IsNullOrWhiteSpace(unidade))
        {
            MessageBox.Show(
                "Preencha Usuário, Senha e Unidade.",
                "Credenciais",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private FrotaItem? GetSelectedViaturaForConsulta()
    {
        if (CmbConsultaViatura.SelectedItem is FrotaItem f)
            return f;

        return _frota.FirstOrDefault();
    }

    private FrotaItem? GetSelectedViaturaForLancamento()
    {
        if (CmbLancViatura.SelectedItem is FrotaItem f)
            return f;

        return _frota.FirstOrDefault();
    }

    // ===========================
    // Eventos - Geral
    // ===========================

    private async void BtnReloadData_Click(object sender, RoutedEventArgs e)
    {
        await ReloadDataAsync();
    }

    private Task ReloadDataAsync()
    {
        try
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            var frotaPath = Path.Combine(dataDir, "frota.json");
            var motoristasPath = Path.Combine(dataDir, "motoristas.json");

            _frota.Clear();
            foreach (var v in ConfigLoader.LoadFrota(frotaPath))
                _frota.Add(v);

            _motoristas.Clear();
            foreach (var m in ConfigLoader.LoadMotoristas(motoristasPath))
                _motoristas.Add(m);

            // Default selections
            if (_frota.Count > 0)
            {
                CmbConsultaViatura.SelectedIndex = 0;
                CmbLancViatura.SelectedIndex = 0;
            }

            if (_motoristas.Count > 0)
                CmbMotorista.SelectedIndex = 0;

            AppendLog($"Dados carregados: {_frota.Count} viaturas, {_motoristas.Count} motoristas.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Falha ao carregar Data/frota.json ou Data/motoristas.json.\n\nDetalhes: {ex.Message}",
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void LoadUnidades()
    {
        try
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            var unidadesPath = Path.Combine(dataDir, "unidades.json");

            _unidades.Clear();

            if (File.Exists(unidadesPath))
            {
                var json = File.ReadAllText(unidadesPath);
                var list = JsonSerializer.Deserialize<List<UnidadeOption>>(json) ?? new List<UnidadeOption>();
                foreach (var u in list.Where(u => !string.IsNullOrWhiteSpace(u.Codigo)))
                    _unidades.Add(u);
            }

            if (_unidades.Count == 0)
            {
                _unidades.Add(new UnidadeOption { Codigo = "1401670", Nome = "FORMIGA" });
            }

            SetUnidadePadrao();
        }
        catch (Exception ex)
        {
            _unidades.Clear();
            _unidades.Add(new UnidadeOption { Codigo = "1401670", Nome = "FORMIGA" });
            SetUnidadePadrao();
            AppendLog($"Falha ao carregar Data/unidades.json: {ex.Message}");
        }
    }

    private string GetUnidadeSelecionada()
    {
        var codigo = CmbUnidade.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(codigo))
        {
            // fallback: tenta pegar do item selecionado
            if (CmbUnidade.SelectedItem is UnidadeOption u && !string.IsNullOrWhiteSpace(u.Codigo))
                return u.Codigo.Trim();
            return "";
        }
        return codigo.Trim();
    }

    private void SetUnidadePadrao()
    {
        // tenta selecionar 1401670 se existir; senão seleciona a primeira
        var preferida = _unidades.FirstOrDefault(u => u.Codigo == "1401670") ?? _unidades.FirstOrDefault();
        if (preferida != null)
            CmbUnidade.SelectedValue = preferida.Codigo;
    }



    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TxtUsuario.Text = "";
        TxtSenha.Password = "";
        SetUnidadePadrao();
AppendLog("Campos de credenciais limpos.");
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        ClearLog();
    }

    private void BtnFontUp_Click(object sender, RoutedEventArgs e)
    {
        _logFontSize = Math.Min(22, _logFontSize + 1);
        TxtLog.FontSize = _logFontSize;
    }

    private void BtnFontDown_Click(object sender, RoutedEventArgs e)
    {
        _logFontSize = Math.Max(9, _logFontSize - 1);
        TxtLog.FontSize = _logFontSize;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        AppendLog("Cancelamento solicitado.");
    }

    private async void BtnTestLogin_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCredenciais(out var usuario, out var senha, out var unidade))
            return;

        ClearLog();
        SetBusy(true, "Testando login...");

        _cts = new CancellationTokenSource();

        try
        {
            IProgress<string> progress = new Progress<string>(AppendLog);
            progress.Report("Testando credenciais...");

            using var client = new SiadClient();
            var ok = await client.TestLoginAsync(usuario, senha, unidade, _cts.Token);

            if (ok)
            {
                progress.Report("✅ Login OK!");
                MessageBox.Show("Login OK.", "SIAD", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                progress.Report("❌ Login inválido.");
                MessageBox.Show("Login inválido. Verifique Usuário/Senha/Unidade.", "SIAD", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operação cancelada.");
        }
        catch (Exception ex)
        {
            AppendLog("Erro: " + ex.Message);
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false, "Pronto.");
        }
    }

    // ===========================
    // Consulta
    // ===========================

    private void RbConsultaTipo_Checked(object sender, RoutedEventArgs e)
    {
        if (CmbConsultaViatura == null || RbConsultaTodas == null) return;

        var todas = RbConsultaTodas.IsChecked == true;
        CmbConsultaViatura.Visibility = todas ? Visibility.Collapsed : Visibility.Visible;
        CmbConsultaViatura.IsEnabled = !todas;

        if (todas)
            CmbConsultaViatura.SelectedIndex = -1;
        else if (CmbConsultaViatura.SelectedIndex < 0 && CmbConsultaViatura.Items.Count > 0)
            CmbConsultaViatura.SelectedIndex = 0;
    }

    private async void BtnConsulta_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCredenciais(out var usuario, out var senha, out var unidade))
            return;

        ClearLog();
        _consultaResultados.Clear();
        SetBusy(true, "Consultando...");

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(AppendLog);

            IReadOnlyList<FrotaItem> alvo;

            if (RbConsultaTodas.IsChecked == true)
                alvo = _frota.ToList();
            else
            {
                var sel = GetSelectedViaturaForConsulta();
                alvo = sel is null ? Array.Empty<FrotaItem>() : new[] { sel };
            }

            if (alvo.Count == 0)
            {
                AppendLog("Nenhuma viatura carregada. Verifique Data/frota.json.");
                return;
            }

            using var client = new SiadClient();
            var resultados = await client.ConsultarAsync(usuario, senha, unidade, alvo, progress, _cts.Token);

            foreach (var r in resultados)
                _consultaResultados.Add(r);

            AppendLog($"✅ Consulta concluída. Itens: {resultados.Count}");
            TxtStatus.Text = "Consulta concluída.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operação cancelada.");
            TxtStatus.Text = "Cancelado.";
        }
        catch (Exception ex)
        {
            AppendLog("❌ Erro: " + ex.Message);
            TxtStatus.Text = "Erro.";
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false, "Pronto.");
        }
    }

    // ===========================
    // Lançamento
    // ===========================

    private void RbLancTipo_Checked(object sender, RoutedEventArgs e)
    {
        // Trava de segurança: se os painéis ainda não foram instanciados, não faz nada.
        if (PanelSaida == null || PanelRetorno == null || RbSaida == null) return;

        var saida = RbSaida.IsChecked == true;
        PanelSaida.Visibility = saida ? Visibility.Visible : Visibility.Collapsed;
        PanelRetorno.Visibility = saida ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnLancamento_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCredenciais(out var usuario, out var senha, out var unidade))
            return;

        ClearLog();
        SetBusy(true, "Executando lançamento...");

        _cts = new CancellationTokenSource();

        try
        {
            IProgress<string> progress = new Progress<string>(AppendLog);

            var viatura = GetSelectedViaturaForLancamento();
            if (viatura is null)
            {
                AppendLog("Nenhuma viatura selecionada.");
                return;
            }

            using var client = new SiadClient();

            if (RbSaida.IsChecked == true)
            {
                if (CmbMotorista.SelectedItem is not Motorista motorista)
                {
                    AppendLog("Selecione um motorista.");
                    return;
                }

                progress.Report($"Condutor: {motorista.Nome}");
                var res = await client.LancarSaidaAsync(usuario, senha, unidade, viatura.Placa, motorista, progress, _cts.Token);

                AppendLog(res.Sucesso ? "✅ " + res.Mensagem : "❌ " + res.Mensagem);
                MessageBox.Show(res.Mensagem, "SIAD", MessageBoxButton.OK, res.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            else
            {
                var odo = (TxtHodometroRetorno.Text ?? "").Trim();
                var reds = (TxtRedsRetorno.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(odo) || string.IsNullOrWhiteSpace(reds))
                {
                    AppendLog("Preencha Hodômetro e REDS.");
                    MessageBox.Show("Preencha Hodômetro e REDS.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var res = await client.LancarRetornoAsync(usuario, senha, unidade, viatura.Placa, odo, reds, progress, _cts.Token);

                AppendLog(res.Sucesso ? "✅ " + res.Mensagem : "❌ " + res.Mensagem);
                MessageBox.Show(res.Mensagem, "SIAD", MessageBoxButton.OK, res.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }

            TxtStatus.Text = "Lançamento concluído.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operação cancelada.");
            TxtStatus.Text = "Cancelado.";
        }
        catch (Exception ex)
        {
            AppendLog("❌ Erro: " + ex.Message);
            TxtStatus.Text = "Erro.";
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false, "Pronto.");
        }
    }


    private static bool IsMissing(string? s)
        => string.IsNullOrWhiteSpace(s) || s.Trim().Equals("Não encontrado", StringComparison.OrdinalIgnoreCase);

    private void GridConsulta_LoadingRow(object sender, System.Windows.Controls.DataGridRowEventArgs e)
    {
        if (e.Row?.Item is not ConsultaResultItem it) return;

        var missingReds = IsMissing(it.Ocorrencias);
        var missingOdo  = IsMissing(it.Hodometro);

        // Regra de cores:
        // - Vermelho: falta hodômetro e REDS (viatura na rua)
        // - Amarelo: falta só REDS
        // - Verde: tem os dois
        if (missingOdo && missingReds)
            e.Row.Background = new SolidColorBrush(Color.FromRgb(255, 210, 210)); // vermelho claro
        else if (!missingOdo && missingReds)
            e.Row.Background = new SolidColorBrush(Color.FromRgb(255, 242, 204)); // amarelo claro
        else
            e.Row.Background = new SolidColorBrush(Color.FromRgb(217, 234, 211)); // verde claro
    }

}