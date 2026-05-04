using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface ISuperlogicaService
{
    Task UploadArquivoAsync(Stream arquivo, string nomeArquivo, CondominioCredenciais credenciais, CancellationToken ct);
}

public sealed class SuperlogicaService : ISuperlogicaService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SuperlogicaSettings _settings;
    private readonly ILogger<SuperlogicaService> _logger;

    public SuperlogicaService(
        IHttpClientFactory httpFactory,
        SuperlogicaSettings settings,
        ILogger<SuperlogicaService> logger)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task UploadArquivoAsync(Stream arquivo, string nomeArquivo, CondominioCredenciais credenciais, CancellationToken ct)
    {
        if (arquivo.CanSeek)
            arquivo.Position = 0;

        // fileContent é gerenciado por content (MultipartFormDataContent.Dispose chama Dispose nos parts)
        var fileContent = new StreamContent(arquivo);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var content = new MultipartFormDataContent();
        content.Add(fileContent, "arquivo", nomeArquivo);

        var uploadUrl = $"{_settings.BaseUrl.TrimEnd('/')}/{_settings.UploadPath.TrimStart('/')}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = content
        };
        request.Headers.Add("app_token", credenciais.AppTokenSuperlogica);
        request.Headers.Add("access_token", credenciais.AccessTokenSuperlogica);

        var client = _httpFactory.CreateClient("superlogica");
        var response = await client.SendAsync(request, ct);

        if ((int)response.StatusCode >= 300)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Superlógica retornou {StatusCode} para {Arquivo}: {Body}",
                (int)response.StatusCode, nomeArquivo, body);
            throw new HttpRequestException(
                $"Upload falhou com status {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        _logger.LogInformation("Arquivo {Arquivo} enviado à Superlógica com status {StatusCode}.",
            nomeArquivo, (int)response.StatusCode);
    }
}
