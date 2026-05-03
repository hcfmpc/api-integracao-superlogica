using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Services;
using SicoobSuperlogica.Worker.Workers;

// Bootstrap logger while reading master password
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var masterPassword = LerSenhaMestre();

var builder = Host.CreateApplicationBuilder(args);

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

// Database
var dbPath = appSettings.Database.Path;
var connectionString = $"Data Source={dbPath}";
builder.Services.AddSingleton(connectionString);

// Crypto
builder.Services.AddSingleton<ICryptoService>(_ => new CryptoService(masterPassword));

// Services
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddScoped<ICondominioService, CondominioService>();
builder.Services.AddScoped<IRetornoSicoobService, RetornoSicoobService>();

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

var host = builder.Build();

// Initialize database
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseInitializer.InitializeAsync(connectionString, logger);
}

await host.RunAsync();

static string LerSenhaMestre()
{
    if (!Console.IsInputRedirected)
    {
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

    // In non-interactive environments (tests, CI) allow env var override
    return Environment.GetEnvironmentVariable("SICOOB_MASTER_PASSWORD")
        ?? throw new InvalidOperationException(
            "Defina SICOOB_MASTER_PASSWORD ou execute em modo interativo.");
}
