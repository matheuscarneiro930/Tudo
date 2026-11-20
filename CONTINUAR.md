# Como continuar este projeto (instruções para o Claude)

Você está recebendo o projeto **Fodinha Online** (Unity 6 + C# + Photon Fusion 2, celular, 2 a 15 jogadores).

## O que já está pronto (etapa 1)
- Núcleo de regras em C# puro em `Assets/Fodinha/Scripts/Core` (sem dependência da Unity/Photon).
- 103 testes NUnit em `Assets/Fodinha/Tests/EditMode`, incluindo partidas simuladas de 2 a 15 jogadores. Todos passando.
- Regras implementadas: `Docs/REGRAS.md`. Arquitetura e próximos passos: `Docs/ARQUITETURA.md`.
- Testes fora da Unity: `dotnet test Tools/CoreTests`.

## Regras de trabalho combinadas com o dono do projeto
1. Não criar classes duplicadas; não renomear nada sem checar dependências.
2. Arquitetura pensada para o jogo final e para o multiplayer desde já (nada provisório).
3. Regras do jogo SÓ no núcleo (`MatchEngine`). A interface apenas desenha `MatchView` e envia pedidos.
4. O cliente nunca recebe informação privada de outro jogador (usar `MatchEngine.BuildView(assento)`).
5. Não avançar com erros de compilação. Testar a lógica para 2 a 15 jogadores.
6. O usuário prefere instruções passo a passo na Unity (onde clicar, que objeto, que referência no Inspector).
   Sempre que possível, FAÇA você mesmo (criar arquivos, rodar comandos, configurar cenas) em vez de pedir ao usuário.

## Feito na etapa 1 (projeto Unity)
- Projeto Unity 6000.4.7f1 criado nesta pasta (`Packages/`, `ProjectSettings/`), pacotes enxutos (uGUI, Test Framework).
- `Tools/run-unity-tests.ps1` roda os testes EditMode em batchmode: 103/103 passando.

## Próxima tarefa (etapa 2)
1. Montar a **partida offline contra bots**: um `LocalMatchController` (MonoBehaviour) que roda o `MatchEngine`,
   controla pausas/animações e timer, e alimenta uma interface para celular (retrato):
   mão com N cartas, mesa com até 14 adversários, painel de aposta, vira/manilha, vidas, vez do jogador, resultado da rodada.
4. Depois: integrar Photon Fusion 2 (modo Host), salas públicas/privadas com código.
