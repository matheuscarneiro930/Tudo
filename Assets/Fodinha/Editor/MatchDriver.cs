using System;
using Fodinha.Core;
using Fodinha.Game;

namespace Fodinha.EditorTools
{
    /// <summary>
    /// Avança uma partida offline até um estado desejado (para testes e capturas de tela).
    /// Quando é a vez do jogador local, envia pedidos simples, como faria a interface.
    /// </summary>
    public static class MatchDriver
    {
        public static bool AdvanceUntil(LocalMatchController controller, Func<MatchView, bool> target,
                                        bool humanActs = true, int maxSteps = 300000, float dt = 0.02f)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                var view = controller.View;
                if (i > 0 && i % 50000 == 0 && view != null)
                    UnityEngine.Debug.Log("[Fodinha][Driver] passo " + i + " fase=" + view.Phase + " rodada=" + view.RoundNumber + " vez=" + view.CurrentSeat);
                if (view != null && target(view)) return true;
                if (view != null && view.Phase == MatchPhase.MatchOver) return false;

                if (humanActs && view != null && view.IsMyTurn && view.Me != null && !view.Me.IsAutoPlay)
                {
                    if (view.Phase == MatchPhase.Betting && view.AllowedBets.Count > 0)
                    {
                        controller.PlaceBet(view.AllowedBets[0]);
                        continue;
                    }
                    if (view.Phase == MatchPhase.Playing && view.MyHand.Count > 0)
                    {
                        controller.PlayCard(view.MyHand[0]);
                        continue;
                    }
                }
                controller.Step(dt);
            }
            return false;
        }
    }
}
