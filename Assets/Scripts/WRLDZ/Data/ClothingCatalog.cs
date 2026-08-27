using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    public enum ClothingSlot
    {
        Hat = 0,
        Facial = 1,
        Shirt = 2,
        Hands = 3,
        Bottoms = 4,
        Shoes = 5
    }

    /// <summary>Static clothing catalog. IDs persist on the account; stats live here.</summary>
    public sealed class ClothingItem
    {
        public string Id;
        public ClothingSlot Slot;
        public string Name;
        public string Blurb;
        /// <summary>Pockets this garment contributes before tailor extras. Shirt/bottoms only.</summary>
        public int BasePockets;
        /// <summary>Extra pockets the tailor can sew onto this piece (0 = cannot sew).</summary>
        public int MaxSewn;
        public int PriceDigi;
        public bool StarterOwned;
        /// <summary>Hidden "none" slot — no sprite, no pockets.</summary>
        public bool EmptySlot;
        public Color Fallback = Color.white;

        public bool CanHoldPockets => MaxSewn > 0 || BasePockets > 0;

        public string ArtPath => EmptySlot ? null : $"WRLDZ/Avatar/clothes/{Id}.png";
    }

    [Serializable]
    public class ClothingSewEntry
    {
        public string itemId;
        public int extraPockets;
    }

    [Serializable]
    public class ClothingWardrobe
    {
        public string[] ownedIds = Array.Empty<string>();
        public ClothingSewEntry[] sewn = Array.Empty<ClothingSewEntry>();

        public void EnsureValid()
        {
            ownedIds ??= Array.Empty<string>();
            sewn ??= Array.Empty<ClothingSewEntry>();
        }

        public bool Owns(string id)
        {
            if (string.IsNullOrEmpty(id) || ownedIds == null) return false;
            for (var i = 0; i < ownedIds.Length; i++)
                if (ownedIds[i] == id) return true;
            return false;
        }

        public int SewnExtra(string id)
        {
            if (string.IsNullOrEmpty(id) || sewn == null) return 0;
            for (var i = 0; i < sewn.Length; i++)
            {
                var e = sewn[i];
                if (e != null && e.itemId == id)
                    return Mathf.Max(0, e.extraPockets);
            }

            return 0;
        }

        public void SetSewnExtra(string id, int extra)
        {
            extra = Mathf.Max(0, extra);
            sewn ??= Array.Empty<ClothingSewEntry>();
            for (var i = 0; i < sewn.Length; i++)
            {
                if (sewn[i] == null || sewn[i].itemId != id) continue;
                sewn[i].extraPockets = extra;
                return;
            }

            var next = new ClothingSewEntry[sewn.Length + 1];
            Array.Copy(sewn, next, sewn.Length);
            next[sewn.Length] = new ClothingSewEntry { itemId = id, extraPockets = extra };
            sewn = next;
        }

        public void Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || Owns(id)) return;
            var next = new string[ownedIds.Length + 1];
            Array.Copy(ownedIds, next, ownedIds.Length);
            next[ownedIds.Length] = id;
            ownedIds = next;
        }
    }

    public static class ClothingCatalog
    {
        public const string DefaultHat = "hat_none";
        public const string DefaultFacial = "face_none";
        public const string DefaultShirt = "shirt_plain";
        public const string DefaultHands = "hands_none";
        public const string DefaultBottoms = "bottoms_plain";
        public const string DefaultShoes = "shoes_plain";

        public const string BodyArtPath = "WRLDZ/Avatar/clothes/body_full.png";
        public const int MaxOutfitPockets = 6;
        public const int PriceSewnPocketDigi = 500;

        public static readonly ClothingItem[] All =
        {
            // Hats
            N("hat_none", ClothingSlot.Hat, "No hat", "Bare head.", 0, 0, 0, true, true, new Color(0.85f, 0.85f, 0.88f)),
            N("hat_cap", ClothingSlot.Hat, "Street cap", "Soft brim cap.", 0, 0, 280, false, false, new Color(0.18f, 0.22f, 0.38f)),
            N("hat_beanie", ClothingSlot.Hat, "Night beanie", "Knit winter beanie.", 0, 0, 320, false, false, new Color(0.22f, 0.45f, 0.72f)),

            // Facial
            N("face_none", ClothingSlot.Facial, "No facial", "Clear face.", 0, 0, 0, true, true, Color.white),
            N("face_goggles", ClothingSlot.Facial, "Duel goggles", "Tinted riding goggles.", 0, 0, 360, false, false, new Color(0.15f, 0.75f, 0.95f)),
            N("face_visor", ClothingSlot.Facial, "Holo visor", "Slim gold visor.", 0, 0, 420, false, false, new Color(1f, 0.82f, 0.28f)),

            // Shirts — pockets live here
            N("shirt_plain", ClothingSlot.Shirt, "Plain tee", "Default shirt. No pockets.", 0, 2, 0, true, false, new Color(0.82f, 0.84f, 0.88f)),
            N("shirt_hoodie", ClothingSlot.Shirt, "Path hoodie", "Kangaroo pocket plus a sleeve pocket.", 2, 2, 700, false, false, new Color(0.18f, 0.42f, 0.78f)),
            N("shirt_cargo", ClothingSlot.Shirt, "Cargo jacket", "Three deck-box pockets stitched in.", 3, 2, 1200, false, false, new Color(0.42f, 0.32f, 0.18f)),
            N("shirt_duelcoat", ClothingSlot.Shirt, "Duel coat", "Academy coat with four deep pockets.", 4, 2, 2000, false, false, new Color(0.12f, 0.14f, 0.28f)),

            // Hands
            N("hands_none", ClothingSlot.Hands, "Bare hands", "No gloves.", 0, 0, 0, true, true, Color.white),
            N("hands_gloves", ClothingSlot.Hands, "Fingerless gloves", "Street fingerless pair.", 0, 0, 240, false, false, new Color(0.16f, 0.16f, 0.18f)),
            N("hands_gauntlets", ClothingSlot.Hands, "Disk gauntlets", "Hard-knuckle duel gloves.", 0, 0, 400, false, false, new Color(0.55f, 0.55f, 0.62f)),

            // Bottoms — extra pockets
            N("bottoms_plain", ClothingSlot.Bottoms, "Plain pants", "Default pants. One hip pocket for a single deck box.", 1, 2, 0, true, false, new Color(0.22f, 0.24f, 0.32f)),
            N("bottoms_duel", ClothingSlot.Bottoms, "Duel slacks", "One cargo pocket on the thigh.", 1, 2, 600, false, false, new Color(0.14f, 0.16f, 0.28f)),
            N("bottoms_cargo", ClothingSlot.Bottoms, "Cargo pants", "Two deep thigh pockets for deck boxes.", 2, 2, 900, false, false, new Color(0.36f, 0.30f, 0.16f)),

            // Shoes
            N("shoes_plain", ClothingSlot.Shoes, "Street shoes", "Default sneakers.", 0, 0, 0, true, false, new Color(0.90f, 0.90f, 0.92f)),
            N("shoes_kicks", ClothingSlot.Shoes, "Neon kicks", "Cyan-trimmed running shoes.", 0, 0, 340, false, false, new Color(0.20f, 0.85f, 1f)),
            N("shoes_boots", ClothingSlot.Shoes, "Path boots", "Heavy travel boots.", 0, 0, 380, false, false, new Color(0.28f, 0.18f, 0.12f)),
        };

        static ClothingItem N(string id, ClothingSlot slot, string name, string blurb,
            int pockets, int maxSewn, int price, bool starter, bool empty, Color fallback) =>
            new()
            {
                Id = id,
                Slot = slot,
                Name = name,
                Blurb = blurb,
                BasePockets = pockets,
                MaxSewn = maxSewn,
                PriceDigi = price,
                StarterOwned = starter,
                EmptySlot = empty,
                Fallback = fallback
            };

        public static ClothingItem Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return null;
        }

        public static string DefaultId(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => DefaultHat,
            ClothingSlot.Facial => DefaultFacial,
            ClothingSlot.Shirt => DefaultShirt,
            ClothingSlot.Hands => DefaultHands,
            ClothingSlot.Bottoms => DefaultBottoms,
            ClothingSlot.Shoes => DefaultShoes,
            _ => DefaultHat
        };

        public static IEnumerable<ClothingItem> InSlot(ClothingSlot slot)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Slot == slot)
                    yield return All[i];
        }

        public static List<ClothingItem> OwnedInSlot(ClothingWardrobe wardrobe, ClothingSlot slot)
        {
            var list = new List<ClothingItem>();
            for (var i = 0; i < All.Length; i++)
            {
                var it = All[i];
                if (it.Slot != slot) continue;
                if (it.StarterOwned || (wardrobe != null && wardrobe.Owns(it.Id)))
                    list.Add(it);
            }

            if (list.Count == 0)
            {
                var fallback = Get(DefaultId(slot));
                if (fallback != null) list.Add(fallback);
            }

            return list;
        }

        public static int PocketsOnItem(ClothingItem item, ClothingWardrobe wardrobe)
        {
            if (item == null) return 0;
            var extra = wardrobe != null ? wardrobe.SewnExtra(item.Id) : 0;
            extra = Mathf.Clamp(extra, 0, Mathf.Max(0, item.MaxSewn));
            return Mathf.Max(0, item.BasePockets + extra);
        }

        public static int ComputeOutfitPockets(AvatarAppearance look, ClothingWardrobe wardrobe)
        {
            look?.EnsureClothingDefaults();
            var shirt = Get(look != null ? look.shirtId : DefaultShirt);
            var bottoms = Get(look != null ? look.bottomsId : DefaultBottoms);
            var n = PocketsOnItem(shirt, wardrobe) + PocketsOnItem(bottoms, wardrobe);
            return Mathf.Clamp(n, 1, MaxOutfitPockets);
        }

        public static string SlotLabel(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => "Hat",
            ClothingSlot.Facial => "Facial",
            ClothingSlot.Shirt => "Shirt",
            ClothingSlot.Hands => "Hands",
            ClothingSlot.Bottoms => "Bottoms",
            ClothingSlot.Shoes => "Shoes",
            _ => "Slot"
        };

        public static void GrantStarter(ClothingWardrobe wardrobe)
        {
            wardrobe ??= new ClothingWardrobe();
            wardrobe.EnsureValid();
            for (var i = 0; i < All.Length; i++)
            {
                if (All[i].StarterOwned)
                    wardrobe.Grant(All[i].Id);
            }
        }

        public static void GrantAll(ClothingWardrobe wardrobe)
        {
            wardrobe ??= new ClothingWardrobe();
            wardrobe.EnsureValid();
            for (var i = 0; i < All.Length; i++)
            {
                wardrobe.Grant(All[i].Id);
                if (All[i].MaxSewn > 0)
                    wardrobe.SetSewnExtra(All[i].Id, All[i].MaxSewn);
            }
        }
    }
}
