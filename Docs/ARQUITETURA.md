# Arquitetura

## Princípio

**As regras do jogo não dependem da Unity nem do Photon.** Elas ficam em C# puro (`Fodinha.Core`),
rodam apenas no host/servidor e são testadas automaticamente para 2 a 15 jogadores.
A rede só transporta pedidos e estado; a interface só desenha e envia pedidos.

```
┌───────────────────────────────┐
│  Apresentação (Unity UI)      │  desenha MatchView, toca animações/sons,
│  CardView, HandView, SeatView │  envia pedidos: "apostar 2", "jogar 7♥"
└──────────────┬────────────────┘
               │ IGameActions (local ou rede)
┌──────────────▼────────────────┐
│  Rede (Photon Fusion 2)       │  RPC cliente → host com o pedido;
│  NetworkMatch, NetworkPlayer  │  host aplica no MatchEngine; envia a cada
└──────────────┬────────────────┘  cliente SÓ a MatchView dele
               │
┌──────────────▼────────────────┐
│  Núcleo (C# puro) ✅ pronto   │  MatchEngine, CardComparer, RuleSet,
│  Assets/Fodinha/Scripts/Core  │  RoundPlanner, BetRules, SimpleBot
└───────────────────────────────┘
```

## Núcleo (`Assets/Fodinha/Scripts/Core`)

| Arquivo | Responsabilidade |
|---|---|
| `Card.cs` | Carta (valor + naipe), id 0–39 para a rede, `Parse("7♥")` |
| `RuleSet.cs` | Todas as regras configuráveis + validação |
| `CardComparer.cs` | Força das cartas, manilha, empates, vencedor da vaza com N jogadores |
| `Deck.cs` | Baralho de 40 cartas, embaralhamento (fonte de aleatoriedade injetável) |
| `RoundPlanner.cs` | Quantas cartas por rodada (máximo por nº de jogadores, progressão) |
| `BetRules.cs` | Apostas permitidas (regra do pé) e vidas perdidas |
| `PlayerState.cs` | Estado oficial do jogador (vidas, aposta, vazas, mão privada, bot) |
| `MatchTypes.cs` | Fases, códigos de erro, eventos, resumo da rodada, `MatchView` |
| `MatchEngine.cs` | Máquina de estados da partida — único lugar onde o estado muda |
| `Bots/SimpleBot.cs` | Bot que decide só com o que o jogador vê (offline, tempo esgotado, desconexão) |

### Fluxo do `MatchEngine`

```
NotStarted ─StartMatch→ Betting ─(todos apostaram)→ Playing ─(todos jogaram)→ TrickComplete
                           ▲                                                     │
                           │                              ContinueAfterTrick ◄───┘
                           │                         (mais vazas → Playing | acabou → pontua)
                           └──StartNextRound── RoundComplete ──(sobrou 1)→ MatchOver
```

- Ações de jogadores (`TryPlaceBet`, `TryPlayCard`) **validam tudo** e retornam um `ActionResult` (nunca confiam no cliente).
- As pausas (`TrickComplete`, `RoundComplete`) existem para o host controlar o tempo das animações.
- `BuildView(assento)` gera o que aquele jogador pode ver. É isso que vai pela rede.
- `EventRaised` emite eventos públicos (sem mãos) para animações e sons.

## Host (`Assets/Fodinha/Scripts/Hosting`, C# puro) ✅

| Arquivo | Responsabilidade |
|---|---|
| `MatchDirector.cs` | Ritmo da partida no host: pausa após distribuir, tempo de pensar dos bots, relógio do jogador (tempo esgotado → `HandleTimeout`), pausas de vaza/rodada (`ContinueAfterTrick`/`StartNextRound`). **Sem regras**: só decide *quando* chamar o motor. Bots de verdade, ausentes e desconectados são "automatizados". |
| `PacingSettings.cs` | Durações das pausas (`Instant` para testes). |

O mesmo `MatchDirector` roda no modo offline e vai rodar no host da sala online.

## Jogo na Unity (`Assets/Fodinha/Scripts/Game`) ✅

| Arquivo | Responsabilidade |
|---|---|
| `Session/IMatchSession.cs` | O que a tela enxerga: `View` (já filtrada), relógios, eventos, e os pedidos (`IGameActions`: `PlaceBet`, `PlayCard`, `ResumeControl`). |
| `Session/LocalMatchController.cs` | Sessão offline: assento 0 humano + bots, roda o `MatchDirector` no `Update`, publica `BuildView(0)`. |
| `Session/OfflineMatchConfig.cs` | Nº de bots, regras, nome, semente; nomes dos bots. |
| `OfflineGameRoot.cs` | Liga tela ↔ sessão na cena offline (jogar de novo, menu). |
| `UI/MatchScreen.cs` | Tela em retrato montada por código: topo (rodada, vira/manilha), mesa, faixa do jogador, mão, ações, painéis. Só desenha `MatchView` e envia pedidos. |
| `UI/TableView.cs`, `SeatView.cs`, `TableLayout.cs` | Mesa oval com até 14 adversários em sentido horário; cartas da vaza na direção de quem jogou. |
| `UI/HandView.cs`, `CardView.cs`, `BetPanel.cs` | Mão (1º toque seleciona, 2º joga), cartas desenhadas por código, botões de aposta (o pé vê o valor proibido desabilitado). |
| `UI/RoundResultPanel.cs`, `MatchOverPanel.cs` | Resultado da rodada e classificação final. |
| `UI/SpriteFactory.cs`, `UiKit.cs` | Sprites gerados por código e atalhos de UI (sem assets externos). |

Editor (`Assets/Fodinha/Editor`): `SceneBuilder` monta as cenas, `ProjectConfigurator` aplica retrato/nome/identificador,
`UiSnapshot` gera PNGs da tela em vários cenários, `MatchDriver` avança partidas em testes.

## Próximas camadas (a fazer)

1. **Menu principal** (offline com nº de bots e regras, criar/entrar em sala, salas públicas, configurações).
2. **Rede (modo Host)**: sessão com código, salas públicas/privadas, `NetworkMatch` no host chamando o `MatchEngine`,
   mão privada entregue só ao dono (objeto por jogador com *Object Interest = Explicit*, ou RPC direcionado).
4. **Fusion 2 (modo Server)** antes das salas públicas: o mesmo núcleo roda no servidor dedicado (sem trapaça do host).
5. Perfil/ranking, polimento, publicação.

## Testes

- Na Unity: **Window → General → Test Runner → EditMode → Run All** (e PlayMode).
- Linha de comando (Unity fechado): `Tools/run-unity-tests.ps1` e `Tools/run-unity-tests.ps1 -Platform PlayMode`.
- Fora da Unity: `dotnet test Tools/CoreTests` (núcleo + host).
  `Tools/CoreBuild` e `Tools/HostingBuild` compilam com as mesmas restrições da Unity 6 (netstandard2.1, C# 9).
- Capturas da tela: `Tools/run-unity-method.ps1 -Method Fodinha.EditorTools.UiSnapshot.CaptureFromCommandLine -Graphics`.
