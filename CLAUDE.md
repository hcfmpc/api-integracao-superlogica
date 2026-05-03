# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Mandatory Development Constraint

Claude must be launched exclusively via `ai-jail claude` in a WSL2 terminal. The VS Code extension chat does not pass through AI-JAIL and must not be used for implementation tasks. The repository must reside inside the WSL2 filesystem (e.g. `~/projetos/`), never under `/mnt/c/`.

## SDD Workflow

This project uses Spec-Driven Development. Before writing any code, always read in order:

1. `sdd/01-spec.md` — business intent and acceptance criteria (immutable)
2. `sdd/02-plan.md` — architecture decisions, schemas, API contracts, directory layout
3. `sdd/03-task.md` — atomic implementation tasks; implement only what is listed here
4. `sdd/04-context.md` — current delivery state, open blockers, confirmed decisions

Do not alter previous code unless strictly necessary. Do not generate the final artefact until all critical edge cases have been clarified.

## Commands

```bash
# Scaffold (first time only)
dotnet new sln -n SicoobSuperlogica
dotnet new worker -n SicoobSuperlogica.Worker -o src/SicoobSuperlogica.Worker
dotnet new xunit  -n SicoobSuperlogica.Tests  -o tests/SicoobSuperlogica.Tests

# Build / run
dotnet build
dotnet run --project src/SicoobSuperlogica.Worker

# Test with coverage
dotnet test --collect:"XPlat Code Coverage"

# Publish self-contained Windows x64
dotnet publish src/SicoobSuperlogica.Worker -c Release -r win-x64 --self-contained

# Format
dotnet format
```

## Architecture

**Purpose**: ETL Worker that polls SICOOB (bank) for CNAB 240 return files and uploads them to SUPERLOGICA (property management ERP) for each managed condominium, on a scheduled basis.

**Host model**: a single .NET 10 process runs both a `BackgroundService` Worker and an ASP.NET Core Minimal API for dashboard consumption.

### Service layer (`src/SicoobSuperlogica.Worker/Services/`)

| Service | Role |
|---|---|
| `IntegracaoWorker` | `BackgroundService` orchestrator; `PeriodicTimer` loop; `SemaphoreSlim(1,1)` prevents overlap |
| `AuthService` | OAuth2 + mTLS to SICOOB; caches token, renews 30 s before expiry |
| `RetornoSicoobService` | POST to request file → polling (max 10 × 2 min) → GET download |
| `CnabParserService` | Parses positional CNAB 240 layout (segments T and U) in memory |
| `SuperlogicaService` | Uploads CNAB 240 to Superlógica Receitas → Retorno Bancário |
| `CondominioService` | CRUD for condominiums with AES-GCM encrypted credentials in SQLite |
| `CryptoService` | AES-GCM + PBKDF2-SHA256 (100k iterations) encryption/decryption |
| `IdempotencyService` | SHA-256 hash (file + condominium + period) prevents reprocessing |
| `StatusService` | Manages the 9-state execution status machine in SQLite |
| `DashboardApi` | Minimal API: `GET /api/status`, `/api/condominios`, `/api/execucoes/{id}` |

### Execution states (9-state machine in `ExecucaoStatus.cs`)

`A_PROCESSAR` → `AUTENTICANDO` → `SOLICITANDO` → `AGUARDANDO` → `BAIXANDO` → `PARSEANDO` → `ENVIANDO` → `FINALIZADO` | `ERRO`

### Persistence (SQLite via Dapper)

Three tables: `condominios`, `execucoes`, `idempotencia`. Full DDL in `sdd/02-plan.md §8` and `db/schema.sql`.

### Security invariants

- Financial data (values, CPFs, titles) is **never persisted** — in-memory only, discarded after success.
- Credentials stored with AES-GCM; the key is never written to disk in plaintext.
- Logs must never contain CPF, name, amount, or `nosso-número` in clear text.
- Rate limit: `Task.Delay(60 s)` between condominiums to avoid SICOOB IP blocks.
- Minimal API accepts requests only from `localhost` and the internal network; CORS allows `http://localhost:4200` in development.

### Resiliency

- Polly retry: 3 attempts, exponential backoff 1 s / 2 s / 4 s.
- `HttpClient.Timeout = 10 s`.
- Each condominium fails in isolation — one failure must not abort the others.

## Open Blockers (as of SDD completion)

- Superlógica programmatic upload endpoint (HTTP method, path, `multipart/form-data` fields) — **required before Phase 3**.
- `access_token` scope per condominium licence in Superlógica — **required before Phase 3**.
- Notification channel for critical failures (e-mail, dashboard, other) — **required before Phase 4**.
