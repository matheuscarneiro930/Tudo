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

        /// <summary>Servidor padrão do jogo online. Trocado para o endereço público quando o servidor for hospedado.</summary>
        public const string DefaultServerUrl = "ws://localhost:5080/ws";

        /// <summary>Endereço do servidor (Configurações → Servidor). Vazio = padrão.</summary>
        public static string ServerUrl
        {
            get
            {
                var url = PlayerPrefs.GetString(KeyServerUrl, "");
                return string.IsNullOrWhiteSpace(url) ? DefaultServerUrl : url.Trim();
            }
            set
            {
                PlayerPrefs.SetString(KeyServerUrl, value == null || value.Trim() == DefaultServerUrl ? "" : value.Trim());
                PlayerPrefs.Save();
            }
        }

        public static bool HasCustomServer => !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(KeyServerUrl, ""));

        /// <summary>Token secreto dado pelo servidor: permite reconectar e voltar ao mesmo lugar na sala.</summary>
        public static string OnlineToken
        {
            get => PlayerPrefs.GetString(KeyOnlineToken, "");
            set { PlayerPrefs.SetString(KeyOnlineToken, value ?? ""); PlayerPrefs.Save(); }
        }

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
