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
                    bool manilha = view.Comparer != null && view.Comparer.IsManilha(c);
                    card.ShowFace(c, manilha);
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

            Layout();
        }

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
                card.Rect.Place(new Vector2(0.5f, 0.5f), new Vector2(cardW, cardH), new Vector2(x0 + spacing * i, y));
                card.Rect.SetSiblingIndex(i);
            }
        }

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
