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
  IMPORTANTE: gerar com `Tools/run-unity-method.ps1 -Method Fodinha.EditorTools.BuildTool.BuildAndroid -BuildTarget Android`,
  senão o processador do AdMob (só existe com UNITY_ANDROID) não roda e o APK sai com o App ID antigo.
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
- Parte 1 (ajustes do dono) ✅ naipes desenhados (SpriteFactory.Suit), vira no meio, "vaza" → "rodada", aposta na
  carta dos adversários, vidas em número, 3 vidas fixas, 30 s para apostar (RuleSet.BetTimeSeconds), 5 min para voltar.
- Parte 2A ✅ economia: contas no servidor (Accounts.cs, AccountStore.cs), 500 moedas iniciais, salas valendo
  (RoomOptions.Stake), pote para o vencedor, recompensa +100 (provisória até o AdMob), ranking global.
  Banco: Postgres no Neon, configurado no Render pela variável DATABASE_URL (/health mostra "storage").
- Parte 2B ✅ AdMob (com.google.ads.mobile 11.7.0 via OpenUPM): Fodinha.Ads/AdMobService (UMP, vídeo premiado,
  tela cheia no fim da partida), Game/Ads/AdService (interface + provisório no site/PC). IDs de TESTE em AdIds e
  AdMobConfigurator. IDs REAIS do dono já configurados (app ~9837967079, premiado /5275693506, intersticial /9647676640).
  Sem vídeo disponível, o premiado cai na tela provisória (o servidor limita a recompensa).
- Parte 2C ✅ avatar e loja com moedas:
  - `Hosting/AvatarSpec` (avatar em texto "1-…", 9 partes; bots = `FromSeed(nome)`), `Hosting/ShopCatalog` (mesas e
    baralhos, preços; grátis = de todos). Desenho no app: `Game/UI/AvatarPainter` (procedural, cache), `AvatarView`,
    `CosmeticLooks` (cores das mesas/baralhos), `SpriteFactory.TableFor/CardBackFor`.
  - Menu: avatar ao lado do nome (página Avatar), botões Loja/Configurações, "+100 moedas" sempre visível
    (apagado com 500+). Avatar aparece no lobby, nos assentos e no jogador local.
  - Servidor: set_avatar, buy_item, equip_item (conta: avatar, owned, table_theme, deck_theme no Postgres);
    `MatchUpdateDto.avatars` por assento; `MemberDto.avatar`.
  - Código VIP (conta sem anúncios): variável VIP_CODE no Render (mín. 6 caracteres; nunca no código).
    Configurações → Código VIP → redeem_code; 5 erros = bloqueio de 10 min. `GamePrefs.NoAds` pula os anúncios.
  - Revisar avatares: `-Method Fodinha.EditorTools.AvatarSheet.ExportFromCommandLine -Extra "-sheetPath <png>"`.
  - Avatar com volume (luz 3D por "normais", cabelo com brilho, olhos com cílios, lábios), brincos e cabelos
    Chanel/Rabo de cavalo. Partes novas do AvatarSpec entram SEMPRE no fim: textos antigos (9 partes) seguem válidos.
    Óculos ficaram de fora de propósito: serão acessório pago (2D).
  - Assento do adversário: avatar fora do quadrado (à esquerda), aposta numa plaquinha amarela grande,
    rodada cega mostra a carta "na testa" (em cima do avatar).
- Decidido pelo dono: mesas e baralhos serão vendidos SÓ com dinheiro de verdade (na 2D); moedas ficam para apostas.
- Próxima: 2D loja de acessórios com dinheiro real (Google Play Billing) — pode incluir "remover anúncios"
  (já existe o campo NoAds na conta).
