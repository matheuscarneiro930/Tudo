using System;
using Fodinha.Core;
using Fodinha.Game;
using Fodinha.Net;
using UnityEngine;

namespace Fodinha.Online
{
    /// <summary>
    /// Partida online do ponto de vista deste aparelho. Recebe do servidor SÓ a visão deste jogador
    /// e envia pedidos; quem valida e roda as regras é o servidor.
    /// </summary>
    public sealed class NetworkMatchSession : IMatchSession, IQuickChatSession
    {
        private readonly IOnlineSender sender;
        private readonly Func<float> clock;
        private MatchUpdateDto last;
        private float receivedAt;

        public event Action<MatchView> ViewChanged;
        public event Action<MatchEvent> EventReceived;
        public event Action<ActionResult> ActionRejected;
        public event Action<int, int> ChatReceived;

        public NetworkMatchSession(IOnlineSender sender, Func<float> clock = null)
        {
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
            this.clock = clock ?? (() => Time.unscaledTime);
        }

        public MatchView View { get; private set; }
        public int LocalSeat => View != null ? View.ViewerSeat : -1;

        public int TimerSeat => last != null ? last.timerSeat : -1;
        public float TurnTimeTotal => last != null && last.turnTotal > 0f ? last.turnTotal : 1f;
        public float TurnTimeRemaining => last != null ? Mathf.Max(0f, last.turnRemaining - Elapsed) : 0f;
        public float PhaseWaitRemaining => last != null ? Mathf.Max(0f, last.phaseWait - Elapsed) : 0f;

        private float Elapsed => clock() - receivedAt;

        /// <summary>Avatares enviados pelo servidor (um por assento).</summary>
        public string AvatarOf(int seat) =>
            last?.avatars != null && seat >= 0 && seat < last.avatars.Count ? last.avatars[seat] : "";

        /// <summary>Aplica uma atualização recebida do servidor.</summary>
        public void Apply(MatchUpdateDto update)
        {
            if (update?.view == null) return;
            last = update;
            receivedAt = clock();
            if (update.events != null)
                foreach (var e in update.events) EventReceived?.Invoke(ViewMapper.ToEvent(e));
            View = ViewMapper.ToView(update.view);
            ViewChanged?.Invoke(View);
        }

        /// <summary>Erro do servidor: se for uma ação recusada, avisa a interface.</summary>
        public void ApplyError(ErrorMsg error)
        {
            if (error != null && error.code == ErrorCodes.Action && error.action >= 0)
                ActionRejected?.Invoke((ActionResult)error.action);
        }

        public void PlaceBet(int bet) => sender.Send(Protocol.ToServer.Bet, new BetMsg { value = bet });

        public void PlayCard(Card card) => sender.Send(Protocol.ToServer.Play, new PlayMsg { card = card.Id });

        public void ResumeControl() => sender.Send(Protocol.ToServer.Resume);

        /// <summary>Manda só o número da mensagem; o balão aparece quando o servidor repassa (para todos ao mesmo tempo).</summary>
        public void SendChat(int messageId)
        {
            if (Fodinha.Hosting.QuickChat.IsValid(messageId)) sender.Send(Protocol.ToServer.Chat, new ChatMsg { id = messageId });
        }

        /// <summary>Mensagem pronta de alguém da sala (vinda do servidor).</summary>
        public void ApplyChat(ChatDto chat)
        {
            if (chat == null || chat.seat < 0 || !Fodinha.Hosting.QuickChat.IsValid(chat.id)) return;
            if (View != null && chat.seat >= View.Players.Count) return;
            ChatReceived?.Invoke(chat.seat, chat.id);
        }
    }
}
