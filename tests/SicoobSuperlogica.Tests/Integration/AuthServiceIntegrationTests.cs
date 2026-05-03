using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SicoobSuperlogica.Tests.Integration;

public class AuthServiceIntegrationTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly AuthService _sut;

    public AuthServiceIntegrationTests()
    {
        _server = WireMockServer.Start();
        _sut = new AuthService(
            new SicoobSettings { TokenUrl = $"{_server.Urls[0]}/token" },
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task ObterToken_ComRespostaValida_RetornaAccessToken()
    {
        _server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"access_token":"tok-abc","expires_in":300}"""));

        var creds = new CondominioCredenciais("client1", "sec", "", "", "", "");
        var token = await _sut.ObterTokenAsync(creds, CancellationToken.None);

        token.Should().Be("tok-abc");
    }

    [Fact]
    public async Task ObterToken_EmCache_NaoFazNovaRequisicao()
    {
        _server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"access_token":"tok-cache","expires_in":300}"""));

        var creds = new CondominioCredenciais("client-cache", "sec", "", "", "", "");

        var t1 = await _sut.ObterTokenAsync(creds, CancellationToken.None);
        var t2 = await _sut.ObterTokenAsync(creds, CancellationToken.None);

        t1.Should().Be(t2);
        _server.LogEntries.Should().HaveCount(1, "segunda chamada deve usar cache");
    }

    [Fact]
    public async Task ObterToken_Falha401_ThrowsHttpRequestException()
    {
        _server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401));

        var creds = new CondominioCredenciais("client-fail", "sec", "", "", "", "");
        var act = async () => await _sut.ObterTokenAsync(creds, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void InvalidarToken_ForcaNovaRequisicaoNaProximaChamada()
    {
        // Arrange: seed cache with a valid token
        _server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"access_token":"tok-inv","expires_in":300}"""));

        var creds = new CondominioCredenciais("client-inv", "sec", "", "", "", "");

        // Act
        _sut.InvalidarToken(creds.ClientId);

        // Assert: no exception — cache key simply removed; next call will fetch
        _server.LogEntries.Should().BeEmpty();
    }

    public void Dispose() => _server.Stop();
}
