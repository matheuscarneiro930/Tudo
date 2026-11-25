using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fodinha.Hosting;
using Fodinha.Net;
using NUnit.Framework;

namespace Fodinha.Server.Tests
{
    /// <summary>
    /// Vídeo premiado verificado pelo Google (AdMob SSV) e outros golpes de moedas.
    /// O "Google" aqui é uma chave de teste gerada na hora (mesmo formato da real).
    /// </summary>
    public class AdRewardSecurityTests
    {
        private ECDsa google;
        private string keysJson;
        private DateTimeOffset clock;

        [SetUp]
        public void SetUp()
        {
            google = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            keysJson = "{\"keys\":[{\"keyId\":777,\"base64\":\"" + Convert.ToBase64String(google.ExportSubjectPublicKeyInfo()) + "\"}]}";
            clock = DateTimeOffset.UtcNow;
        }

        private AdMobVerifier NewVerifier() => new AdMobVerifier(_ => Task.FromResult(keysJson), () => clock);

        private string SignedQuery(string userId, string tx, DateTimeOffset when, ECDsa signer = null)
        {
            string content = "ad_network=5450213213286189855&ad_unit=5275693506&reward_amount=100&reward_item=moedas" +
                             "&timestamp=" + when.ToUnixTimeMilliseconds() + "&transaction_id=" + tx + "&user_id=" + userId;
            var sig = (signer ?? google).SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            string b64url = Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return "?" + content + "&signature=" + b64url + "&key_id=777";
        }

        [Test]
        public async Task AvisoDoGoogle_Valido_EhAceitoUmaVezSo()
        {
            var v = NewVerifier();
            string q = SignedQuery("p123", "tx1", clock);
            var ok = await v.VerifyAsync(q);
            Assert.That(ok.Valid, Is.True, ok.Reason);
            Assert.That(ok.UserId, Is.EqualTo("p123"));
            Assert.That((await v.VerifyAsync(q)).Valid, Is.False, "O mesmo aviso não vale duas vezes (replay).");
        }

        /// <summary>Ataque: forjar o aviso do Google (trocar a conta, assinar com outra chave, aviso velho).</summary>
        [Test]
        public async Task AvisoForjado_EhRecusado()
        {
            var v = NewVerifier();
            string q = SignedQuery("p123", "tx2", clock);
            Assert.That((await v.VerifyAsync(q.Replace("user_id=p123", "user_id=p999"))).Valid, Is.False, "Mudou a conta: assinatura não bate.");

            using var fake = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Assert.That((await v.VerifyAsync(SignedQuery("p123", "tx3", clock, fake))).Valid, Is.False, "Assinado por outra chave.");
            Assert.That((await v.VerifyAsync(SignedQuery("p123", "tx4", clock - TimeSpan.FromHours(3)))).Valid, Is.False, "Aviso velho.");
            Assert.That((await v.VerifyAsync(SignedQuery("p123", "tx5", clock).Replace("key_id=777", "key_id=1"))).Valid, Is.False, "Chave que não existe.");
            Assert.That((await v.VerifyAsync("?user_id=p123&signature=abc&key_id=777")).Valid, Is.False);
            Assert.That((await v.VerifyAsync("")).Valid, Is.False);
            Assert.That((await v.VerifyAsync(null)).Valid, Is.False);
        }

        [Test]
        public void ComVerificacaoLigada_PedidoDoAppNaoDaMoedas_SoOAvisoDoGoogle()
        {
            var core = new ServerCore(new ServerOptions { RequireVerifiedAds = true });
            var c = new FakeClient(core, "Ana").Hello();
            var account = core.Accounts.FindByToken(c.Token);
            core.Accounts.AddCoins(account, -500);
            Assert.That(c.Profile.rewardNeedsApp, Is.True);

            c.Do(Protocol.ToServer.ClaimReward);
            Assert.That(account.Coins, Is.EqualTo(0), "App modificado pedindo moedas sem vídeo: nada.");

            Assert.That(core.CreditVerifiedReward(c.PlayerId), Is.True);
            Assert.That(account.Coins, Is.EqualTo(100));
            Assert.That(c.Profile.reason, Is.EqualTo("recompensa"), "O jogador conectado é avisado na hora.");
            Assert.That(core.CreditVerifiedReward("conta-que-nao-existe"), Is.False);
        }

        [Test]
        public void ComVerificacaoLigada_ContaVip_ContinuaPedindoDireto()
        {
            var core = new ServerCore(new ServerOptions { RequireVerifiedAds = true, VipCode = "Segredo-123" });
            var c = new FakeClient(core, "Dono").Hello();
            c.Do(Protocol.ToServer.RedeemCode, new CodeMsg { code = "SEGREDO-123" });
            var account = core.Accounts.FindByToken(c.Token);
            core.Accounts.AddCoins(account, -500);
            c.Do(Protocol.ToServer.ClaimReward);
            Assert.That(account.Coins, Is.EqualTo(100));
        }

        /// <summary>Golpe: o dono sobe o valor da sala logo antes de começar (moedas dos outros saem sem eles verem).</summary>
        [Test]
        public void DonoNaoMudaOValorDaSala_ComOutrosDentro()
        {
            var core = new ServerCore(new ServerOptions { Pacing = PacingSettings.Instant });
            var owner = new FakeClient(core, "Dono").Hello();
            owner.Create(new RoomOptions { Stake = 50 });
            var victim = new FakeClient(core, "Vitima").Hello();
            victim.Join(owner.Room.code);

            var options = new RoomOptions { Stake = 500 };
            owner.Do(Protocol.ToServer.UpdateOptions, new UpdateOptionsMsg { options = options });
            Assert.That(owner.LastError?.code, Is.EqualTo(ErrorCodes.InvalidOptions));
            Assert.That(victim.Room.stake, Is.EqualTo(50));

            owner.Do(Protocol.ToServer.StartMatch);
            Assert.That(core.Accounts.FindByToken(victim.Token).Coins, Is.EqualTo(450), "Pagou só o valor que viu ao entrar.");
        }
    }
}
