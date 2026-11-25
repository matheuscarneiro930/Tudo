using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Fodinha.Server
{
    /// <summary>Conta de um jogador: carteira de moedas fictícias e estatísticas. Só o servidor altera.</summary>
    public sealed class Account
    {
        public string Id;
        /// <summary>Hash (SHA-256) do token secreto do aparelho. O token em si nunca é guardado.</summary>
        public string TokenHash;
        public string Name;
        public int Coins;
        public int Games;
        public int Wins;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        /// <summary>Avatar (texto do <see cref="Fodinha.Hosting.AvatarSpec"/>). Vazio = sorteado pelo id.</summary>
        public string Avatar = "";
        /// <summary>Itens comprados na loja.</summary>
        public List<string> Owned = new List<string>();
        /// <summary>Mesa e baralho em uso.</summary>
        public string TableTheme = Fodinha.Hosting.ShopCatalog.DefaultTable;
        public string DeckTheme = Fodinha.Hosting.ShopCatalog.DefaultDeck;
        /// <summary>Conta sem anúncios (código VIP do dono do jogo; no futuro, compra "remover anúncios").</summary>
        public bool NoAds;

        /// <summary>Tentativas erradas de código VIP (só em memória; bloqueia chute em massa).</summary>
        [NonSerialized] public int CodeFailures;
        [NonSerialized] public double CodeLockedUntil = double.NegativeInfinity;

        /// <summary>Última vez que ganhou moedas por vídeo (só em memória; limita a frequência).</summary>
        [NonSerialized] public double LastRewardAt = double.NegativeInfinity;

        public Account Snapshot()
        {
            var copy = (Account)MemberwiseClone();
            copy.Owned = new List<string>(Owned ?? new List<string>());
            return copy;
        }

        /// <summary>Avatar para mostrar (o salvo ou um sorteado a partir do id, sempre o mesmo).</summary>
        public string EffectiveAvatar =>
            string.IsNullOrEmpty(Avatar) ? Fodinha.Hosting.AvatarSpec.FromSeed(Id).Encode() : Avatar;
    }

    /// <summary>Resultado de uma compra na loja.</summary>
    public enum PurchaseResult { Ok, UnknownItem, AlreadyOwned, NotEnoughCoins }

    /// <summary>
    /// Todas as contas, em memória (carregadas do banco ao iniciar). Cada alteração é enviada para
    /// <see cref="Persist"/>, que grava no banco em segundo plano. Não é thread-safe: usado só pelo laço do jogo.
    /// </summary>
    public sealed class AccountBook
    {
        public const int StartingCoins = 500;

        private readonly Dictionary<string, Account> byTokenHash = new Dictionary<string, Account>();
        private readonly Dictionary<string, Account> byId = new Dictionary<string, Account>();

        /// <summary>Chamado com uma cópia da conta sempre que ela muda (gravação no banco).</summary>
        public Action<Account> Persist = _ => { };

        public AccountBook(IEnumerable<Account> existing = null)
        {
            if (existing == null) return;
            foreach (var a in existing)
            {
                if (a?.Id == null || a.TokenHash == null) continue;
                byTokenHash[a.TokenHash] = a;
                byId[a.Id] = a;
            }
        }

        public int Count => byId.Count;

        public static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token ?? ""));
            return Convert.ToBase64String(bytes);
        }

        public Account FindById(string id) =>
            string.IsNullOrEmpty(id) ? null : byId.TryGetValue(id, out var a) ? a : null;

        public Account FindByToken(string token) =>
            string.IsNullOrEmpty(token) ? null : byTokenHash.TryGetValue(HashToken(token), out var a) ? a : null;

        public Account Create(string id, string token, string name)
        {
            var now = DateTime.UtcNow;
            var a = new Account
            {
                Id = id,
                TokenHash = HashToken(token),
                Name = name,
                Coins = StartingCoins,
                CreatedAt = now,
                UpdatedAt = now
            };
            byTokenHash[a.TokenHash] = a;
            byId[a.Id] = a;
            Changed(a);
            return a;
        }

        public void Rename(Account a, string name)
        {
            if (a == null || a.Name == name) return;
            a.Name = name;
            Changed(a);
        }

        /// <summary>Soma (ou subtrai) moedas. Nunca deixa o saldo negativo.</summary>
        public int AddCoins(Account a, int amount)
        {
            if (a == null || amount == 0) return 0;
            int before = a.Coins;
            a.Coins = Math.Max(0, a.Coins + amount);
            Changed(a);
            return a.Coins - before;
        }

        public void RecordGame(Account a, bool won)
        {
            if (a == null) return;
            a.Games++;
            if (won) a.Wins++;
            Changed(a);
        }

        /// <summary>Troca o avatar (texto já validado; inválido vira vazio = sorteado).</summary>
        public void SetAvatar(Account a, string avatar)
        {
            avatar = Fodinha.Hosting.AvatarSpec.Sanitize(avatar);
            if (a == null || a.Avatar == avatar || avatar.Length == 0) return;
            a.Avatar = avatar;
            Changed(a);
        }

        public void SetNoAds(Account a, bool noAds)
        {
            if (a == null || a.NoAds == noAds) return;
            a.NoAds = noAds;
            Changed(a);
        }

        /// <summary>Compra um item da loja: confere se existe, se já é dele e o saldo; desconta e passa a usar.</summary>
        public PurchaseResult Buy(Account a, string itemId)
        {
            var item = Fodinha.Hosting.ShopCatalog.Find(itemId);
            if (a == null || item == null) return PurchaseResult.UnknownItem;
            if (Fodinha.Hosting.ShopCatalog.Owns(a.Owned, item.Id)) return PurchaseResult.AlreadyOwned;
            if (a.Coins < item.Price) return PurchaseResult.NotEnoughCoins;
            a.Coins -= item.Price;
            a.Owned.Add(item.Id);
            if (item.Category == Fodinha.Hosting.ShopCategory.Table) a.TableTheme = item.Id;
            else a.DeckTheme = item.Id;
            Changed(a);
            return PurchaseResult.Ok;
        }

        /// <summary>Passa a usar um item que já é do jogador. Falso se não existir ou não for dele.</summary>
        public bool Equip(Account a, string itemId)
        {
            var item = Fodinha.Hosting.ShopCatalog.Find(itemId);
            if (a == null || item == null || !Fodinha.Hosting.ShopCatalog.Owns(a.Owned, item.Id)) return false;
            if (item.Category == Fodinha.Hosting.ShopCategory.Table)
            {
                if (a.TableTheme == item.Id) return true;
                a.TableTheme = item.Id;
            }
            else
            {
                if (a.DeckTheme == item.Id) return true;
                a.DeckTheme = item.Id;
            }
            Changed(a);
            return true;
        }

        /// <summary>Ranking: mais moedas primeiro; empate, mais vitórias; depois quem chegou antes.</summary>
        public List<Account> Ranking() =>
            byId.Values.OrderByDescending(a => a.Coins).ThenByDescending(a => a.Wins).ThenBy(a => a.CreatedAt).ToList();

        private void Changed(Account a)
        {
            a.UpdatedAt = DateTime.UtcNow;
            Persist(a.Snapshot());
        }
    }
}
