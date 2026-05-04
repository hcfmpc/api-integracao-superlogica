using System.Net;
using System.Net.Sockets;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Services;
using SicoobSuperlogica.Worker.Workers;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var masterPassword = LerSenhaMestre();

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Services.AddSerilog((sp, cfg) =>
    cfg.ReadFrom.Configuration(builder.Configuration)
       .ReadFrom.Services(sp)
       .Enrich.FromLogContext());

// Settings
var appSettings = builder.Configuration.Get<AppSettings>() ?? new AppSettings();
builder.Services.AddSingleton(appSettings.Worker);
builder.Services.AddSingleton(appSettings.Sicoob);
builder.Services.AddSingleton(appSettings.Superlogica);
builder.Services.AddSingleton(appSettings.Database);

// CORS
builder.Services.AddCors(opts =>
    opts.AddPolicy("dashboard", p =>
        p.WithOrigins(appSettings.Api.CorsOrigins)
         .AllowAnyMethod()
         .AllowAnyHeader()));

// Database
var dbPath = appSettings.Database.Path;
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton(connectionString);

// Crypto
builder.Services.AddSingleton<ICryptoService>(_ => new CryptoService(masterPassword));

// Services — all stateless (open/close SQLite per call); singleton avoids captive-dependency issue
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<ICondominioService, CondominioService>();
builder.Services.AddSingleton<IRetornoSicoobService, RetornoSicoobService>();
builder.Services.AddSingleton<ICnabParserService, CnabParserService>();
builder.Services.AddSingleton<IIdempotencyService, IdempotencyService>();
builder.Services.AddSingleton<IStatusService, StatusService>();
builder.Services.AddSingleton<ISuperlogicaService, SuperlogicaService>();

// HTTP clients
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(
        appSettings.Worker.RetryCount,
        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
        (outcome, delay, attempt, _) =>
            Log.Warning("Retry {Attempt} após {Delay}s: {Error}",
                attempt, delay.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));

builder.Services.AddHttpClient("sicoob")
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(retryPolicy);

builder.Services.AddHttpClient("superlogica")
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(retryPolicy);

// Worker
builder.Services.AddHostedService<IntegracaoWorker>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseInitializer.InitializeAsync(connectionString, logger);
}

// IP filter — allow only localhost and RFC-1918 private ranges
app.Use(async (ctx, next) =>
{
    var remote = ctx.Connection.RemoteIpAddress;
    if (remote is not null && !EhLocalOuInterna(remote))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(ctx);
});

app.UseCors("dashboard");
app.UseStaticFiles();

// Dashboard endpoints
app.MapGet("/api/status", async (IStatusService status) =>
    Results.Ok(await status.ListarUltimasExecucoesAsync()));

app.MapGet("/api/condominios", async (IStatusService status) =>
    Results.Ok(await status.ListarCondominiosAsync()));

app.MapGet("/api/execucoes/{id:int}", async (int id, IStatusService status) =>
{
    var exec = await status.ObterExecucaoPorIdAsync(id);
    return exec is null ? Results.NotFound() : Results.Ok(exec);
});

await app.RunAsync();

static bool EhLocalOuInterna(IPAddress ip)
{
    if (IPAddress.IsLoopback(ip)) return true;
    var v4 = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    if (v4.AddressFamily != AddressFamily.InterNetwork) return false;
    var b = v4.GetAddressBytes();
    return b[0] == 10
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
        || (b[0] == 192 && b[1] == 168);
}

static string LerSenhaMestre()
{
    // Env var takes precedence (CI, tests, unattended Windows Service)
    var envSenha = Environment.GetEnvironmentVariable("SICOOB_MASTER_PASSWORD");
    if (envSenha is not null) return envSenha;

    if (Console.IsInputRedirected)
        throw new InvalidOperationException(
            "Console redirecionado e SICOOB_MASTER_PASSWORD não definida. " +
            "Defina a variável de ambiente antes de iniciar o serviço.");

    Console.Write("Senha mestre: ");
    var senha = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace && senha.Length > 0)
        {
            senha.Remove(senha.Length - 1, 1);
            continue;
        }
        senha.Append(k.KeyChar);
    }
    Console.WriteLine();
    return senha.ToString();
}

public partial class Program { }
