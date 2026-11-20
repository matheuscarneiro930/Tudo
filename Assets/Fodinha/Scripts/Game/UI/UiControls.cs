using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinha.Game.UI
{
    /// <summary>Controles de formulário montados por código: seletor ‹ valor ›, campo de texto, lista com rolagem, diálogo.</summary>
    public static class UiControls
    {
        /// <summary>Campo de texto (legado do uGUI; abre o teclado do celular).</summary>
        public static InputField TextField(string name, Transform parent, string placeholder, int fontSize, int characterLimit)
        {
            var bg = UiKit.Image(name, parent, new Color(1f, 1f, 1f, 0.95f), SpriteFactory.RoundedRect, 1.5f);
            var field = bg.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;

            var text = UiKit.Label("Texto", bg.transform, "", fontSize, Palette.BlackSuit, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(24f, 6f);
            text.rectTransform.offsetMax = new Vector2(-24f, -6f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var hint = UiKit.Label("Dica", bg.transform, placeholder, fontSize, new Color(0f, 0f, 0f, 0.35f), TextAnchor.MiddleLeft, FontStyle.Italic);
            hint.rectTransform.Fill();
            hint.rectTransform.offsetMin = new Vector2(24f, 6f);
            hint.rectTransform.offsetMax = new Vector2(-24f, -6f);

            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        /// <summary>Área com rolagem vertical. Retorna o conteúdo, onde os itens são empilhados (cada item precisa de LayoutElement).</summary>
        public static RectTransform ScrollArea(string name, Transform parent, float spacing = 16f)
        {
            var viewport = UiKit.Rect(name, parent);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var content = UiKit.Rect("Conteudo", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(4, 4, 4, 24);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static void SetHeight(Component item, float height)
        {
            var le = item.GetComponent<LayoutElement>();
            if (le == null) le = item.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
        }

        /// <summary>Diálogo modal de confirmação, criado sob <paramref name="root"/> e destruído ao responder.</summary>
        public static GameObject Confirm(Transform root, string title, string message, string confirmLabel, Action onConfirm,
                                         string cancelLabel = "Cancelar", Action onCancel = null)
        {
            var overlay = UiKit.Image("Dialogo", root, new Color(0f, 0f, 0f, 0.7f));
            overlay.rectTransform.Fill();
            var box = UiKit.Image("Caixa", overlay.transform, Palette.PanelStrong, SpriteFactory.RoundedRect, 0.8f);
            box.rectTransform.Anchor(0.08f, 0.36f, 0.92f, 0.64f);
            var border = UiKit.Image("Borda", box.transform, Palette.Accent, SpriteFactory.RoundedOutline, 0.8f);
            border.rectTransform.Fill();
            border.raycastTarget = false;

            var t = UiKit.Label("Titulo", box.transform, title, 52, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            t.rectTransform.Anchor(0.05f, 0.74f, 0.95f, 0.95f);
            var m = UiKit.Label("Mensagem", box.transform, message, 38, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            m.rectTransform.Anchor(0.06f, 0.36f, 0.94f, 0.74f);

            var go = overlay.gameObject;
            var cancel = UiKit.Button("Cancelar", box.transform, cancelLabel, () =>
            {
                UiKit.SafeDestroy(go);
                onCancel?.Invoke();
            }, Palette.ButtonSecondary, Palette.Text, 40);
            cancel.GetComponent<RectTransform>().Anchor(0.05f, 0.07f, 0.48f, 0.3f);

            var ok = UiKit.Button("Confirmar", box.transform, confirmLabel, () =>
            {
                UiKit.SafeDestroy(go);
                onConfirm?.Invoke();
            }, Palette.Button, Palette.ButtonText, 40);
            ok.GetComponent<RectTransform>().Anchor(0.52f, 0.07f, 0.95f, 0.3f);
            return go;
        }
    }

    /// <summary>Seletor de opção para toque: rótulo à esquerda e "‹ valor ›" à direita.</summary>
    public sealed class OptionStepper : MonoBehaviour
    {
        private Text label;
        private Text valueText;
        private Button prev;
        private Button next;
        private string[] options = Array.Empty<string>();
        private int index;

        public event Action<int> Changed;

        public int Index => index;
        public int Count => options.Length;
        public string Value => options.Length > 0 ? options[index] : "";

        public static OptionStepper Create(Transform parent, string labelText, string[] options, int index)
        {
            var bg = UiKit.Image("Opcao_" + labelText, parent, Palette.Panel, SpriteFactory.RoundedRect, 1.5f);
            var stepper = bg.gameObject.AddComponent<OptionStepper>();
            stepper.Build(bg.transform, labelText);
            stepper.SetOptions(options, index);
            UiControls.SetHeight(bg, 104f);
            return stepper;
        }

        private void Build(Transform root, string labelText)
        {
            label = UiKit.Label("Rotulo", root, labelText, 36, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            label.rectTransform.Anchor(0.04f, 0.1f, 0.46f, 0.9f);

            prev = UiKit.Button("Anterior", root, "‹", () => Step(-1), Palette.ButtonSecondary, Palette.Text, 60);
            prev.GetComponent<RectTransform>().Anchor(0.47f, 0.12f, 0.58f, 0.88f);

            valueText = UiKit.Label("Valor", root, "", 36, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            valueText.rectTransform.Anchor(0.585f, 0.1f, 0.875f, 0.9f);

            next = UiKit.Button("Proximo", root, "›", () => Step(+1), Palette.ButtonSecondary, Palette.Text, 60);
            next.GetComponent<RectTransform>().Anchor(0.88f, 0.12f, 0.98f, 0.88f);
        }

        public void SetOptions(string[] newOptions, int newIndex)
        {
            options = newOptions ?? Array.Empty<string>();
            SetIndex(newIndex);
        }

        /// <summary>Muda o valor sem disparar <see cref="Changed"/>.</summary>
        public void SetIndex(int newIndex)
        {
            index = options.Length == 0 ? 0 : Mathf.Clamp(newIndex, 0, options.Length - 1);
            Refresh();
        }

        public void Step(int delta)
        {
            int old = index;
            SetIndex(index + delta);
            if (index != old) Changed?.Invoke(index);
        }

        private void Refresh()
        {
            valueText.text = Value;
            prev.interactable = index > 0;
            next.interactable = index < options.Length - 1;
        }
    }
}
