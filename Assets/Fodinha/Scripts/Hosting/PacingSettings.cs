using System;

namespace Fodinha.Hosting
{
    /// <summary>
    /// Ritmo da partida (pausas para animação e "tempo de pensar" dos bots), em segundos.
    /// Não é regra do jogo: só controla QUANDO o host chama o <see cref="Core.MatchEngine"/>.
    /// O tempo de jogada do jogador humano é regra e fica em <see cref="Core.RuleSet.TurnTimeSeconds"/>.
    /// </summary>
    [Serializable]
    public sealed class PacingSettings
    {
        /// <summary>Pausa depois de distribuir as cartas, antes da primeira aposta.</summary>
        public float DealSeconds = 1.2f;

        /// <summary>Tempo mínimo que um bot "pensa" antes de agir.</summary>
        public float BotMinSeconds = 0.7f;

        /// <summary>Tempo máximo que um bot "pensa" antes de agir.</summary>
        public float BotMaxSeconds = 1.5f;

        /// <summary>Tempo mostrando o resultado de cada vaza.</summary>
        public float TrickResultSeconds = 1.8f;

        /// <summary>Tempo extra na vaza da rodada cega (todas as cartas reveladas de uma vez).</summary>
        public float BlindRevealExtraSeconds = 1.5f;

        /// <summary>Tempo mostrando o resultado da rodada.</summary>
        public float RoundResultSeconds = 5f;

        /// <summary>Sem pausas: usado nos testes e em simulações.</summary>
        public static PacingSettings Instant => new PacingSettings
        {
            DealSeconds = 0f,
            BotMinSeconds = 0f,
            BotMaxSeconds = 0f,
            TrickResultSeconds = 0f,
            BlindRevealExtraSeconds = 0f,
            RoundResultSeconds = 0f
        };

        public PacingSettings Clone() => (PacingSettings)MemberwiseClone();
    }
}
