using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SicoobSuperlogica.Worker.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(string connectionString, ILogger logger)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");

        if (!File.Exists(schemaPath))
            schemaPath = Path.Combine(Directory.GetCurrentDirectory(), "db", "schema.sql");

        if (!File.Exists(schemaPath))
        {
            logger.LogWarning("schema.sql não encontrado em {Path}. Banco pode não estar inicializado.", schemaPath);
            return;
        }

        var sql = await File.ReadAllTextAsync(schemaPath);
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();

        logger.LogInformation("Banco SQLite inicializado.");
    }
}
