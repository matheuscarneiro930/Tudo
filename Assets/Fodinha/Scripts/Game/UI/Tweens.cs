using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Animações simples de interface (posição, escala e transparência) com desaceleração suave.
    /// Puramente visual: nunca muda o estado do jogo. Fora do Play Mode (testes, capturas) aplica o final na hora.
    /// </summary>
    public sealed class Tweens : MonoBehaviour
    {
        private sealed class Tween
        {
            public RectTransform Target;
            public CanvasGroup Group;
            public Vector2 FromPos, ToPos;
            public Vector3 FromScale, ToScale;
            public float FromAlpha, ToAlpha;
            public bool AnimatePos, AnimateScale, AnimateAlpha;
            public float Start, Duration;
            public Action Done;
        }

        private static Tweens instance;
        private readonly List<Tween> active = new List<Tween>();

        /// <summary>Velocidade das animações (1 = normal). 0 desliga (aplica o final na hora).</summary>
        public static float Speed = 1f;

        public static bool Enabled => Application.isPlaying && Speed > 0f;

        private static Tweens Runner
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Tweens") { hideFlags = HideFlags.HideInHierarchy };
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<Tweens>();
                }
                return instance;
            }
        }

        /// <summary>Move de <paramref name="from"/> para <paramref name="to"/> (anchoredPosition), opcionalmente escalando.</summary>
        public static void Move(RectTransform rt, Vector2 from, Vector2 to, float duration, float delay = 0f,
                                Vector3? fromScale = null, Vector3? toScale = null, Action done = null)
        {
            if (rt == null) return;
            var t = new Tween
            {
                Target = rt,
                FromPos = from, ToPos = to, AnimatePos = true,
                FromScale = fromScale ?? rt.localScale, ToScale = toScale ?? rt.localScale,
                AnimateScale = fromScale.HasValue || toScale.HasValue,
                Duration = duration, Done = done
            };
            Run(t, delay);
        }

        /// <summary>Escala de <paramref name="from"/> para <paramref name="to"/> (ex.: "pulo" de destaque).</summary>
        public static void Scale(RectTransform rt, Vector3 from, Vector3 to, float duration, float delay = 0f)
        {
            if (rt == null) return;
            Run(new Tween { Target = rt, FromScale = from, ToScale = to, AnimateScale = true, Duration = duration }, delay);
        }

        /// <summary>Muda a transparência de um CanvasGroup.</summary>
        public static void Fade(CanvasGroup group, float from, float to, float duration, float delay = 0f, Action done = null)
        {
            if (group == null) return;
            Run(new Tween
            {
                Target = (RectTransform)group.transform, Group = group, FromAlpha = from, ToAlpha = to, AnimateAlpha = true,
                Duration = duration, Done = done
            }, delay);
        }

        /// <summary>Cancela as animações de um objeto (sem aplicar o final).</summary>
        public static void Cancel(RectTransform rt)
        {
            if (instance == null || rt == null) return;
            instance.active.RemoveAll(t => t.Target == rt);
        }

        public static bool IsAnimating(RectTransform rt) => instance != null && instance.active.Exists(t => t.Target == rt);

        private static void Run(Tween t, float delay)
        {
            if (!Enabled || t.Duration <= 0f)
            {
                Apply(t, 1f);
                t.Done?.Invoke();
                return;
            }
            var runner = Runner;
            // Uma animação nova do mesmo tipo substitui a anterior no mesmo objeto.
            runner.active.RemoveAll(o => o.Target == t.Target && o.AnimatePos == t.AnimatePos && o.AnimateScale == t.AnimateScale
                                         && o.AnimateAlpha == t.AnimateAlpha);
            t.Duration /= Speed;
            t.Start = Time.unscaledTime + delay / Speed;
            Apply(t, 0f);
            runner.active.Add(t);
        }

        private static void Apply(Tween t, float progress)
        {
            if (t.Target == null) return;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(progress), 3f); // desacelera no fim
            if (t.AnimatePos) t.Target.anchoredPosition = Vector2.LerpUnclamped(t.FromPos, t.ToPos, e);
            if (t.AnimateScale) t.Target.localScale = Vector3.LerpUnclamped(t.FromScale, t.ToScale, e);
            if (t.AnimateAlpha && t.Group != null) t.Group.alpha = Mathf.Lerp(t.FromAlpha, t.ToAlpha, e);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var t = active[i];
                if (t.Target == null) { active.RemoveAt(i); continue; }
                float p = (now - t.Start) / t.Duration;
                if (p < 0f) { Apply(t, 0f); continue; }
                Apply(t, p);
                if (p >= 1f)
                {
                    active.RemoveAt(i);
                    t.Done?.Invoke();
                }
            }
        }
    }

    /// <summary>Faz um elemento "pulsar" (transparência) enquanto estiver ativo — usado no destaque de vez.</summary>
    public sealed class Pulse : MonoBehaviour
    {
        public float MinAlpha = 0.35f;
        public float MaxAlpha = 1f;
        public float Period = 1.1f;
        private UnityEngine.UI.Graphic graphic;
        private float baseAlpha = -1f;

        private void Update()
        {
            if (graphic == null) graphic = GetComponent<UnityEngine.UI.Graphic>();
            if (graphic == null) return;
            if (baseAlpha < 0f) baseAlpha = graphic.color.a;
            float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Period);
            var c = graphic.color;
            c.a = baseAlpha * Mathf.Lerp(MinAlpha, MaxAlpha, s);
            graphic.color = c;
        }
    }
}
