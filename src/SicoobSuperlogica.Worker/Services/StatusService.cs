using Dapper;
using Microsoft.Data.Sqlite;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface IStatusService
{
    Task<int> CriarExecucaoAsync(int condominioId, string dataInicial, string dataFinal, ExecucaoStatus status);
    Task AtualizarAsync(int execucaoId, ExecucaoStatus status, int? totalRegistros = null, string? mensagemErro = null);
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
}
