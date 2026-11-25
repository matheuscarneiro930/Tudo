using System;
using System.Collections.Generic;
using Fodinha.Core;
using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// A mão do jogador local, de 1 a 19 cartas, ordenada da mais fraca para a mais forte.
    /// Quando é a vez de jogar: 1º toque seleciona (a carta sobe), 2º toque joga.
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        private readonly List<CardView> pool = new List<CardView>();
        private readonly List<Card> shown = new List<Card>();
        private RectTransform rect;
        private MatchView lastView;
        private bool canPlay;
        private Card? selected;

        /// <summary>O jogador confirmou uma carta para jogar.</summary>
        public event Action<Card> PlayRequested;

        public static HandView Create(Transform parent)
        {
            var rt = UiKit.Rect("Mao", parent);
            var view = rt.gameObject.AddComponent<HandView>();
            view.rect = rt;
            return view;
        }

        public bool CanPlay => canPlay;
        public Card? Selected => selected;
        public int VisibleCardCount { get; private set; }

        public void Render(MatchView view)
        {
            lastView = view;
            shown.Clear();
            int unknown = 0;

            var me = view?.Me;
            if (me != null && !me.IsEliminated)
            {
                if (view.MyHand.Count > 0)
                {
                    shown.AddRange(view.MyHand);
                    if (view.Comparer != null)
                    {
                        var cmp = view.Comparer;
                        shown.Sort((a, b) =>
                        {
                            int c = cmp.Strength(a).CompareTo(cmp.Strength(b));
                            return c != 0 ? c : a.Suit.CompareTo(b.Suit);
                        });
                    }
                }
                else if (view.IsBlindRound && me.HandCount > 0)
                {
                    unknown = me.HandCount; // rodada cega: o jogador não vê a própria carta
                }
            }

            canPlay = view != null && view.Phase == MatchPhase.Playing && view.IsMyTurn && me != null && !me.IsAutoPlay;
            if (selected.HasValue && (!canPlay || !shown.Contains(selected.Value))) selected = null;

            int total = shown.Count + unknown;
            VisibleCardCount = total;
            EnsurePool(total);

            for (int i = 0; i < pool.Count; i++)
            {
                var card = pool[i];
                bool active = i < total;
                card.gameObject.SetActive(active);
                if (!active) continue;

                if (i < shown.Count)
                {
                    var c = shown[i];
                    card.ShowFace(c);
                    card.SetInteractable(canPlay);
                    card.SetHighlight(selected.HasValue && selected.Value == c);
                    card.SetDimmed(false);
                }
                else
                {
                    card.ShowUnknown();
                    card.SetInteractable(false);
                    card.SetHighlight(false);
                    card.SetDimmed(false);
                }
            }

            // Rodada nova com cartas na mão: anima a distribuição.
            int round = view != null ? view.RoundNumber : -1;
            dealing = total > 0 && round != dealtRound;
            if (total > 0) dealtRound = round;
            if (dealing) placed.Clear();
            Layout();
            dealing = false;
        }

        /// <summary>Verdadeiro durante a animação de distribuição (som/vibração podem acompanhar).</summary>
        public int DealtRound => dealtRound;

        private void EnsurePool(int count)
        {
            while (pool.Count < count)
            {
                var card = CardView.Create(rect, "Carta" + pool.Count);
                card.Clicked += OnCardClicked;
                pool.Add(card);
            }
        }

        private void Layout()
        {
            int n = VisibleCardCount;
            if (n == 0) return;

            float width = rect.rect.width > 1f ? rect.rect.width : 1000f;
            float height = rect.rect.height > 1f ? rect.rect.height : 400f;

            float cardH = Mathf.Min(height * 0.78f, 330f);
            float cardW = cardH * CardView.Aspect;
            float maxSpacing = cardW * 1.06f;
            float spacing = n > 1 ? Mathf.Min(maxSpacing, (width - cardW - 20f) / (n - 1)) : 0f;
            float total = cardW + spacing * (n - 1);
            float x0 = -total * 0.5f + cardW * 0.5f;
            float raise = cardH * 0.14f;

            for (int i = 0; i < n; i++)
            {
                var card = pool[i];
                bool isSelected = i < shown.Count && selected.HasValue && selected.Value == shown[i];
                float y = -height * 0.5f + cardH * 0.5f + height * 0.06f + (isSelected ? raise : 0f);
                var target = new Vector2(x0 + spacing * i, y);
                var size = new Vector2(cardW, cardH);
                Vector2 old = card.Rect.anchoredPosition;
                bool wasPlaced = placed.Contains(i);

                if (dealing)
                {
                    // Distribuição: as cartas chegam da mesa, uma a uma.
                    card.Rect.Place(new Vector2(0.5f, 0.5f), size, target);
                    Tweens.Move(card.Rect, target + new Vector2(0f, height * 1.4f), target, 0.35f, i * 0.06f,
                                new Vector3(0.45f, 0.45f, 1f), Vector3.one);
                }
                else if (wasPlaced && (old - target).sqrMagnitude > 1f)
                {
                    // Reajuste (carta jogada/selecionada): desliza até o novo lugar.
                    card.Rect.Place(new Vector2(0.5f, 0.5f), size, target);
                    Tweens.Move(card.Rect, old, target, 0.18f);
                }
                else if (!Tweens.IsAnimating(card.Rect))
                {
                    card.Rect.Place(new Vector2(0.5f, 0.5f), size, target);
                }
                card.Rect.SetSiblingIndex(i);
                placed.Add(i);
            }
            for (int i = n; i < pool.Count; i++) placed.Remove(i);
        }

        private readonly HashSet<int> placed = new HashSet<int>();
        private int dealtRound = -1;
        private bool dealing;

        private void OnRectTransformDimensionsChange()
        {
            if (rect != null && lastView != null) Layout();
        }

        private void OnCardClicked(CardView card)
        {
            if (!canPlay || !card.Card.HasValue) return;
            var c = card.Card.Value;
            if (selected.HasValue && selected.Value == c)
            {
                selected = null;
                PlayRequested?.Invoke(c);
                return;
            }
            selected = c;
            Audio.Sfx.Play(Audio.Sound.Card, 0.5f);
            Render(lastView);
        }

        /// <summary>Simula o toque numa carta (usado em testes).</summary>
        public void Tap(Card card)
        {
            for (int i = 0; i < shown.Count; i++)
                if (shown[i] == card) { OnCardClicked(pool[i]); return; }
        }
    }
}
