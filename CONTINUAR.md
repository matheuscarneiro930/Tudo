# Como continuar este projeto (instruções para o Claude)

Você está recebendo o projeto **Fodinha Online** (Unity 6000.4.7f1 + C#, celular, 2 a 15 jogadores).

## Regras de trabalho combinadas com o dono do projeto
1. Não criar classes duplicadas; não renomear nada sem checar dependências.
2. Arquitetura pensada para o jogo final e para o multiplayer desde já (nada provisório).
3. Regras do jogo SÓ no núcleo (`MatchEngine`). A interface apenas desenha `MatchView` e envia pedidos.
4. O cliente nunca recebe informação privada de outro jogador (usar `MatchEngine.BuildView(assento)`).
5. Não avançar com erros de compilação ou testes falhando. Testar de 2 a 15 jogadores. Commit ao fim de cada etapa.
6. FAÇA você mesmo (arquivos, comandos, cenas por script de Editor); só peça ao usuário o que for impossível sem ele.

## Estado
- Etapa 1 ✅ projeto Unity criado; testes rodam em batchmode.
- Etapa 2 ✅ partida offline contra bots: `MatchDirector` (Hosting), `LocalMatchController`, UI em retrato por código,
  cena `Assets/Fodinha/Scenes/PartidaOffline.unity` (gerada por `Fodinha.EditorTools.SceneBuilder.BuildAll`).
  164 testes EditMode + 1 PlayMode passando.
- Etapa 3 ✅ menu principal (cena `Menu`, primeira da build): `MenuScreen` (páginas), `MenuRoot` (abre a partida offline;
  pedidos online vão para `MenuRoot.OnlineHandler : IOnlineMenuHandler`, ainda não implementado), `RulesEditor`,
  `GamePrefs` (nome, som, vibração, últimas regras), `RoomCode`/`RoomOptions`/`RoomListing` (Hosting).
  184 EditMode + 3 PlayMode passando.
- Etapa 4 ✅: o dono escolheu **servidor .NET próprio** (não Photon). `Server/Fodinha.Server` (ServerCore,
  GameHost, WebSocket /ws), protocolo em `Assets/Fodinha/Scripts/Net`, cliente em `Assets/Fodinha/Scripts/Online`,
  cenas `Sala` e `PartidaOnline`. Testes: 136 dotnet (núcleo+host), 26 do servidor (inclui WebSocket real),
  197 EditMode, 4 PlayMode (inclui ponta a ponta subindo o servidor). Servidor no ar: https://fodinha-server-0ccs.onrender.com (Render, plano free; dorme após 15 min sem uso, ~50 s para acordar).
  `GamePrefs.DefaultServerUrl` = wss://fodinha-server-0ccs.onrender.com/ws. O push no branch faz o Render publicar sozinho.
- Etapa 5 ✅ polimento: Tweens (animações), Sfx (sons sintetizados), vibração, IconBuilder (ícone), splash.
- Etapa 6 ✅ APK: `BuildTool.BuildAndroid` (rodar por C:\Fodinha, junção sem acento: Gradle não aceita "Área").
  Saída Builds/Android/FodinhaOnline.apk; instruções em Docs/INSTALAR_ANDROID.md.
- Regras oficiais (pedido do dono): `RuleSet.WithOfficialRules()` — vira, naipe desempata, pé não fecha,
  errou perde 1, +1 carta por rodada, 1ª rodada cega. Só vidas, tempo, teto de cartas e tempos esgotados são opções.

## Ferramentas
- `Tools/run-unity-tests.ps1 [-Platform PlayMode]` — testes no Unity (feche o Editor antes).
- `Tools/run-unity-method.ps1 -Method <Classe.Metodo> [-Graphics]` — métodos de Editor em batchmode.
- Capturas da UI: `-Method Fodinha.EditorTools.UiSnapshot.CaptureFromCommandLine -Graphics -Extra "-snapshotDir <pasta>"`.
- `dotnet test Tools/CoreTests` — núcleo + host fora da Unity (rápido).
- Git e .NET 8 instalados via winget (`C:\Program Files\Git\cmd`, `C:\Program Files\dotnet`).

## Próximas etapas
3. Menu principal (offline com nº de bots e regras, criar sala, entrar com código, salas públicas, configurações).
4. Multiplayer (modo Host): salas públicas/privadas com código de 6 caracteres, lobby, host roda `MatchDirector`,
   clientes enviam pedidos e recebem só `BuildView(assento)`; timer, reconexão, bot no lugar de quem caiu.
5. Polimento (animações, sons, layout responsivo, ícone). 6. Build Android (.apk).
