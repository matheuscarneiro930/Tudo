using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Aparência de cada item da loja (<see cref="Hosting.ShopCatalog"/>): cores das mesas e dos baralhos.
    /// Item desconhecido = visual padrão (feltro verde / clássico vinho).
    /// </summary>
    public static class CosmeticLooks
    {
        public struct TableLook
        {
            public Color Center, Edge, Rim;
            public bool ShinyRim;
        }

        public enum DeckPattern { Diamonds, Stripes, Dots, Checker }

        public struct DeckLook
        {
            public Color Back, PatternColor, Frame;
            public DeckPattern Pattern;
        }

        private static readonly Color Wood = Palette.Rim;
        private static readonly Color DarkWood = Palette.Hex("3E2210");
        private static readonly Color Gold = Palette.Hex("C9A13B");

        public static TableLook Table(string id)
        {
            switch (id)
            {
                case "mesa_azul": return T("2F7BC4", "16406E", Wood);
                case "mesa_vinho": return T("9A2A40", "561424", DarkWood);
                case "mesa_petroleo": return T("23807C", "0E4442", Wood);
                case "mesa_roxa": return T("7442BE", "36186A", DarkWood);
                case "mesa_preta": return T("3A3A3A", "141414", Gold, true);
                case "mesa_cassino": return T("17874A", "0A3F22", Gold, true);
                default: return new TableLook { Center = Palette.FeltCenter, Edge = Palette.FeltEdge, Rim = Wood };
            }
        }

        public static DeckLook Deck(string id)
        {
            switch (id)
            {
                case "baralho_azul": return D("1E4FA3", "3A6FD0", DeckPattern.Diamonds);
                case "baralho_verde": return D("1D6B3F", "2E8F57", DeckPattern.Stripes);
                case "baralho_rosa": return D("C2457A", "E57AA6", DeckPattern.Dots);
                case "baralho_preto": return D("1C1C1C", "3A3A3A", DeckPattern.Diamonds);
                case "baralho_roxo": return D("5A2A8A", "7E46B8", DeckPattern.Checker);
                case "baralho_dourado": return new DeckLook
                {
                    Back = Palette.Hex("7A5A10"), PatternColor = Palette.Hex("D4AF37"),
                    Frame = Palette.Hex("FFF3C4"), Pattern = DeckPattern.Diamonds
                };
                default: return new DeckLook
                {
                    Back = Palette.CardBack, PatternColor = Palette.CardBackPattern,
                    Frame = Palette.CardFace, Pattern = DeckPattern.Diamonds
                };
            }
        }

        /// <summary>O pixel (em pixels da textura) cai no desenho do padrão?</summary>
        public static bool InsidePattern(DeckPattern pattern, float px, float py)
        {
            switch (pattern)
            {
                case DeckPattern.Stripes:
                    return Mathf.Repeat((px + py) / 16f, 1f) < 0.38f;
                case DeckPattern.Dots:
                {
                    float u = Mathf.Repeat(px / 18f, 1f) - 0.5f, v = Mathf.Repeat(py / 18f, 1f) - 0.5f;
                    return u * u + v * v < 0.07f;
                }
                case DeckPattern.Checker:
                    return ((Mathf.FloorToInt(px / 14f) + Mathf.FloorToInt(py / 14f)) & 1) == 0;
                default:
                {
                    float u = (px + py) / 14f, v = (px - py) / 14f;
                    float fu = Mathf.Abs(u - Mathf.Round(u)), fv = Mathf.Abs(v - Mathf.Round(v));
                    return fu < 0.12f || fv < 0.12f;
                }
            }
        }

        private static TableLook T(string center, string edge, Color rim, bool shiny = false) =>
            new TableLook { Center = Palette.Hex(center), Edge = Palette.Hex(edge), Rim = rim, ShinyRim = shiny };

        private static DeckLook D(string back, string pattern, DeckPattern p) =>
            new DeckLook { Back = Palette.Hex(back), PatternColor = Palette.Hex(pattern), Frame = Palette.CardFace, Pattern = p };
    }
}
