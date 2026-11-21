using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>Cliente falso: registra tudo que o servidor envia e confere que nenhuma informação privada vaza.</summary>
    internal sealed class FakeClient : IClientLink
    {
        private readonly ServerCore core;
        public readonly string Name;
        public readonly List<Envelope> Inbox = new List<Envelope>();
        public bool Closed;
        public string PlayerId;
        public string Token;
        public RoomStateDto Room;
        public MatchUpdateDto Match;
        public ErrorMsg LastError;
        public LeftRoomMsg LastLeft;
        public RoomListMsg LastList;
        public ProfileMsg Profile;
        public RankingMsg LastRanking;
        public WelcomeMsg Welcome;
        public int MatchUpdates;

        public FakeClient(ServerCore core, string name)
        {
            this.core = core;
            Name = name;
        }

        public void Send(string json)
        {
            Assert.That(Protocol.TryDecode(json, out var env), Is.True, json);
            Inbox.Add(env);
            switch (env.t)
            {
                case Protocol.ToClient.Welcome:
                    var w = Protocol.Payload<WelcomeMsg>(env);
                    Welcome = w;
                    PlayerId = w.playerId;
                    Token = w.token;
                    break;
                case Protocol.ToClient.RoomState: Room = Protocol.Payload<RoomStateDto>(env); break;
                case Protocol.ToClient.Match:
                    Match = Protocol.Payload<MatchUpdateDto>(env);
                    MatchUpdates++;
                    CheckPrivacy(Match.view);
                    break;
                case Protocol.ToClient.Error: LastError = Protocol.Payload<ErrorMsg>(env); break;
                case Protocol.ToClient.LeftRoom: LastLeft = Protocol.Payload<LeftRoomMsg>(env); Room = null; break;
                case Protocol.ToClient.RoomList: LastList = Protocol.Payload<RoomListMsg>(env); break;
                case Protocol.ToClient.Profile: Profile = Protocol.Payload<ProfileMsg>(env); break;
                case Protocol.ToClient.Ranking: LastRanking = Protocol.Payload<RankingMsg>(env); break;
            }
        }

        public void Close() => Closed = true;

        public void Do(string type, object payload = null) => core.OnMessage(this, Protocol.Encode(type, payload));

        public FakeClient Hello(string token = null, string avatar = null)
        {
            core.OnConnected(this);
            Do(Protocol.ToServer.Hello, new HelloMsg { name = Name, token = token, version = Protocol.Version, avatar = avatar });
            return this;
        }

        public void Create(RoomOptions options = null) =>
            Do(Protocol.ToServer.CreateRoom, new CreateRoomMsg { options = options ?? new RoomOptions() });

        public void Join(string code) => Do(Protocol.ToServer.JoinRoom, new JoinRoomMsg { code = code });

        public int MySeat => Match?.view?.viewer ?? -1;

        /// <summary>Se for a vez deste cliente, aposta/joga a primeira opção válida (como um jogador apressado).</summary>
        public bool AutoAct()
        {
            var v = Match?.view;
            if (v == null || v.current != v.viewer) return false;
            if (v.phase == (int)MatchPhase.Betting && v.allowed.Count > 0)
            {
                Do(Protocol.ToServer.Bet, new BetMsg { value = v.allowed[0] });
                return true;
            }
            if (v.phase == (int)MatchPhase.Playing && v.hand.Count > 0)
            {
                Do(Protocol.ToServer.Play, new PlayMsg { card = v.hand[0] });
                return true;
            }
            return false;
        }

        /// <summary>Cada cliente só pode ver a própria mão (e, na aposta da rodada cega, a carta dos outros).</summary>
        private static void CheckPrivacy(ViewDto v)
        {
            Assert.That(v.viewer, Is.GreaterThanOrEqualTo(0));
            var me = v.players[v.viewer];
            if (v.blind && v.phase == (int)MatchPhase.Betting)
                Assert.That(v.hand, Is.Empty, "Na rodada cega o jogador não pode receber a própria carta.");
            else if (!v.blind)
                Assert.That(v.hand.Count, Is.EqualTo(me.handCount), "A mão enviada deve ser exatamente a do próprio jogador.");

            foreach (var p in v.players)
            {
                if (p.seat == v.viewer) Assert.That(p.visible, Is.Empty, "Ninguém vê a própria carta da testa.");
                else if (!(v.blind && v.phase == (int)MatchPhase.Betting))
                    Assert.That(p.visible, Is.Empty, "Cartas de outro jogador só aparecem na aposta da rodada cega.");
            }
        }
    }
}
