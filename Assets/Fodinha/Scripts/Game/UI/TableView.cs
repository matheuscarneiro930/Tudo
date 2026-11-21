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
        private CardView viraCard;
        private Text viraLabel;
        private readonly List<SeatView> seats = new List<SeatView>();
        private readonly List<CardView> trickCards = new List<CardView>();
        private readonly Dictionary<int, Vector2> directionBySeat = new Dictionary<int, Vector2>();

        // Estado anterior, só para animar transições (puramente visual).
        private int shownTrickKey = -1;
        private readonly HashSet<int> shownTrickSeats = new HashSet<int>();
        private TrickResult shownResult;
        private int shownResultKey = -1;
        private Vector2 shownCardSize;
        private readonly List<CardView> flyingCards = new List<CardView>();

        public IReadOnlyList<SeatView> Seats => seats;

        /// <summary>Avatar de cada assento (dado pela sessão). Null = avatar sorteado pelo nome.</summary>
        public System.Func<int, string> AvatarProvider;

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
            centerInfo.gameObject.SetActive(false); // a informação da rodada fica no topo

            // Vira no meio da mesa, com a etiqueta "VIRA".
            viraCard = CardView.Create(rt, "Vira");
            viraCard.SetInteractable(false);
            viraLabel = UiKit.Label("Etiqueta", viraCard.Rect, "VIRA", 30, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            viraLabel.rectTransform.Anchor(-0.3f, -0.34f, 1.3f, -0.06f);
            viraCard.gameObject.SetActive(false);

            trickLayer = UiKit.Rect("Rodada", rt).Fill();
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
                sv.Rect.Place(TableLayout.ClampSeatAnchor(TableLayout.SeatAnchor(dir), SeatView.BaseSize * scale, seatsLayer.rect.size), SeatView.BaseSize);
                sv.Rect.localScale = new Vector3(scale, scale, 1f);
                var p = view.Players[seat];
                bool isTurn = view.CurrentSeat == seat && (view.Phase == MatchPhase.Betting || view.Phase == MatchPhase.Playing);
                sv.Render(p, view, isTurn);
                sv.SetAvatar(AvatarProvider?.Invoke(p.Seat), p.Name);
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

            int trickKey = view.RoundNumber * 1000 + view.TrickNumber;

            // Fim da vaza mostrada: as cartas voam para quem levou. (Compara por rodada/vaza: no online cada
            // atualização traz objetos novos.)
            int resultKey = result != null ? trickKey : -1;
            if (shownResult != null && resultKey != shownResultKey) FlyTrickToWinner(shownResult, shownCardSize);
            bool resultIsNew = result != null && resultKey != shownResultKey;
            shownResult = result;
            shownResultKey = resultKey;
            shownCardSize = cardSize;

            if (trickKey != shownTrickKey)
            {
                shownTrickKey = trickKey;
                shownTrickSeats.Clear();
            }
            var tableSize = rect.rect.size;
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
                if (Tweens.IsAnimating(cv.Rect))
                {
                    // Não interrompe a animação em andamento: só garante âncora e tamanho.
                    cv.Rect.anchorMin = cv.Rect.anchorMax = TableLayout.TrickAnchor(dir);
                    cv.Rect.sizeDelta = cardSize;
                }
                else
                {
                    cv.Rect.Place(TableLayout.TrickAnchor(dir), cardSize);
                    cv.Rect.localScale = Vector3.one;
                }
                cv.Rect.SetSiblingIndex(i);
                cv.ShowFace(play.Card, view.Comparer != null && view.Comparer.IsManilha(play.Card));

                // Carta nova na mesa: voa de quem jogou até o centro.
                if (shownTrickSeats.Add(play.Seat) && tableSize.x > 1f)
                {
                    var trickAnchor = TableLayout.TrickAnchor(dir);
                    var fromAnchor = play.Seat == local ? new Vector2(0.5f, -0.25f) : TableLayout.SeatAnchor(dir);
                    var from = Vector2.Scale(fromAnchor - trickAnchor, tableSize);
                    Tweens.Move(cv.Rect, from, Vector2.zero, 0.28f, 0f, new Vector3(0.55f, 0.55f, 1f), Vector3.one);
                }

                bool winner = result != null && result.WinnerSeat == play.Seat;
                bool canceled = result != null && Contains(result.CanceledSeats, play.Seat);
                cv.SetHighlight(winner, Palette.Accent);
                cv.SetDimmed(result != null && !winner);
                if (winner)
                {
                    var big = new Vector3(1.12f, 1.12f, 1f);
                    if (resultIsNew) Tweens.Scale(cv.Rect, new Vector3(1.35f, 1.35f, 1f), big, 0.35f);
                    else if (!Tweens.IsAnimating(cv.Rect)) cv.Rect.localScale = big;
                }
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
                    message = who + (result.WinnerSeat == local ? " levou a rodada!" : " levou a rodada");
                }
                else
                {
                    message = "Empate! Ninguém levou a rodada";
                }
            }
            bannerBg.gameObject.SetActive(message != null);
            if (message != null)
            {
                banner.text = message;
                bannerBg.rectTransform.anchoredPosition = new Vector2(0f, -cardH * 0.95f);
            }

            // Vira no meio da mesa (abaixo das cartas jogadas).
            bool showVira = view.Vira.HasValue && view.Phase != MatchPhase.MatchOver && view.Phase != MatchPhase.NotStarted;
            viraCard.gameObject.SetActive(showVira);
            if (showVira)
            {
                float viraH = cardH * 0.8f;
                viraCard.Rect.Place(new Vector2(0.5f, 0.5f), new Vector2(viraH * CardView.Aspect, viraH));
                viraCard.ShowFace(view.Vira.Value, false); // criada antes da camada das jogadas: fica por baixo delas
            }
        }

        public bool IsViraVisible => viraCard != null && viraCard.gameObject.activeSelf;

        /// <summary>Cópias das cartas da vaza encerrada voam até quem levou (ou somem no centro, se ninguém levou).</summary>
        private void FlyTrickToWinner(TrickResult trick, Vector2 cardSize)
        {
            if (!Tweens.Enabled) return;
            var tableSize = rect.rect.size;
            Vector2 target = trick.HasWinner && directionBySeat.TryGetValue(trick.WinnerSeat, out var wd)
                ? (wd == TableLayout.LocalDirection ? new Vector2(0.5f, -0.2f) : TableLayout.SeatAnchor(wd))
                : new Vector2(0.5f, 0.5f);

            for (int i = 0; i < trick.Plays.Count; i++)
            {
                var play = trick.Plays[i];
                var ghost = FlyingCard(i);
                var dir = directionBySeat.TryGetValue(play.Seat, out var d) ? d : Vector2.zero;
                var anchor = TableLayout.TrickAnchor(dir);
                ghost.Rect.Place(anchor, cardSize);
                ghost.ShowFace(play.Card, false);
                ghost.SetHighlight(false);
                ghost.SetDimmed(false);
                ghost.gameObject.SetActive(true);
                var go = ghost.gameObject;
                Tweens.Move(ghost.Rect, Vector2.zero, Vector2.Scale(target - anchor, tableSize), 0.4f, i * 0.02f,
                            Vector3.one, new Vector3(0.35f, 0.35f, 1f), () => go.SetActive(false));
            }
        }

        private CardView FlyingCard(int index)
        {
            while (flyingCards.Count <= index)
            {
                var c = CardView.Create(trickLayer, "Recolhendo" + flyingCards.Count);
                c.SetInteractable(false);
                c.gameObject.SetActive(false);
                flyingCards.Add(c);
            }
            flyingCards[index].Rect.SetAsLastSibling();
            return flyingCards[index];
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
