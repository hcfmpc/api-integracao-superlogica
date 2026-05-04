using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;

namespace SicoobSuperlogica.Worker.Workers;

public sealed class IntegracaoWorker : BackgroundService
{
    private readonly ICondominioService _condominios;
    private readonly IRetornoSicoobService _retornoSicoob;
    private readonly ICnabParserService _cnabParser;
    private readonly ISuperlogicaService _superlogica;
    private readonly IIdempotencyService _idempotency;
    private readonly IStatusService _status;
    private readonly WorkerSettings _settings;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<IntegracaoWorker> _logger;
    private readonly SemaphoreSlim _cicloLock = new(1, 1);

    public IntegracaoWorker(
        ICondominioService condominios,
        IRetornoSicoobService retornoSicoob,
        ICnabParserService cnabParser,
        ISuperlogicaService superlogica,
        IIdempotencyService idempotency,
        IStatusService status,
        WorkerSettings settings,
        IHostApplicationLifetime lifetime,
        ILogger<IntegracaoWorker> logger)
    {
        _condominios = condominios;
        _retornoSicoob = retornoSicoob;
        _cnabParser = cnabParser;
        _superlogica = superlogica;
        _idempotency = idempotency;
        _status = status;
        _settings = settings;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("IntegracaoWorker iniciado. Intervalo: {H}h", _settings.IntervalHours);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.IntervalHours));

            await ExecutarCicloAsync(ct);

            while (await timer.WaitForNextTickAsync(ct))
                await ExecutarCicloAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("IntegracaoWorker encerrado por solicitação de shutdown.");
        }
        catch (Exception ex)
        {
            using (_logger.BeginScope(new Dictionary<string, object> { ["alerta"] = true, ["tipo"] = "CRITICO" }))
                _logger.LogError(ex, "Erro fatal no IntegracaoWorker. Encerrando aplicação.");
            _lifetime.StopApplication();
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IntegracaoWorker: aguardando conclusão do ciclo atual antes de encerrar.");
        await base.StopAsync(stoppingToken);
    }

    private async Task ExecutarCicloAsync(CancellationToken ct)
    {
        if (!await _cicloLock.WaitAsync(0, ct))
        {
            _logger.LogWarning("Ciclo anterior ainda em execução. Pulando este tick.");
            return;
        }

        try
        {
            await ExecutarCicloInternoAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado no ciclo de integração. O próximo ciclo será tentado.");
        }
        finally
        {
            _cicloLock.Release();
        }
    }

    private async Task ExecutarCicloInternoAsync(CancellationToken ct)
    {
        var condominios = (await _condominios.ListarAtivosAsync()).ToList();
        _logger.LogInformation("Iniciando ciclo para {N} condomínio(s).", condominios.Count);

        VerificarExpiracaoCertificados(condominios);

        var falhas = (await _status.ListarFalhasTemporariasAsync()).ToList();
        if (falhas.Count > 0)
        {
            _logger.LogInformation("Reprocessando {N} falha(s) temporária(s) do ciclo anterior.", falhas.Count);
            foreach (var falha in falhas)
            {
                if (ct.IsCancellationRequested) break;
                var condFalha = condominios.FirstOrDefault(c => c.Id == falha.CondominioId);
                if (condFalha is null)
                {
                    _logger.LogWarning(
                        "Condomínio {Id} não encontrado ou inativo. Pulando reprocessamento.",
                        falha.CondominioId);
                    continue;
                }
                await _status.MarcarReprocessandoAsync((int)falha.CondominioId, falha.DataInicial, falha.DataFinal);
                await ProcessarCondominioAsync(condFalha, falha.DataInicial, falha.DataFinal, ct);
                await Task.Delay(TimeSpan.FromSeconds(_settings.CondominioDelaySeconds), ct);
            }
        }

        if (ct.IsCancellationRequested) return;

        var dataInicial = DateTime.Today.ToString("yyyy-MM-dd");
        var dataFinal = dataInicial;
        var sucessos = 0;

        for (var i = 0; i < condominios.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            if (await ProcessarCondominioAsync(condominios[i], dataInicial, dataFinal, ct))
                sucessos++;

            if (i < condominios.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(_settings.CondominioDelaySeconds), ct);
        }

        if (condominios.Count > 0 && sucessos == 0)
        {
            using (_logger.BeginScope(new Dictionary<string, object> { ["alerta"] = true, ["tipo"] = "CRITICO" }))
                _logger.LogError("Todos os {Total} condomínio(s) falharam neste ciclo.", condominios.Count);
        }

        _logger.LogInformation("Ciclo concluído.");
    }

    private void VerificarExpiracaoCertificados(IEnumerable<Condominio> condominios)
    {
        foreach (var cond in condominios)
        {
            var creds = cond.Credenciais;
            if (creds is null || string.IsNullOrWhiteSpace(creds.CertificatePath)) continue;
            if (!File.Exists(creds.CertificatePath)) continue;

            try
            {
                using var cert = new X509Certificate2(creds.CertificatePath, creds.CertificatePassword);
                var diasRestantes = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).Days;
                if (diasRestantes <= _settings.CertificateAlertDaysBeforeExpiry)
                {
                    using (_logger.BeginScope(new Dictionary<string, object> { ["alerta"] = true, ["tipo"] = "CRITICO" }))
                        _logger.LogError(
                            "Certificado PFX do condomínio {CondominioId} expira em {Dias} dia(s) ({Expiracao:dd/MM/yyyy}).",
                            cond.Id, diasRestantes, cert.NotAfter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Não foi possível verificar certificado do condomínio {CondominioId}.", cond.Id);
            }
        }
    }

    private async Task<bool> ProcessarCondominioAsync(
        Condominio cond, string dataInicial, string dataFinal, CancellationToken ct)
    {
        var execucaoId = await _status.CriarExecucaoAsync(
            cond.Id, dataInicial, dataFinal, ExecucaoStatus.A_PROCESSAR);

        try
        {
            _logger.LogInformation("Processando condomínio {Id} – {Nome}", cond.Id, cond.Nome);

            var (semMovimento, arquivo) = await _retornoSicoob
                .ObterArquivoRetornoAsync(cond, dataInicial, dataFinal, ct);

            if (semMovimento)
            {
                _logger.LogInformation("Condomínio {Id}: sem movimento.", cond.Id);
                await _status.AtualizarAsync(execucaoId, ExecucaoStatus.SEM_TITULOS, totalRegistros: 0);
                return true;
            }

            await using var _ = arquivo;

            var chave = _idempotency.ComputarChave(cond.Id, dataInicial, dataFinal, arquivo!);
            if (await _idempotency.JaProcessadoAsync(chave))
            {
                _logger.LogInformation("Condomínio {Id}: já processado (idempotência). Ignorando.", cond.Id);
                await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FINALIZADO);
                return true;
            }

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.ARQUIVO_BAIXADO);

            var registros = _cnabParser.Parsear(arquivo!);
            _logger.LogInformation("Condomínio {Id}: {N} registro(s) parseados.", cond.Id, registros.Count);

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.PROCESSAMENTO_FINALIZADO,
                totalRegistros: registros.Count);

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.ENVIANDO_TITULOS);

            var nomeArquivo = $"retorno_{cond.Id}_{dataInicial}.ret";
            var creds = cond.Credenciais
                ?? throw new InvalidOperationException($"Credenciais não carregadas para condomínio {cond.Id}.");

            await _superlogica.UploadArquivoAsync(arquivo!, nomeArquivo, creds, ct);

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.ENVIADO_SUPERLOGICA);

            await _idempotency.RegistrarSucessoAsync(chave, cond.Id);
            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FINALIZADO);

            _logger.LogInformation("Condomínio {Id}: processamento concluído.", cond.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no condomínio {Id}. Seguindo para o próximo.", cond.Id);
            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FALHA_TEMPORARIA,
                mensagemErro: ex.Message);
            return false;
        }
    }
}
