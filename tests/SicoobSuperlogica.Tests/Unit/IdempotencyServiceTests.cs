using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SicoobSuperlogica.Worker.Services;

namespace SicoobSuperlogica.Tests.Unit;

public class IdempotencyServiceTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private IdempotencyService _sut = null!;

    public async Task InitializeAsync()
    {
        // Named in-memory DB com shared cache: múltiplas conexões enxergam o mesmo banco
        var connStr = $"Data Source=file:idempotencia_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _conn = new SqliteConnection(connStr);
        await _conn.OpenAsync();   // mantém o banco vivo enquanto o teste roda

        await _conn.ExecuteAsync("""
            CREATE TABLE idempotencia (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                hash          TEXT    NOT NULL UNIQUE,
                condominio_id INTEGER NOT NULL,
                executado_em  TEXT    NOT NULL,
                status        TEXT    NOT NULL
            );
            """);

        _sut = new IdempotencyService(connStr, NullLogger<IdempotencyService>.Instance);
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    private static Stream CriarStream(string conteudo = "CNAB240CONTEUDO")
        => new MemoryStream(Encoding.UTF8.GetBytes(conteudo));

    [Fact]
    public void ComputarChave_MesmoInput_RetornaMesmaChave()
    {
        var stream1 = CriarStream("ABC");
        var stream2 = CriarStream("ABC");

        var chave1 = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream1);
        var chave2 = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream2);

        chave1.Should().Be(chave2, "mesmos inputs devem produzir o mesmo hash");
    }

    [Fact]
    public void ComputarChave_DiferenteConteudo_RetornaChavesDiferentes()
    {
        var stream1 = CriarStream("ARQUIVO1");
        var stream2 = CriarStream("ARQUIVO2");

        var chave1 = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream1);
        var chave2 = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream2);

        chave1.Should().NotBe(chave2);
    }

    [Fact]
    public void ComputarChave_ReposicionaStreamParaZero()
    {
        var stream = CriarStream("DADOS");
        _ = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream);

        stream.Position.Should().Be(0, "o stream deve ser reposicionado em 0 após compute");
    }

    [Fact]
    public async Task JaProcessado_HashInexistente_RetornaFalse()
    {
        var resultado = await _sut.JaProcessadoAsync("hashqualquer");
        resultado.Should().BeFalse();
    }

    [Fact]
    public async Task JaProcessado_HashExistenteSucesso_RetornaTrue()
    {
        var stream = CriarStream("FILE");
        var chave = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream);
        await _sut.RegistrarSucessoAsync(chave, condominioId: 1);

        var resultado = await _sut.JaProcessadoAsync(chave);
        resultado.Should().BeTrue();
    }

    [Fact]
    public async Task RegistrarSucesso_DuplicataIgnorada()
    {
        var stream = CriarStream("FILE");
        var chave = _sut.ComputarChave(1, "2026-05-01", "2026-05-01", stream);

        await _sut.RegistrarSucessoAsync(chave, condominioId: 1);

        // Segunda chamada com mesma chave não deve lançar exceção (INSERT OR IGNORE)
        var act = async () => await _sut.RegistrarSucessoAsync(chave, condominioId: 1);
        await act.Should().NotThrowAsync();
    }
}
