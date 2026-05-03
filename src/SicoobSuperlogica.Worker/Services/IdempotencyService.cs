using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SicoobSuperlogica.Worker.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Lê o stream para calcular o checksum do arquivo, reposiciona o stream em 0
    /// e retorna a chave de idempotência (hash SHA-256 do contexto completo).
    /// </summary>
    string ComputarChave(int condominioId, string dataInicial, string dataFinal, Stream fileStream);

    /// <summary>Retorna true se o hash já existe na tabela com status SUCESSO.</summary>
    Task<bool> JaProcessadoAsync(string chave);

    /// <summary>Persiste a chave com status SUCESSO após processamento bem-sucedido.</summary>
    Task RegistrarSucessoAsync(string chave, int condominioId);
}

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly string _connectionString;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(string connectionString, ILogger<IdempotencyService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public string ComputarChave(int condominioId, string dataInicial, string dataFinal, Stream fileStream)
    {
        // Checksum do conteúdo do arquivo
        fileStream.Position = 0;
        var fileHash = SHA256.HashData(fileStream);
        fileStream.Position = 0;

        var fileHashHex = Convert.ToHexString(fileHash).ToLowerInvariant();
        var entrada = $"{condominioId}:{dataInicial}:{dataFinal}:{fileHashHex}";

        var chaveBytes = SHA256.HashData(Encoding.UTF8.GetBytes(entrada));
        return Convert.ToHexString(chaveBytes).ToLowerInvariant();
    }

    public async Task<bool> JaProcessadoAsync(string chave)
    {
        using var conn = new SqliteConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM idempotencia WHERE hash = @hash AND status = 'SUCESSO'",
            new { hash = chave });

        if (count > 0)
            _logger.LogInformation("Idempotência: hash {Hash} já processado com sucesso — ignorando.", chave[..8]);

        return count > 0;
    }

    public async Task RegistrarSucessoAsync(string chave, int condominioId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            """
            INSERT OR IGNORE INTO idempotencia (hash, condominio_id, executado_em, status)
            VALUES (@hash, @condominioId, @executadoEm, 'SUCESSO')
            """,
            new { hash = chave, condominioId, executadoEm = DateTime.UtcNow.ToString("o") });
    }
}
