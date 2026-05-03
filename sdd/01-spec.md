# API Integração Superlógica – Especificação do Backend

> **Repositório**: `api-integracao-superlogica`
> **Parte de**: [Sistema Global](../../sdd/01-spec.md)

## 1. Missão

Implementar o agente de integração responsável por extrair arquivos de retorno CNAB 240 do Sicoob e entregá-los automaticamente na Superlógica Condomínios, eliminando o processo manual diário de download e importação condomínio a condomínio.

## 2. Personas

- **Administrador de condomínios**: usa o dashboard (projeto irmão) para acompanhar; não interage diretamente com este backend.
- **Responsável técnico**: configura credenciais, monitora logs e opera o serviço Windows.

## 3. Histórias de Usuário (escopo backend)

- **HU1**: Como sistema, devo autenticar na API Sicoob com OAuth2 + mTLS usando certificado PFX A1.
- **HU2**: Como sistema, devo solicitar e baixar o arquivo CNAB 240 de retorno para cada condomínio de forma assíncrona com polling controlado.
- **HU3**: Como sistema, devo parsear o CNAB 240 e extrair registros de liquidação dos segmentos T e U.
- **HU4**: Como sistema, devo fazer upload do arquivo CNAB 240 para a Superlógica Condomínios no caminho Receitas → Retorno Bancário → Processar Arquivos.
- **HU5**: Como sistema, devo armazenar credenciais de cada condomínio criptografadas em repouso (AES-GCM).
- **HU6**: Como sistema, devo garantir idempotência: nunca reprocessar um arquivo já integrado com sucesso.
- **HU7**: Como sistema, devo expor via Minimal API REST os endpoints `/api/status`, `/api/condominios` e `/api/execucoes/{id}` para consumo pelo dashboard Angular.
- **HU8**: Como sistema, devo atualizar o status de cada execução em tempo real no SQLite, percorrendo os 9 estados definidos no contrato global.

## 4. Regras de Negócio Invariantes

- Nenhum dado financeiro de condôminos é persistido além do tempo da operação ETL; tudo em memória.
- Credenciais armazenadas criptografadas; nunca em texto plano em disco ou logs.
- Processamento sequencial por condomínio com intervalo mínimo de 60 s entre cada um.
- Falha de um condomínio não interrompe os demais.
- O sistema não reprocessa arquivo já integrado com sucesso (idempotência por hash SHA-256).
- Logs não contêm CPF, nome de condômino, valor de boleto ou nosso-número em texto claro.
- Todo ciclo de execução deve persistir log com resultado, timestamp e status final.

## 5. Cenários de Falha

- **Timeout/erro Sicoob**: registrar falha, pular para próximo condomínio, retentar na próxima janela.
- **Arquivo CNAB ainda não gerado**: polling até 10 tentativas × 2 min; após limite → `FALHA_TEMPORARIA`.
- **Falha no upload para Superlógica**: preservar referência, marcar `FALHA_TEMPORARIA`, reprocessar automaticamente no próximo ciclo.
- **Credenciais inválidas ou certificado expirado**: bloquear condomínios vinculados, emitir alerta crítico.
- **Condomínio sem retorno no período**: registrar `SEM_MOVIMENTO`, não gerar erro, seguir para o próximo.
- **Arquivo duplicado**: idempotência ativa; ignorar silenciosamente.
- **Falha de rede**: retry com Polly; após limite, aguardar próxima janela agendada.
- **Endpoint Superlógica retorna status >= 300**: registrar body de erro, marcar `FALHA_TEMPORARIA`.

## 6. Modelo de Status da Execução

| Enum | Label (contrato com dashboard) | Condição de entrada |
|---|---|---|
| `A_PROCESSAR` | A PROCESSAR | Início do ciclo para o condomínio |
| `PROCESSAMENTO_FINALIZADO` | PROCESSAMENTO FINALIZADO | Sicoob confirmou `GERADO` |
| `ARQUIVO_BAIXADO` | ARQUIVO BAIXADO | Download em memória concluído |
| `ENVIANDO_TITULOS` | ENVIANDO TÍTULOS BAIXADOS | Upload para Superlógica iniciado |
| `SEM_TITULOS` | NÃO HÁ TÍTULOS BAIXADOS | Sicoob retornou `SEM_MOVIMENTO` |
| `ENVIADO_SUPERLOGICA` | ENVIADO À SUPERLÓGICA | Superlógica confirmou upload (2xx) |
| `FINALIZADO` | FINALIZADO BAIXA DE TÍTULOS | Idempotência persistida no SQLite |
| `FALHA_TEMPORARIA` | FALHA (temp.) | Erro transitório; reprocessamento automático |
| `FALHA_PERMANENTE` | FALHA PERMANENTE | Erro de negócio; requer intervenção |

> Este modelo é o contrato entre backend e dashboard. A definição canônica está em [sdd global §02-plan](../../sdd/02-plan.md#modelo-de-status).
