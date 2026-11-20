# Fodinha Online

Jogo de cartas multiplayer (2 a 15 jogadores) inspirado na Fodinha, com hierarquia do Truco,
apostas de vazas, vidas e manilha. Unity 6 + C# + Photon Fusion 2, foco em celular.

- Regras implementadas: [`Docs/REGRAS.md`](Docs/REGRAS.md)
- Arquitetura: [`Docs/ARQUITETURA.md`](Docs/ARQUITETURA.md)

## Estado

| Etapa | Situação |
|---|---|
| 1. Núcleo de regras em C# puro + testes (2–15 jogadores) | ✅ pronto |
| 2. Partida offline contra bots na Unity | ⏳ próxima |
| 3. Multiplayer com Photon Fusion 2 (salas públicas/privadas) | ⏳ |
| 4. Interface final, animações e som | ⏳ |
| 5. Perfil, ranking, servidor dedicado, publicação | ⏳ |

## Estrutura

```
Assets/Fodinha/Scripts/Core   regras do jogo (sem dependência da Unity)
Assets/Fodinha/Tests/EditMode testes NUnit (rodam na Unity e no dotnet)
Tools/                        projetos .NET para compilar/testar o núcleo fora da Unity
Docs/                         regras e arquitetura
```

Este repositório é a raiz do projeto Unity. As pastas `ProjectSettings/` e `Packages/` entram quando o projeto Unity 6 for criado nesta pasta (próxima etapa).

## Rodando os testes

```bash
dotnet test Tools/CoreTests
```
