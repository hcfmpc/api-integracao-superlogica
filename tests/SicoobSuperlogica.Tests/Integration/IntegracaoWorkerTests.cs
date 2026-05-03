using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;
using SicoobSuperlogica.Worker.Workers;

namespace SicoobSuperlogica.Tests.Integration;

/// <summary>
/// Testa o <see cref="IntegracaoWorker"/> em nível de integração com mocks
/// de todos os serviços externos, focando no comportamento de falha parcial.
/// </summary>
public class IntegracaoWorkerTests
{
    private readonly Mock<ICondominioService> _condominiosMock = new();
    private readonly Mock<IRetornoSicoobService> _retornoMock = new();
    private readonly Mock<ICnabParserService> _parserMock = new();
    private readonly Mock<IIdempotencyService> _idempotencyMock = new();
    private readonly Mock<IStatusService> _statusMock = new();

    private readonly WorkerSettings _settings = new()
    {
        IntervalHours = 1,
        CondominioDelaySeconds = 0,  // sem delay em testes
        PollingMaxAttempts = 1,
        PollingIntervalMinutes = 0,
        HttpTimeoutSeconds = 10,
        RetryCount = 1
    };

    private IntegracaoWorker CriarWorker() => new(
        _condominiosMock.Object,
        _retornoMock.Object,
        _parserMock.Object,
        _idempotencyMock.Object,
        _statusMock.Object,
        _settings,
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
        // Arrange: 3 condomínios – o do meio lança exceção
        var condominios = new[] { CriarCondominio(1), CriarCondominio(2), CriarCondominio(3) };
        _condominiosMock.Setup(c => c.ListarAtivosAsync())
                        .ReturnsAsync(condominios);

        // Status: cria execução e retorna IDs sequenciais
        var execucaoSeq = 0;
        _statusMock.Setup(s => s.CriarExecucaoAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ExecucaoStatus>()))
            .ReturnsAsync(() => ++execucaoSeq);

        // Condomínio 1: arquivo baixado e parseado com sucesso
        var streamCond1 = CriarStream();
        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.Is<Condominio>(c => c.Id == 1), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (Stream?)streamCond1));

        _idempotencyMock.Setup(i => i.ComputarChave(1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .Returns("chave-cond1");
        _idempotencyMock.Setup(i => i.JaProcessadoAsync("chave-cond1"))
            .ReturnsAsync(false);
        _parserMock.Setup(p => p.Parsear(It.IsAny<Stream>()))
            .Returns([]);

        // Condomínio 2: lança exceção (falha)
        _retornoMock.Setup(r => r.ObterArquivoRetornoAsync(
                It.Is<Condominio>(c => c.Id == 2), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Sicoob indisponível"));

        // Condomínio 3: arquivo baixado e parseado com sucesso
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

        // Act: executa um ciclo completo
        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Invoca via ExecuteAsync e cancela após o primeiro ciclo
        var workerTask = worker.StartAsync(cts.Token);
        await Task.Delay(500, CancellationToken.None); // tempo para o ciclo completar
        await worker.StopAsync(CancellationToken.None);
        await workerTask;

        // Assert: execuções foram criadas para os 3 condomínios
        _statusMock.Verify(s => s.CriarExecucaoAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), ExecucaoStatus.A_PROCESSAR),
            Times.Exactly(3));

        // Condomínio 2 deve ter FALHA_TEMPORARIA
        statusUpdates.Should().Contain(u => u.Status == ExecucaoStatus.FALHA_TEMPORARIA);

        // Condomínios 1 e 3 devem ter FINALIZADO
        statusUpdates.Count(u => u.Status == ExecucaoStatus.FINALIZADO).Should().BeGreaterThanOrEqualTo(2);

        // Condomínio 2 não deve ter parado o processamento dos demais
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

        // Idempotency não deve ser verificada para SEM_MOVIMENTO
        _idempotencyMock.Verify(i => i.JaProcessadoAsync(It.IsAny<string>()), Times.Never);
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
            .ReturnsAsync(true);  // já processado

        var worker = CriarWorker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Parser não deve ser chamado quando idempotência bloqueia
        _parserMock.Verify(p => p.Parsear(It.IsAny<Stream>()), Times.Never);

        // Status final deve ser FINALIZADO (ignorado silenciosamente)
        _statusMock.Verify(s => s.AtualizarAsync(1, ExecucaoStatus.FINALIZADO, null, null), Times.Once);
    }
}
