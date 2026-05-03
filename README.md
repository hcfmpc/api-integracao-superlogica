# API Integração Para Boletos Liquidados SICOOB x SUPERLOGICA
Integração destinada à sincronização de informações de boletos liquidados do Sicoob com a plataforma de gestão condominial Superlógica (https://superlogica.com/condominios/), visando automatizar o fluxo de dados financeiros e garantir maior eficiência na administração de boletos liquidados.

## SDD - Spec-Driven Development

### Playbook-SDD

PDF de orientação do modo de emprego do SDD, contendo um Playbook de Engenharia: Implementação de Spec-Driven Development com IA Generativa Pura.

### 01-spec.md

Este documento captura a intenção do negócio, as personas dos usuários e os critérios de sucesso. Ele é agnóstico à tecnologia. Seu objetivo é eliminar a ambiguidade semântica.

### 02-plan.md

Este é o projeto de arquitetura. Ele traduz a Spec em decisões técnicas: schemas de banco de dados, contratos de API (OpenAPI), estrutura de diretórios e seleção de bibliotecas.

### 03-taks.md

Uma lista de tarefas atômicas, sequenciais e testáveis. Este arquivo transforma o projeto arquitetural num grafo de dependências linearizado que a IA pode consumir passo a passo.

### 04-context.md

Um arquivo dinâmico onde o desenvolvedor consolida as decisões tomadas e fragmentos de código críticos já implementados. Este arquivo atua como um "buffer de contexto" para novas sessões de chat.

### Para desenvolver no projeto por favor leia o PDF contendo as orientações para desenvolver com SDD e inicie seu prompt com as orientações:

Definição de Persona: "Você é um Engenheiro Sênior especialista em desenvolvimento REACT NATIVE que respeita e e emprega Clean Code, DDD, Clean Architecture, 12 factors e Design Patterns."

Injeção da Verdade: "Aqui está a Especificação Imutável (./sdd/01-spec.md) e o Plano Técnico (./sdd/02-plan.md)."

Estado Atual: "Aqui está o que já foi implementado (./sdd/04-context.md)."

Diretiva de Foco: "Implemente apenas as Tarefas listadas em (./sdd/03-task.md). Não altere códigos anteriores a menos que estritamente necessário. Não gerar o documento final até que
todas as dúvidas críticas (edge cases, fluxos de erro, restrições de negócio) sejam esclarecidas."

- Esta abordagem simula o funcionamento de agentes autônomos que leem arquivos a cada iteração, garantindo que a IA esteja sempre "ancorada" na versão mais atual da verdade, e não em alucinações de conversas passadas.

---

## ⚙️ Ambiente de Desenvolvimento com WSL2, AI-JAIL e Claude Code

### 1. Instalar WSL2 com Ubuntu
No PowerShell do Windows (como administrador):
```powershell
wsl --install -d Ubuntu
```

Defina o Ubuntu como distribuição padrão:

```powershell
wsl --set-default Ubuntu
```

### 2. Acessar o WSL
Abra o terminal e digite:

```powershell
wsl
```

Isso inicia o Ubuntu dentro do WSL2.

### 3. Instalar dependências no Ubuntu

#### Bubblewrap
```bash
sudo apt update
sudo apt install bubblewrap
```

#### AI-JAIL
```bash
cd ~
curl -fsSL https://github.com/akitaonrails/ai-jail/releases/latest/download/ai-jail-linux-x86_64.tar.gz | tar xz
sudo mv ai-jail /usr/local/bin/
```

#### Claude Code
```bash
curl -fsSL https://claude.ai/install.sh | bash
```

#### RTK (Rust Token Kille)

RTK é um poxy de linha de comando que reduz 60–90% do consumo de tokens em operações de desenvolvimento. Um hook `PreToolUse` em `~/.claude/settings.json` intercepta toda chamada da ferramenta `Bash` e reescreve o comando automaticamente via `rtk-rewrite.sh`, sem overhead para o Claude.

```bash
curl -fsSL https://raw.githubusercontent.com/rtk-ai/rtk/refs/heads/master/install.sh | sh
```

Após a instalação, inicialize o RTK **antes** de odar o `ai-jail claude`:

```bash
rtk init -g
```

Isso cia o hook em `~/.claude/settings.json` que intercepta Bash calls automaticamente:

```json
{
  "hooks": {
    "PeToolUse": [
      {
        "matche": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "~/.claude/hooks/tk-rewrite.sh"
          }
        ]
      }
    ]
  }
}
```

Em seguida, inicie o Claude nomalmente:

```bash
ai-jail claude
```

### 4. Uso do AI-JAIL no projeto

> **IMPORTANTE:** O projeto deve obrigatoriamente residir dentro do filesystem do WSL2, **não** na pasta Windows acessada via `/mnt/c/`. Trabalhar com o repositório em `C:\...` (mesmo pelo terminal WSL2 via `/mnt/c/...`) causa divergência silenciosa de line endings entre o git do Windows e o git do WSL2, fazendo arquivos aparecerem como modificados sem que nenhuma alteração real tenha sido feita. O repositório já inclui um `.gitattributes` com `* text=auto eol=lf` e o local config `core.autocrlf=false` deve ser mantido — se arquivos voltarem a aparecer como modificados sem alterações reais, verifique com `git config core.autocrlf`.

Abra o terminal WSL2 e clone o repositório diretamente na home do Ubuntu:

```bash
mkdir -p ~/projetos
cd ~/projetos
git clone https://github.com/hcfmpc/api-integracao-superlogica.git
cd mobile-green-wave
```

Abra o VS Code a partir de dentro do WSL2, para que a extensão Remote WSL use o git e o ambiente Linux:

```bash
code .
```

No terminal integrado do VS Code (que já estará no contexto WSL2), rode o Claude Code dentro da sandbox:

```bash
ai-jail claude
```

> Na primeira execução, será criado um arquivo `.ai-jail` no diretório do projeto, que guarda as configurações da sandbox.

---

## 🔒 Uso Obrigatório do AI-JAIL no Projeto

Neste projeto, o Claude deve ser executado sempre pelo terminal dentro do WSL2, usando o comando abaixo:

```bash
ai-jail claude
```

Isso não é uma preferência operacional. É um requisito do projeto.

### Como o AI-JAIL funciona

O AI-JAIL funciona como um wrapper de processo CLI. Quando você executa `ai-jail claude`, ele envolve o processo do Claude Code e intercepta as chamadas de ferramentas, como comandos de shell e operações de escrita ou edição, aplicando as regras definidas no arquivo `.ai-jail`.

Em outras palavras, o AI-JAIL só consegue controlar o Claude quando o processo do Claude é iniciado por ele.

### Diferença entre usar no terminal e usar no chat da extensão

| Contexto | AI-JAIL ativo? | Quem controla as ferramentas? | Resultado prático |
| --- | --- | --- | --- |
| Terminal no WSL2 com `ai-jail claude` | Sim | O AI-JAIL aplica as regras do `.ai-jail` | O Claude opera dentro da sandbox prevista pelo projeto |
| Chat do VS Code pela extensão | Não | Apenas o mecanismo nativo da extensão | O processo não passa pelo AI-JAIL e as regras não são aplicadas |

### O que isso significa na prática

- O chat da extensão do VS Code não executa o Claude por meio do AI-JAIL.
- Quando a extensão dispara ferramentas de terminal, edição ou escrita, isso acontece fora do wrapper do AI-JAIL.
- Como resultado, as regras do arquivo `.ai-jail` sao ignoradas nesse fluxo.

### Diretriz obrigatória para quem clonar este repositório

- Clonar o repositório dentro do filesystem do WSL2 (ex: `~/projetos/`), nunca em `C:\...`.
- Abrir o VS Code com `code .` a partir do terminal WSL2 (extensão Remote WSL ativa).
- Sempre iniciar o Claude com `ai-jail claude` no terminal integrado do VS Code.
- Não usar o chat da extensão do VS Code para tarefas de implementação, alteração de arquivos ou execução de comandos no projeto.
- Não acessar o repositório via `/mnt/c/...` pelo WSL2 — isso equivale ao filesystem Windows e mantém o problema de divergência de git.

### Recomendação de uso seguro

Se o projeto exigir conformidade com as regras de sandbox e execução controlada, o fluxo correto é exclusivamente via terminal com AI-JAIL.

O chat da extensão pode até servir para consultas pontuais, mas ele não garante aderência às regras do projeto e pode introduzir drift entre o processo esperado e o processo realmente executado.
