using System;
using Fodinha.Core;
using Fodinha.Hosting;
using UnityEngine;

namespace Fodinha.Game
{
    /// <summary>Preferências do jogador salvas no aparelho (PlayerPrefs).</summary>
    public static class GamePrefs
    {
        private const string KeyName = "fodinha.nome";
        private const string KeySound = "fodinha.som";
        private const string KeyVibration = "fodinha.vibracao";
        private const string KeyBots = "fodinha.offline.bots";
        private const string KeyOfflineRules = "fodinha.offline.regras";
        private const string KeyRoomOptions = "fodinha.sala.opcoes";

        private const string KeyServerUrl = "fodinha.servidor";
        private const string KeyOnlineToken = "fodinha.token";

        public const int MaxNameLength = 14;

        /// <summary>Servidor padrão do jogo online (Render). Para testar com um servidor local: ws://IP-DO-PC:5080/ws.</summary>
        public const string DefaultServerUrl = "wss://fodinha-server-0ccs.onrender.com/ws";

        /// <summary>Site do jogo (versão para navegador; é por ele que quem tem iPhone joga).</summary>
        public const string SiteUrl = "https://fodinha-server-0ccs.onrender.com";

        /// <summary>Mensagem do botão "Convidar amigos iOS": leva para o site (não para uma sala).</summary>
        public const string IosInviteMessage =
            "Bora jogar Fodinha Online comigo! 🃏\n" +
            "No iPhone, abre esse link no Safari e joga direto pelo navegador:\n" +
            SiteUrl + "\n" +
            "Dica: toque em Compartilhar → \"Adicionar à Tela de Início\" para ficar com o ícone do jogo.";

        /// <summary>Endereço do servidor (Configurações → Servidor). Vazio = padrão.</summary>
        public static string ServerUrl
        {
            get
            {
                var url = PlayerPrefs.GetString(KeyServerUrl, "");
                return string.IsNullOrWhiteSpace(url) ? DefaultForPlatform() : url.Trim();
            }
            set
            {
                PlayerPrefs.SetString(KeyServerUrl, value == null || value.Trim() == DefaultServerUrl ? "" : value.Trim());
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Na versão web, o jogo é servido pelo próprio servidor: usa o mesmo endereço da página (https → wss, /ws).
        /// Nas outras plataformas, o servidor padrão.
        /// </summary>
        private static string DefaultForPlatform()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer &&
                System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out var page) &&
                (page.Scheme == "https" || page.Scheme == "http"))
                return (page.Scheme == "https" ? "wss://" : "ws://") + page.Authority + "/ws";
            return DefaultServerUrl;
        }

        public static bool HasCustomServer => !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(KeyServerUrl, ""));

        private static string sessionToken = "";

        /// <summary>
        /// Token secreto dado pelo servidor: permite reconectar e voltar ao mesmo lugar na sala.
        /// No celular fica salvo (sobrevive a fechar o app). No PC fica só na memória, para várias janelas
        /// abertas ao mesmo tempo (testes) serem jogadores diferentes.
        /// </summary>
        public static string OnlineToken
        {
            get => PersistToken ? PlayerPrefs.GetString(TokenKey, "") : sessionToken;
            set
            {
                if (!PersistToken) { sessionToken = value ?? ""; return; }
                PlayerPrefs.SetString(TokenKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Onde o token fica guardado. Servidor oficial: a chave de sempre. Servidor digitado em Configurações:
        /// uma chave própria para aquele endereço — assim a chave da conta oficial NUNCA é enviada para um servidor
        /// de terceiros (golpe "troque o servidor para este aqui").
        /// </summary>
        public static string TokenKey => HasCustomServer ? KeyOnlineToken + "." + StableHash(ServerUrl) : KeyOnlineToken;

        /// <summary>Hash estável (FNV-1a) em hexadecimal.</summary>
        private static string StableHash(string text)
        {
            uint h = 2166136261u;
            foreach (char c in (text ?? "").Trim().ToLowerInvariant()) { h ^= c; h *= 16777619u; }
            return h.ToString("x8");
        }

        /// <summary>
        /// O token (e com ele as moedas) fica salvo no celular e no navegador. Só no PC (app de teste, várias janelas)
        /// fica na memória, para cada janela ser um jogador diferente.
        /// </summary>
        private static bool PersistToken =>
            Application.platform != RuntimePlatform.WindowsPlayer &&
            Application.platform != RuntimePlatform.WindowsEditor &&
            Application.platform != RuntimePlatform.OSXPlayer &&
            Application.platform != RuntimePlatform.OSXEditor &&
            Application.platform != RuntimePlatform.LinuxPlayer &&
            Application.platform != RuntimePlatform.LinuxEditor;

        public static string PlayerName
        {
            get
            {
                var name = PlayerPrefs.GetString(KeyName, "");
                return string.IsNullOrWhiteSpace(name) ? "Jogador" : name;
            }
            set
            {
                PlayerPrefs.SetString(KeyName, SanitizeName(value));
                PlayerPrefs.Save();
            }
        }

        public static bool HasCustomName => !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(KeyName, ""));

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(KeySound, 1) == 1;
            set { PlayerPrefs.SetInt(KeySound, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool VibrationOn
        {
            get => PlayerPrefs.GetInt(KeyVibration, 1) == 1;
            set { PlayerPrefs.SetInt(KeyVibration, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static int OfflineBots
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KeyBots, 3), OfflineMatchConfig.MinBots, OfflineMatchConfig.MaxBots);
            set { PlayerPrefs.SetInt(KeyBots, value); PlayerPrefs.Save(); }
        }

        public static RuleSet OfflineRules
        {
            get => LoadJson(KeyOfflineRules, RuleSet.CreateDefault, r => r.IsValid);
            set => SaveJson(KeyOfflineRules, value);
        }

        public static RoomOptions LastRoomOptions
        {
            get => LoadJson(KeyRoomOptions, () => new RoomOptions(), o => o.Rules != null && o.GetValidationErrors().Count == 0);
            set => SaveJson(KeyRoomOptions, value);
        }

        // ------------------------------------------------------------------ avatar e loja

        private const string KeyAvatar = "fodinha.avatar";
        private const string KeyTable = "fodinha.mesa";
        private const string KeyDeck = "fodinha.baralho";
        private const string KeyOwned = "fodinha.itens";
        private const string KeyNoAds = "fodinha.semanuncios";

        /// <summary>Muda quando o avatar, a mesa ou o baralho mudam (para as telas se redesenharem).</summary>
        public static event Action CosmeticsChanged;

        /// <summary>
        /// Avatar do jogador (texto do <see cref="AvatarSpec"/>). Na primeira vez sorteia um e guarda.
        /// Fica no aparelho e é enviado ao servidor para os outros verem.
        /// </summary>
        public static string Avatar
        {
            get
            {
                var saved = PlayerPrefs.GetString(KeyAvatar, "");
                if (AvatarSpec.TryParse(saved, out _)) return saved;
                var fresh = AvatarSpec.Random(new System.Random()).Encode();
                PlayerPrefs.SetString(KeyAvatar, fresh);
                PlayerPrefs.Save();
                return fresh;
            }
            set
            {
                var clean = AvatarSpec.Sanitize(value);
                if (clean.Length == 0 || clean == PlayerPrefs.GetString(KeyAvatar, "")) return;
                PlayerPrefs.SetString(KeyAvatar, clean);
                PlayerPrefs.Save();
                CosmeticsChanged?.Invoke();
            }
        }

        /// <summary>Mesa em uso (item da loja).</summary>
        public static string TableTheme
        {
            get => ShopCatalog.ValidEquipped(ShopCategory.Table, PlayerPrefs.GetString(KeyTable, ""), OwnedItems);
            set => SetEquipped(KeyTable, ShopCategory.Table, value);
        }

        /// <summary>Baralho (verso das cartas) em uso.</summary>
        public static string DeckTheme
        {
            get => ShopCatalog.ValidEquipped(ShopCategory.Deck, PlayerPrefs.GetString(KeyDeck, ""), OwnedItems);
            set => SetEquipped(KeyDeck, ShopCategory.Deck, value);
        }

        private static void SetEquipped(string key, ShopCategory category, string id)
        {
            var valid = ShopCatalog.ValidEquipped(category, id, OwnedItems);
            if (valid == PlayerPrefs.GetString(key, "")) return;
            PlayerPrefs.SetString(key, valid);
            PlayerPrefs.Save();
            CosmeticsChanged?.Invoke();
        }

        /// <summary>Itens comprados (cópia do que o servidor informou; quem decide é o servidor).</summary>
        public static System.Collections.Generic.List<string> OwnedItems
        {
            get => SplitIds(PlayerPrefs.GetString(KeyOwned, ""));
            set
            {
                PlayerPrefs.SetString(KeyOwned, value == null ? "" : string.Join(",", value));
                PlayerPrefs.Save();
            }
        }

        private static System.Collections.Generic.List<string> SplitIds(string text)
        {
            var list = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var id in text.Split(','))
                if (ShopCatalog.Find(id) != null && !list.Contains(id)) list.Add(id);
            return list;
        }

        /// <summary>Conta sem anúncios (código VIP). Guardado aqui para valer também no modo offline.</summary>
        public static bool NoAds
        {
            get => PlayerPrefs.GetInt(KeyNoAds, 0) == 1;
            set { PlayerPrefs.SetInt(KeyNoAds, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Aplica o que o servidor mandou no perfil: itens, mesa, baralho e "sem anúncios".</summary>
        public static void ApplyServerProfile(System.Collections.Generic.List<string> owned, string table, string deck, bool noAds)
        {
            OwnedItems = owned;
            NoAds = noAds;
            if (!string.IsNullOrEmpty(table)) TableTheme = table;
            if (!string.IsNullOrEmpty(deck)) DeckTheme = deck;
        }

        /// <summary>Nome sem espaços nas pontas, sem rich text e com no máximo <see cref="MaxNameLength"/> caracteres.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            name = name.Trim().Replace("<", "").Replace(">", "");
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
        }

        private static T LoadJson<T>(string key, Func<T> fallback, Func<T, bool> isValid) where T : class
        {
            var json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return fallback();
            try
            {
                var value = JsonUtility.FromJson<T>(json);
                return value != null && isValid(value) ? value : fallback();
            }
            catch (Exception)
            {
                return fallback();
            }
        }

        private static void SaveJson<T>(string key, T value)
        {
            if (value == null) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
        }
    }
}
