# Documentação Técnica – API Integração Superlógica

> **Repositório**: `api-integracao-superlogica`
> Contrato REST com o dashboard: [sdd global §02-plan](../../sdd/02-plan.md#contrato-rest)

## 1. Stack e Dependências

| Pacote | Versão mínima | Uso |
|---|---|---|
| .NET SDK | 10.0.201 | Runtime e build |
| `Serilog.AspNetCore` | 9+ | Log estruturado JSON |
| `Serilog.Formatting.Compact` | 3+ | Formato JSON compacto |
| `Microsoft.Data.Sqlite` | 9+ | Persistência SQLite |
| `Dapper` | 2+ | Mapeamento SQL→objeto |
| `FluentValidation` | 11+ | Validação de schemas |
| `Polly` | 8+ | Retry/resilience |
| `Microsoft.Extensions.Http.Polly` | 9+ | Integração com IHttpClientFactory |
| `Moq` | 4+ | Mocks em testes |
| `FluentAssertions` | 7+ | Asserções expressivas |
| `WireMock.Net` | 1+ | Mock de APIs HTTP em testes |

## 2. Fluxo Operacional

```
PeriodicTimer → IntegracaoWorker
    └── SemaphoreSlim(1,1) garante execução exclusiva
    └── Para cada condomínio (Task.Delay 60s entre cada):
        1. StatusService → A_PROCESSAR
        2. IdempotencyService → verificar hash; pular se já integrado
        3. AuthService → obter/renovar token Sicoob
        4. RetornoSicoobService → POST solicitar CNAB 240
           └── Polling × 10:
               ├── SEM_MOVIMENTO → StatusService → SEM_TITULOS; continuar
               └── GERADO → StatusService → PROCESSAMENTO_FINALIZADO
        5. Download via ReadAsStreamAsync() → StatusService → ARQUIVO_BAIXADO
        6. CnabParserService → extrair registros segmentos T e U
        7. StatusService → ENVIANDO_TITULOS
        8. SuperlogicaService → upload multipart CNAB 240
           ├── 2xx → StatusService → ENVIADO_SUPERLOGICA
           └── ≥300 → FALHA_TEMPORARIA; log Error; preservar referência
        9. IdempotencyService → persistir hash SUCESSO
       10. StatusService → FINALIZADO
```

## 3. Autenticação Sicoob

```csharp
// HttpClientHandler com PFX nativo
var handler = new HttpClientHandler();
handler.ClientCertificates.Add(
    new X509Certificate2("cert.pfx", senha, X509KeyStorageFlags.MachineKeySet));
var client = new HttpClient(handler);

// Renovação de token: cache com expiração - 30s
```

## 4. Minimal API – Endpoints para o Dashboard

Configurados em `Api/DashboardApi.cs` e registrados em `Program.cs`:

```csharp
app.MapGet("/api/status", (StatusService svc) => svc.GetAllStatus());
app.MapGet("/api/condominios", (CondominioService svc) => svc.GetAtivos());
app.MapGet("/api/execucoes/{condominioId}", (int condominioId, StatusService svc)
    => svc.GetHistorico(condominioId, limit: 30));
```

CORS em desenvolvimento:
```csharp
builder.Services.AddCors(o => o.AddPolicy("Dashboard",
    p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
app.UseCors("Dashboard");
```

## 5. Polly Retry

```csharp
services.AddHttpClient("Sicoob")
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

`HttpClient.Timeout = TimeSpan.FromSeconds(10)`.

## 6. Schema SQLite

Ver `db/schema.sql`. Campo relevante para o painel:
- `execucoes.status`: valor do enum `ExecucaoStatus` (9 estados; ver `01-spec.md §6`)

## 7. Enum ExecucaoStatus

```csharp
public enum ExecucaoStatus
{
    A_PROCESSAR,
    PROCESSAMENTO_FINALIZADO,
    ARQUIVO_BAIXADO,
    ENVIANDO_TITULOS,
    SEM_TITULOS,
    ENVIADO_SUPERLOGICA,
    FINALIZADO,
    FALHA_TEMPORARIA,
    FALHA_PERMANENTE
}
```

## 8. Ambiente de Desenvolvimento

- .NET SDK 10.0.201 (`dotnet --list-sdks`)
- Visual Studio 2022+ ou Rider com suporte .NET 10
- DB Browser for SQLite para inspeção
- Postman/curl para testar endpoints da Minimal API

```bash
dotnet build
dotnet run --project src/SicoobSuperlogica.Worker   # porta :5000
dotnet test --collect:"XPlat Code Coverage"
```

## 9. Pendências Técnicas

| Item | Status |
|---|---|
| Endpoint programático upload Superlógica | ⚠ A confirmar (suporte antes da Fase 3) |
| Content-Type + campos multipart | ⚠ A confirmar |
| Rate limits Sicoob | ⚠ Validar em homologação Fase 1 |
| Escopo access_token por licença | ⚠ Validar em trial Fase 3 |
