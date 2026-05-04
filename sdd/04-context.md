# API Integração Superlógica – Contexto de Entrega

> **Repositório**: `api-integracao-superlogica`

## Estado Atual

**Fase 5 concluída em 2026-05-04.** Minimal API REST com 3 endpoints (`/api/status`, `/api/condominios`, `/api/execucoes/{id}`), filtro de IP RFC-1918, `UseStaticFiles()`, CORS para `localhost:4200`, singletons para todos os serviços, e isolamento de ciclo via `ExecutarCicloInternoAsync`. Teste E2E Superlógica bloqueado até confirmação do endpoint.

### O que foi implementado (Fases 1–4)

| Arquivo | Responsabilidade |
|---|---|
| `src/.../Configuration/AppSettings.cs` | POCOs tipados; binding com `IConfiguration` |
| `src/.../Models/{Condominio,ExecucaoLog,ExecucaoStatus,SolicitacaoRetorno}.cs` | Modelos de domínio e enums |
| `src/.../Services/CryptoService.cs` | AES-256-GCM + PBKDF2-SHA256; blob: salt\|nonce\|tag\|ciphertext |
| `src/.../Services/CondominioService.cs` | CRUD SQLite via Dapper; `MatchNamesWithUnderscores = true` |
| `src/.../Services/AuthService.cs` | OAuth2 client_credentials + mTLS X509; cache in-memory com TTL - 30s |
| `src/.../Services/RetornoSicoobService.cs` | POST solicitar → polling → GET download em MemoryStream |
| `src/.../Services/DatabaseInitializer.cs` | Aplica `db/schema.sql` na inicialização |
| `src/.../Services/CnabParserService.cs` | Parser posicional CNAB 240 segmentos T+U; FluentValidation; Latin-1 |
| `src/.../Services/IdempotencyService.cs` | SHA-256(condId+dataInicial+dataFinal+fileHash); INSERT OR IGNORE |
| `src/.../Services/StatusService.cs` | Criar/atualizar `execucoes` no SQLite (Dapper) |
| `src/.../Models/CnabRegistro.cs` | `record` com dados financeiros em memória (nunca persistidos) |
| `src/.../Services/SuperlogicaService.cs` | Upload multipart CNAB 240 à Superlógica; headers por condomínio; status >= 300 = erro |
| `src/.../Workers/IntegracaoWorker.cs` | Pipeline completo: status → idempotência → parse → upload Superlógica → FINALIZADO |
| `src/.../Program.cs` | DI completo, Serilog, leitura senha mestre com máscara |
| `db/schema.sql` | DDL com `IF NOT EXISTS`, WAL mode, FK, índices |
| `tests/.../Unit/CryptoServiceTests.cs` | 5 testes unitários |
| `tests/.../Unit/CondominioServiceTests.cs` | 3 testes unitários com SQLite in-memory |
| `tests/.../Integration/AuthServiceIntegrationTests.cs` | 4 testes com WireMock.Net |
| `tests/.../Integration/RetornoSicoobServiceTests.cs` | 3 testes com WireMock.Net |
| `tests/.../Unit/CnabParserServiceTests.cs` | 8 testes unitários com fixtures CNAB 240 |
| `tests/.../Unit/IdempotencyServiceTests.cs` | 6 testes unitários com SQLite in-memory |
| `tests/.../Integration/IntegracaoWorkerTests.cs` | 6 testes: falha parcial, sem movimento, idempotência, falha upload, sequência de status, reprocessamento FALHA_TEMPORARIA |
| `tests/.../Integration/SuperlogicaServiceTests.cs` | 5 testes com WireMock.Net: sucesso, headers, 4xx, 5xx, stream reposicionado |
| `tests/.../Integration/DashboardApiTests.cs` | 6 testes com `WebApplicationFactory<Program>`: status, condominios, id existente, id inexistente, dados corretos, filtro IP |

**Resultados:** `dotnet build` ✅ | `dotnet test` 47/47 ✅

### Decisões técnicas tomadas na Fase 1

| Decisão | Justificativa |
|---|---|
| SDK `Microsoft.NET.Sdk.Web` em vez de `.Worker` | Preparação para Minimal API na Fase 5 sem trocar SDK |
| `.slnx` (novo formato .NET 10) | Gerado automaticamente pelo `dotnet new sln` no .NET 10 |
| `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` | Mapeamento automático `criado_em` → `CriadoEm` sem atributos |
| `SICOOB_MASTER_PASSWORD` env var | Permite execução não-interativa em CI/CD |
| `schema.sql` copiado para output via `<Content CopyToOutputDirectory>` | `DatabaseInitializer` localiza pelo `AppContext.BaseDirectory` |

## Premissas Confirmadas

| Premissa | Fonte |
|---|---|
| Sicoob disponibiliza API REST (Cobrança Bancária V2) com OAuth2 + mTLS | Manual de homologação Sicoob |
| Autenticação Sicoob exige certificado digital PFX A1 | Manual de homologação Sicoob |
| Arquivo de retorno é CNAB 240, gerado assincronamente por condomínio | Entrevista com gestor |
| Não é possível solicitar retorno de múltiplos condomínios em uma chamada | Entrevista com gestor |
| Superlógica Condomínios: API REST em `https://api.superlogica.net/v2/condor` | Portal apicondominios.superlogica.com |
| Autenticação Superlógica por `app_token` + `access_token` nos headers | Portal apicondominios.superlogica.com |
| Formato de data: `MM/DD/YYYY`; decimais com `.`; `status >= 300` = erro | Portal apicondominios.superlogica.com |
| Caminho de importação: Receitas → Retorno Bancário → Processar Arquivos | Informação direta do gestor |
| O arquivo aceito é o retorno CNAB 240; processamento automático após upload | Informação direta do gestor |
| SDK .NET 10 LTS (10.0.201) disponível no ambiente de desenvolvimento | `dotnet --list-sdks` |
| O dashboard consumirá os endpoints REST desta API na porta :5000 | Decisão de arquitetura global |

## Pendências Abertas

| Item | Responsável | Prazo |
|---|---|---|
| Endpoint programático de upload CNAB 240 (Superlógica) | Responsável técnico | Antes da Fase 5 (E2E) |
| `Content-Type` e campos do `multipart/form-data` do upload | Dev | Antes da Fase 5 (E2E) |
| Rate limits da API Sicoob em homologação | Dev | Durante testes E2E |
| Escopo do `access_token` por licença/condomínio na Superlógica | Dev | Durante testes E2E |
| Canal de notificação externo (e-mail, painel) — Fase 4 usou Serilog estruturado | Gestor + Dev | Pós-Fase 5 (backlog) |

## Decisões Técnicas

| Decisão | Justificativa |
|---|---|
| .NET 10 LTS (C#) | PFX nativo, `IHostedService` robusto, `AesGcm` nativo, deploy autocontido |
| Worker Service + Minimal API no mesmo host | Deploy único; sem processo separado para a API |
| CORS para `localhost:4200` em dev | Dashboard Angular em porta separada em desenvolvimento |
| Dados financeiros nunca persistidos | Segurança e privacidade; somente em memória durante ETL |
| AES-GCM + PBKDF2 para credenciais | Proteção em repouso; chave nunca gravada em texto plano |
| Polly para retry | Política declarativa sem duplicar lógica nos services |
| Idempotência por hash SHA-256 | Previne dupla baixa em reprocessamento ou falha parcial |
| Reprocessamento via `MarcarReprocessandoAsync` (→ `A_PROCESSAR`) | Evita acúmulo infinito de `FALHA_TEMPORARIA` sem esquema adicional |
| Log estruturado `alerta=true, tipo=CRITICO` via `BeginScope` | Permite filtro por campo no Serilog sem acoplar o worker a Serilog diretamente |
| `IHostApplicationLifetime.StopApplication()` em exceção fatal | Garante shutdown limpo sem matar o processo abruptamente |

## Riscos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Superlógica não ter endpoint programático de upload | Média | Alto | Fallback: automação de interface (Playwright) como plano B |
| Sicoob bloquear IP por volume de requisições | Baixa | Alto | Rate limiting + intervalo cadenciado + testes em homologação |
| Certificado PFX expirar sem alerta | Média | Alto | Alerta preventivo (Fase 4, padrão: 30 dias antes) |
| Mudança de layout CNAB 240 | Baixa | Médio | Parser com validação FluentValidation; falha explícita |

## Marcos

| Marco | Status |
|---|---|
| SDD backend concluído | ✅ |
| Setup .NET 10 + autenticação Sicoob em homologação | ✅ Fase 1 (2026-05-03) |
| Parser CNAB 240 + múltiplos condomínios + idempotência | ✅ Fase 2 (2026-05-03) |
| Confirmação endpoint Superlógica | ⬜ Pendente (path configurável; E2E bloqueado) |
| Integração Superlógica em homologação | ✅ Fase 3 (2026-05-03) |
| PeriodicTimer + reprocessamento + notificação | ✅ Fase 4 (2026-05-03) |
| Minimal API + hardening + publicação Windows | ✅ Fase 5 (2026-05-04) |
