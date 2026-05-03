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
    private readonly IIdempotencyService _idempotency;
    private readonly IStatusService _status;
    private readonly WorkerSettings _settings;
    private readonly ILogger<IntegracaoWorker> _logger;
    private readonly SemaphoreSlim _cicloLock = new(1, 1);

    public IntegracaoWorker(
        ICondominioService condominios,
        IRetornoSicoobService retornoSicoob,
        ICnabParserService cnabParser,
        IIdempotencyService idempotency,
        IStatusService status,
        WorkerSettings settings,
        ILogger<IntegracaoWorker> logger)
    {
        _condominios = condominios;
        _retornoSicoob = retornoSicoob;
        _cnabParser = cnabParser;
        _idempotency = idempotency;
        _status = status;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("IntegracaoWorker iniciado. Intervalo: {H}h", _settings.IntervalHours);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.IntervalHours));

        await ExecutarCicloAsync(ct);

        while (await timer.WaitForNextTickAsync(ct))
            await ExecutarCicloAsync(ct);
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
            var condominios = (await _condominios.ListarAtivosAsync()).ToList();
            _logger.LogInformation("Iniciando ciclo para {N} condomínio(s).", condominios.Count);

            var dataInicial = DateTime.Today.ToString("yyyy-MM-dd");
            var dataFinal = dataInicial;

            foreach (var cond in condominios)
            {
                if (ct.IsCancellationRequested) break;

                await ProcessarCondominioAsync(cond, dataInicial, dataFinal, ct);

                if (condominios.Last() != cond)
                    await Task.Delay(TimeSpan.FromSeconds(_settings.CondominioDelaySeconds), ct);
            }

            _logger.LogInformation("Ciclo concluído.");
        }
        finally
        {
            _cicloLock.Release();
        }
    }

    private async Task ProcessarCondominioAsync(
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
                return;
            }

            await using var _ = arquivo;

            // Idempotência: evitar reprocessamento do mesmo arquivo
            var chave = _idempotency.ComputarChave(cond.Id, dataInicial, dataFinal, arquivo!);
            if (await _idempotency.JaProcessadoAsync(chave))
            {
                _logger.LogInformation("Condomínio {Id}: já processado (idempotência). Ignorando.", cond.Id);
                await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FINALIZADO);
                return;
            }

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.ARQUIVO_BAIXADO);

            var registros = _cnabParser.Parsear(arquivo!);
            _logger.LogInformation("Condomínio {Id}: {N} registro(s) parseados.", cond.Id, registros.Count);

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.PROCESSAMENTO_FINALIZADO,
                totalRegistros: registros.Count);

            // Fase 3: upload para Superlógica (SuperlogicaService)

            await _idempotency.RegistrarSucessoAsync(chave, cond.Id);
            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FINALIZADO);

            _logger.LogInformation("Condomínio {Id}: processamento concluído.", cond.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no condomínio {Id}. Seguindo para o próximo.", cond.Id);

            await _status.AtualizarAsync(execucaoId, ExecucaoStatus.FALHA_TEMPORARIA,
                mensagemErro: ex.Message);
        }
    }
}
