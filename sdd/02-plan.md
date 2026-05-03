# API Integração Superlógica – Plano Técnico

> **Repositório**: `api-integracao-superlogica`

## 1. Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 LTS (C#) — SDK 10.0.201 |
| Modelo de aplicação | Worker Service (`BackgroundService` / `IHostedService`) |
| Agendamento | `PeriodicTimer` nativo do .NET |
| HTTP – Sicoob (mTLS) | `HttpClient` + `HttpClientHandler` com `X509Certificate2` (PFX nativo) |
| HTTP – Superlógica | `IHttpClientFactory` com `DefaultRequestHeaders` |
| Criptografia | `AesGcm` + `Rfc2898DeriveBytes` (`System.Security.Cryptography`) |
| Resiliência | Polly (`AddTransientHttpErrorPolicy`) |
| Log | Serilog com `Serilog.Formatting.Compact` (JSON) |
| Persistência | SQLite via `Microsoft.Data.Sqlite` + Dapper |
| Validação | FluentValidation |
| Minimal API (dashboard) | ASP.NET Core Minimal API (mesmo host do Worker) |
| Testes | xUnit + Moq + FluentAssertions + WireMock.Net |

## 2. Arquitetura de Serviços

| Service | Responsabilidade |
|---|---|
| `AuthService` | OAuth2 + mTLS Sicoob; cache e renovação de token |
| `RetornoSicoobService` | Solicitar, fazer polling e baixar CNAB 240 por condomínio |
| `CnabParserService` | Parsear layout posicional CNAB 240 (segmentos T e U) |
| `SuperlogicaService` | Upload do arquivo CNAB 240 para Receitas → Retorno Bancário |
| `CondominioService` | CRUD de condomínios com credenciais criptografadas no SQLite |
| `IdempotencyService` | Prevenir reprocessamento (hash SHA-256 por arquivo+condomínio+período) |
| `CryptoService` | Criptografia/descriptografia `AesGcm` de credenciais em repouso |
| `StatusService` | Atualizar e consultar status por condomínio no SQLite |
| `IntegracaoWorker` | Orquestrar o ciclo completo como `BackgroundService` com `PeriodicTimer` |
| `DashboardApi` | Minimal API expondo `/api/status`, `/api/condominios`, `/api/execucoes/{id}` |

## 3. API Sicoob – Cobrança Bancária V2

### 3.1 Autenticação OAuth2 + mTLS

```
POST https://auth.sicoob.com.br/auth/realms/cooperado/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id={client_id}
```

Configuração mTLS:
```csharp
var handler = new HttpClientHandler();
handler.ClientCertificates.Add(
    new X509Certificate2("cert.pfx", senha, X509KeyStorageFlags.MachineKeySet)
);
```

Resposta: `{ "access_token": "string", "expires_in": 300 }`
Cache: renovar 30 s antes da expiração.

### 3.2 Solicitar CNAB 240

```
POST https://api.sicoob.com.br/cobranca-bancaria/v2/boletos/arquivo-retorno/solicitar
Authorization: Bearer {access_token}
Content-Type: application/json
```

Body:
```json
{
  "numeroContrato": "string",
  "numeroConta": "string",
  "dataInicial": "YYYY-MM-DD",
  "dataFinal": "YYYY-MM-DD"
}
```

Resposta 202: `{ "idSolicitacao": "string", "status": "EM_PROCESSAMENTO" }`

### 3.3 Polling de Status

```
GET https://api.sicoob.com.br/cobranca-bancaria/v2/boletos/arquivo-retorno/{idSolicitacao}
Authorization: Bearer {access_token}
```

Resposta: `{ "status": "EM_PROCESSAMENTO | GERADO | SEM_MOVIMENTO", "urlDownload": "string | null" }`

Estratégia: `Task.Delay(TimeSpan.FromMinutes(2), ct)` × 10 tentativas.

### 3.4 Download

`GET {urlDownload}` — lido com `ReadAsStreamAsync()`, processado em memória.

## 4. API Superlógica Condomínios

**Base URL**: `https://api.superlogica.net/v2/condor`

**Headers obrigatórios**:
```
app_token: {app_token}
access_token: {access_token}
```

**Caminho de importação confirmado** (informação direta do gestor):
> Receitas → Retorno Bancário → Processar Arquivos → upload do arquivo CNAB 240 → processamento automático

> ⚠ **Endpoint programático a confirmar**: método HTTP e path exatos para upload (`multipart/form-data` provável). Obter junto ao suporte Superlógica antes da Fase 3.

**Formatos**: datas `MM/DD/YYYY`; decimais com `.`; `status >= 300` = erro.

## 5. Minimal API – Contrato REST para o Dashboard

> Contrato canônico em [sdd global §02-plan](../../sdd/02-plan.md#contrato-rest).

### `GET /api/status`

```json
[{
  "condominioId": 1,
  "nome": "Res. Primavera",
  "status": "FINALIZADO",
  "statusLabel": "FINALIZADO BAIXA DE TÍTULOS",
  "ultimaExecucao": "2026-05-02T08:30:00",
  "totalTitulos": 14,
  "proximaExecucao": "2026-05-02T09:30:00"
}]
```

### `GET /api/condominios`

```json
[{ "id": 1, "nome": "Res. Primavera", "ativo": true, "proximaExecucao": "2026-05-02T09:30:00" }]
```

### `GET /api/execucoes/{condominioId}`

```json
[{
  "id": 10,
  "dataInicial": "2026-05-01",
  "dataFinal": "2026-05-01",
  "status": "FINALIZADO",
  "totalRegistros": 14,
  "mensagemErro": null,
  "executadoEm": "2026-05-02T08:30:00"
}]
```

**CORS**: configurar no `Program.cs` para aceitar `http://localhost:4200` em desenvolvimento:
```csharp
builder.Services.AddCors(o => o.AddPolicy("Dashboard",
    p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
app.UseCors("Dashboard");
```

## 6. Segurança

| Aspecto | Implementação |
|---|---|
| Credenciais em repouso | `AesGcm` + `Rfc2898DeriveBytes` (PBKDF2-SHA256, 100k iterações) |
| Certificado PFX | `X509Certificate2` direto; nunca convertido para PEM |
| Dados financeiros | Em memória; descartados após confirmação de sucesso |
| Logs | Sem CPF, nome, valor ou nosso-número em texto claro |
| Rate limiting | `Task.Delay(60 s)` entre condomínios |
| Acesso à Minimal API | Filtro de IP: aceitar apenas `localhost` + rede interna + `localhost:4200` em dev |

## 7. Resiliência

- **Polly**: 3 tentativas, backoff exponencial 1 s / 2 s / 4 s.
- **Timeout**: `HttpClient.Timeout = TimeSpan.FromSeconds(10)`.
- **Polling**: máx 10 tentativas × 2 min.
- **SemaphoreSlim(1,1)**: impede ciclos sobrepostos no Worker.
- **CancellationToken**: propagado em todas as operações async.

## 8. Schema SQLite

```sql
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

CREATE TABLE execucoes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  condominio_id INTEGER NOT NULL REFERENCES condominios(id),
  data_inicial TEXT NOT NULL,
  data_final TEXT NOT NULL,
  status TEXT NOT NULL,
  total_registros INTEGER,
  mensagem_erro TEXT,
  executado_em TEXT NOT NULL
);

CREATE TABLE idempotencia (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  hash TEXT NOT NULL UNIQUE,
  condominio_id INTEGER NOT NULL REFERENCES condominios(id),
  executado_em TEXT NOT NULL,
  status TEXT NOT NULL
);
```

## 9. Estrutura de Diretórios

```text
api-integracao-superlogica/
├── sdd/                     ← este SDD
├── src/
│   └── SicoobSuperlogica.Worker/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Workers/
│       │   └── IntegracaoWorker.cs
│       ├── Services/
│       │   ├── AuthService.cs
│       │   ├── RetornoSicoobService.cs
│       │   ├── CnabParserService.cs
│       │   ├── SuperlogicaService.cs
│       │   ├── CondominioService.cs
│       │   ├── IdempotencyService.cs
│       │   ├── CryptoService.cs
│       │   └── StatusService.cs
│       ├── Api/
│       │   └── DashboardApi.cs
│       ├── Models/
│       │   ├── Condominio.cs
│       │   ├── CnabRegistro.cs
│       │   ├── SolicitacaoRetorno.cs
│       │   ├── ExecucaoLog.cs
│       │   └── ExecucaoStatus.cs  ← enum com os 9 estados
│       └── Configuration/
│           ├── AppSettings.cs
│           └── CondominioSettings.cs
├── db/
│   └── schema.sql
└── tests/
    └── SicoobSuperlogica.Tests/
        ├── Unit/
        ├── Integration/
        └── E2E/
```

## 10. Estratégia de Testes

| Tipo | Ferramentas | Cobertura mínima |
|---|---|---|
| Unitários | xUnit + Moq + FluentAssertions | 80% em `Services/` |
| Integração | WireMock.Net | Todos os cenários §11 |
| E2E | Homologação Sicoob + trial Superlógica | Fluxo feliz + falha de inserção |

## 11. Cenários de Teste Obrigatórios

- [ ] Falha de autenticação (certificado inválido, token expirado)
- [ ] Polling timeout (10 tentativas esgotadas)
- [ ] Arquivo CNAB duplicado (idempotência ativa)
- [ ] Falha de rede (`HttpRequestException`)
- [ ] `SEM_MOVIMENTO` do Sicoob
- [ ] Erro 4xx da Superlógica
- [ ] Falha parcial: 1 de N condomínios falha sem interromper os demais
- [ ] Endpoint `/api/status` retorna o estado correto após cada transição

## 12. Fases de Entrega

| Fase | Entregável |
|---|---|
| **1** | Setup .NET 10, autenticação Sicoob (OAuth2 + mTLS), extração CNAB para 1 condomínio |
| **2** | Polling, parser CNAB 240, idempotência, múltiplos condomínios, credenciais criptografadas |
| **3** | Upload para Superlógica (endpoint a confirmar), Serilog, reprocessamento de falhas |
| **4** | `PeriodicTimer`, `SemaphoreSlim`, rate limiting, notificação de falhas críticas |
| **5** | Minimal API REST, CORS, `StatusService`, publicação autocontida Windows x64 |

## 13. Comandos

```bash
dotnet new sln -n SicoobSuperlogica
dotnet new worker -n SicoobSuperlogica.Worker -o src/SicoobSuperlogica.Worker
dotnet new xunit -n SicoobSuperlogica.Tests -o tests/SicoobSuperlogica.Tests
dotnet build
dotnet run --project src/SicoobSuperlogica.Worker
dotnet test --collect:"XPlat Code Coverage"
dotnet publish src/SicoobSuperlogica.Worker -c Release -r win-x64 --self-contained
sc create SicoobIntegracao binPath= "C:\caminho\SicoobSuperlogica.Worker.exe"
```
