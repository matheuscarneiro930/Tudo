using System;
using System.Collections.Generic;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Classificação final calculada só com informação pública (quem venceu e quando cada um caiu).</summary>
    public static class MatchRanking
    {
        public struct Entry
        {
            public int Place;
            public PlayerView Player;
        }

        public static List<Entry> Build(MatchView view)
        {
            var players = new List<PlayerView>(view.Players);
            players.Sort((a, b) =>
            {
                bool aw = a.Seat == view.WinnerSeat, bw = b.Seat == view.WinnerSeat;
                if (aw != bw) return aw ? -1 : 1;
                int ra = a.IsEliminated ? a.EliminatedAtRound : int.MaxValue;
                int rb = b.IsEliminated ? b.EliminatedAtRound : int.MaxValue;
                int c = rb.CompareTo(ra); // quem caiu mais tarde fica na frente
                return c != 0 ? c : a.Seat.CompareTo(b.Seat);
            });

            var result = new List<Entry>(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                int place = i + 1;
                if (i > 0)
                {
                    var prev = players[i - 1];
                    var cur = players[i];
                    bool tie = prev.Seat != view.WinnerSeat && prev.IsEliminated && cur.IsEliminated &&
                               prev.EliminatedAtRound == cur.EliminatedAtRound;
                    if (tie) place = result[i - 1].Place;
                }
                result.Add(new Entry { Place = place, Player = players[i] });
            }
            return result;
        }
    }

    /// <summary>Tela de fim de partida: vencedor, classificação e botões.</summary>
    public sealed class MatchOverPanel : MonoBehaviour
    {
        private Text title;
        private Text subtitle;
        private RectTransform list;
        private Button menuButton;
        private Button againButton;
        private readonly List<Text> rows = new List<Text>();

        public void SetPlayAgainLabel(string label) => UiKit.ButtonLabel(againButton).text = label;

        public event Action PlayAgainClicked;
        public event Action MenuClicked;

        public static MatchOverPanel Create(Transform parent)
        {
            var overlay = UiKit.Image("FimDePartida", parent, new Color(0f, 0f, 0f, 0.7f));
            overlay.rectTransform.Fill();
            var panel = overlay.gameObject.AddComponent<MatchOverPanel>();
            panel.Build(overlay.rectTransform);
            return panel;
        }

        private void Build(RectTransform rt)
        {
            var glow = UiKit.Image("Brilho", rt, new Color(1f, 0.78f, 0.24f, 0.35f), SpriteFactory.SoftGlow);
            glow.rectTransform.Anchor(-0.2f, 0.6f, 1.2f, 1.05f);
            glow.raycastTarget = false;

            var box = UiKit.Image("Caixa", rt, Palette.PanelStrong, SpriteFactory.RoundedRect, 0.8f);
            box.rectTransform.Anchor(0.05f, 0.1f, 0.95f, 0.9f);
            var border = UiKit.Image("Borda", box.transform, Palette.Accent, SpriteFactory.RoundedOutline, 0.8f);
            border.rectTransform.Fill();
            border.raycastTarget = false;

            title = UiKit.Label("Titulo", box.transform, "", 80, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            title.rectTransform.Anchor(0.04f, 0.86f, 0.96f, 0.97f);
            subtitle = UiKit.Label("Subtitulo", box.transform, "", 36, Palette.TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            subtitle.rectTransform.Anchor(0.04f, 0.8f, 0.96f, 0.86f);

            list = UiKit.Rect("Classificacao", box.transform).Anchor(0.06f, 0.2f, 0.94f, 0.79f);

            againButton = UiKit.Button("JogarDeNovo", box.transform, "Jogar de novo", () => PlayAgainClicked?.Invoke(), Palette.Button, Palette.ButtonText, 46);
            againButton.GetComponent<RectTransform>().Anchor(0.06f, 0.04f, 0.94f, 0.12f);

            menuButton = UiKit.Button("Menu", box.transform, "Menu principal", () => MenuClicked?.Invoke(), Palette.ButtonSecondary, Palette.Text, 40);
            menuButton.GetComponent<RectTransform>().Anchor(0.06f, 0.13f, 0.94f, 0.19f);
        }

        public void Show(MatchView view, bool showMenuButton)
        {
            gameObject.SetActive(true);
            menuButton.gameObject.SetActive(showMenuButton);

            bool iWon = view.WinnerSeat >= 0 && view.WinnerSeat == view.ViewerSeat;
            string winnerName = view.WinnerSeat >= 0 ? view.Players[view.WinnerSeat].Name : "?";
            title.text = iWon ? "Você venceu!" : winnerName + " venceu!";
            subtitle.text = "Partida de " + view.Players.Count + " jogadores";

            var ranking = MatchRanking.Build(view);
            while (rows.Count < ranking.Count)
            {
                var t = UiKit.Label("Linha" + rows.Count, list, "", 40, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Normal, true);
                rows.Add(t);
            }

            float h = Mathf.Min(1f / Mathf.Max(1, ranking.Count), 0.1f);
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i];
                bool active = i < ranking.Count;
                t.gameObject.SetActive(active);
                if (!active) continue;
                t.rectTransform.Anchor(0f, 1f - h * (i + 1), 1f, 1f - h * i);
                var e = ranking[i];
                bool me = e.Player.Seat == view.ViewerSeat;
                string name = me ? UiKit.Colored(e.Player.Name, Palette.Accent) : e.Player.Name;
                string detail = e.Player.Seat == view.WinnerSeat
                    ? UiKit.Colored("  vencedor", Palette.Good)
                    : e.Player.IsEliminated ? UiKit.Colored("  eliminado", Palette.TextMuted) : "";
                t.text = "<b>" + e.Place + "º</b>  " + name + detail;
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
