using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Services;

namespace SicoobSuperlogica.Worker.Workers;

public sealed class IntegracaoWorker : BackgroundService
{
    private readonly ICondominioService _condominios;
    private readonly IRetornoSicoobService _retornoSicoob;
    private readonly WorkerSettings _settings;
    private readonly ILogger<IntegracaoWorker> _logger;
    private readonly SemaphoreSlim _cicloLock = new(1, 1);

    public IntegracaoWorker(
        ICondominioService condominios,
        IRetornoSicoobService retornoSicoob,
        WorkerSettings settings,
        ILogger<IntegracaoWorker> logger)
    {
        _condominios = condominios;
        _retornoSicoob = retornoSicoob;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("IntegracaoWorker iniciado. Intervalo: {H}h", _settings.IntervalHours);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.IntervalHours));

        // Execute immediately on startup, then repeat on interval
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

                try
                {
                    _logger.LogInformation("Processando condomínio {Id} – {Nome}", cond.Id, cond.Nome);

                    var (semMovimento, arquivo) = await _retornoSicoob
                        .ObterArquivoRetornoAsync(cond, dataInicial, dataFinal, ct);

                    if (semMovimento)
                    {
                        _logger.LogInformation("Condomínio {Id}: sem movimento.", cond.Id);
                    }
                    else
                    {
                        await using (arquivo) { /* Fases 2-3: parser + upload */ }
                        _logger.LogInformation("Condomínio {Id}: arquivo obtido.", cond.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no condomínio {Id}. Seguindo para o próximo.", cond.Id);
                }

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
}
