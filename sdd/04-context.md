# API Integração Superlógica – Contexto de Entrega

> **Repositório**: `api-integracao-superlogica`

## Estado Atual

**Fase 2 concluída em 2026-05-03.** Pronto para iniciar a Fase 3 (pré-requisito: confirmar endpoint Superlógica).

### O que foi implementado (Fases 1 e 2)

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
| `src/.../Workers/IntegracaoWorker.cs` | Pipeline completo: status → idempotência → parse → FINALIZADO |
| `src/.../Program.cs` | DI completo, Serilog, leitura senha mestre com máscara |
| `db/schema.sql` | DDL com `IF NOT EXISTS`, WAL mode, FK, índices |
| `tests/.../Unit/CryptoServiceTests.cs` | 5 testes unitários |
| `tests/.../Unit/CondominioServiceTests.cs` | 3 testes unitários com SQLite in-memory |
| `tests/.../Integration/AuthServiceIntegrationTests.cs` | 4 testes com WireMock.Net |
| `tests/.../Integration/RetornoSicoobServiceTests.cs` | 3 testes com WireMock.Net |
| `tests/.../Unit/CnabParserServiceTests.cs` | 8 testes unitários com fixtures CNAB 240 |
| `tests/.../Unit/IdempotencyServiceTests.cs` | 6 testes unitários com SQLite in-memory |
| `tests/.../Integration/IntegracaoWorkerTests.cs` | 3 testes: falha parcial, sem movimento, idempotência |

**Resultados:** `dotnet build` Release ✅ | `dotnet test` 33/33 ✅

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
| Endpoint programático de upload CNAB 240 (Superlógica) | Responsável técnico | Antes da Fase 3 |
| `Content-Type` e campos do `multipart/form-data` do upload | Dev | Antes da Fase 3 |
| Rate limits da API Sicoob em homologação | Dev | Durante a Fase 1 |
| Escopo do `access_token` por licença/condomínio na Superlógica | Dev | Durante a Fase 3 |
| Meio de notificação de falhas críticas (e-mail, painel, outro) | Gestor + Dev | Antes da Fase 4 |

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
| Confirmação endpoint Superlógica | ⬜ Pré-Fase 3 |
| Integração Superlógica em homologação | ⬜ Fase 3 |
| PeriodicTimer + reprocessamento + notificação | ⬜ Fase 4 |
| Minimal API + hardening + publicação Windows | ⬜ Fase 5 |
