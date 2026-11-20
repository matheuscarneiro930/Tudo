using System.Collections.Generic;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Resultado da rodada: aposta, vazas feitas e vidas de cada jogador.</summary>
    public sealed class RoundResultPanel : MonoBehaviour
    {
        private Text title;
        private Text note;
        private Text countdown;
        private RectTransform list;
        private readonly List<Text[]> rows = new List<Text[]>();

        public static RoundResultPanel Create(Transform parent)
        {
            var overlay = UiKit.Image("ResultadoRodada", parent, new Color(0f, 0f, 0f, 0.55f));
            overlay.rectTransform.Fill();
            var panel = overlay.gameObject.AddComponent<RoundResultPanel>();
            panel.Build(overlay.rectTransform);
            return panel;
        }

        private void Build(RectTransform rt)
        {
            var box = UiKit.Image("Caixa", rt, Palette.PanelStrong, SpriteFactory.RoundedRect, 0.8f);
            box.rectTransform.Anchor(0.04f, 0.14f, 0.96f, 0.86f);
            var border = UiKit.Image("Borda", box.transform, new Color(1f, 0.78f, 0.24f, 0.6f), SpriteFactory.RoundedOutline, 0.8f);
            border.rectTransform.Fill();
            border.raycastTarget = false;

            title = UiKit.Label("Titulo", box.transform, "", 56, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            title.rectTransform.Anchor(0.04f, 0.9f, 0.96f, 0.98f);

            var header = UiKit.Rect("Cabecalho", box.transform).Anchor(0.04f, 0.84f, 0.96f, 0.9f);
            MakeRow(header, new[] { "Jogador", "Aposta", "Fez", "Vidas" }, 30, Palette.TextMuted);

            list = UiKit.Rect("Linhas", box.transform).Anchor(0.04f, 0.12f, 0.96f, 0.84f);

            note = UiKit.Label("Nota", box.transform, "", 30, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Italic, true);
            note.rectTransform.Anchor(0.04f, 0.065f, 0.96f, 0.12f);

            countdown = UiKit.Label("Contagem", box.transform, "", 34, Palette.TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            countdown.rectTransform.Anchor(0.04f, 0.01f, 0.96f, 0.065f);
        }

        private static readonly float[] Columns = { 0f, 0.46f, 0.62f, 0.76f, 1f };

        private static Text[] MakeRow(RectTransform row, string[] values, int size, Color color)
        {
            var texts = new Text[4];
            for (int c = 0; c < 4; c++)
            {
                var anchor = c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                var t = UiKit.Label("Col" + c, row, values[c], size, color, anchor, c == 0 ? FontStyle.Bold : FontStyle.Normal, true);
                t.rectTransform.Anchor(Columns[c], 0f, Columns[c + 1], 1f);
                texts[c] = t;
            }
            return texts;
        }

        public void Show(RoundSummary summary, MatchView view)
        {
            gameObject.SetActive(true);
            title.text = "Fim da rodada " + summary.RoundNumber + "  ·  " + summary.Cards + (summary.Cards == 1 ? " carta" : " cartas");
            note.text = summary.SimultaneousEliminationTiebreak ? "Todos zeraram! Quem tinha mais vidas volta com 1." : "";

            var results = summary.Results;
            int count = results.Count;
            while (rows.Count < count)
            {
                var row = UiKit.Rect("Linha" + rows.Count, list);
                rows.Add(MakeRow(row, new[] { "", "", "", "" }, 34, Palette.Text));
            }

            float rowHeight = Mathf.Min(1f / Mathf.Max(count, 1), 0.11f);
            int maxLives = view.Rules != null ? view.Rules.StartingLives : 3;
            for (int i = 0; i < rows.Count; i++)
            {
                var texts = rows[i];
                var rowRect = (RectTransform)texts[0].transform.parent;
                bool active = i < count;
                rowRect.gameObject.SetActive(active);
                if (!active) continue;

                rowRect.Anchor(0f, 1f - rowHeight * (i + 1), 1f, 1f - rowHeight * i);
                var r = results[i];
                var p = view.Players[r.Seat];
                bool isMe = r.Seat == view.ViewerSeat;
                texts[0].text = isMe ? UiKit.Colored(p.Name, Palette.Accent) : p.Name;
                texts[1].text = r.Bet.ToString();
                texts[2].text = UiKit.Colored(r.TricksWon.ToString(), r.Hit ? Palette.Good : Palette.Bad);
                if (r.Eliminated)
                    texts[3].text = UiKit.Colored("Eliminado", Palette.Bad);
                else
                    texts[3].text = UiKit.Hearts(r.LivesAfter, Mathf.Max(maxLives, r.LivesAfter)) +
                                    (r.LivesLost > 0 ? " " + UiKit.Colored("-" + r.LivesLost, Palette.Bad) : "");
            }
        }

        public void SetCountdown(float seconds)
        {
            countdown.text = seconds > 0.05f ? "Próxima rodada em " + Mathf.CeilToInt(seconds) + "s" : "";
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
