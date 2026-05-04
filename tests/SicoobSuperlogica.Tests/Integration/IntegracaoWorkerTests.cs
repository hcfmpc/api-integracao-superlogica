using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;
using SicoobSuperlogica.Worker.Workers;

namespace SicoobSuperlogica.Tests.Integration;

public class IntegracaoWorkerTests
{
    private readonly Mock<ICondominioService> _condominiosMock = new();
    private readonly Mock<IRetornoSicoobService> _retornoMock = new();
    private readonly Mock<ICnabParserService> _parserMock = new();
    private readonly Mock<ISuperlogicaService> _superlogicaMock = new();
    private readonly Mock<IIdempotencyService> _idempotencyMock = new();
    private readonly Mock<IStatusService> _statusMock = new();
    private readonly Mock<IHostApplicationLifetime> _lifetimeMock = new();

    private readonly WorkerSettings _settings = new()
    {
        IntervalHours = 1,
        CondominioDelaySeconds = 0,
        PollingMaxAttempts = 1,
        PollingIntervalMinutes = 0,
        HttpTimeoutSeconds = 10,
        RetryCount = 1,
        CertificateAlertDaysBeforeExpiry = 30
    };

    public IntegracaoWorkerTests()
    {
        _superlogicaMock.Setup(s => s.UploadArquivoAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Default: no pending failures
        _statusMock.Setup(s => s.ListarFalhasTemporariasAsync())
            .ReturnsAsync([]);
    }

    private IntegracaoWorker CriarWorker() => new(
        _condominiosMock.Object,
        _retornoMock.Object,
        _parserMock.Object,
        _superlogicaMock.Object,
        _idempotencyMock.Object,
        _statusMock.Object,
        _settings,
        _lifetimeMock.Object,
        NullLogger<IntegracaoWorker>.Instance);

    private static Condominio CriarCondominio(int id) => new()
    {
        Id = id, Nome = $"Cond {id}", NumeroContrato = $"C{id}",
        NumeroConta = $"CC{id}", Cooperativa = "001",
        Credenciais = new CondominioCredenciais("cli", "sec", "", "", "app", "acc")
    };

    private static Stream CriarStream() =>
        new MemoryStream(System.Text.Encoding.Latin1.GetBytes("CNAB240CONTEUDO"));

    [Fact]
    public async Task ExecutarCiclo_FalhaParcial_UmCondominioFalha_OutrosContinuam()
    {
        var condominios = new[] { CriarCondominio(1), CriarCondominio(2), CriarCondominio(3) };
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync(condominios);

        var execucaoSeq = 0;
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(() => ++execucaoSeq);

        var streamCond1 = CriarStream();
        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.Is<Condominio>(c => c.Id == 1), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)streamCond1));

        _idempotencyMock.Setup(i => i.ComputarChave(1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-cond1");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-cond1"))
            .ReturnsAsync(false);
        _parserMock.Setup(p => p.Parsear(It.IsAny<Stream>())).Returns([]);

        // Condomínio 2: falha na extração Sicoob
        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.Is<Condominio>(c => c.Id == 2), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Sicoob indisponível"));

        var streamCond3 = CriarStream();
        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.Is<Condominio>(c => c.Id == 3), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)streamCond3));

        _idempotencyMock.Setup(i => i.ComputarChave(3, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-cond3");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-cond3"))
            .ReturnsAsync(false);

        var statusUpdates = new List<(int ExecucaoId, ExecucaoStatus Status)>();
        _statusMock.Setup(s => s.AtualizarAsync(
                It.IsAny<int>(), It.IsAny<ExecucaoStatus>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .Callback<int, ExecucaoStatus, int?, string?>((id, st, _, __) => statusUpdates.Add((id, st)))
            .Returns(Task.CompletedTask);

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var workerTask = worker.StartAsync(cts.Token);
        await Task.Delay(500, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        await workerTask;

        _statusMock.Verify(s => s.CriarExecucaoAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), ExecucaoStatus.A_PROCESSAR),
            Times.Exactly(3));

        statusUpdates.Should().Contain(u => u.Status == ExecucaoStatus.FALHA_TEMPORARIA);
        statusUpdates.Count(u => u.Status == ExecucaoStatus.FINALIZADO).Should().BeGreaterThanOrEqualTo(2);

        _retornoMock.Verify(r => r.ObterArquivoRetornoAsync(
            It.Is<Condominio>(c => c.Id == 3),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecutarCiclo_SemMovimento_RegistraStatusSemTitulos()
    {
        var cond = CriarCondominio(1);
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync([cond]);
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(1);

        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                cond, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (Stream?)null));

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        _statusMock.Verify(s => s.AtualizarAsync(
            1, ExecucaoStatus.SEM_TITULOS, 0, null), Times.Once);

        _idempotencyMock.Verify(i => i.JaProcessadoAsync(It.IsAny<string>()), Times.Never);
        _superlogicaMock.Verify(s => s.UploadArquivoAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecutarCiclo_IdempotenciaAtiva_PulaProcessamento()
    {
        var cond = CriarCondominio(1);
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync([cond]);
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(1);

        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                cond, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)CriarStream()));

        _idempotencyMock.Setup(i => i.ComputarChave(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-existente");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-existente"))
            .ReturnsAsync(true);

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        _parserMock.Verify(p => p.Parsear(It.IsAny<Stream>()), Times.Never);
        _superlogicaMock.Verify(s => s.UploadArquivoAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _statusMock.Verify(s => s.AtualizarAsync(1, ExecucaoStatus.FINALIZADO, null, null), Times.Once);
    }

    [Fact]
    public async Task ExecutarCiclo_FalhaSuperlogica_RegistraFalhaTemporaria()
    {
        var cond = CriarCondominio(1);
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync([cond]);
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(1);

        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                cond, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)CriarStream()));

        _idempotencyMock.Setup(i => i.ComputarChave(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-nova");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-nova")).ReturnsAsync(false);
        _parserMock.Setup(p => p.Parsear(It.IsAny<Stream>())).Returns([]);

        _superlogicaMock.Setup(s => s.UploadArquivoAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Upload falhou com status 503"));

        var statusUpdates = new List<(int ExecucaoId, ExecucaoStatus Status)>();
        _statusMock.Setup(s => s.AtualizarAsync(
                It.IsAny<int>(), It.IsAny<ExecucaoStatus>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .Callback<int, ExecucaoStatus, int?, string?>((id, st, _, __) => statusUpdates.Add((id, st)))
            .Returns(Task.CompletedTask);

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        statusUpdates.Should().Contain(u => u.Status == ExecucaoStatus.FALHA_TEMPORARIA);
        _idempotencyMock.Verify(i => i.RegistrarSucessoAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecutarCiclo_UploadBemSucedido_TransicionaStatusCorretamente()
    {
        var cond = CriarCondominio(1);
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync([cond]);
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(1);

        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                cond, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)CriarStream()));

        _idempotencyMock.Setup(i => i.ComputarChave(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-nova");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-nova")).ReturnsAsync(false);
        _parserMock.Setup(p => p.Parsear(It.IsAny<Stream>())).Returns([]);

        var statusUpdates = new List<ExecucaoStatus>();
        _statusMock.Setup(s => s.AtualizarAsync(
                It.IsAny<int>(), It.IsAny<ExecucaoStatus>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .Callback<int, ExecucaoStatus, int?, string?>((_, st, _, __) => statusUpdates.Add(st))
            .Returns(Task.CompletedTask);

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        statusUpdates.Should().ContainInOrder(
            ExecucaoStatus.ARQUIVO_BAIXADO,
            ExecucaoStatus.PROCESSAMENTO_FINALIZADO,
            ExecucaoStatus.ENVIANDO_TITULOS,
            ExecucaoStatus.ENVIADO_SUPERLOGICA,
            ExecucaoStatus.FINALIZADO);

        _superlogicaMock.Verify(s => s.UploadArquivoAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CondominioCredenciais>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _idempotencyMock.Verify(i => i.RegistrarSucessoAsync("chave-nova", 1), Times.Once);
    }

    [Fact]
    public async Task ExecutarCiclo_FalhaTemporariaPendente_EReprocessadaNoProximoCiclo()
    {
        var cond = CriarCondominio(5);
        _condominiosMock.Setup(c => c.ListarAtivosAsync()).ReturnsAsync([cond]);

        _statusMock.Setup(s => s.ListarFalhasTemporariasAsync())
            .ReturnsAsync([new ExecucaoFalha(5, "2026-01-01", "2026-01-01")]);
        _statusMock.Setup(s => s.MarcarReprocessandoAsync(5, "2026-01-01", "2026-01-01"))
            .Returns(Task.CompletedTask);
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(1);

        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.IsAny<Condominio>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (Stream?)null));

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(400, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        _statusMock.Verify(s => s.MarcarReprocessandoAsync(5, "2026-01-01", "2026-01-01"), Times.Once);
        _retornoMock.Verify(r => r.ObterArquivoRetornoAsync(
            cond, "2026-01-01", "2026-01-01", It.IsAny<CancellationToken>()), Times.Once);
    }
}
