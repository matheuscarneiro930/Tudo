namespace Fodinha.Ads
{
    /// <summary>
    /// IDs do AdMob. Enquanto os IDs reais estiverem vazios, usa os IDs de TESTE oficiais do Google
    /// (anúncios de exemplo, liberados para desenvolvimento). Nunca clique nos seus próprios anúncios reais.
    /// </summary>
    public static class AdIds
    {
        // ---- IDs reais (preencher com os da conta AdMob do dono do jogo)
        public const string AndroidRewardedReal = "";
        public const string AndroidInterstitialReal = "";
        public const string IosRewardedReal = "";
        public const string IosInterstitialReal = "";

        // ---- IDs de teste oficiais do Google
        public const string AndroidAppTest = "ca-app-pub-3940256099942544~3347511713";
        public const string IosAppTest = "ca-app-pub-3940256099942544~1458002511";
        public const string AndroidRewardedTest = "ca-app-pub-3940256099942544/5224354917";
        public const string AndroidInterstitialTest = "ca-app-pub-3940256099942544/1033173712";
        public const string IosRewardedTest = "ca-app-pub-3940256099942544/1712485313";
        public const string IosInterstitialTest = "ca-app-pub-3940256099942544/4411468910";

#if UNITY_IOS
        public static string Rewarded => Pick(IosRewardedReal, IosRewardedTest);
        public static string Interstitial => Pick(IosInterstitialReal, IosInterstitialTest);
#else
        public static string Rewarded => Pick(AndroidRewardedReal, AndroidRewardedTest);
        public static string Interstitial => Pick(AndroidInterstitialReal, AndroidInterstitialTest);
#endif

        public static bool UsingTestAds => string.IsNullOrEmpty(AndroidRewardedReal);

        private static string Pick(string real, string test) => string.IsNullOrEmpty(real) ? test : real;
    }
}
