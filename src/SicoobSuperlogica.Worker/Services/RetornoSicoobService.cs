using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface IRetornoSicoobService
{
    Task<(bool SemMovimento, Stream? Arquivo)> ObterArquivoRetornoAsync(
        Condominio condominio,
        string dataInicial,
        string dataFinal,
        CancellationToken ct);
}

public sealed class RetornoSicoobService : IRetornoSicoobService
{
    private readonly IAuthService _auth;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SicoobSettings _settings;
    private readonly WorkerSettings _workerSettings;
    private readonly ILogger<RetornoSicoobService> _logger;

    public RetornoSicoobService(
        IAuthService auth,
        IHttpClientFactory httpFactory,
        SicoobSettings settings,
        WorkerSettings workerSettings,
        ILogger<RetornoSicoobService> logger)
    {
        _auth = auth;
        _httpFactory = httpFactory;
        _settings = settings;
        _workerSettings = workerSettings;
        _logger = logger;
    }

    public async Task<(bool SemMovimento, Stream? Arquivo)> ObterArquivoRetornoAsync(
        Condominio condominio,
        string dataInicial,
        string dataFinal,
        CancellationToken ct)
    {
        var creds = condominio.Credenciais
            ?? throw new InvalidOperationException($"Credenciais não carregadas para condomínio {condominio.Id}.");

        var token = await _auth.ObterTokenAsync(creds, ct);
        var client = CriarClienteAutenticado(token);

        var idSolicitacao = await SolicitarArquivoAsync(client, condominio, dataInicial, dataFinal, ct);

        _logger.LogInformation(
            "Polling iniciado para condomínio {Id}, solicitação {SolicitacaoId}",
            condominio.Id, idSolicitacao);

        for (var tentativa = 1; tentativa <= _workerSettings.PollingMaxAttempts; tentativa++)
        {
            await Task.Delay(TimeSpan.FromMinutes(_workerSettings.PollingIntervalMinutes), ct);

            var polling = await ConsultarStatusAsync(client, idSolicitacao, ct);

            if (polling.Status == SicoobRetornoStatus.SemMovimento)
            {
                _logger.LogInformation("Condomínio {Id}: SEM_MOVIMENTO no período.", condominio.Id);
                return (true, null);
            }

            if (polling.Status == SicoobRetornoStatus.Gerado && !string.IsNullOrEmpty(polling.UrlDownload))
            {
                _logger.LogInformation("Condomínio {Id}: arquivo gerado. Iniciando download.", condominio.Id);
                var stream = await DownloadAsync(client, polling.UrlDownload, ct);
                return (false, stream);
            }

            _logger.LogInformation(
                "Condomínio {Id}: tentativa {Tentativa}/{Max}, status={Status}",
                condominio.Id, tentativa, _workerSettings.PollingMaxAttempts, polling.Status);
        }

        throw new TimeoutException(
            $"Condomínio {condominio.Id}: polling esgotado após {_workerSettings.PollingMaxAttempts} tentativas.");
    }

    private async Task<string> SolicitarArquivoAsync(
        HttpClient client,
        Condominio condominio,
        string dataInicial,
        string dataFinal,
        CancellationToken ct)
    {
        var body = new SolicitacaoRetornoRequest(
            condominio.NumeroContrato,
            condominio.NumeroConta,
            dataInicial,
            dataFinal);

        var response = await client.PostAsJsonAsync(
            $"{_settings.ApiBaseUrl}/boletos/arquivo-retorno/solicitar", body, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SolicitacaoRetornoResponse>(ct)
            ?? throw new InvalidOperationException("Resposta de solicitação nula.");

        return result.IdSolicitacao;
    }

    private async Task<PollingRetornoResponse> ConsultarStatusAsync(
        HttpClient client, string idSolicitacao, CancellationToken ct)
    {
        var response = await client.GetAsync(
            $"{_settings.ApiBaseUrl}/boletos/arquivo-retorno/{idSolicitacao}", ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PollingRetornoResponse>(ct)
            ?? throw new InvalidOperationException("Resposta de polling nula.");
    }

    private static async Task<Stream> DownloadAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var mem = new MemoryStream();
        await response.Content.CopyToAsync(mem, ct);
        mem.Position = 0;
        return mem;
    }

    private HttpClient CriarClienteAutenticado(string token)
    {
        var client = _httpFactory.CreateClient("sicoob");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
