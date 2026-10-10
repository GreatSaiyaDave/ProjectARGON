using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The one Battle City deck pack: exactly the cards left in the Main Deck, one layer per card, each the
    /// same size and back as a card played on the disk (<see cref="ArZoneLayout.CardScaleOnDisk"/> ×
    /// <see cref="ArAnimePresentation.CardAspectY"/>, true card thickness at that scale). The stack lies in
    /// the hub's deck housing — the counter box from the round hub's edge (x ≈ −0.06) to the body end
    /// (x ≈ −0.13), 62 mm deep — long side along the housing, under the DECK FLAP.
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
        /// <summary>Card long side, along the housing (mesh X): a disk card's height (46 mm × 1.45).</summary>
        public const float Width = ArZoneLayout.CardScaleOnDisk * ArAnimePresentation.CardAspectY;

        /// <summary>Card short side, across the housing (mesh Z): a disk card's width.</summary>
        public const float Length = ArZoneLayout.CardScaleOnDisk;

        /// <summary>Legacy alias of <see cref="Length"/> — the card's short side (mesh Z).</summary>
        public const float Height = Length;

        /// <summary>
        /// One card's thickness (stack axis = +Y): a real 0.305 mm card at the disk's card scale (46 / 59 mm), so
        /// the stack is as thick as the deck — 40 cards ≈ 9.5 mm — and visibly thins with every draw.
        /// </summary>
        public const float Thickness = 0.305e-3f * ArZoneLayout.CardScaleOnDisk / 0.059f;

        /// <summary>
        /// Room inside the magazine, zone-local. The zone sits on <see cref="WellOrigin"/>,
        /// so this is the tallest stack that stays under <see cref="WellCeilingY"/>.
        /// </summary>
        public static float UsableHeight => WellCeilingY - WellOrigin.y - 0.0002f;

        /// <summary>Full magazine. Live piles use <see cref="StackHeight"/>.</summary>
        public static float PackHeight => UsableHeight;

        /// <summary>Compressed so <paramref name="count"/> layers fit under the flap.</summary>
        public static float StackHeight(int count)
        {
            if (count <= 0) return 0f;
            return Mathf.Min(UsableHeight, count * Thickness);
        }

        public static float CardThickness(int count)
        {
            if (count <= 0) return Thickness;
            return StackHeight(count) / count;
        }

        /// <summary>
        /// Underside of the v37 DECK FLAP. Pack top must stay at or below this
        /// so the brick does not punch through the hub / LP housing.
        /// </summary>
        public const float WellCeilingY = 0.0168f;

        /// <summary>Kept so shuffle / older callers compile. Pack is Y-stacked, not Z-stacked.</summary>
        public static float PackDepth => PackHeight;

        public const float FrontBias = 0.50f;

        /// <summary>
        /// Floor-centre of the deck housing (mesh-local, same space as
        /// <see cref="ArPlaymatLayout.DiskMonsterSurface"/>). Y is the v37 DECK SPRING HOLDER floor; X/Z centre
        /// the card in the counter box (x −0.0642…−0.1309, z 0.205…0.251 inside x −0.060…−0.130, z 0.194…0.256).
        /// </summary>
        public static readonly Vector3 WellOrigin = new(-0.0975f, 0.0016f, 0.2280f);

        /// <summary>i = 0 is the top / draw card. One distinct height per remaining card.</summary>
        public static Vector3 RestLocal(int i, int count)
        {
            count = Mathf.Max(count, 1);
            i = Mathf.Clamp(i, 0, count - 1);
            var t = CardThickness(count);
            var y = StackHeight(count) - (i + 0.5f) * t;
            return new Vector3(0f, y, 0f);
        }

        /// <summary>One cardboard layer per card still in the Main Deck. Zero leaves the magazine empty.</summary>
        public static Transform Build(Transform well, int layer, int count, out List<Transform> looseCards)
        {
            looseCards = new List<Transform>();
            var root = new GameObject("DeckWellPack").transform;
            root.SetParent(well, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            if (count <= 0) return root;

            // Shared across every rebuild and both disks: SyncDeckCount rebuilds the pack on each draw / search /
            // mill, and building two fresh Materials per rebuild leaked them for the whole session.
            var backMat = SharedBackMaterial();
            var edgeMat = SharedEdgeMaterial();

            var thick = CardThickness(count);
            // Back quad is parented to the card (scale 1), not to a non-uniform cube. Quad X = the card's short side
            // (mesh Z), quad Y = its long side (mesh X), so the portrait card-back art is not stretched.
            var face = new Vector3(Length * 0.96f, Width * 0.96f, 1f);
            for (var i = 0; i < count; i++)
            {
                var card = new GameObject("DeckWellCard_" + i).transform;
                card.SetParent(root, false);
                card.localPosition = RestLocal(i, count);
                card.localRotation = Quaternion.identity;
                SetLayer(card.gameObject, layer);

                MakeLying(card, "Body", Width, thick * 0.92f, Length,
                    Vector3.zero, edgeMat, layer);
                MakeBack(card, "Back", backMat, layer,
                    localPos: new Vector3(0f, thick * 0.5f, 0f),
                    localScale: face);

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
            // −90° X lays the quad flat; 90° Z first turns its long (Y) side onto mesh X, along the housing.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 90f);
            go.transform.localScale = localScale;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            SetLayer(go, layer);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Material _sharedBack;
        static bool _sharedBackIsFallback;
        static Material _sharedEdge;

        /// <summary>
        /// Cached card-back material. A flat-colour fallback (card-back art not loadable yet) is retried on the next
        /// build so the real art takes over as soon as it resolves; the replaced fallback is left to the cards that
        /// still use it (they are rebuilt on the next count change).
        /// </summary>
        static Material SharedBackMaterial()
        {
            if (_sharedBack != null && !_sharedBackIsFallback) return _sharedBack;
            var m = ResolveBackMaterial(out var isFallback);
            if (_sharedBack == null || !isFallback)
            {
                _sharedBack = m;
                _sharedBackIsFallback = isFallback;
            }
            else if (m != _sharedBack)
                ArObjectUtil.Destroy(m);

            return _sharedBack;
        }

        static Material SharedEdgeMaterial()
        {
            if (_sharedEdge != null) return _sharedEdge;
            _sharedEdge = ArAnimePresentation.MakeSolid(new Color(0.78f, 0.62f, 0.38f, 1f), "DeckPaperEdge");
            SeatReadable(_sharedEdge);
            return _sharedEdge;
        }

        /// <summary>
        /// Same card back as the hand and set cards (<see cref="StreamingSprite.CardBack"/>).
        /// </summary>
        static Material ResolveBackMaterial(out bool isFallback)
        {
            isFallback = false;
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
                        SeatReadable(m);
                        return m;
                    }
                }
            }
            catch { /* fallback */ }

            isFallback = true;
            var shared = ArFieldMaterials.Get(new Color(0.18f, 0.28f, 0.58f, 1f));
            var fallback = new Material(shared) { name = "DeckWellCardBack" };
            SeatReadable(fallback);
            return fallback;
        }

        /// <summary>
        /// Ghost disk is queue 3000 and does not write depth. Cardboard has to draw after it
        /// or the hub alpha-blends the pack away.
        /// </summary>
        static void SeatReadable(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetInt("_ZWrite", 1);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = 3100;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
