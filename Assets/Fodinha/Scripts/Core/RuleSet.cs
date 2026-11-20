using System;
using System.Collections.Generic;

namespace Fodinha.Core
{
    /// <summary>Como a manilha é definida.</summary>
    public enum ManilhaMode
    {
        /// <summary>Uma carta é virada; o próximo valor vira manilha (estilo Truco Paulista).</summary>
        Vira = 0,

        /// <summary>Manilhas fixas: 4♣ (zap) &gt; 7♥ (copas) &gt; A♠ (espadilha) &gt; 7♦ (pica-fumo). Sem vira.</summary>
        Fixed = 1
    }

    /// <summary>O que acontece quando cartas comuns de mesmo valor empatam numa vaza.</summary>
    public enum TieRule
    {
        /// <summary>Cartas empatadas se anulam; vence a maior das restantes. Se todas se anularem, ninguém leva.</summary>
        CancelOut = 0,

        /// <summary>Se as maiores cartas empatarem, ninguém leva a vaza.</summary>
        NoWinner = 1,

        /// <summary>Entre as maiores empatadas, vence quem jogou primeiro.</summary>
        FirstPlayedWins = 2,

        /// <summary>O naipe desempata também as cartas comuns (Paus &gt; Copas &gt; Espadas &gt; Ouros). Nunca há empate.</summary>
        SuitBreaksTie = 3
    }

    /// <summary>Quantas vidas o jogador perde ao errar a aposta.</summary>
    public enum LifeLossMode
    {
        /// <summary>Errou: perde 1 vida, não importa por quanto errou.</summary>
        OnePerMiss = 0,

        /// <summary>Perde a diferença entre a aposta e as vazas feitas.</summary>
        Difference = 1
    }

    /// <summary>Como a quantidade de cartas evolui entre as rodadas.</summary>
    public enum CardProgression
    {
        /// <summary>1, 2, 3... até o máximo possível; depois repete o máximo (sobe de novo quando alguém é eliminado).</summary>
        UpThenHold = 0,

        /// <summary>1, 2, 3... até o máximo; depois desce até 1 e volta a subir.</summary>
        UpAndDown = 1
    }

    /// <summary>
    /// Todas as regras configuráveis de uma partida. Nenhuma regra do jogo fica "escondida" no código:
    /// se for configurável, está aqui. Os campos são públicos para a Unity conseguir serializar no Inspector.
    /// </summary>
    [Serializable]
    public sealed class RuleSet
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 15;
        public const int MinLives = 2;
        public const int MaxLives = 5;
        public const int MinTurnSeconds = 20;
        public const int MaxTurnSeconds = 60;

        /// <summary>Vidas iniciais de cada jogador (2 a 5).</summary>
        public int StartingLives = 3;

        /// <summary>Tempo de cada jogada/aposta em segundos (20 a 60). Controlado pela camada de rede.</summary>
        public int TurnTimeSeconds = 30;

        public ManilhaMode ManilhaMode = ManilhaMode.Vira;

        public TieRule TieRule = TieRule.CancelOut;

        /// <summary>
        /// Regra do "pé": quem aposta por último não pode fazer a soma das apostas
        /// ficar igual ao número de cartas da rodada (garante que alguém erre).
        /// </summary>
        public bool LastBidderRestriction = true;

        public LifeLossMode LifeLossMode = LifeLossMode.OnePerMiss;

        public CardProgression Progression = CardProgression.UpThenHold;

        /// <summary>Teto de cartas por jogador. 0 = limitado apenas pelo baralho.</summary>
        public int MaxCardsPerPlayer = 0;

        /// <summary>
        /// Rodadas de 1 carta são "cegas": cada jogador vê as cartas dos outros, mas não a própria.
        /// Depois das apostas, as cartas são reveladas todas juntas.
        /// </summary>
        public bool BlindOneCardRounds = true;

        /// <summary>Quantos tempos esgotados seguidos até o bot assumir o jogador.</summary>
        public int TimeoutsBeforeAutoPlay = 2;

        public static RuleSet CreateDefault() => new RuleSet();

        public RuleSet Clone() => (RuleSet)MemberwiseClone();

        /// <summary>Lista de problemas da configuração. Vazia = configuração válida.</summary>
        public List<string> GetValidationErrors()
        {
            var errors = new List<string>();
            if (StartingLives < MinLives || StartingLives > MaxLives)
                errors.Add("Vidas iniciais devem estar entre " + MinLives + " e " + MaxLives + ".");
            if (TurnTimeSeconds < MinTurnSeconds || TurnTimeSeconds > MaxTurnSeconds)
                errors.Add("Tempo de jogada deve estar entre " + MinTurnSeconds + " e " + MaxTurnSeconds + " segundos.");
            if (!Enum.IsDefined(typeof(ManilhaMode), ManilhaMode)) errors.Add("Modo de manilha inválido.");
            if (!Enum.IsDefined(typeof(TieRule), TieRule)) errors.Add("Regra de empate inválida.");
            if (!Enum.IsDefined(typeof(LifeLossMode), LifeLossMode)) errors.Add("Modo de perda de vida inválido.");
            if (!Enum.IsDefined(typeof(CardProgression), Progression)) errors.Add("Progressão de cartas inválida.");
            if (MaxCardsPerPlayer < 0) errors.Add("Teto de cartas não pode ser negativo.");
            if (TimeoutsBeforeAutoPlay < 1) errors.Add("Tempos esgotados antes do bot deve ser pelo menos 1.");
            return errors;
        }

        public bool IsValid => GetValidationErrors().Count == 0;
    }
}
