using System;
using System.Collections.Generic;
using Fodinha.Core;

namespace Fodinha.Net
{
    /// <summary>
    /// Converte a <see cref="MatchView"/> (já filtrada para um assento) no formato de rede e de volta.
    /// O servidor só converte o que <see cref="MatchEngine.BuildView"/> devolveu: nada privado de outros jogadores entra aqui.
    /// </summary>
    public static class ViewMapper
    {
        public static ViewDto ToDto(MatchView v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            var dto = new ViewDto
            {
                viewer = v.ViewerSeat,
                phase = (int)v.Phase,
                round = v.RoundNumber,
                cards = v.CardsThisRound,
                trick = v.TrickNumber,
                dealer = v.DealerSeat,
                current = v.CurrentSeat,
                blind = v.IsBlindRound,
                vira = v.Vira.HasValue ? v.Vira.Value.Id : -1,
                rules = v.Rules,
                winner = v.WinnerSeat,
                lastTrick = ToDto(v.LastTrick),
                lastRound = ToDto(v.LastRoundSummary)
            };
            foreach (var p in v.Players)
            {
                var pd = new PlayerDto
                {
                    seat = p.Seat, name = p.Name, lives = p.Lives, elim = p.IsEliminated, elimRound = p.EliminatedAtRound,
                    bet = p.Bet, won = p.TricksWon, handCount = p.HandCount, connected = p.IsConnected, auto = p.IsAutoPlay
                };
                foreach (var c in p.VisibleCards) pd.visible.Add(c.Id);
                dto.players.Add(pd);
            }
            foreach (var c in v.MyHand) dto.hand.Add(c.Id);
            foreach (var pc in v.CurrentTrick) dto.table.Add(new PlayDto { seat = pc.Seat, card = pc.Card.Id });
            foreach (var b in v.AllowedBets) dto.allowed.Add(b);
            return dto;
        }

        public static MatchView ToView(ViewDto d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var rules = d.rules ?? RuleSet.CreateDefault();
            Card? vira = d.vira >= 0 ? Card.FromId(d.vira) : (Card?)null;
            var phase = (MatchPhase)d.phase;

            CardComparer comparer = null;
            if (phase != MatchPhase.NotStarted && (rules.ManilhaMode == ManilhaMode.Fixed || vira.HasValue))
                comparer = new CardComparer(rules, vira);

            var players = new List<PlayerView>(d.players?.Count ?? 0);
            if (d.players != null)
            {
                foreach (var p in d.players)
                {
                    players.Add(new PlayerView
                    {
                        Seat = p.seat, Name = p.name, Lives = p.lives, IsEliminated = p.elim, EliminatedAtRound = p.elimRound,
                        Bet = p.bet, TricksWon = p.won, HandCount = p.handCount, IsConnected = p.connected, IsAutoPlay = p.auto,
                        VisibleCards = Cards(p.visible)
                    });
                }
            }

            return new MatchView
            {
                ViewerSeat = d.viewer,
                Phase = phase,
                RoundNumber = d.round,
                CardsThisRound = d.cards,
                TrickNumber = d.trick,
                DealerSeat = d.dealer,
                CurrentSeat = d.current,
                IsBlindRound = d.blind,
                Vira = vira,
                Comparer = comparer,
                Rules = rules,
                Players = players,
                MyHand = Cards(d.hand),
                CurrentTrick = Plays(d.table),
                LastTrick = ToTrick(d.lastTrick),
                LastRoundSummary = ToRound(d.lastRound),
                AllowedBets = d.allowed != null ? d.allowed.ToArray() : Array.Empty<int>(),
                WinnerSeat = d.winner
            };
        }

        public static EventDto ToDto(MatchEvent e) => new EventDto
        {
            type = (int)e.Type, seat = e.Seat, value = e.Value, card = e.Card.HasValue ? e.Card.Value.Id : -1
        };

        public static MatchEvent ToEvent(EventDto e) =>
            new MatchEvent((MatchEventType)e.type, e.seat, e.value, e.card >= 0 ? Card.FromId(e.card) : (Card?)null);

        // ------------------------------------------------------------------ partes

        private static TrickDto ToDto(TrickResult t)
        {
            if (t == null) return null;
            var dto = new TrickDto { winner = t.WinnerSeat };
            foreach (var p in t.Plays) dto.plays.Add(new PlayDto { seat = p.Seat, card = p.Card.Id });
            dto.canceled.AddRange(t.CanceledSeats);
            return dto;
        }

        private static RoundDto ToDto(RoundSummary r)
        {
            if (r == null) return null;
            var dto = new RoundDto { round = r.RoundNumber, cards = r.Cards, tiebreak = r.SimultaneousEliminationTiebreak };
            foreach (var x in r.Results)
                dto.results.Add(new ResultDto { seat = x.Seat, bet = x.Bet, won = x.TricksWon, lost = x.LivesLost, livesAfter = x.LivesAfter, elim = x.Eliminated });
            return dto;
        }

        private static TrickResult ToTrick(TrickDto t)
        {
            if (t == null) return null;
            return new TrickResult(t.winner, Plays(t.plays), t.canceled != null ? t.canceled.ToArray() : Array.Empty<int>());
        }

        private static RoundSummary ToRound(RoundDto r)
        {
            if (r == null) return null;
            var results = new List<PlayerRoundResult>();
            if (r.results != null)
                foreach (var x in r.results) results.Add(new PlayerRoundResult(x.seat, x.bet, x.won, x.lost, x.livesAfter, x.elim));
            return new RoundSummary(r.round, r.cards, results, r.tiebreak);
        }

        private static Card[] Cards(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return Array.Empty<Card>();
            var result = new Card[ids.Count];
            for (int i = 0; i < ids.Count; i++) result[i] = Card.FromId(ids[i]);
            return result;
        }

        private static PlayedCard[] Plays(List<PlayDto> plays)
        {
            if (plays == null || plays.Count == 0) return Array.Empty<PlayedCard>();
            var result = new PlayedCard[plays.Count];
            for (int i = 0; i < plays.Count; i++) result[i] = new PlayedCard(plays[i].seat, Card.FromId(plays[i].card));
            return result;
        }
    }
}
