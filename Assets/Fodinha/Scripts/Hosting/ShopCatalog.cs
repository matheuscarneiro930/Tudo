using System;
using System.Collections.Generic;

namespace Fodinha.Hosting
{
    public enum ShopCategory { Table, Deck }

    /// <summary>Item da loja de moedas (fictícias). O desenho de cada item fica no app; aqui só o que o servidor precisa.</summary>
    public sealed class ShopItem
    {
        public readonly string Id;
        public readonly ShopCategory Category;
        public readonly string Name;
        public readonly int Price;

        public ShopItem(string id, ShopCategory category, string name, int price)
        {
            Id = id;
            Category = category;
            Name = name;
            Price = price;
        }

        public bool IsFree => Price <= 0;
    }

    /// <summary>
    /// Catálogo da loja: mesas e baralhos comprados com moedas. Os itens grátis são de todo mundo.
    /// Só o servidor decide compras (confere o saldo); o app apenas mostra e pede.
    /// </summary>
    public static class ShopCatalog
    {
        public const string DefaultTable = "mesa_verde";
        public const string DefaultDeck = "baralho_vinho";

        public static readonly IReadOnlyList<ShopItem> Items = new List<ShopItem>
        {
            new ShopItem(DefaultTable, ShopCategory.Table, "Feltro verde", 0),
            new ShopItem("mesa_azul", ShopCategory.Table, "Feltro azul", 300),
            new ShopItem("mesa_vinho", ShopCategory.Table, "Vinho", 500),
            new ShopItem("mesa_petroleo", ShopCategory.Table, "Azul-petróleo", 800),
            new ShopItem("mesa_roxa", ShopCategory.Table, "Roxa", 1000),
            new ShopItem("mesa_preta", ShopCategory.Table, "Preta e ouro", 1500),
            new ShopItem("mesa_cassino", ShopCategory.Table, "Cassino real", 3000),

            new ShopItem(DefaultDeck, ShopCategory.Deck, "Clássico vinho", 0),
            new ShopItem("baralho_azul", ShopCategory.Deck, "Azul royal", 300),
            new ShopItem("baralho_verde", ShopCategory.Deck, "Listras verdes", 300),
            new ShopItem("baralho_rosa", ShopCategory.Deck, "Bolinhas rosa", 600),
            new ShopItem("baralho_preto", ShopCategory.Deck, "Preto", 800),
            new ShopItem("baralho_roxo", ShopCategory.Deck, "Xadrez roxo", 1000),
            new ShopItem("baralho_dourado", ShopCategory.Deck, "Dourado", 2000),
        };

        private static readonly Dictionary<string, ShopItem> byId = BuildIndex();

        private static Dictionary<string, ShopItem> BuildIndex()
        {
            var d = new Dictionary<string, ShopItem>(StringComparer.Ordinal);
            foreach (var item in Items) d.Add(item.Id, item);
            return d;
        }

        public static ShopItem Find(string id) => id != null && byId.TryGetValue(id, out var item) ? item : null;

        public static string DefaultFor(ShopCategory category) => category == ShopCategory.Table ? DefaultTable : DefaultDeck;

        public static List<ShopItem> Of(ShopCategory category)
        {
            var list = new List<ShopItem>();
            foreach (var item in Items) if (item.Category == category) list.Add(item);
            return list;
        }

        /// <summary>O jogador tem o item? (grátis = todos têm).</summary>
        public static bool Owns(ICollection<string> owned, string id)
        {
            var item = Find(id);
            if (item == null) return false;
            return item.IsFree || (owned != null && owned.Contains(id));
        }

        /// <summary>Item equipado válido: se não existir, for de outra categoria ou não for dele, volta ao padrão.</summary>
        public static string ValidEquipped(ShopCategory category, string id, ICollection<string> owned)
        {
            var item = Find(id);
            return item != null && item.Category == category && Owns(owned, id) ? id : DefaultFor(category);
        }
    }
}
