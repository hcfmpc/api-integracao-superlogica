using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SicoobSuperlogica.Worker.Models;
using SicoobSuperlogica.Worker.Services;

namespace SicoobSuperlogica.Tests.Unit;

public class CondominioServiceTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private CondominioService _sut = null!;
    private CryptoService _crypto = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();
        await _conn.ExecuteAsync("""
            CREATE TABLE condominios (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nome TEXT NOT NULL,
                numero_contrato TEXT NOT NULL,
                numero_conta TEXT NOT NULL,
                cooperativa TEXT NOT NULL,
                credenciais_enc BLOB NOT NULL,
                ativo INTEGER NOT NULL DEFAULT 1,
                criado_em TEXT NOT NULL,
                atualizado_em TEXT NOT NULL
            );
            """);

        _crypto = new CryptoService("senha-teste");
        // Use the connection string that resolves to this in-memory connection
        _sut = new CondominioService($"Data Source=:memory:", _crypto);
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task Criar_E_ListarAtivos_RetornaCondominioCriado()
    {
        // Use a shared connection to avoid separate in-memory DBs
        var connStr = $"Data Source=file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        await CriarEsquemaAsync(connStr);

        var crypto = new CryptoService("senha-teste");
        var service = new CondominioService(connStr, crypto);

        var creds = new CondominioCredenciais("cli", "sec", "", "", "app", "acc");
        var cond = new Condominio
        {
            Nome = "Res. Teste",
            NumeroContrato = "123",
            NumeroConta = "456",
            Cooperativa = "0001",
            Credenciais = creds
        };

        var id = await service.CriarAsync(cond);
        var lista = (await service.ListarAtivosAsync()).ToList();

        id.Should().BeGreaterThan(0);
        lista.Should().ContainSingle(c => c.Id == id && c.Nome == "Res. Teste");
        lista[0].Credenciais.Should().BeEquivalentTo(creds);
    }

    [Fact]
    public async Task Desativar_RemoveCondominioDaListaAtivos()
    {
        var connStr = $"Data Source=file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        await CriarEsquemaAsync(connStr);

        var service = new CondominioService(connStr, new CryptoService("senha-teste"));

        var cond = new Condominio
        {
            Nome = "Inativo",
            NumeroContrato = "999",
            NumeroConta = "888",
            Cooperativa = "0002",
            Credenciais = new CondominioCredenciais("c", "s", "", "", "a", "b")
        };

        var id = await service.CriarAsync(cond);
        await service.DesativarAsync(id);

        var lista = await service.ListarAtivosAsync();
        lista.Should().NotContain(c => c.Id == id);
    }

    [Fact]
    public async Task Criar_SemCredenciais_ThrowsArgumentException()
    {
        var connStr = $"Data Source=file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        var service = new CondominioService(connStr, new CryptoService("senha-teste"));

        var act = async () => await service.CriarAsync(new Condominio { Nome = "Sem creds" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static async Task CriarEsquemaAsync(string connStr)
    {
        using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS condominios (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nome TEXT NOT NULL,
                numero_contrato TEXT NOT NULL,
                numero_conta TEXT NOT NULL,
                cooperativa TEXT NOT NULL,
                credenciais_enc BLOB NOT NULL,
                ativo INTEGER NOT NULL DEFAULT 1,
                criado_em TEXT NOT NULL,
                atualizado_em TEXT NOT NULL
            );
            """);
    }
}
