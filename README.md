# Fodinha Online

Jogo de cartas multiplayer (2 a 15 jogadores) inspirado na Fodinha, com hierarquia do Truco,
apostas de vazas, vidas e manilha. Unity 6 + C# + Photon Fusion 2, foco em celular.

- Regras implementadas: [`Docs/REGRAS.md`](Docs/REGRAS.md)
- Arquitetura: [`Docs/ARQUITETURA.md`](Docs/ARQUITETURA.md)

## Estado

| Etapa | Situação |
|---|---|
| 0. Núcleo de regras em C# puro + testes (2–15 jogadores) | ✅ pronto |
| 1. Projeto Unity 6 (6000.4.7f1) criado, testes EditMode passando na Unity | ✅ pronto |
| 2. Partida offline contra bots na Unity (cena `PartidaOffline`) | ✅ pronto |
| 3. Menu principal (cena `Menu`: offline com regras, criar sala, código, salas públicas, configurações) | ✅ pronto |
| 4. Multiplayer online (salas públicas/privadas com código) | ⏳ próxima |
| 5. Polimento: animações, sons, layout responsivo, ícone | ⏳ |
| 6. Build Android (.apk) | ⏳ |

## Estrutura

```
Assets/Fodinha/Scripts/Core   regras do jogo (sem dependência da Unity)
Assets/Fodinha/Tests/EditMode testes NUnit (rodam na Unity e no dotnet)
Tools/                        projetos .NET para compilar/testar o núcleo fora da Unity
Docs/                         regras e arquitetura
```

Este repositório é a raiz do projeto Unity (Unity 6000.4.7f1). Abra a pasta pelo Unity Hub → Add → Add project from disk.

## Rodando os testes

Na Unity (batchmode, com o editor fechado):

```bash
powershell -ExecutionPolicy Bypass -File Tools/run-unity-tests.ps1
```

Fora da Unity:

```bash
dotnet test Tools/CoreTests
```
