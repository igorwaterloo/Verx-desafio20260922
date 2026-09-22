# Guia de Contribuição

## Fluxo de branches

O projeto segue um fluxo baseado em **feature branches** com integração no `main`:

- `main` é sempre estável e recebe apenas **merges** (`--no-ff`, preservando o histórico de cada entrega).
- Cada fluxo entregue é desenvolvido em uma branch própria, criada a partir do `main` atualizado.
- Após concluída e validada (build + testes verdes), a branch é integrada ao `main` e removida.

### Nomenclatura de branches

`<tipo>/<descricao-curta-em-kebab-case>`

| Prefixo | Uso | Exemplo |
|---|---|---|
| `feature/` | Nova funcionalidade | `feature/servico-lancamentos` |
| `fix/` | Correção de defeito | `fix/calculo-saldo-estorno` |
| `docs/` | Documentação, diagramas, ADRs | `docs/arquitetura-c4` |
| `test/` | Testes (unitários, integração, carga) | `test/stress-resiliencia` |
| `chore/` | Configuração, manutenção | `chore/setup-repositorio` |
| `refactor/` | Refatoração sem mudança de comportamento | `refactor/extrai-value-object-valor` |
| `ci/` | Pipeline e automação | `ci/github-actions` |

### Passo a passo

```bash
git checkout main && git pull
git checkout -b feature/minha-entrega
# ... commits ...
git checkout main && git pull
git merge --no-ff feature/minha-entrega
git push origin main
git branch -d feature/minha-entrega
```

## Padrão de commits — Conventional Commits

`<tipo>(escopo opcional): descrição no imperativo, em minúsculas`

| Tipo | Quando usar |
|---|---|
| `feat` | Nova funcionalidade |
| `fix` | Correção de bug |
| `docs` | Somente documentação |
| `test` | Adição/ajuste de testes |
| `refactor` | Refatoração sem mudança de comportamento |
| `perf` | Melhoria de desempenho |
| `build` | Build, dependências, MSBuild/NuGet/npm |
| `ci` | Pipeline de CI/CD |
| `chore` | Tarefas gerais/configuração |

Exemplos:

```
feat(lancamentos): adiciona comando de registro de lançamento
test(consolidado): cobre idempotência do consumidor de eventos
docs(adr): registra decisão sobre transactional outbox
```

## TDD

Novas regras de negócio seguem o ciclo **vermelho → verde → refatorar**: o teste que descreve o comportamento é escrito antes da implementação, preferencialmente no mesmo commit ou em commit imediatamente anterior (`test: ...` seguido de `feat: ...`).

## Qualidade

- `dotnet build` sem warnings (warnings são tratados como erro).
- `dotnet test` verde antes do merge.
- Estilo definido em `.editorconfig`, validado no build.
