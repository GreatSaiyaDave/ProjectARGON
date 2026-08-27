using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The one Battle City deck pack. Seated in the hub magazine
    /// (Fusion v37 DECK FLAP / DECK SPRING HOLDER).
    ///
    /// Kit: benjohnson789 "Working Full Size Yu-Gi-Oh! Battle City Duel Disk"
    /// (Cults, v37). <c>deck spring holder.3mf</c> is a shallow spring tray
    /// ("just a spring attached by 2 screws"). Yugipedia V2 Deck Holder has a
    /// cover; inserting a deck pushes that cover back.
    ///
    /// Measured on BattleCityDuelDisk.obj (cm→m, same groups as the Fusion file):
    ///   DECK SPRING HOLDER  36.7 × 19.9 × 26.0 mm
    ///     x[-0.1009,-0.0643] y[0.0014,0.0213] z[0.2090,0.2350]
    ///   DECK FLAP (lid)     40.1 × 4.5 × 20.0 mm
    ///     y 0.0169…0.0215
    ///   COUNTER TOP roof    y 0.0215…0.0415 — pack must stay below this.
    ///
    /// The mesh already is a deck there. This pack covers that magazine so
    /// only one deck is visible. Cards lie in the tray (backs up), stacked +Y.
    /// Draw peels the top card.
    /// </summary>
    public static class ArDeckWellCards
    {
        /// <summary>Across the magazine (mesh +X). Inset inside the 36.7 mm holder.</summary>
        public const float Width = 0.033f;

        /// <summary>Toward the wearer (mesh +Z). Inset inside the 26.0 mm holder.</summary>
        public const float Length = 0.024f;

        /// <summary>Legacy alias — lying pack uses <see cref="Length"/> as the card's long side.</summary>
        public const float Height = Length;

        /// <summary>One card's thickness (stack axis = +Y).</summary>
        public const float Thickness = 0.00095f;

        public const int LooseCards = 5;
        public const float BrickCards = 10f;

        public static float BrickHeight => Thickness * BrickCards;
        public static float PackHeight => Thickness * (BrickCards + LooseCards);

        /// <summary>
        /// Underside of the v37 DECK FLAP. Pack top must stay at or below this
        /// so the brick does not punch through the hub / LP housing.
        /// </summary>
        public const float WellCeilingY = 0.0168f;

        /// <summary>Kept so shuffle / older callers compile. Pack is Y-stacked, not Z-stacked.</summary>
        public static float PackDepth => PackHeight;

        public const float FrontBias = 0.50f;

        /// <summary>
        /// Floor-center of the v37 DECK SPRING HOLDER (Yugipedia V2 Deck Holder).
        /// Mesh-local, same space as <see cref="ArPlaymatLayout.DiskMonsterSurface"/>.
        /// Y is the holder floor so the stack fills the 20 mm well instead of
        /// floating up through the COUNTER TOP.
        /// </summary>
        public static readonly Vector3 WellOrigin = new(-0.0826f, 0.0016f, 0.2220f);

        /// <summary>i = 0 is the top / draw card.</summary>
        public static Vector3 RestLocal(int i)
        {
            i = Mathf.Clamp(i, 0, LooseCards - 1);
            var y = PackHeight - (i + 0.5f) * Thickness;
            return new Vector3(0f, y, 0f);
        }

        public static Transform Build(Transform well, int layer, out List<Transform> looseCards)
        {
            looseCards = new List<Transform>();
            var root = new GameObject("DeckWellPack").transform;
            root.SetParent(well, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            var backMat = ResolveBackMaterial();
            var edgeMat = ArAnimePresentation.MakeSolid(
                new Color(0.78f, 0.62f, 0.38f, 1f), "DeckPaperEdge");

            // Solid lower brick covers the sculpted brown block.
            // Card-back is parented to the pack root (scale 1) — parenting a
            // rotated quad to the non-uniform brick cube shears it out of the well.
            MakeLying(root, "DeckBrick",
                Width, BrickHeight, Length,
                new Vector3(0f, BrickHeight * 0.5f, 0f),
                edgeMat, layer);
            MakeBack(root, "BrickBack", backMat, layer,
                localPos: new Vector3(0f, BrickHeight + 0.00012f, 0f),
                localScale: new Vector3(Width * 0.96f, Length * 0.96f, 1f));

            for (var i = 0; i < LooseCards; i++)
            {
                var card = new GameObject("DeckWellCard_" + i).transform;
                card.SetParent(root, false);
                card.localPosition = RestLocal(i);
                card.localRotation = Quaternion.Euler(0f, (i - LooseCards * 0.5f) * 0.35f, 0f);
                SetLayer(card.gameObject, layer);

                MakeLying(card, "Body", Width, Thickness * 0.92f, Length,
                    Vector3.zero, edgeMat, layer);
                MakeBack(card, "Back", backMat, layer,
                    localPos: new Vector3(0f, Thickness * 0.5f, 0f),
                    localScale: new Vector3(Width * 0.96f, Length * 0.96f, 1f));

                looseCards.Add(card);
            }

            return root;
        }

        static Transform MakeLying(Transform parent, string name,
            float w, float h, float d, Vector3 localPos, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(w, h, d);
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            SetLayer(go, layer);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Card-back quad on the +Y face (looking down into the magazine).</summary>
        static void MakeBack(Transform parent, string name, Material mat, int layer,
            Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = localScale;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            SetLayer(go, layer);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Material ResolveBackMaterial()
        {
            try
            {
                var spr = StreamingSprite.CardBack()
                          ?? ImagineAssets.CardBackWrldz()
                          ?? YgoCardFrames.CardBack();
                var tex = spr != null ? spr.texture : null;
                if (tex != null)
                {
                    var m = ArFieldMaterials.CreateUnlitTextureDoubleSided(tex);
                    if (m != null)
                    {
                        m.name = "DeckWellCardBack";
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                        return m;
                    }
                }
            }
            catch { /* fallback */ }

            return ArFieldMaterials.Get(new Color(0.18f, 0.28f, 0.58f, 1f));
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
