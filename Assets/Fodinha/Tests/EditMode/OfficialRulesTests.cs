using NUnit.Framework;

namespace Fodinha.Core.Tests
{
    /// <summary>Regras oficiais definidas pelo dono do jogo (as únicas usadas pelo app e pelo servidor).</summary>
    public class OfficialRulesTests
    {
        [Test]
        public void PadraoJaEhOficial()
        {
            Assert.IsTrue(RuleSet.CreateDefault().FollowsOfficialRules);
            Assert.AreEqual(TieRule.SuitBreaksTie, RuleSet.CreateDefault().TieRule);
        }

        [Test]
        public void WithOfficialRules_ForcaRegrasFixas_EMantemAsConfiguraveis()
        {
            var custom = new RuleSet
            {
                StartingLives = 5,
                TurnTimeSeconds = 45,
                MaxCardsPerPlayer = 6,
                TimeoutsBeforeAutoPlay = 3,
                ManilhaMode = ManilhaMode.Fixed,
                TieRule = TieRule.CancelOut,
                LastBidderRestriction = false,
                LifeLossMode = LifeLossMode.Difference,
                Progression = CardProgression.UpAndDown,
                BlindOneCardRounds = false
            };

            var official = custom.WithOfficialRules();

            Assert.IsTrue(official.FollowsOfficialRules);
            Assert.AreEqual(ManilhaMode.Vira, official.ManilhaMode);
            Assert.AreEqual(TieRule.SuitBreaksTie, official.TieRule);
            Assert.IsTrue(official.LastBidderRestriction);
            Assert.AreEqual(LifeLossMode.OnePerMiss, official.LifeLossMode);
            Assert.AreEqual(CardProgression.UpThenHold, official.Progression);
            Assert.IsTrue(official.BlindOneCardRounds);

            Assert.AreEqual(5, official.StartingLives);
            Assert.AreEqual(45, official.TurnTimeSeconds);
            Assert.AreEqual(6, official.MaxCardsPerPlayer);
            Assert.AreEqual(3, official.TimeoutsBeforeAutoPlay);

            Assert.IsFalse(custom.FollowsOfficialRules, "O original não é alterado.");
        }

        [Test]
        public void ExemploDoDono_PeNaoPodeFecharASoma()
        {
            // Rodada de 3 cartas, 4 jogadores: os três primeiros apostam 1 cada (soma 3).
            // O pé não pode apostar 0 (fecharia a soma em 3): é obrigado a apostar 1 ou mais.
            var allowed = BetRules.AllowedBets(3, isLastBidder: true, sumOfPreviousBets: 3, RuleSet.CreateDefault());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, allowed);
        }
    }
}
