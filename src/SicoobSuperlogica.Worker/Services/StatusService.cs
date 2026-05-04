using Dapper;
using Microsoft.Data.Sqlite;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public record ExecucaoFalha(long CondominioId, string DataInicial, string DataFinal);

public record ExecucaoResumo(
    int Id,
    int CondominioId,
    string CondominioNome,
    string DataInicial,
    string DataFinal,
    string Status,
    int? TotalRegistros,
    string? MensagemErro,
    string ExecutadoEm);

public record CondominioResumo(
    int Id,
    string Nome,
    string NumeroContrato,
    string NumeroConta,
    string Cooperativa,
    bool Ativo,
    string CriadoEm);

public interface IStatusService
{
    Task<int> CriarExecucaoAsync(int condominioId, string dataInicial, string dataFinal, ExecucaoStatus status);
    Task AtualizarAsync(int execucaoId, ExecucaoStatus status, int? totalRegistros = null, string? mensagemErro = null);
    Task<IEnumerable<ExecucaoFalha>> ListarFalhasTemporariasAsync();
    Task MarcarReprocessandoAsync(int condominioId, string dataInicial, string dataFinal);
    Task<IEnumerable<ExecucaoResumo>> ListarUltimasExecucoesAsync();
    Task<IEnumerable<CondominioResumo>> ListarCondominiosAsync();
    Task<ExecucaoLog?> ObterExecucaoPorIdAsync(int id);
}

public sealed class StatusService : IStatusService
{
    private readonly string _connectionString;

    public StatusService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> CriarExecucaoAsync(
        int condominioId, string dataInicial, string dataFinal, ExecucaoStatus status)
    {
        using var conn = new SqliteConnection(_connectionString);
        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO execucoes (condominio_id, data_inicial, data_final, status, executado_em)
            VALUES (@condominioId, @dataInicial, @dataFinal, @status, @executadoEm);
            SELECT last_insert_rowid();
            """,
            new
            {
                condominioId,
                dataInicial,
                dataFinal,
                status = status.ToString(),
                executadoEm = DateTime.UtcNow.ToString("o")
            });
        return id;
    }

    public async Task AtualizarAsync(
        int execucaoId, ExecucaoStatus status, int? totalRegistros = null, string? mensagemErro = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            """
            UPDATE execucoes
            SET status = @status,
                total_registros = COALESCE(@totalRegistros, total_registros),
                mensagem_erro = COALESCE(@mensagemErro, mensagem_erro)
            WHERE id = @execucaoId
            """,
            new { execucaoId, status = status.ToString(), totalRegistros, mensagemErro });
    }

    public async Task<IEnumerable<ExecucaoFalha>> ListarFalhasTemporariasAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        return await conn.QueryAsync<ExecucaoFalha>(
            """
            SELECT DISTINCT condominio_id AS CondominioId,
                            data_inicial  AS DataInicial,
                            data_final    AS DataFinal
            FROM execucoes
            WHERE status = 'FALHA_TEMPORARIA'
            """);
    }

    public async Task MarcarReprocessandoAsync(int condominioId, string dataInicial, string dataFinal)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            """
            UPDATE execucoes
            SET status = 'A_PROCESSAR'
            WHERE status = 'FALHA_TEMPORARIA'
              AND condominio_id = @condominioId
              AND data_inicial  = @dataInicial
              AND data_final    = @dataFinal
            """,
            new { condominioId, dataInicial, dataFinal });
    }

    public async Task<IEnumerable<ExecucaoResumo>> ListarUltimasExecucoesAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<ExecucaoResumoRow>(
            """
            SELECT e.id              AS id,
                   e.condominio_id   AS condominio_id,
                   c.nome            AS condominio_nome,
                   e.data_inicial    AS data_inicial,
                   e.data_final      AS data_final,
                   e.status          AS status,
                   e.total_registros AS total_registros,
                   e.mensagem_erro   AS mensagem_erro,
                   e.executado_em    AS executado_em
            FROM execucoes e
            INNER JOIN condominios c ON c.id = e.condominio_id
            WHERE e.id IN (SELECT MAX(id) FROM execucoes GROUP BY condominio_id)
            ORDER BY e.executado_em DESC
            """);
        return rows.Select(r => new ExecucaoResumo(
            r.Id, r.CondominioId, r.CondominioNome,
            r.DataInicial, r.DataFinal, r.Status,
            r.TotalRegistros, r.MensagemErro, r.ExecutadoEm));
    }

    public async Task<IEnumerable<CondominioResumo>> ListarCondominiosAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<CondominioResumoRow>(
            """
            SELECT id, nome, numero_contrato, numero_conta, cooperativa, ativo, criado_em
            FROM condominios
            ORDER BY nome
            """);
        return rows.Select(r => new CondominioResumo(
            r.Id, r.Nome, r.NumeroContrato, r.NumeroConta, r.Cooperativa,
            r.Ativo == 1, r.CriadoEm));
    }

    public async Task<ExecucaoLog?> ObterExecucaoPorIdAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        return await conn.QuerySingleOrDefaultAsync<ExecucaoLog>(
            """
            SELECT id, condominio_id, data_inicial, data_final,
                   status, total_registros, mensagem_erro, executado_em
            FROM execucoes
            WHERE id = @id
            """,
            new { id });
    }

    private sealed class ExecucaoResumoRow
    {
        public int Id { get; set; }
        public int CondominioId { get; set; }
        public string CondominioNome { get; set; } = string.Empty;
        public string DataInicial { get; set; } = string.Empty;
        public string DataFinal { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? TotalRegistros { get; set; }
        public string? MensagemErro { get; set; }
        public string ExecutadoEm { get; set; } = string.Empty;
    }

    private sealed class CondominioResumoRow
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string NumeroContrato { get; set; } = string.Empty;
        public string NumeroConta { get; set; } = string.Empty;
        public string Cooperativa { get; set; } = string.Empty;
        public int Ativo { get; set; }
        public string CriadoEm { get; set; } = string.Empty;
    }
}
