using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;
using SicoobSuperlogica.Worker.Workers;

namespace SicoobSuperlogica.Tests.Integration;

public class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");

    public ApiTestFactory()
    {
        // Read by WebApplication.CreateBuilder when it initializes IConfiguration
        Environment.SetEnvironmentVariable("SICOOB_MASTER_PASSWORD", "test-password-fase5");
        Environment.SetEnvironmentVariable("Database__Path", _dbPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace connection string singleton so all services use the test DB
            var connDesc = services.FirstOrDefault(d =>
                d.ServiceType == typeof(string) &&
                d.ImplementationInstance is string s &&
                s.StartsWith("Data Source="));
            if (connDesc is not null) services.Remove(connDesc);
            services.AddSingleton($"Data Source={_dbPath}");

            // Remove the background worker — prevents StopApplication() calls in tests
            var workerDesc = services
                .Where(d => d.ImplementationType == typeof(IntegracaoWorker))
                .ToList();
            foreach (var d in workerDesc) services.Remove(d);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Apply DB schema to the test database before any test runs
        var connStr = $"Data Source={_dbPath}";
        DatabaseInitializer.InitializeAsync(connStr, NullLogger.Instance).GetAwaiter().GetResult();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("Database__Path", null);
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

public class DashboardApiTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public DashboardApiTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStatus_Retorna200ComArrayJson()
    {
        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task GetCondominios_Retorna200ComArrayJson()
    {
        var response = await _client.GetAsync("/api/condominios");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task GetExecucao_IdInexistente_Retorna404()
    {
        var response = await _client.GetAsync("/api/execucoes/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExecucao_IdExistente_Retorna200ComDados()
    {
        using var scope = _factory.Services.CreateScope();
        var condService = scope.ServiceProvider.GetRequiredService<ICondominioService>();
        var statusService = scope.ServiceProvider.GetRequiredService<IStatusService>();

        var condId = await condService.CriarAsync(new Condominio
        {
            Nome = "Cond API Test",
            NumeroContrato = "CT-API",
            NumeroConta = "CC-API",
            Cooperativa = "001",
            Credenciais = new CondominioCredenciais("cli", "sec", "", "", "app", "acc")
        });

        var execId = await statusService.CriarExecucaoAsync(
            condId, "2026-05-03", "2026-05-03", ExecucaoStatus.FINALIZADO);
        await statusService.AtualizarAsync(execId, ExecucaoStatus.FINALIZADO, totalRegistros: 3);

        var response = await _client.GetAsync($"/api/execucoes/{execId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("FINALIZADO");
        body.Should().Contain("2026-05-03");
    }

    [Fact]
    public async Task GetStatus_ComExecucaoCriada_RetornaUltimaExecucaoPorCondominio()
    {
        using var scope = _factory.Services.CreateScope();
        var condService = scope.ServiceProvider.GetRequiredService<ICondominioService>();
        var statusService = scope.ServiceProvider.GetRequiredService<IStatusService>();

        var condId = await condService.CriarAsync(new Condominio
        {
            Nome = "Residencial Horizonte",
            NumeroContrato = "CT-HOR",
            NumeroConta = "CC-HOR",
            Cooperativa = "001",
            Credenciais = new CondominioCredenciais("cli", "sec", "", "", "app", "acc")
        });

        await statusService.CriarExecucaoAsync(
            condId, "2026-05-01", "2026-05-01", ExecucaoStatus.FALHA_TEMPORARIA);
        var execId2 = await statusService.CriarExecucaoAsync(
            condId, "2026-05-03", "2026-05-03", ExecucaoStatus.FINALIZADO);
        await statusService.AtualizarAsync(execId2, ExecucaoStatus.FINALIZADO, totalRegistros: 7);

        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Residencial Horizonte");
        body.Should().Contain("FINALIZADO");
    }

    [Fact]
    public async Task FiltroIp_RequisicaoLocalhost_Permitida()
    {
        // TestServer uses loopback internally — non-403 proves the IP filter allowed it
        var response = await _client.GetAsync("/api/status");
        ((int)response.StatusCode).Should().NotBe(403);
    }
}
