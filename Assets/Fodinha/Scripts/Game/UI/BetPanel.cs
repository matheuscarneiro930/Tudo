using System;
using System.Collections.Generic;
using Fodinha.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Painel de aposta: botões de 0 até o número de cartas da rodada.
    /// Os valores proibidos (regra do pé) aparecem desabilitados — quem decide é o motor (<see cref="MatchView.AllowedBets"/>).
    /// </summary>
    public sealed class BetPanel : MonoBehaviour
    {
        private const int MaxColumns = 10;

        private RectTransform rect;
        private Text prompt;
        private RectTransform grid;
        private GridLayoutGroup layout;
        private readonly List<Button> buttons = new List<Button>();
        private int shownCards = -1;

        public event Action<int> BetChosen;

        public static BetPanel Create(Transform parent)
        {
            var bg = UiKit.Image("PainelAposta", parent, Palette.PanelStrong, SpriteFactory.RoundedRect, 1f);
            var panel = bg.gameObject.AddComponent<BetPanel>();
            panel.Build((RectTransform)bg.transform);
            return panel;
        }

        private void Build(RectTransform rt)
        {
            rect = rt;
            prompt = UiKit.Label("Pergunta", rt, "", 38, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            prompt.rectTransform.Anchor(0.03f, 0.64f, 0.97f, 0.97f);

            grid = UiKit.Rect("Botoes", rt);
            grid.Anchor(0.03f, 0.06f, 0.97f, 0.64f);
            layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.spacing = new Vector2(12f, 12f);
        }

        public IReadOnlyList<Button> Buttons => buttons;

        public void Show(MatchView view)
        {
            int cards = view.CardsThisRound;
            int sum = 0, betsMade = 0;
            foreach (var p in view.Players)
            {
                if (p.IsEliminated || !p.HasBet) continue;
                sum += p.Bet;
                betsMade++;
            }

            bool isLast = betsMade == view.ActivePlayerCount - 1;
            int forbidden = cards - sum;
            string text = "Quantas vazas você vai fazer?  " + UiKit.Colored("Apostas na mesa: " + sum + " de " + cards, Palette.TextMuted);
            if (isLast && view.Rules != null && view.Rules.LastBidderRestriction && forbidden >= 0 && forbidden <= cards)
                text += "\n" + UiKit.Colored("Você é o pé: não pode apostar " + forbidden, Palette.Accent);
            prompt.text = text;

            EnsureButtons(cards + 1);
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                bool active = i <= cards;
                b.gameObject.SetActive(active);
                if (!active) continue;
                bool allowed = Contains(view.AllowedBets, i);
                b.interactable = allowed;
                ((Image)b.targetGraphic).color = allowed ? Palette.Button : Palette.Disabled;
            }

            if (shownCards != cards) LayoutButtons(cards + 1);
            shownCards = cards;
        }

        private void EnsureButtons(int count)
        {
            while (buttons.Count < count)
            {
                int value = buttons.Count;
                var b = UiKit.Button("Aposta" + value, grid, value.ToString(), () => BetChosen?.Invoke(value), Palette.Button, Palette.ButtonText, 52);
                buttons.Add(b);
            }
        }

        private void LayoutButtons(int count)
        {
            int columns = Mathf.Min(count, MaxColumns);
            int rows = Mathf.CeilToInt(count / (float)columns);
            float width = grid.rect.width > 1f ? grid.rect.width : 980f;
            float height = grid.rect.height > 1f ? grid.rect.height : 200f;
            float cellW = (width - layout.spacing.x * (columns - 1)) / columns;
            float cellH = (height - layout.spacing.y * (rows - 1)) / rows;
            cellW = Mathf.Min(cellW, 150f);
            cellH = Mathf.Min(cellH, 120f);
            layout.constraintCount = columns;
            layout.cellSize = new Vector2(cellW, cellH);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (rect != null && shownCards >= 0) LayoutButtons(shownCards + 1);
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }
    }
}
