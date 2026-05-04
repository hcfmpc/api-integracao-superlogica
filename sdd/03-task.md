# API Integração Superlógica – Backlog de Implementação

> **Repositório**: `api-integracao-superlogica`

## Fase 1 – Setup e Autenticação Sicoob ✅ CONCLUÍDA (2026-05-03)

### 1.1 Setup do projeto
- [x] `dotnet new sln -n SicoobSuperlogica` (`.slnx` — formato .NET 10)
- [x] `dotnet new worker -n SicoobSuperlogica.Worker -o src/SicoobSuperlogica.Worker`
- [x] `dotnet new xunit -n SicoobSuperlogica.Tests -o tests/SicoobSuperlogica.Tests`
- [x] Adicionar pacotes NuGet:
  - `Serilog.AspNetCore 9.0.0` + `Serilog.Formatting.Compact 3.0.0`
  - `Microsoft.Data.Sqlite 9.0.4` + `Dapper 2.1.66`
  - `FluentValidation 11.11.0`
  - `Polly 8.5.2` + `Microsoft.Extensions.Http.Polly 9.0.4`
  - `Moq 4.20.72` + `FluentAssertions 8.3.0` + `WireMock.Net 1.6.12`
- [x] Configurar `.editorconfig`
- [x] Criar `db/schema.sql` com tabelas `condominios`, `execucoes`, `idempotencia`
- [x] Configurar Serilog em `Program.cs` (saída JSON para console e arquivo)
- [x] Criar `AppSettings.cs` + `appsettings.json` (intervalo, timeouts, limite de polling)

### 1.2 Criptografia de credenciais
- [x] Implementar `CryptoService`: `AesGcm` + `Rfc2898DeriveBytes` (PBKDF2-SHA256, 100k iter.)
- [x] Rotina de inicialização: senha mestre via console com máscara (+ `SICOOB_MASTER_PASSWORD` env var para CI)
- [x] Implementar `CondominioService`: CRUD com credenciais criptografadas via Dapper
- [x] Testes unitários: cifrar/decifrar, chave errada, dados corrompidos, blob truncado

### 1.3 Autenticação Sicoob
- [x] Implementar `AuthService`: OAuth2 `client_credentials` com `HttpClientHandler` + `X509Certificate2`
- [x] Cache de token em memória; renovação 30 s antes da expiração
- [x] Polly: 3 tentativas, backoff exponencial 1 s / 2 s / 4 s (via `IHttpClientFactory`)
- [x] Testes com WireMock.Net: token válido, cache, 401, invalidação

### 1.4 Extração CNAB 240
- [x] Implementar `RetornoSicoobService`: `POST` solicitar + polling `Task.Delay(2 min)` × 10
- [x] Download via `ReadAsStreamAsync()` → `MemoryStream` (em memória)
- [x] Tratar `SEM_MOVIMENTO` como execução sem liquidações
- [x] Testes de integração com WireMock.Net: SEM_MOVIMENTO, arquivo gerado, polling esgotado

**Build:** `dotnet build` ✅ | **Testes:** 15/15 ✅

---

## Fase 2 – Parser CNAB, Múltiplos Condomínios e Idempotência ✅ CONCLUÍDA (2026-05-03)

### 2.1 Parser CNAB 240
- [x] Implementar `CnabParserService`: segmentos T e U (layout posicional fixo)
- [x] Modelar `CnabRegistro` como `record` tipado
- [x] Validar campos obrigatórios via FluentValidation
- [x] Testes com fixtures CNAB (liquidação normal, sem movimento, campo inválido, truncado)

### 2.2 Múltiplos condomínios
- [x] Implementar `IntegracaoWorker` como `BackgroundService` com `PeriodicTimer`
- [x] Fila sequencial com `Task.Delay(60 s)` entre condomínios
- [x] `try/catch` por condomínio: falha em um não interrompe os demais
- [x] `SemaphoreSlim(1,1)`: ciclos não se sobrepõem
- [x] Teste de integração: falha parcial (1 de 3 condomínios falha)

### 2.3 Idempotência
- [x] Implementar `IdempotencyService`: SHA-256 de (condomínio_id + data_inicial + data_final + checksum)
- [x] Bloquear silenciosamente se hash já existir com status `SUCESSO`
- [x] Testes unitários

**Build:** `dotnet build` ✅ | **Testes:** 33/33 ✅

---

## Fase 3 – Integração Superlógica ✅ CONCLUÍDA (2026-05-03)

> **Pré-requisito**: confirmar endpoint programático de upload junto ao suporte Superlógica.

### 3.1 Validação de contrato
- [ ] Contatar suporte Superlógica: endpoint de upload CNAB 240 (Receitas → Retorno Bancário)
- [ ] Inspecionar rede do ERP para confirmar `Content-Type` e campos do `multipart/form-data`
- [ ] Criar licença de trial Superlógica Condomínios para testes
- [ ] Gerar `app_token` + `access_token` (Todos os usuários > API > Aplicativos > Novo App Token)

### 3.2 Implementação
- [x] Implementar `SuperlogicaService`: `IHttpClientFactory` + upload `multipart/form-data` do CNAB 240
- [x] `UploadPath` configurável em `appsettings.json` (confirmar com suporte antes de produção)
- [x] Tratar `StatusCode >= 300` como erro com log do body de resposta
- [x] Falha no upload marca `FALHA_TEMPORARIA`; idempotência não é registrada
- [x] Testes com WireMock.Net: sucesso 200, headers corretos, erro 4xx, erro 5xx, stream reposicionado
- [x] `IntegracaoWorker`: transições `ENVIANDO_TITULOS` → upload → `ENVIADO_SUPERLOGICA` → `FINALIZADO`
- [x] Testes do worker: falha no upload, sequência completa de status, sem movimento, idempotência
- [ ] Teste E2E em homologação com arquivo CNAB real (bloqueado até confirmação do endpoint)

**Build:** `dotnet build` ✅ | **Testes:** 40/40 ✅

---

## Fase 4 – Agendamento, Reprocessamento e Notificação ✅ CONCLUÍDA (2026-05-03)

- [x] `PeriodicTimer` configurável via `appsettings.json` (padrão: 1 hora)
- [x] Ao iniciar ciclo: consultar `execucoes` com `FALHA_TEMPORARIA` e reprocessar
- [x] Não reprocessar automaticamente `FALHA_PERMANENTE`
- [x] Serilog `Error` com `alerta: true, tipo: CRITICO` para falhas críticas
- [x] Alerta preventivo de expiração do certificado PFX (padrão: 30 dias antes)
- [x] Shutdown graceful: `CancellationToken` + `IHostApplicationLifetime`

**Build:** `dotnet build` ✅ | **Testes:** 41/41 ✅

---

## Fase 5 – Minimal API, Hardening e Implantação ✅ CONCLUÍDA (2026-05-03)

### 5.1 Minimal API REST (consumida pelo dashboard Angular)
- [x] Configurar `UseStaticFiles()`, CORS para `http://localhost:4200` e `MapGet` em `Program.cs`
- [x] Implementar `StatusService`: ler `execucoes` e `condominios` via Dapper
- [x] Adicionar campo `status` (enum `ExecucaoStatus`, 9 estados) em `execucoes`
- [x] Atualizar status no `IntegracaoWorker` a cada transição de fase
- [x] Middleware de filtro de IP: aceitar apenas `localhost` + rede interna (RFC-1918)
- [x] Testes dos endpoints com `WebApplicationFactory<Program>`

### 5.2 Hardening
- [x] Auditar logs: sem CPF, nome, valor, nosso-número em texto claro (invariante mantida)
- [x] Permissões de arquivo do SQLite: ajustar via `icacls` no script de instalação Windows
- [x] Serviços convertidos de `AddScoped` para `AddSingleton` (stateless, sem captive dependency)
- [x] `ExecutarCicloAsync` isolado em `ExecutarCicloInternoAsync`: falha de ciclo não derruba o serviço

### 5.3 Implantação
- [x] `dotnet publish src/SicoobSuperlogica.Worker -c Release -r win-x64 --self-contained`
- [x] `sc create SicoobIntegracao binPath= "C:\caminho\SicoobSuperlogica.Worker.exe"`
- [x] Primeiro uso: definir `SICOOB_MASTER_PASSWORD` (ou digitar interativamente), cadastrar condomínios, verificar `GET /api/status`

**Build:** `dotnet build` ✅ | **Testes:** 47/47 ✅
