using System;
using Fodinha.Core;
using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Formulário das regras configuráveis (<see cref="RuleSet"/>), para a partida offline ou para criar uma sala.
    /// Só monta o objeto de regras; quem valida é o <see cref="RuleSet"/>/<see cref="MatchEngine"/>.
    /// </summary>
    public sealed class RulesEditor : MonoBehaviour
    {
        public enum Mode { Offline, Room }

        private static readonly int[] TurnTimes = { 20, 25, 30, 35, 40, 45, 50, 55, 60 };
        private static readonly int[] CardCaps = { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        private Mode mode;
        private OptionStepper players;
        private OptionStepper visibility;
        private OptionStepper lives;
        private OptionStepper turnTime;
        private OptionStepper cardCap;
        private OptionStepper timeouts;

        public Mode EditorMode => mode;

        public static RulesEditor Create(Transform parent, Mode mode)
        {
            var content = UiControls.ScrollArea("Regras", parent, 14f);
            var editor = content.parent.gameObject.AddComponent<RulesEditor>();
            editor.Build(content, mode);
            return editor;
        }

        private void Build(RectTransform content, Mode editorMode)
        {
            mode = editorMode;
            if (mode == Mode.Offline)
            {
                players = OptionStepper.Create(content, "Bots na mesa", Range(OfflineMatchConfig.MinBots, OfflineMatchConfig.MaxBots, ""), 2);
            }
            else
            {
                visibility = OptionStepper.Create(content, "Sala", new[] { "Privada", "Pública" }, 1);
                players = OptionStepper.Create(content, "Máx. jogadores", Range(RuleSet.MinPlayers, RuleSet.MaxPlayers, ""), 6);
            }

            lives = OptionStepper.Create(content, "Vidas", Range(RuleSet.MinLives, RuleSet.MaxLives, ""), 1);
            turnTime = OptionStepper.Create(content, "Tempo de jogada", Map(TurnTimes, v => v + " s"), 2);
            cardCap = OptionStepper.Create(content, "Máx. de cartas", Map(CardCaps, v => v == 0 ? "Sem limite" : v.ToString()), 0);
            timeouts = OptionStepper.Create(content, "Tempo esgotado: bot assume após",
                                            new[] { "1 vez", "2 vezes", "3 vezes", "4 vezes", "5 vezes" }, 1);

            // As demais regras são fixas (regras oficiais): só informa.
            var info = UiKit.Label("RegrasOficiais", content, OfficialRulesText, 30, Palette.TextMuted, TextAnchor.UpperLeft);
            UiControls.SetHeight(info, 300f);
        }

        /// <summary>Texto com as regras fixas do jogo.</summary>
        public const string OfficialRulesText =
            "<b>Regras oficiais (sempre valem):</b>\n" +
            "• Manilha pela vira (o valor seguinte à carta virada)\n" +
            "• Empate: o naipe desempata (♣ > ♥ > ♠ > ♦)\n" +
            "• O pé não pode apostar o número que fecha a soma\n" +
            "• Errou a aposta: perde 1 vida\n" +
            "• Cada rodada tem 1 carta a mais, até o baralho permitir\n" +
            "• A 1ª rodada (1 carta) é cega";

        // ------------------------------------------------------------------ leitura/escrita

        public void Load(RuleSet rules, int playerOrBotCount, bool isPublic = true)
        {
            rules = rules ?? RuleSet.CreateDefault();
            if (mode == Mode.Offline) players.SetIndex(playerOrBotCount - OfflineMatchConfig.MinBots);
            else
            {
                players.SetIndex(playerOrBotCount - RuleSet.MinPlayers);
                visibility.SetIndex(isPublic ? 1 : 0);
            }

            lives.SetIndex(rules.StartingLives - RuleSet.MinLives);
            turnTime.SetIndex(IndexOf(TurnTimes, rules.TurnTimeSeconds, 2));
            cardCap.SetIndex(IndexOf(CardCaps, rules.MaxCardsPerPlayer, 0));
            timeouts.SetIndex(rules.TimeoutsBeforeAutoPlay - 1);
        }

        /// <summary>Regras escolhidas + regras oficiais fixas.</summary>
        public RuleSet BuildRules()
        {
            return new RuleSet
            {
                StartingLives = RuleSet.MinLives + lives.Index,
                TurnTimeSeconds = TurnTimes[turnTime.Index],
                MaxCardsPerPlayer = CardCaps[cardCap.Index],
                TimeoutsBeforeAutoPlay = 1 + timeouts.Index
            }.WithOfficialRules();
        }

        /// <summary>Offline: número de bots. Sala: máximo de jogadores.</summary>
        public int PlayerOrBotCount => mode == Mode.Offline
            ? OfflineMatchConfig.MinBots + players.Index
            : RuleSet.MinPlayers + players.Index;

        public bool IsPublic => mode == Mode.Room && visibility.Index == 1;

        public OptionStepper PlayersStepper => players;

        // ------------------------------------------------------------------ utilitários

        private static string[] Range(int min, int max, string suffix)
        {
            var result = new string[max - min + 1];
            for (int i = 0; i < result.Length; i++) result[i] = (min + i) + suffix;
            return result;
        }

        private static string[] Map(int[] values, Func<int, string> f)
        {
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = f(values[i]);
            return result;
        }

        private static int IndexOf(int[] values, int value, int fallback)
        {
            int i = Array.IndexOf(values, value);
            return i >= 0 ? i : fallback;
        }
    }
}
