using System.Collections.Generic;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// A mesa oval com os adversários (até 14) em volta e as cartas da vaza no centro,
    /// cada carta deslocada na direção de quem a jogou.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        private RectTransform rect;
        private RectTransform seatsLayer;
        private RectTransform trickLayer;
        private Image bannerBg;
        private Text banner;
        private Text centerInfo;
        private readonly List<SeatView> seats = new List<SeatView>();
        private readonly List<CardView> trickCards = new List<CardView>();
        private readonly Dictionary<int, Vector2> directionBySeat = new Dictionary<int, Vector2>();

        public IReadOnlyList<SeatView> Seats => seats;

        public static TableView Create(Transform parent)
        {
            var rt = UiKit.Rect("Mesa", parent);
            var view = rt.gameObject.AddComponent<TableView>();
            view.Build(rt);
            return view;
        }

        private void Build(RectTransform rt)
        {
            rect = rt;
            var felt = UiKit.Image("Feltro", rt, Color.white, SpriteFactory.Table);
            felt.rectTransform.Anchor(0.08f, 0.1f, 0.92f, 0.9f);
            felt.raycastTarget = false;

            centerInfo = UiKit.Label("InfoCentro", rt, "", 30, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold, true);
            centerInfo.rectTransform.Anchor(0.3f, 0.44f, 0.7f, 0.56f);

            trickLayer = UiKit.Rect("Vaza", rt).Fill();
            seatsLayer = UiKit.Rect("Adversarios", rt).Fill();

            bannerBg = UiKit.Image("Aviso", rt, new Color(0f, 0f, 0f, 0.72f), SpriteFactory.RoundedRect, 1.2f);
            bannerBg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(560f, 92f));
            bannerBg.raycastTarget = false;
            banner = UiKit.Label("Texto", bannerBg.transform, "", 40, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            banner.rectTransform.Fill();
            banner.rectTransform.offsetMin = new Vector2(16f, 6f);
            banner.rectTransform.offsetMax = new Vector2(-16f, -6f);
            bannerBg.gameObject.SetActive(false);
        }

        public void Render(MatchView view, int timerSeat)
        {
            int n = view.Players.Count;
            int local = view.ViewerSeat;
            int opponents = local >= 0 ? n - 1 : n;

            // ---- adversários
            directionBySeat.Clear();
            float scale = TableLayout.SeatScale(opponents);
            int index = 0;
            for (int seat = 0; seat < n; seat++)
            {
                if (seat == local)
                {
                    directionBySeat[seat] = TableLayout.LocalDirection;
                    continue;
                }

                int relative = TableLayout.Relative(seat, local, n);
                var dir = TableLayout.OpponentDirection(relative, opponents);
                directionBySeat[seat] = dir;

                if (seats.Count <= index) seats.Add(SeatView.Create(seatsLayer));
                var sv = seats[index++];
                sv.gameObject.SetActive(true);
                sv.Rect.Place(TableLayout.SeatAnchor(dir), SeatView.BaseSize);
                sv.Rect.localScale = new Vector3(scale, scale, 1f);
                var p = view.Players[seat];
                bool isTurn = view.CurrentSeat == seat && (view.Phase == MatchPhase.Betting || view.Phase == MatchPhase.Playing);
                sv.Render(p, view, isTurn);
                sv.SetTimer(false, 0f);
            }
            for (int i = index; i < seats.Count; i++) seats[i].gameObject.SetActive(false);

            // ---- cartas da vaza
            IReadOnlyList<PlayedCard> plays = view.CurrentTrick;
            TrickResult result = null;
            if (view.Phase == MatchPhase.TrickComplete && view.LastTrick != null)
            {
                result = view.LastTrick;
                plays = result.Plays;
            }
            else if (view.Phase == MatchPhase.RoundComplete || view.Phase == MatchPhase.MatchOver)
            {
                plays = System.Array.Empty<PlayedCard>();
            }

            float cardH = TableLayout.TrickCardHeight(n);
            var cardSize = new Vector2(cardH * CardView.Aspect, cardH);
            while (trickCards.Count < plays.Count)
            {
                var c = CardView.Create(trickLayer, "Jogada" + trickCards.Count);
                c.SetInteractable(false);
                trickCards.Add(c);
            }

            for (int i = 0; i < trickCards.Count; i++)
            {
                var cv = trickCards[i];
                if (i >= plays.Count)
                {
                    cv.gameObject.SetActive(false);
                    continue;
                }

                var play = plays[i];
                cv.gameObject.SetActive(true);
                var dir = directionBySeat.TryGetValue(play.Seat, out var d) ? d : Vector2.zero;
                cv.Rect.Place(TableLayout.TrickAnchor(dir), cardSize);
                cv.Rect.localScale = Vector3.one;
                cv.Rect.SetSiblingIndex(i);
                cv.ShowFace(play.Card, view.Comparer != null && view.Comparer.IsManilha(play.Card));

                bool winner = result != null && result.WinnerSeat == play.Seat;
                bool canceled = result != null && Contains(result.CanceledSeats, play.Seat);
                cv.SetHighlight(winner, Palette.Accent);
                cv.SetDimmed(result != null && !winner);
                if (winner) cv.Rect.localScale = new Vector3(1.12f, 1.12f, 1f);
                if (canceled) cv.SetDimmed(true);
            }
            if (result != null)
            {
                for (int i = 0; i < plays.Count; i++)
                    if (plays[i].Seat == result.WinnerSeat) trickCards[i].Rect.SetAsLastSibling();
            }

            // ---- aviso central
            string message = null;
            if (result != null)
            {
                if (result.HasWinner)
                {
                    string who = result.WinnerSeat == local ? "Você" : view.Players[result.WinnerSeat].Name;
                    message = who + (result.WinnerSeat == local ? " levou a vaza!" : " levou a vaza");
                }
                else
                {
                    message = "Empate! Ninguém levou";
                }
            }
            bannerBg.gameObject.SetActive(message != null);
            if (message != null)
            {
                banner.text = message;
                bannerBg.rectTransform.anchoredPosition = new Vector2(0f, -cardH * 0.95f);
            }

            bool showInfo = plays.Count == 0 && (view.Phase == MatchPhase.Betting || view.Phase == MatchPhase.Playing);
            centerInfo.gameObject.SetActive(showInfo);
            if (showInfo)
                centerInfo.text = view.Phase == MatchPhase.Betting ? "APOSTAS" : "VAZA " + view.TrickNumber + " DE " + view.CardsThisRound;
        }

        /// <summary>Atualiza o relógio do adversário da vez (chamado a cada quadro).</summary>
        public void UpdateTimer(int timerSeat, float fraction)
        {
            foreach (var sv in seats)
            {
                if (!sv.gameObject.activeSelf) continue;
                sv.SetTimer(sv.Seat == timerSeat, fraction);
            }
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }
    }
}
