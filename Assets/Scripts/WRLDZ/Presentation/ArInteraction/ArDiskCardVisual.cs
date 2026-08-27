using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Physical TCG card on a duel-disk zone (anime 2D art on a 3D card body).
    /// Face-up → card art. Face-down → card back on the physical disk
    /// (private knowledge is via inspect UI, not by leaking set faces on the plate).
    /// </summary>
    public class ArDiskCardVisual : MonoBehaviour
    {
        public CardInstance Card;
        public int InstanceId;
        public bool FaceUp;
        public bool Defense;

        /// <summary>
        /// Legacy: was used to reveal own face-down sets on the plate. Physical disk
        /// always shows backs for sets; inspect UI remains the private view.
        /// </summary>
        public bool ControllerView = true;

        /// <summary>Monster vs S/T for set landscape orientation when zone-less.</summary>
        public bool IsMonsterCard = true;

        /// <summary>True while a seat / toaster / flip sequence is driving this transform.</summary>
        public bool MotionBusy;

        ArPhysicalCardBuilder.CardParts _parts;
        int _layer;
        Color _accent = new Color(0.35f, 0.85f, 1f, 1f);
        CardDatabase _db;

        public static ArDiskCardVisual Create(Transform zone, CardInstance card, CardDatabase db,
            int layer, Color accent, bool isMonster = true, bool controllerView = true)
        {
            var go = new GameObject(card != null ? $"DiskCard_{card.Name}" : "DiskCard");
            go.transform.SetParent(zone, false);
            go.layer = layer;
            var v = go.AddComponent<ArDiskCardVisual>();
            v._layer = layer;
            v.IsMonsterCard = isMonster;
            v.ControllerView = controllerView;
            v._accent = accent;
            v._db = db;
            v.Build(card, db, accent);
            return v;
        }

        void Build(CardInstance card, CardDatabase db, Color accent)
        {
            Card = card;
            InstanceId = card?.InstanceId ?? 0;
            FaceUp = card?.FaceUp ?? false;
            Defense = card != null && card.Position == BattlePosition.Defense;
            _db = db;

            var backTex = ResolveCardBackTexture();
            Texture faceTex = backTex;
            if (ShouldShowArtToViewer() && card != null && db != null)
            {
                var art = db.GetArt(card.CardId);
                if (art != null && art.texture != null)
                    faceTex = art.texture;
            }

            _parts = ArPhysicalCardBuilder.Build(transform, _layer, faceTex, backTex, accent);
            if (_parts.Root != null)
            {
                _parts.Root.localPosition = Vector3.zero;
                _parts.Root.localRotation = Quaternion.identity;
                _parts.Root.localScale = Vector3.one;
            }

            // Force every mesh onto the AR layer
            foreach (var t in GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = _layer;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null) continue;
                mr.enabled = true;
                // Disk ghost is queue 2440 / ZWrite off. Every disk card must draw
                // after it or the plate alpha-blends through the art (flicker).
                mr.sortingOrder = IsMonsterCard ? 12 : 10;
                // sharedMaterial — Renderer.material clones and Flip Summon would
                // keep drawing the set card-back copy.
                var mat = mr.sharedMaterial;
                if (mat != null)
                    mat.renderQueue = 2475;
                if (!IsMonsterCard)
                    ArFieldMaterials.ForceDoubleSided(mat);
                ArAnimePresentation.ConfigureHoloRenderer(mr);
            }

            // Rebind after renderer setup so later ApplyFace hits the drawn materials.
            if (_parts.FaceRenderer != null)
                _parts.FaceMaterial = _parts.FaceRenderer.sharedMaterial;
            if (_parts.BackRenderer != null)
                _parts.BackMaterial = _parts.BackRenderer.sharedMaterial;

            // S/T toaster: no glow rim (would read through the plate from above)
            if (!IsMonsterCard && _parts.RimRenderer != null)
                _parts.RimRenderer.enabled = false;

            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            // Monsters: full card. S/T: pick volume biased to the bottom lip that peeks out.
            if (IsMonsterCard)
            {
                col.size = new Vector3(1.05f, ArAnimePresentation.CardAspectY * 1.08f, 0.40f);
                col.center = Vector3.zero;
            }
            else
            {
                // After Rx(−102) the bottom lip is the only part in open air — pick that strip
                var peek = ArAnimePresentation.CardAspectY * ArZoneLayout.SlotBottomPeekFraction;
                col.size = new Vector3(1.2f, peek * 2.2f, 0.7f);
                col.center = new Vector3(0f, -ArAnimePresentation.CardAspectY * 0.5f + peek * 0.55f, 0f);
            }

            ApplyFace(db);
            ApplyPose();
        }

        public void Sync(CardInstance card, CardDatabase db)
        {
            if (card == null) return;
            var faceChanged = FaceUp != card.FaceUp;
            var defChanged = Defense != (card.Position == BattlePosition.Defense);
            var idChanged = InstanceId != card.InstanceId;
            var flippedUp = faceChanged && card.FaceUp && !FaceUp;
            Card = card;
            InstanceId = card.InstanceId;
            FaceUp = card.FaceUp;
            Defense = card.Position == BattlePosition.Defense;
            _db = db;
            // Face refresh + pose — callers may suppress pose during toaster sequences
            ApplyFace(db);
            if (MotionBusy) return;
            if (defChanged || faceChanged || idChanged)
                ApplyPose();
            // Monster flip-up gets a paced 3D flip; S/T uses toaster eject (field controller)
            if (flippedUp && IsMonsterCard)
                CardFlip3dAnimator.PlayIfNeeded(transform, true, db);
        }

        /// <summary>
        /// Physical disk presentation (what you see on the plate / in the toaster):
        /// · Face-up → art.
        /// · Face-down (monster or S/T) → card back. Private knowledge is inspect UI only.
        /// </summary>
        bool ShouldShowArtToViewer()
        {
            if (Card == null) return false;
            return Card.FaceUp;
        }

        public void ApplyFace(CardDatabase db)
        {
            if (_parts.FaceMaterial == null) return;
            if (db != null) _db = db;
            var backTex = ResolveCardBackTexture();
            if (ShouldShowArtToViewer() && Card != null && _db != null)
            {
                var art = _db.GetArt(Card.CardId);
                var tex = art != null && art.texture != null ? art.texture : Texture2D.whiteTexture;
                _parts.SetFaceTexture(tex, _accent);
                _parts.SetBackTexture(backTex);
            }
            else
            {
                // Face-down: both faces card back so the pad / toaster edge reads as a set
                _parts.SetFaceTexture(backTex, _accent);
                _parts.SetBackTexture(backTex);
            }
        }

        /// <summary>Reveal art mid-flip (toaster activate) without waiting for FaceUp flag.</summary>
        public void ForceShowArt()
        {
            if (_parts.FaceMaterial == null || Card == null || _db == null) return;
            var art = _db.GetArt(Card.CardId);
            var tex = art != null && art.texture != null ? art.texture : Texture2D.whiteTexture;
            _parts.SetFaceTexture(tex, _accent);
        }

        void ApplyPose()
        {
            var zone = GetComponentInParent<ArDiskZone>();
            if (zone != null)
            {
                var orient = !FaceUp
                    ? ArZoneOrientation.FaceDownSet
                    : Defense
                        ? ArZoneOrientation.FaceUpDefense
                        : ArZoneOrientation.FaceUpAttack;
                // Always recompute flush lock (parent scale may have changed this frame)
                ArZoneLayout.ApplyFlushLock(zone, transform, orient);
                return;
            }

            if (!FaceUp)
                transform.localRotation = Quaternion.Euler(-90f, 0f, IsMonsterLandscape() ? 90f : 0f);
            else if (Defense)
                transform.localRotation = Quaternion.Euler(-90f, 0f, 90f);
            else
                transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        bool IsMonsterLandscape()
        {
            var zone = GetComponentInParent<ArDiskZone>();
            return zone == null || zone.Kind == ArDuelZoneKind.Monster
                   || zone.Kind == ArDuelZoneKind.PendulumLeft
                   || zone.Kind == ArDuelZoneKind.PendulumRight;
        }

        static Texture ResolveCardBackTexture()
        {
            var tex = StreamingSprite.CardBackTexture();
            return tex != null ? tex : Texture2D.grayTexture;
        }
    }
}
