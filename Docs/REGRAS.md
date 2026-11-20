# Regras do jogo (implementadas no núcleo)

Este documento descreve **exatamente** o que o código em `Assets/Fodinha/Scripts/Core` faz.
Tudo que estiver marcado como *configurável* está em `RuleSet.cs`. Valor padrão entre parênteses.

## Baralho e força das cartas

- 40 cartas: 4 5 6 7 Q J K A 2 3 em Paus, Copas, Espadas e Ouros.
- Força normal (mais fraca → mais forte): **4 < 5 < 6 < 7 < Q < J < K < A < 2 < 3**.
- Cartas comuns de mesmo valor **empatam** (o naipe não desempata), exceto com a regra de empate `SuitBreaksTie`.

## Manilha (gato)

*Configurável — `ManilhaMode` (Vira):*

- **Vira**: depois de dar as cartas, uma carta é virada; o **próximo valor** é a manilha da rodada.
  A sequência é circular: vira 3 → manilha 4. Exemplos: vira 5 → 6; vira 7 → Q; vira K → A.
  Entre manilhas, o naipe decide: **Paus > Copas > Espadas > Ouros**. Qualquer manilha vence um 3.
- **Fixas** (opção): 4♣ (zap) > 7♥ (sete de copas) > A♠ (espadilha) > 7♦ (pica-fumo); sem vira.

> A carta "Salmoura" ainda não está implementada — falta definir qual carta é.

## Rodadas e cartas

- Rodada 1: 1 carta. A cada rodada, +1 carta.
- Máximo por jogador = (40 − 1 da vira) ÷ jogadores vivos:

| Jogadores | 2 | 3 | 4 | 5 | 6 | 7 | 8–9 | 10–13 | 14–15 |
|---|---|---|---|---|---|---|---|---|---|
| Máx. cartas | 19 | 13 | 9 | 7 | 6 | 5 | 4 | 3 | 2 |

- *Configurável — `Progression` (UpThenHold):* ao chegar no máximo, **repete** o máximo (e volta a subir quando alguém é eliminado). Opção `UpAndDown`: sobe até o máximo, desce até 1 e sobe de novo.
- *Configurável — `MaxCardsPerPlayer` (0 = sem teto):* teto opcional para não ter mãos gigantes no celular.

## Rodada cega (1 carta)

*Configurável — `BlindOneCardRounds` (sim):* em rodadas de 1 carta, cada jogador vê as cartas dos
outros mas **não a própria**. Depois das apostas, todas as cartas são reveladas juntas.
A informação é filtrada no servidor: o cliente nunca recebe a própria carta nessa rodada.

## Ordem

- O primeiro distribuidor é **sorteado**. A cada rodada o distribuidor passa para o próximo jogador vivo (sentido horário).
- As cartas são dadas a partir do jogador depois do distribuidor.
- **Apostas** começam no jogador depois do distribuidor; o distribuidor aposta por último (o "pé").
- **Primeira vaza** de cada rodada começa no jogador depois do distribuidor (o distribuidor joga por último).
- Nas vazas seguintes, **quem levou a vaza anterior começa**. Se ninguém levou (empate), começa quem abriu a anterior.
- Jogadores eliminados são sempre pulados.

## Apostas

- Cada jogador aposta de 0 até o número de cartas da rodada. Apostas são públicas assim que feitas.
- *Configurável — `LastBidderRestriction` (sim):* o pé **não pode** fazer a soma das apostas ficar igual ao número de cartas (garante que alguém erre).

## Vazas e empates

- Vence a carta mais forte. *Configurável — `TieRule` (CancelOut):*
  - **CancelOut**: cartas empatadas se anulam; vence a maior das restantes; se todas se anularem, ninguém leva.
  - **NoWinner**: se as maiores empatarem, ninguém leva.
  - **FirstPlayedWins**: entre as maiores empatadas, vence quem jogou primeiro.
  - **SuitBreaksTie**: o naipe desempata tudo (nunca há empate).

## Vidas

- *Configurável — `StartingLives` (3, entre 2 e 5).*
- Acertou a aposta: não perde vida. Errou: *configurável — `LifeLossMode` (OnePerMiss)*: perde **1** vida (opção `Difference`: perde a diferença).
- Com 0 vidas o jogador é eliminado: não recebe mais cartas e vira espectador.
- **Todos os vivos zeraram na mesma rodada**: vence quem tinha mais vidas no início da rodada.
  Se houver empate, os empatados voltam com 1 vida e o jogo continua.
- A partida termina quando sobra 1 jogador.

## Tempo, ausência e desconexão

- *Configurável — `TurnTimeSeconds` (30, entre 20 e 60).* O timer é controlado pela camada de rede.
- Tempo esgotado: o bot faz uma aposta/jogada razoável no lugar do jogador.
- *Configurável — `TimeoutsBeforeAutoPlay` (2):* depois de N tempos esgotados seguidos, o bot assume até o jogador tocar em "voltar a jogar".
- Desconexão: o bot joga pelo jogador (a camada de rede decide quando).
- Desistência: o bot termina a rodada e o jogador é eliminado ao final dela.
