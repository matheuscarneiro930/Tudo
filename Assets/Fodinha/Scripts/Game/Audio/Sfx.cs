using System.Collections.Generic;
using UnityEngine;

namespace Fodinha.Game.Audio
{
    /// <summary>Efeitos sonoros do jogo.</summary>
    public enum Sound
    {
        Click,
        Card,
        CardOnTable,
        YourTurn,
        TrickWon,
        LifeLost,
        Victory,
        Defeat,
        Error,
        Chat
    }

    /// <summary>
    /// Toca os efeitos sonoros. Os sons são SINTETIZADOS por código na primeira vez (sem arquivos de áudio).
    /// Respeita Configurações → Sons; a vibração respeita Configurações → Vibração.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        private const int Rate = 44100;
        private static Sfx instance;
        private readonly Dictionary<Sound, AudioClip> clips = new Dictionary<Sound, AudioClip>();
        private AudioSource source;
        private float lastCardTime;

        /// <summary>Quantos sons foram pedidos (para testes).</summary>
        public static int PlayedCount { get; private set; }

        public static Sound? LastPlayed { get; private set; }

        public static void Play(Sound sound, float volume = 1f)
        {
            PlayedCount++;
            LastPlayed = sound;
            if (!Application.isPlaying || !GamePrefs.SoundOn) return;
            var s = Instance;
            // Evita "metralhadora" quando várias cartas chegam no mesmo instante.
            if ((sound == Sound.Card || sound == Sound.CardOnTable) && Time.unscaledTime - s.lastCardTime < 0.03f) return;
            if (sound == Sound.Card || sound == Sound.CardOnTable) s.lastCardTime = Time.unscaledTime;
            s.source.PlayOneShot(s.Clip(sound), volume);
        }

        /// <summary>Vibração curta (só em celular, se ativada).</summary>
        public static void Vibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (Application.isPlaying && GamePrefs.VibrationOn) Handheld.Vibrate();
#endif
        }

        private static Sfx Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Sons");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<Sfx>();
                    instance.source = go.AddComponent<AudioSource>();
                    instance.source.playOnAwake = false;
                }
                return instance;
            }
        }

        private AudioClip Clip(Sound sound)
        {
            if (!clips.TryGetValue(sound, out var clip))
            {
                clip = Synth(sound);
                clips[sound] = clip;
            }
            return clip;
        }

        // ------------------------------------------------------------------ síntese

        /// <summary>Gera as amostras de um som (público para testes).</summary>
        public static float[] Samples(Sound sound)
        {
            switch (sound)
            {
                case Sound.Click: return Tone(1800f, 0.025f, 0.35f, 400f);
                case Sound.Card: return Noise(0.07f, 0.5f, 60f, 0.35f, 1);
                case Sound.CardOnTable: return Mix(Noise(0.09f, 0.55f, 45f, 0.6f, 2), Tone(140f, 0.09f, 0.5f, 40f));
                case Sound.YourTurn: return Concat(Tone(880f, 0.12f, 0.35f, 18f), Tone(1318.5f, 0.22f, 0.35f, 12f));
                case Sound.TrickWon: return Concat(Tone(660f, 0.09f, 0.35f, 20f), Tone(990f, 0.18f, 0.35f, 14f));
                case Sound.LifeLost: return Sweep(420f, 220f, 0.45f, 0.4f);
                case Sound.Victory:
                    return Concat(Tone(523.25f, 0.12f, 0.33f, 14f), Tone(659.25f, 0.12f, 0.33f, 14f),
                                  Tone(783.99f, 0.12f, 0.33f, 14f), Tone(1046.5f, 0.45f, 0.35f, 5f));
                case Sound.Defeat:
                    return Concat(Tone(392f, 0.18f, 0.3f, 10f), Tone(349.23f, 0.18f, 0.3f, 10f), Tone(261.63f, 0.5f, 0.3f, 5f));
                case Sound.Error: return Square(180f, 0.14f, 0.18f);
                case Sound.Chat: return Concat(Tone(1046.5f, 0.05f, 0.22f, 30f), Tone(1568f, 0.09f, 0.2f, 24f)); // "plim" do balão
                default: return new float[1];
            }
        }

        private static AudioClip Synth(Sound sound)
        {
            var data = Samples(sound);
            var clip = AudioClip.Create(sound.ToString(), data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Seno com ataque curto e decaimento exponencial.</summary>
        private static float[] Tone(float freq, float seconds, float volume, float decay)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * decay);
                // fundamental + um pouco do 2º harmônico deixa o som menos "apito"
                s[i] = volume * env * (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.8f + Mathf.Sin(4f * Mathf.PI * freq * t) * 0.2f);
            }
            return s;
        }

        /// <summary>Ruído filtrado (som de carta deslizando/batendo).</summary>
        private static float[] Noise(float seconds, float volume, float decay, float smoothing, int seed)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var s = new float[n];
            var rng = new System.Random(seed);
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                y = Mathf.Lerp(x, y, smoothing); // passa-baixas simples
                s[i] = volume * y * Mathf.Min(1f, t * 800f) * Mathf.Exp(-t * decay);
            }
            return s;
        }

        private static float[] Sweep(float from, float to, float seconds, float volume)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(from, to, t / seconds);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Min(1f, t * 200f) * (1f - t / seconds);
                s[i] = volume * env * Mathf.Sin(phase);
            }
            return s;
        }

        private static float[] Square(float freq, float seconds, float volume)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 300f) * (1f - t / seconds);
                s[i] = volume * env * (Mathf.Sin(2f * Mathf.PI * freq * t) >= 0f ? 1f : -1f);
            }
            return s;
        }

        private static float[] Mix(float[] a, float[] b)
        {
            var s = new float[Mathf.Max(a.Length, b.Length)];
            for (int i = 0; i < s.Length; i++)
                s[i] = Mathf.Clamp((i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f), -1f, 1f);
            return s;
        }

        private static float[] Concat(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var s = new float[n];
            int o = 0;
            foreach (var p in parts)
            {
                System.Array.Copy(p, 0, s, o, p.Length);
                o += p.Length;
            }
            return s;
        }
    }
}
