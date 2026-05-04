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

public class SuperlogicaServiceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly SuperlogicaService _sut;
    private readonly IHttpClientFactory _httpFactory;

    private const string UploadPath = "/financeiro/cobranca/retorno";

    private static readonly CondominioCredenciais Credenciais =
        new("cli", "sec", "", "", "app-tok-123", "acc-tok-456");

    public SuperlogicaServiceTests()
    {
        _server = WireMockServer.Start();

        var httpClient = new HttpClient();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("superlogica")).Returns(httpClient);
        _httpFactory = factoryMock.Object;

        _sut = new SuperlogicaService(
            _httpFactory,
            new SuperlogicaSettings { BaseUrl = _server.Urls[0], UploadPath = UploadPath },
            NullLogger<SuperlogicaService>.Instance);
    }

    [Fact]
    public async Task Upload_ComSucesso_NaoLancaExcecao()
    {
        _server.Given(Request.Create().WithPath(UploadPath).UsingPost())
               .RespondWith(Response.Create().WithStatusCode(200));

        var arquivo = new MemoryStream("CNAB240CONTEUDO"u8.ToArray());
        var act = async () => await _sut.UploadArquivoAsync(arquivo, "retorno_1.ret", Credenciais, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Upload_ComSucesso_EnviaHeadersCorretos()
    {
        _server.Given(Request.Create()
                   .WithPath(UploadPath)
                   .UsingPost()
                   .WithHeader("app_token", "app-tok-123")
                   .WithHeader("access_token", "acc-tok-456"))
               .RespondWith(Response.Create().WithStatusCode(200));

        var arquivo = new MemoryStream("CNAB240CONTEUDO"u8.ToArray());
        var act = async () => await _sut.UploadArquivoAsync(arquivo, "retorno_1.ret", Credenciais, CancellationToken.None);

        await act.Should().NotThrowAsync();

        _server.LogEntries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Upload_Erro4xx_LancaHttpRequestException()
    {
        _server.Given(Request.Create().WithPath(UploadPath).UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(422)
                   .WithBody("{\"mensagem\":\"arquivo inválido\"}"));

        var arquivo = new MemoryStream("CNAB_INVALIDO"u8.ToArray());
        var act = async () => await _sut.UploadArquivoAsync(arquivo, "retorno_1.ret", Credenciais, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*422*");
    }

    [Fact]
    public async Task Upload_Erro5xx_LancaHttpRequestException()
    {
        _server.Given(Request.Create().WithPath(UploadPath).UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(503)
                   .WithBody("Service Unavailable"));

        var arquivo = new MemoryStream("CNAB240CONTEUDO"u8.ToArray());
        var act = async () => await _sut.UploadArquivoAsync(arquivo, "retorno_1.ret", Credenciais, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*503*");
    }

    [Fact]
    public async Task Upload_StreamJaLida_ReposicionaAntesDeEnviar()
    {
        _server.Given(Request.Create().WithPath(UploadPath).UsingPost())
               .RespondWith(Response.Create().WithStatusCode(200));

        var conteudo = "CNAB240CONTEUDO"u8.ToArray();
        var arquivo = new MemoryStream(conteudo);
        // Avança a posição para simular que o stream já foi lido
        arquivo.Position = conteudo.Length;

        var act = async () => await _sut.UploadArquivoAsync(arquivo, "retorno_1.ret", Credenciais, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public void Dispose() => _server.Stop();
}
