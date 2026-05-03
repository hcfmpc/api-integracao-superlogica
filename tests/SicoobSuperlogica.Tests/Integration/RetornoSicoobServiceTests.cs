using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SicoobSuperlogica.Tests.Integration;

public class RetornoSicoobServiceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly RetornoSicoobService _sut;
    private readonly Mock<IAuthService> _authMock = new();
    private readonly IHttpClientFactory _httpFactory;

    private const string DataInicial = "2026-05-01";
    private const string DataFinal = "2026-05-01";

    public RetornoSicoobServiceTests()
    {
        _server = WireMockServer.Start();
        _authMock.Setup(a => a.ObterTokenAsync(It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("tok-test");

        var httpClient = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("sicoob")).Returns(httpClient);
        _httpFactory = factoryMock.Object;

        _sut = new RetornoSicoobService(
            _authMock.Object,
            _httpFactory,
            new SicoobSettings { ApiBaseUrl = _server.Urls[0] },
            new WorkerSettings { PollingMaxAttempts = 3, PollingIntervalMinutes = 0 },
            NullLogger<RetornoSicoobService>.Instance);
    }

    private static Condominio CriarCondominio() => new()
    {
        Id = 1, Nome = "Teste", NumeroContrato = "C1", NumeroConta = "CC1", Cooperativa = "001",
        Credenciais = new CondominioCredenciais("cli", "sec", "", "", "app", "acc")
    };

    [Fact]
    public async Task ObterArquivo_SemMovimento_RetornaSemMovimentoTrue()
    {
        ConfigurarSolicitar("sol-001");
        ConfigurarPolling("sol-001", SicoobRetornoStatus.SemMovimento, null);

        var (semMov, arquivo) = await _sut.ObterArquivoRetornoAsync(
            CriarCondominio(), DataInicial, DataFinal, CancellationToken.None);

        semMov.Should().BeTrue();
        arquivo.Should().BeNull();
    }

    [Fact]
    public async Task ObterArquivo_Gerado_RetornaStream()
    {
        var downloadPath = "/download/cnab.ret";
        ConfigurarSolicitar("sol-002");
        ConfigurarPolling("sol-002", SicoobRetornoStatus.Gerado, $"{_server.Urls[0]}{downloadPath}");
        _server.Given(Request.Create().WithPath(downloadPath).UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody("CNAB240CONTENT"));

        var (semMov, arquivo) = await _sut.ObterArquivoRetornoAsync(
            CriarCondominio(), DataInicial, DataFinal, CancellationToken.None);

        semMov.Should().BeFalse();
        arquivo.Should().NotBeNull();
        using var reader = new StreamReader(arquivo!);
        (await reader.ReadToEndAsync()).Should().Contain("CNAB240CONTENT");
    }

    [Fact]
    public async Task ObterArquivo_PollingEsgotado_ThrowsTimeoutException()
    {
        ConfigurarSolicitar("sol-003");
        // Always returns EM_PROCESSAMENTO
        _server.Given(Request.Create().WithPath("/boletos/arquivo-retorno/sol-003").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody($$"""{"status":"{{SicoobRetornoStatus.EmProcessamento}}","urlDownload":null}"""));

        var act = async () => await _sut.ObterArquivoRetornoAsync(
            CriarCondominio(), DataInicial, DataFinal, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    private void ConfigurarSolicitar(string idSolicitacao)
    {
        _server.Given(Request.Create().WithPath("/boletos/arquivo-retorno/solicitar").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(202)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody($$"""{"idSolicitacao":"{{idSolicitacao}}","status":"EM_PROCESSAMENTO"}"""));
    }

    private void ConfigurarPolling(string id, string status, string? urlDownload)
    {
        var url = urlDownload is null ? "null" : $"\"{urlDownload}\"";
        _server.Given(Request.Create().WithPath($"/boletos/arquivo-retorno/{id}").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody($$"""{"status":"{{status}}","urlDownload":{{url}}}"""));
    }

    public void Dispose() => _server.Stop();
}
