namespace SicoobSuperlogica.Worker.Configuration;

public class AppSettings
{
    public WorkerSettings Worker { get; set; } = new();
    public SicoobSettings Sicoob { get; set; } = new();
    public SuperlogicaSettings Superlogica { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
}

public class WorkerSettings
{
    public int IntervalHours { get; set; } = 1;
    public int PollingMaxAttempts { get; set; } = 10;
    public int PollingIntervalMinutes { get; set; } = 2;
    public int CondominioDelaySeconds { get; set; } = 60;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int CertificateAlertDaysBeforeExpiry { get; set; } = 30;
}

public class SicoobSettings
{
    public string TokenUrl { get; set; } =
        "https://auth.sicoob.com.br/auth/realms/cooperado/protocol/openid-connect/token";
    public string ApiBaseUrl { get; set; } =
        "https://api.sicoob.com.br/cobranca-bancaria/v2";
}

public class SuperlogicaSettings
{
    public string BaseUrl { get; set; } = "https://api.superlogica.net/v2/condor";

    // TODO: confirmar path exato com suporte Superlógica antes da Fase 3 em produção
    public string UploadPath { get; set; } = "/financeiro/cobranca/retorno";
}

public class DatabaseSettings
{
    public string Path { get; set; } = "sicoob.db";
}

public class ApiSettings
{
    public string[] CorsOrigins { get; set; } = ["http://localhost:4200"];
}
