using Dapper;
using Microsoft.Data.Sqlite;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface ICondominioService
{
    Task<IEnumerable<Condominio>> ListarAtivosAsync();
    Task<Condominio?> ObterPorIdAsync(int id);
    Task<int> CriarAsync(Condominio condominio);
    Task AtualizarAsync(Condominio condominio);
    Task DesativarAsync(int id);
}

public sealed class CondominioService : ICondominioService
{
    private readonly string _connectionString;
    private readonly ICryptoService _crypto;

    static CondominioService()
    {
        // Map snake_case columns (criado_em) to PascalCase properties (CriadoEm)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public CondominioService(string connectionString, ICryptoService crypto)
    {
        _connectionString = connectionString;
        _crypto = crypto;
    }

    public async Task<IEnumerable<Condominio>> ListarAtivosAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<CondominioRow>(
            "SELECT * FROM condominios WHERE ativo = 1 ORDER BY nome");
        return rows.Select(Mapear);
    }

    public async Task<Condominio?> ObterPorIdAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        var row = await conn.QuerySingleOrDefaultAsync<CondominioRow>(
            "SELECT * FROM condominios WHERE id = @id", new { id });
        return row is null ? null : Mapear(row);
    }

    public async Task<int> CriarAsync(Condominio condominio)
    {
        if (condominio.Credenciais is null)
            throw new ArgumentException("Credenciais obrigatórias para criação.");

        var encBlob = _crypto.Encrypt(condominio.Credenciais);
        var now = DateTime.UtcNow.ToString("o");

        using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<int>("""
            INSERT INTO condominios (nome, numero_contrato, numero_conta, cooperativa,
                                     credenciais_enc, ativo, criado_em, atualizado_em)
            VALUES (@Nome, @NumeroContrato, @NumeroConta, @Cooperativa,
                    @CredenciaisEnc, 1, @CriadoEm, @AtualizadoEm);
            SELECT last_insert_rowid();
            """,
            new
            {
                condominio.Nome,
                condominio.NumeroContrato,
                condominio.NumeroConta,
                condominio.Cooperativa,
                CredenciaisEnc = encBlob,
                CriadoEm = now,
                AtualizadoEm = now
            });
        return id;
    }

    public async Task AtualizarAsync(Condominio condominio)
    {
        var encBlob = condominio.Credenciais is not null
            ? _crypto.Encrypt(condominio.Credenciais)
            : null;

        using var conn = new SqliteConnection(_connectionString);

        if (encBlob is not null)
        {
            await conn.ExecuteAsync("""
                UPDATE condominios
                SET nome = @Nome, numero_contrato = @NumeroContrato,
                    numero_conta = @NumeroConta, cooperativa = @Cooperativa,
                    credenciais_enc = @CredenciaisEnc, atualizado_em = @AtualizadoEm
                WHERE id = @Id
                """,
                new
                {
                    condominio.Nome, condominio.NumeroContrato, condominio.NumeroConta,
                    condominio.Cooperativa, CredenciaisEnc = encBlob,
                    AtualizadoEm = DateTime.UtcNow.ToString("o"), condominio.Id
                });
        }
        else
        {
            await conn.ExecuteAsync("""
                UPDATE condominios
                SET nome = @Nome, numero_contrato = @NumeroContrato,
                    numero_conta = @NumeroConta, cooperativa = @Cooperativa,
                    atualizado_em = @AtualizadoEm
                WHERE id = @Id
                """,
                new
                {
                    condominio.Nome, condominio.NumeroContrato, condominio.NumeroConta,
                    condominio.Cooperativa, AtualizadoEm = DateTime.UtcNow.ToString("o"),
                    condominio.Id
                });
        }
    }

    public async Task DesativarAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE condominios SET ativo = 0, atualizado_em = @now WHERE id = @id",
            new { now = DateTime.UtcNow.ToString("o"), id });
    }

    private Condominio Mapear(CondominioRow row)
    {
        CondominioCredenciais? credenciais = null;
        try
        {
            credenciais = _crypto.Decrypt<CondominioCredenciais>(row.CredenciaisEnc);
        }
        catch
        {
            // Decryption failure surfaced at usage time (e.g. AuthService)
        }

        return new Condominio
        {
            Id = row.Id,
            Nome = row.Nome,
            NumeroContrato = row.NumeroContrato,
            NumeroConta = row.NumeroConta,
            Cooperativa = row.Cooperativa,
            CredenciaisEnc = row.CredenciaisEnc,
            Ativo = row.Ativo == 1,
            CriadoEm = DateTime.Parse(row.CriadoEm),
            AtualizadoEm = DateTime.Parse(row.AtualizadoEm),
            Credenciais = credenciais
        };
    }

    // Dapper DTO matching column names
    private sealed class CondominioRow
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string NumeroContrato { get; set; } = string.Empty;
        public string NumeroConta { get; set; } = string.Empty;
        public string Cooperativa { get; set; } = string.Empty;
        public byte[] CredenciaisEnc { get; set; } = [];
        public int Ativo { get; set; }
        public string CriadoEm { get; set; } = string.Empty;
        public string AtualizadoEm { get; set; } = string.Empty;
    }
}
