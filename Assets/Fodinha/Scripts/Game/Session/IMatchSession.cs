using System;
using Fodinha.Core;

namespace Fodinha.Game
{
    /// <summary>
    /// Pedidos que a interface pode fazer. A interface NUNCA altera o jogo diretamente:
    /// apenas pede, e quem roda o <see cref="MatchEngine"/> (local ou host da sala) valida.
    /// </summary>
    public interface IGameActions
    {
        void PlaceBet(int bet);
        void PlayCard(Card card);

        /// <summary>"Voltar a jogar": desliga o bot que estava jogando pelo jogador.</summary>
        void ResumeControl();
    }

    /// <summary>
    /// Uma partida do ponto de vista do jogador deste aparelho.
    /// Implementada pelo modo offline (<see cref="LocalMatchController"/>) e, depois, pela rede.
    /// A interface só lê <see cref="View"/> (já filtrada para este assento) e envia pedidos.
    /// </summary>
    public interface IMatchSession : IGameActions
    {
        /// <summary>Assento deste aparelho (-1 = espectador).</summary>
        int LocalSeat { get; }

        /// <summary>Última visão recebida (null antes da partida começar).</summary>
        MatchView View { get; }

        /// <summary>Assento com o relógio correndo (-1 = nenhum, ex.: bot pensando).</summary>
        int TimerSeat { get; }

        float TurnTimeRemaining { get; }
        float TurnTimeTotal { get; }

        /// <summary>Segundos até a partida seguir depois de uma vaza/rodada concluída.</summary>
        float PhaseWaitRemaining { get; }

        /// <summary>Avatar do assento (texto do AvatarSpec). Vazio = a tela sorteia um pelo nome.</summary>
        string AvatarOf(int seat);

        /// <summary>Nova visão disponível (estado mudou).</summary>
        event Action<MatchView> ViewChanged;

        /// <summary>Eventos públicos da partida, para animações e sons.</summary>
        event Action<MatchEvent> EventReceived;

        /// <summary>Um pedido deste jogador foi recusado (ex.: carta inválida, fora da vez).</summary>
        event Action<ActionResult> ActionRejected;
    }

    /// <summary>
    /// Partida com mensagens prontas (<see cref="Fodinha.Hosting.QuickChat"/>): sem texto livre, só o número.
    /// Offline os bots respondem; online o servidor repassa para a sala.
    /// </summary>
    public interface IQuickChatSession
    {
        /// <summary>Alguém da mesa mandou uma mensagem: (assento, número da mensagem).</summary>
        event Action<int, int> ChatReceived;

        /// <summary>Manda uma mensagem pronta (quem roda a partida confere o limite).</summary>
        void SendChat(int messageId);
    }
}
