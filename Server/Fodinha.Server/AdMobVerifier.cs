using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fodinha.Server
{
    /// <summary>
    /// Verificação no servidor (SSV) do vídeo premiado do AdMob: quando alguém assiste o vídeo até o fim, o GOOGLE
    /// chama o nosso servidor com os dados assinados digitalmente. Só esse aviso assinado prova que o vídeo foi visto;
    /// o pedido do app sozinho pode ser forjado por um app modificado.
    /// https://developers.google.com/admob/android/ssv
    /// </summary>
    public sealed class AdMobVerifier
    {
        public const string KeysUrl = "https://www.gstatic.com/admob/reward/verifier-keys.json";

        /// <summary>Aviso aceito só se for recente (evita reaproveitar avisos antigos).</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

        private readonly Func<CancellationToken, Task<string>> fetchKeys;
        private readonly Func<DateTimeOffset> clock;
        private readonly ConcurrentDictionary<string, DateTimeOffset> seenTransactions = new ConcurrentDictionary<string, DateTimeOffset>();
        private readonly SemaphoreSlim keysLock = new SemaphoreSlim(1, 1);
        private Dictionary<long, ECDsa> keys = new Dictionary<long, ECDsa>();
        private DateTimeOffset keysFetchedAt = DateTimeOffset.MinValue;

        public AdMobVerifier(Func<CancellationToken, Task<string>> fetchKeys = null, Func<DateTimeOffset> clock = null)
        {
            this.fetchKeys = fetchKeys ?? DefaultFetch;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private static Task<string> DefaultFetch(CancellationToken ct) => http.GetStringAsync(KeysUrl, ct);

        public sealed class Result
        {
            public bool Valid;
            public string UserId;
            public string TransactionId;
            public string Reason;
        }

        /// <summary>
        /// Confere o aviso. <paramref name="rawQuery"/> é a parte depois do "?" EXATAMENTE como chegou
        /// (a assinatura vale para o texto original, na ordem original).
        /// </summary>
        public async Task<Result> VerifyAsync(string rawQuery, CancellationToken ct = default)
        {
            var fail = new Result { Valid = false };
            if (string.IsNullOrEmpty(rawQuery) || rawQuery.Length > 4096) { fail.Reason = "vazio"; return fail; }
            rawQuery = rawQuery.TrimStart('?');

            int sigAt = rawQuery.IndexOf("&signature=", StringComparison.Ordinal);
            if (sigAt <= 0) { fail.Reason = "sem assinatura"; return fail; }
            string content = rawQuery.Substring(0, sigAt);
            var all = Parse(rawQuery);
            if (!all.TryGetValue("signature", out var signature) || !all.TryGetValue("key_id", out var keyIdText) ||
                !long.TryParse(keyIdText, out long keyId))
            {
                fail.Reason = "campos faltando";
                return fail;
            }

            var key = await KeyAsync(keyId, ct);
            if (key == null) { fail.Reason = "chave desconhecida"; return fail; }

            byte[] sig;
            try { sig = FromBase64Url(signature); }
            catch (FormatException) { fail.Reason = "assinatura malformada"; return fail; }
            bool ok;
            try { ok = key.VerifyData(Encoding.UTF8.GetBytes(content), sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence); }
            catch (CryptographicException) { ok = false; }
            if (!ok) { fail.Reason = "assinatura inválida"; return fail; }

            // Recente e nunca usado antes.
            if (!all.TryGetValue("timestamp", out var tsText) || !long.TryParse(tsText, out long ms))
            {
                fail.Reason = "sem horário";
                return fail;
            }
            var when = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            var now = clock();
            if (now - when > MaxAge || when - now > TimeSpan.FromMinutes(10)) { fail.Reason = "aviso velho"; return fail; }

            all.TryGetValue("transaction_id", out var tx);
            if (string.IsNullOrEmpty(tx) || !seenTransactions.TryAdd(tx, now)) { fail.Reason = "repetido"; return fail; }
            Forget(now);

            all.TryGetValue("user_id", out var user);
            return new Result { Valid = true, UserId = user ?? "", TransactionId = tx };
        }

        private void Forget(DateTimeOffset now)
        {
            if (seenTransactions.Count < 1000) return;
            foreach (var kv in seenTransactions)
                if (now - kv.Value > MaxAge + MaxAge) seenTransactions.TryRemove(kv.Key, out _);
        }

        private async Task<ECDsa> KeyAsync(long keyId, CancellationToken ct)
        {
            if (keys.TryGetValue(keyId, out var k) && clock() - keysFetchedAt < TimeSpan.FromHours(24)) return k;
            // Chave nova (o Google troca de tempos em tempos): busca de novo, no máximo a cada 5 minutos.
            if (clock() - keysFetchedAt < TimeSpan.FromMinutes(5)) return keys.TryGetValue(keyId, out k) ? k : null;
            await keysLock.WaitAsync(ct);
            try
            {
                if (clock() - keysFetchedAt >= TimeSpan.FromMinutes(5))
                {
                    keysFetchedAt = clock();
                    keys = ParseKeys(await fetchKeys(ct));
                }
            }
            catch (Exception) { /* sem rede: tenta de novo mais tarde */ }
            finally { keysLock.Release(); }
            return keys.TryGetValue(keyId, out k) ? k : null;
        }

        /// <summary>{"keys":[{"keyId":123,"base64":"MFkw..."}]} → chaves públicas.</summary>
        public static Dictionary<long, ECDsa> ParseKeys(string json)
        {
            var result = new Dictionary<long, ECDsa>();
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.GetProperty("keys").EnumerateArray())
            {
                long id = item.GetProperty("keyId").GetInt64();
                var der = Convert.FromBase64String(item.GetProperty("base64").GetString());
                var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(der, out _);
                result[id] = ec;
            }
            return result;
        }

        private static Dictionary<string, string> Parse(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                d[Uri.UnescapeDataString(part.Substring(0, eq))] = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
            }
            return d;
        }

        public static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }
}
