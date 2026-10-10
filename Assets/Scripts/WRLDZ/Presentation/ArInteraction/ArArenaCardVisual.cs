using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Arena projection for a single zone occupant.
    ///
    /// Face-up monsters → upright artwork hologram (billboard).
    /// Face-up DEFENSE monsters (anime, Battle City) → the art stays upright but braced (crouched,
    /// leaning back, cool tint, shield glint toward the opponent) standing on the far long edge of its
    /// full CARD lying flat on the street turned sideways (landscape) — the "DefenseCardPlate" — so the
    /// whole card reads in front of it from the stage camera. Attack-position summons show the
    /// same card flat in PORTRAIT under the materialize for ~1.2 s, then it fades (owner, 2026-09-23).
    /// Face-down monsters / S/T (either side) → the CARD BACK lies flat on its zone (monsters sideways),
    /// Battle City style; the face stays hidden until activation (owner, 2026-09-23 / 2026-09-27: set
    /// cards must show in the arena, their face must not). A Flip Summon, battle flip or S/T activation
    /// plays its flip/rise from that card back; it never leaks private face art.
    ///
    /// Spell/Trap activation (anime): face-down set pops up, then the card
    /// travels toward its controller and leans so the face is readable.
    /// One-shots hover in that present pose then fade. Lingering S/T settle
    /// back over the zone. Field Spells are wrap walls, not this stand-up.
    ///
    /// Future: <see cref="PreferMeshWhenAvailable"/> swaps in dedicated meshes.
    /// </summary>
    public class ArArenaCardVisual : MonoBehaviour
    {
        /// <summary>How high face-down set cards float above the street (meters).</summary>
        public const float FaceDownHoverHeight = 0.12f;


        public int InstanceId;
        public int CardId;
        /// <summary>Live engine card — set on spawn/sync for AR tap → Attack menu.</summary>
        public CardInstance Card;
        public bool FaceUp;
        public bool Defense;
        public bool IsMonster;
        public bool PlayerSide;

        /// <summary>
        /// When true and a dedicated OBJ exists, prefer mesh over artwork billboard.
        /// Default false until model coverage is ready.
        /// </summary>
        public bool PreferMeshWhenAvailable;
        /// <summary>Spring-bone / capsule rig when the card has a per-shell GLB; null otherwise.</summary>
        public ShellRig ShellRigInstance => _shellRig;
        ShellRig _shellRig;

        /// <summary>
        /// Spell/Trap is playing its activate sequence (disk-turn wait, rise, present/hover, fade).
        /// False once a lingering card has settled over its zone — it is then an ordinary face-up
        /// S/T (destroy → shatter, relayout moves it).
        /// </summary>
        public bool SpellActivateSequenceActive =>
            _spellSeq != SpellSeqPhase.None && _spellSeq != SpellSeqPhase.Settled;

        /// <summary>
        /// Engine card still occupies this visual's zone. The hologram manager clears it when the zone
        /// empties; ghosts never had a zone. An unresolved activation keeps presenting while true.
        /// </summary>
        public bool StillInZone = true;

        /// <summary>Ghost: the engine card already left the field; this visual only plays the reveal.</summary>
        public bool IsGhost => _isGhost;

        /// <summary>True while fading out after activation → GY.</summary>
        public bool IsFadingOut => _spellSeq == SpellSeqPhase.Fading;

        /// <summary>True while Flip Summon owns pose (do not PulseReveal / billboard).</summary>
        public bool FlipSummoning => _flipSummoning;

        /// <summary>True while any flip sequence owns pose (Flip Summon or battle flip).</summary>
        public bool FlipAnimating => _flipSummoning || _battleFlipping;

        /// <summary>True while the in-zone battle flip (set card → Defense plate → braced monster) runs.</summary>
        public bool BattleFlipping => _battleFlipping;

        /// <summary>Renderers are on (false during the disk-seat hold, and before a Spell activated from the hand rises).</summary>
        public bool IsShown => _shown;

        MeshRenderer _faceMr;
        Material _faceMat;
        MeshRenderer _backMr;
        Material _backMat;
        Transform _body;
        Transform _ring;
        int _layer;
        bool _spawnAnimating;
        int _washVersion = -1;
        Vector3 _targetLocalPos;
        Quaternion _targetLocalRot;
        Vector3 _targetLocalScale = Vector3.one;
        Vector3 _spawnStartWorld;
        Vector3 _spawnStartScale;
        Quaternion _spawnStartLocalRot;
        float _spawnT;
        const float SpawnDuration = 0.55f;
        float _attackGlow;
        float _defendPulse;
        Transform _defendShield;
        MeshRenderer _defendMr;
        Transform _attackAura;
        MeshRenderer _attackMr;
        bool _flipSummoning;
        bool _pendingHoloFx;
        bool _battleFlipping;
        /// <summary>Face-down Defense monster is the current attack target (its sideways card back is up).</summary>
        bool _targetReveal;

        // ── Card plate (full card flat on the street) / braced Defense pose ──────────
        // Sizes are root-local (× LiveHoloScale). Long side ≤ 0.92 × MonsterColumnPitch so a landscape
        // Defense card never reaches the neighbouring column; short side = long × 59/86 (TCG card).
        /// <summary>Long side of the card plate: 0.92 × 1.20 = 1.104.</summary>
        public const float CardPlateLong = 0.92f * ArPlaymatLayout.MonsterColumnPitch;
        /// <summary>Short side of the card plate: 1.104 × 59/86 ≈ 0.757.</summary>
        public const float CardPlateShort = CardPlateLong * CardArtFocus.FullCardAspect;
        /// <summary>Plate height above the monster root (root sits at MonsterHoverY): clears the summon ring disc (top 0.022).</summary>
        public const float CardPlateY = 0.030f;
        const float CardPlateGlowY = 0.027f;
        /// <summary>Glow border per side: long 1.104 + 2 × 0.045 = 1.194 &lt; 1.20 pitch.</summary>
        const float CardPlateGlowMargin = 0.045f;
        const float CardPlateGlowFeather = 0.045f; // UV fraction of the soft edge
        /// <summary>Art base sits on the plate top in the braced pose.</summary>
        const float BracedArtBaseY = CardPlateY + 0.002f;
        /// <summary>Braced 2D art: height × 0.88 (1.335 → 1.175), lean back 6°, 20% cool accent tint.</summary>
        public const float DefenseCrouchY = 0.88f;
        public const float DefenseLeanDeg = 6f;
        const float DefenseTintBlend = 0.20f;
        const float PlateTurnSeconds = 0.35f;
        const float BraceSeconds = 0.30f;
        const float PlateFadeDelayOnRaise = 0.15f;
        const float ArtRiseSeconds = ArDiskMotion.BattleFlipArtRise;
        public const float SummonPlateRiseSeconds = 0.35f;
        public const float SummonPlateHoldSeconds = 1.2f;
        public const float SummonPlateFadeSeconds = 0.5f;
        // Shield glint (bracing pane toward the opponent), root units, posed parallel to the braced art
        const float GlintY = 0.60f;
        /// <summary>Pane distance from the art plane along its normal, toward the attacker's side (clears the wings' wrap-back 0.075).</summary>
        const float GlintOffset = 0.16f;
        const float GlintH = 0.95f;
        const float GlintCenterW = 0.62f;
        const float GlintWingW = 0.30f;
        const float GlintWingDeg = 30f;
        /// <summary>Plate face lifts the art base this much above the plate (no z-fight, feet on the card).</summary>
        const float ArtOnPlateClearance = 0.004f;

        /// <summary>Cubic ease-out tween on unscaled time; a new Go starts from the current value (never snaps).</summary>
        sealed class Tween
        {
            public float V, From, To, T0, Dur, Delay;
            public bool Active;
            public Tween(float v = 0f) { V = From = To = v; }
            public void Snap(float v) { V = From = To = v; Active = false; }

            public void Go(float to, float dur, float delay = 0f)
            {
                if (dur <= 0f && delay <= 0f) { Snap(to); return; }
                From = V; To = to; T0 = Time.unscaledTime; Dur = Mathf.Max(0.01f, dur); Delay = delay;
                Active = true;
            }

            public bool Tick()
            {
                if (!Active) return false;
                var u = Mathf.Clamp01((Time.unscaledTime - T0 - Delay) / Dur);
                var e = 1f - (1f - u) * (1f - u) * (1f - u);
                V = Mathf.Lerp(From, To, e);
                if (u >= 1f) { V = To; Active = false; }
                return true;
            }
        }

        readonly Tween _plateAlpha = new(0f);
        /// <summary>0 = portrait (Attack summon card), 1 = landscape (Defense).</summary>
        readonly Tween _plateTurn = new(0f);
        readonly Tween _plateRise = new(0f);
        /// <summary>0 = standing (Attack), 1 = braced (Defense).</summary>
        readonly Tween _brace = new(0f);
        /// <summary>Art height factor while rising out of a battle-flipped card (1 = full).</summary>
        readonly Tween _artRise = new(1f);
        Transform _plate;
        Transform _plateGlow;
        Material _plateMat;
        Material _plateGlowMat;
        bool _plateIsDefense;
        float _plateFadeAt = -1f;
        float _lastPlateAlpha = -1f;
        bool _artDirty;
        float _lastTintBrace = -1f;
        // 2D artwork quads (face + reverse) and their unbraced layout
        bool _art2D;
        Transform _artFront;
        Transform _artBack;
        Vector3 _artScale;
        float _artLift;
        float _artThick;
        /// <summary>Shell / dedicated mesh body scale as built (art rise scales it).</summary>
        Vector3 _bodyBaseScale = Vector3.one;
        /// <summary>Shell / dedicated mesh body local position as built (lifted onto the plate).</summary>
        Vector3 _bodyBasePos;
        /// <summary>False when the card has no art (tokens): the plate shows only its glow border.</summary>
        bool _plateHasFace = true;
        /// <summary>Last billboard camera (every visual faces the same stage camera).</summary>
        static Camera _billboardCam;

        /// <summary>
        /// The arena names the stage camera every frame it billboards, even with no card on the field yet, so a
        /// first Set card already knows where the viewer stands (its hinge faces them).
        /// </summary>
        public static void NoteViewerCamera(Camera cam)
        {
            if (cam != null) _billboardCam = cam;
        }
        /// <summary>Which long edge (frame ±Z) the braced art stands on: the one away from the camera.</summary>
        float _braceAway;
        // Flip-then-destroy: the shatter spec is taken when the flip starts (so the disk card clears to
        // the GY instead of racing for it) and played once the braced monster has held over its card.
        bool _destroyAfterFlip;
        bool _hasFlipDestroySpec;
        ShatterSpec _flipDestroySpec;
        Transform _defendStreak;
        Material _defendMat;
        Material _defendStreakMat;
        static Texture2D _softRectTex;

        // ── Spell/Trap activation sequence ──────────────────────────
        enum SpellSeqPhase
        {
            None = 0,
            FaceDownSpawn,
            RisingReveal,
            ActiveHover,
            Fading,
            /// <summary>Lingering S/T finished its present and now stands over its zone.</summary>
            Settled
        }

        SpellSeqPhase _spellSeq;
        /// <summary>Zone pose the current activation rose from (an undone activation lies back down there).</summary>
        Vector3 _spellZoneLocal;
        /// <summary>
        /// This card's zone centre (parent-local). S/T poses are built from it, never from the current pose: a
        /// stood-up card's centre sits off its zone.
        /// </summary>
        Vector3 _zoneLocal;
        bool _hasZone;
        bool _spellArtRevealed;
        /// <summary>This activation was played from the hand (appears already risen) rather than flipped from a Set.</summary>
        bool _activatedFromHand;
        bool _spellActivateStarted;
        bool _fadeRequested;
        bool _isGhost;
        bool _shown = true;
        int _spellCounters;
        Transform _counterBadge;
        readonly List<Material> _fadeMats = new();
        CardDatabase _db;
        Coroutine _spellCo;

        public static ArArenaCardVisual Create(
            Transform parent,
            CardInstance card,
            CardDatabase db,
            int layer,
            bool playerSide,
            bool isMonster,
            Vector3 localPos,
            Vector3? spawnFromWorld = null,
            bool preferMesh = false)
        {
            var go = new GameObject(card != null
                ? $"ArenaArt_{(card.FaceUp ? card.Name : "Set")}"
                : "ArenaArt");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var v = go.AddComponent<ArArenaCardVisual>();
            v._layer = layer;
            v.PlayerSide = playerSide;
            v.IsMonster = isMonster;
            v.PreferMeshWhenAvailable = preferMesh;
            v.Build(card, db, localPos, spawnFromWorld);
            return v;
        }

        void Build(CardInstance card, CardDatabase db, Vector3 localPos, Vector3? spawnFromWorld)
        {
            _db = db;
            ApplyIdentity(card);
            _zoneLocal = localPos;
            _hasZone = true;
            _targetLocalPos = ApplyFaceDownHover(localPos);
            _targetLocalRot = ComputePoseRotation();
            _targetLocalScale = ArPlaymatLayout.LiveVisualScale;

            // Spell/Trap activating face-up: start as face-down hologram, then rise+reveal
            var spellActivate = !IsMonster && FaceUp &&
                                SpellActivationPresentation.HasActivation(InstanceId);
            if (spellActivate)
            {
                // Build face-down first for the set pose
                var savedFace = FaceUp;
                FaceUp = false;
                _targetLocalPos = ApplyFaceDownHover(localPos);
                _targetLocalRot = ComputePoseRotation();
                BuildBody(db);
                FaceUp = savedFace;
                _spellActivateStarted = true;
                _spellSeq = SpellSeqPhase.FaceDownSpawn;
                // Spawned already face-up for its activation: played from the hand (never Set on the field).
                _activatedFromHand = true;

                _spellArtRevealed = false;
            }
            else
            {
                BuildBody(db);
                BuildSummonRing();
                if (IsMonster && Defense && FaceUp)
                {
                    // Summoned face-up in Defense: braced over its sideways card. In play the card
                    // stays dark until the materialize (after the disk-seat hold), then rises in.
                    SetDefenseDisplay(true, animate: false);
                    if (Application.isPlaying)
                    {
                        _plateAlpha.Snap(0f);
                        _plateRise.Snap(0f);
                    }
                }
            }

            if (spawnFromWorld.HasValue && transform.parent != null && !spellActivate)
            {
                transform.position = spawnFromWorld.Value;
                transform.localRotation = _targetLocalRot;
                transform.localScale = Vector3.one * 0.08f;
            }
            else
            {
                transform.localPosition = _targetLocalPos;
                transform.localRotation = _targetLocalRot;
                transform.localScale = Vector3.one * 0.05f;
            }

            EnsurePickCollider();

            _spawnStartWorld = transform.position;
            _spawnStartScale = transform.localScale;
            _spawnStartLocalRot = transform.localRotation;
            _spawnAnimating = !spellActivate; // spell uses its own sequence
            _spawnT = 0f;
            _pendingHoloFx = false;
            if (!spellActivate && Application.isPlaying)
            {
                // Card seats on the disk first. The hologram stays dark until that beat ends.
                _seatHold = IsMonster
                    ? ArDiskMotion.ArenaAfterMonsterSeat
                    : ArDiskMotion.ArenaAfterSlotSeat;
                if (_seatHold > 0.02f)
                {
                    _holdForSeat = true;
                    _spawnAnimating = false;
                    SetShown(false);
                }
            }

            if (spellActivate)
            {
                // Hidden until the disk has turned the card (was a 0.05-scale card back on the pad).
                if (Application.isPlaying)
                    SetShown(false);
                _spellCo = StartCoroutine(ActivateAfterDiskTurn(localPos));
            }
            else if (_holdForSeat)
            {
                // Update() plays the materialize after the disk seat (a face-down set as its card back).
            }
            else if (Application.isPlaying)
            {
                // Wait until the holo has left the disk so the Ka light is in midfield,
                // not on the pad. Instant spawns (no disk origin) fire at the landing now.
                if (spawnFromWorld.HasValue && _spawnAnimating)
                    _pendingHoloFx = true;
                else
                    PlayHoloSpawnFx();
            }

            BindEffectCallout();
        }

        void ApplyIdentity(CardInstance card)
        {
            Card = card;
            InstanceId = card?.InstanceId ?? 0;
            CardId = card?.CardId ?? 0;
            FaceUp = card?.FaceUp ?? false;
            Defense = card != null && card.Position == BattlePosition.Defense;
        }

        /// <summary>
        /// Pick volume for trap/effect targeting. Summon/drop still ignore holos
        /// in <see cref="ArCardDragSystem"/> unless a target window is open.
        /// </summary>
        public void EnsurePickCollider()
        {
            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            if (IsMonster && FaceUp)
            {
                var s = CardArtFocus.MonsterArtworkScale;
                col.center = new Vector3(0f, CardArtFocus.MonsterArtLift, 0f);
                col.size = new Vector3(s.x * 1.08f, s.y * 1.08f, 0.14f);
            }
            else
            {
                var s = CardArtFocus.SpellTrapArtworkScale;
                col.center = Vector3.zero;
                col.size = new Vector3(s.x * 1.12f, Mathf.Max(0.12f, s.y * 0.35f), s.y * 1.05f);
            }
        }

        void BuildBody(CardDatabase db)
        {
            if (PreferMeshWhenAvailable && FaceUp && CardModelCatalog.HasShellModel(CardId))
            {
                // Per-shell relief (cape / wings / body as separate renderers) with its
                // spring-bone + capsule rig. Sword/body textures come from the GLB itself.
                var tintS = PlayerSide ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.55f, 0.7f);
                var rig = CardModelCatalog.InstantiateShellModel(CardId, transform, tintS, CardModelCatalog.GetLifesizeHeight(CardId));
                if (rig != null)
                {
                    _shellRig = rig;
                    _body = rig.transform;
                    var bodyR = rig.BodyShell != null ? rig.BodyShell.GetComponent<Renderer>() : rig.GetComponentInChildren<Renderer>();
                    _faceMr = bodyR as MeshRenderer;
                    _faceMat = bodyR != null ? bodyR.sharedMaterial : null;
                    _bodyBaseScale = _body.localScale;
                    _bodyBasePos = _body.localPosition;
                    _artDirty = true;
                    return;
                }
            }

            if (PreferMeshWhenAvailable && CardModelCatalog.HasDedicatedModel(CardId) && FaceUp)
            {
                var mesh = CardModelCatalog.GetMesh(CardId);
                var body = new GameObject("MonsterModel", typeof(MeshFilter), typeof(MeshRenderer));
                body.transform.SetParent(transform, false);
                body.layer = _layer;
                body.GetComponent<MeshFilter>().sharedMesh = mesh;
                var tint = PlayerSide ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.55f, 0.7f);
                var mat = ArAnimePresentation.MakeFaceMaterial(Texture2D.whiteTexture, tint, "MonsterMeshArt");
                // Cropped illustration window on mesh (not full card face)
                CardArtFocus.ApplyMonsterArtwork(mat, db, CardId);
                body.GetComponent<MeshRenderer>().sharedMaterial = mat;
                ArAnimePresentation.ConfigureHoloRenderer(body.GetComponent<MeshRenderer>());
                _body = body.transform;
                _faceMr = body.GetComponent<MeshRenderer>();
                _faceMat = mat;
                _bodyBaseScale = _body.localScale;
                _bodyBasePos = _body.localPosition;
                _artDirty = true;
                return;
            }

            if (!FaceUp)
            {
                BuildFaceDownSetCard(db);
                return;
            }

            // Face-up monsters: upright cropped illustration hologram (Solid Vision).
            // Face-up S/T: full TCG card in the near row — do not crop to artwork.
            var faceScale = IsMonster
                ? CardArtFocus.MonsterArtworkScale
                : CardArtFocus.SpellTrapArtworkScale;
            // Monsters stand from the street (lift = half height). S/T rotate
            // from the Set pose about the card center — keep the mesh at origin.
            var lift = IsMonster ? CardArtFocus.MonsterArtLift : 0f;
            var accent = PlayerSide
                ? new Color(0.45f, 0.95f, 1f, 1f)
                : new Color(1f, 0.55f, 0.7f, 1f);

            // Thickness so −Z is a real opposite face (not z-fight with art).
            var thick = IsMonster ? 0.004f : 0.018f;

            // Primary art quad. A Unity quad reads from its −Z side, so the art shows on local −Z; the
            // S/T card back (below) shows on +Z.
            var cardGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cardGo.name = IsMonster ? "MonsterArtwork" : "SpellTrapArtwork";
            cardGo.transform.SetParent(transform, false);
            cardGo.transform.localPosition = new Vector3(0f, lift, thick * 0.5f);
            cardGo.transform.localRotation = Quaternion.identity;
            cardGo.transform.localScale = faceScale;
            ArObjectUtil.Destroy(cardGo.GetComponent<Collider>());
            cardGo.layer = _layer;
            _body = cardGo.transform;
            _faceMr = cardGo.GetComponent<MeshRenderer>();
            _faceMat = ArAnimePresentation.MakeFaceMaterial(Texture2D.whiteTexture, accent, "ArenaArtworkMat");
            if (IsMonster)
                ArFieldMaterials.ForceDoubleSided(_faceMat);
            else
                ArFieldMaterials.ForceFrontFacesOnly(_faceMat);
            _faceMr.sharedMaterial = _faceMat;
            ArAnimePresentation.ConfigureHoloRenderer(_faceMr);
            ApplyFaceTexture(db);

            // Reverse face: monsters duplicate art; S/T show the card back to the
            // controller (Yugipedia Activate / Duel Disk — Set card stands up).
            var backGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backGo.name = cardGo.name + "Back";
            backGo.transform.SetParent(transform, false);
            backGo.transform.localPosition = new Vector3(0f, lift, -thick * 0.5f);
            backGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            backGo.transform.localScale = faceScale;
            ArObjectUtil.Destroy(backGo.GetComponent<Collider>());
            backGo.layer = _layer;
            _backMr = backGo.GetComponent<MeshRenderer>();
            if (IsMonster)
            {
                _backMr.sharedMaterial = _faceMat;
                _backMat = _faceMat;
                // Braced Defense pose / art rise re-lay these two quads (ApplyArtPose).
                _art2D = true;
                _artFront = cardGo.transform;
                _artBack = backGo.transform;
                _artScale = faceScale;
                _artLift = lift;
                _artThick = thick;
                _lastTintBrace = -1f;
                _artDirty = true;
            }
            else
            {
                ApplySpellTrapCardBack();
            }

            ArAnimePresentation.ConfigureHoloRenderer(_backMr);
        }

        void ApplySpellTrapCardBack()
        {
            var tex = ResolveCardBackTexture();
            if (_backMat == null)
                _backMat = ArAnimePresentation.MakeFaceMaterial(tex, Color.white, "SpellTrapCardBack");
            CardArtFocus.ApplyToMaterial(_backMat, tex, cropToArtwork: false);
            ArFieldMaterials.ForceFrontFacesOnly(_backMat);
            var col = ArAnimePresentation.HoloFaceColor();
            if (_backMat.HasProperty("_BaseColor")) _backMat.SetColor("_BaseColor", col);
            if (_backMat.HasProperty("_Color")) _backMat.SetColor("_Color", col);
            if (_backMr != null)
                _backMr.sharedMaterial = _backMat;
        }

        /// <summary>
        /// Face-down set: full card-back, flat parallel to ground, double-sided so AR camera
        /// always sees it (single-sided quads cull when normal points into the field).
        /// </summary>
        void BuildFaceDownSetCard(CardDatabase db)
        {
            _ = db;
            // Monster set = exactly the Defense card plate (the battle flip lays this card down and it
            // becomes the plate the monster rises out of — same size, same sideways pose).
            var faceScale = IsMonster
                ? new Vector3(CardPlateShort, CardPlateLong, 1f)
                : CardArtFocus.SpellTrapArtworkScale;
            var back = ResolveCardBackTexture();
            // Prefer anime unlit so passthrough AR never drops the back to black
            _faceMat = ArAnimePresentation.MakeFaceMaterial(back, Color.white, "SetCardBack");
            CardArtFocus.ApplyToMaterial(_faceMat, back, cropToArtwork: false);
            ArFieldMaterials.ForceDoubleSided(_faceMat);
            if (_faceMat.HasProperty("_Cull")) _faceMat.SetFloat("_Cull", 0f);

            // A + B quads back-to-back — guarantees visibility regardless of normal
            // B sits 2 mm behind A (not coplanar): coplanar A/B z-fought, flickering the mirrored
            // reverse through the face once a set monster is shown flat (attack target / battle flip).
            var a = MakeSetQuad("CardBackA", faceScale, 0f, _faceMat);
            MakeSetQuad("CardBackB", faceScale, 180f, _faceMat, 0.002f);
            _body = a;
            _faceMr = a.GetComponent<MeshRenderer>();
            if (_faceMr != null)
            {
                _faceMr.enabled = true;
                ArAnimePresentation.ConfigureHoloRenderer(_faceMr);
            }

            Debug.Log(
                $"[WRLDZ AR] Arena face-down set holo · monster={IsMonster} · " +
                $"playerSide={PlayerSide} · id={InstanceId}");
        }

        Transform MakeSetQuad(string name, Vector3 scale, float yFlipDeg, Material mat, float backOffset = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, backOffset);
            go.transform.localRotation = Quaternion.Euler(0f, yFlipDeg, 0f);
            go.transform.localScale = scale;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = _layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go.transform;
        }

        void BuildSummonRing()
        {
            if (!IsMonster || !FaceUp) return;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SummonRing";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            var ringW = ArPlaymatLayout.SummonRingDiameter;
            ring.transform.localScale = new Vector3(ringW, 0.012f, ringW);
            ArObjectUtil.Destroy(ring.GetComponent<Collider>());
            ring.layer = _layer;
            ring.GetComponent<MeshRenderer>().sharedMaterial = MakeTint(
                PlayerSide
                    ? new Color(0.2f, 0.9f, 1f, 0.45f)
                    : new Color(1f, 0.35f, 0.55f, 0.45f));
            _ring = ring.transform;
        }

        public void Sync(CardInstance card, CardDatabase db)
        {
            if (card == null) return;
            if (_flipSummoning || _battleFlipping) return;
            _db = db;
            var faceChanged = FaceUp != card.FaceUp;
            var defChanged = Defense != (card.Position == BattlePosition.Defense);
            var idChanged = InstanceId != card.InstanceId || CardId != card.CardId;
            ApplyIdentity(card);

            // Set S/T activation undone (target pick cancelled / timed out): the engine turned it back
            // face-down and dropped the activation. Stop presenting its art and lay the card back down.
            if (!IsMonster && SpellActivateSequenceActive && _spellSeq != SpellSeqPhase.Fading &&
                !card.FaceUp && !_isGhost && StillInZone &&
                !SpellActivationPresentation.HasActivation(InstanceId))
            {
                AbortActivationToSet(db);
                return;
            }

            // Spell activate sequence owns pose until hover/fade
            if (!IsMonster && SpellActivateSequenceActive && _spellSeq != SpellSeqPhase.Fading)
            {
                RefreshSpellCounters(card);
                if (SpellActivationPresentation.WantsFadeToGy(InstanceId))
                    RequestFadeToGy();
                return;
            }

            _targetLocalRot = ComputePoseRotation();
            // Re-apply hover if face flipped (preserve XZ column)
            if (faceChanged)
            {
                var col = !IsMonster && _hasZone ? _zoneLocal : _targetLocalPos;
                var basePos = new Vector3(col.x,
                    IsMonster ? ArPlaymatLayout.MonsterHoverY : ArPlaymatLayout.SpellTrapHoverY,
                    col.z);
                _targetLocalPos = ApplyFaceDownHover(basePos);
            }

            if (idChanged || faceChanged)
            {
                var flippedUp = faceChanged && FaceUp;
                // S/T flipping up for activation → dedicated sequence
                if (!IsMonster && flippedUp && SpellActivationPresentation.HasActivation(InstanceId) &&
                    !_spellActivateStarted)
                {
                    _spellActivateStarted = true;
                    _activatedFromHand = false;
                    _holdForSeat = false;
                    if (_spellCo != null) StopCoroutine(_spellCo);
                    // The card back stays on its zone while the disk turns the card; ActivateAfterDiskTurn
                    // then casts and runs the rise/present. FaceDownSpawn keeps Sync from snapping it into
                    // the settled S/T pose during that wait.
                    _spellSeq = SpellSeqPhase.FaceDownSpawn;
                    _spellCo = StartCoroutine(ActivateAfterDiskTurn(SpellZoneForActivation()));
                    return;
                }

                // Flip Summon: keep the set body, flip to art, then turn into ATK
                if (IsMonster && flippedUp && !Defense && !_flipSummoning && Application.isPlaying)
                {
                    _flipSummoning = true;
                    if (_spellCo != null) StopCoroutine(_spellCo);
                    _spellCo = StartCoroutine(FlipSummonCo(db));
                    return;
                }

                // Attacked while set (or flipped face-up in Defense by an effect): the sideways set card
                // flips over, lies down as the Defense plate and the monster rises braced out of it.
                if (IsMonster && flippedUp && Defense && !_battleFlipping && Application.isPlaying)
                {
                    _battleFlipping = true;
                    if (_spellCo != null) StopCoroutine(_spellCo);
                    _spellCo = StartCoroutine(BattleFlipCo(db));
                    return;
                }

                ClearChildren();
                BuildBody(db);
                BuildSummonRing();
                EnsurePickCollider();
                if (Defense && IsMonster && FaceUp)
                    SetDefenseDisplay(true, animate: false);
                if (_holdForSeat)
                {
                    // Still inside the disk-seat hold: the fresh renderers are enabled by default and would
                    // show before the disk beat. Keep them dark; the hold's end materializes a face-up card
                    // (spawn animation + FX), a face-down one as its card back.
                    SetShown(false);
                }
                else if (flippedUp)
                {
                    SetShown(true);
                    PulseReveal();
                    CardFlip3dAnimator.PlayIfNeeded(transform, true, db);
                    PlaySummonFx();
                    if (IsMonster && !Defense)
                        BeginSummonCardPlate();
                }
                else if (!FaceUp)
                {
                    // Turned face-down (Book of Moon…) or a new face-down occupant: its card back on the zone.
                    RestAsSetCard();
                }
            }
            else if (defChanged && IsMonster && FaceUp)
            {
                // ATK ↔ DEF turn. A Spell/Trap-caused change only reaches here once its result gate
                // opened (ArArenaHologramManager.SyncMonsters holds mismatched positions while
                // ActivationBeat.AnyResultPending), so this never runs ahead of the card's reveal.
                // Tweens start from the current values: an interrupted turn never snaps.
                SetDefenseDisplay(Defense, animate: Application.isPlaying);
                if (Defense)
                    ArArenaCombatFx.PlayDefendShield(transform.position, PlayerSide);
            }

            RefreshSpellCounters(card);
            if (!IsMonster && SpellActivationPresentation.WantsFadeToGy(InstanceId))
                RequestFadeToGy();

            BindEffectCallout();

            if (_spellSeq == SpellSeqPhase.None || _spellSeq == SpellSeqPhase.Settled)
            {
                if (!_spawnAnimating && FaceUp && IsMonster)
                    transform.localRotation = _targetLocalRot;
                else if (!_spawnAnimating && !FaceUp)
                {
                    transform.localRotation = _targetLocalRot;
                    transform.localPosition = _targetLocalPos;
                }
                else if (!_spawnAnimating && FaceUp && !IsMonster)
                {
                    SpellStandPose(SpellZone(), out var standPos, out var standRot);
                    transform.localRotation = standRot;
                    transform.localPosition = standPos;
                }
            }
        }

        /// <summary>
        /// Zone emptied after resolve. Do not cut the arena rise/read hover short —
        /// the activate sequence fades itself after <see cref="SpellActivationPresentation.MinHoverSeconds"/>.
        /// </summary>
        public void RequestFadeToGy()
        {
            if (_spellSeq == SpellSeqPhase.Fading) return;
            var settledIdle = _spellSeq == SpellSeqPhase.Settled ||
                              (_spellSeq == SpellSeqPhase.ActiveHover && _spellCo == null);
            if (_fadeRequested && !settledIdle) return;
            _fadeRequested = true;
            // Disk-turn wait / rise / hover still running: keep the hologram readable; the sequence
            // fades itself at the beat's fade time (ActivationBeat.FadeAt).
            if (_spellSeq == SpellSeqPhase.FaceDownSpawn ||
                _spellSeq == SpellSeqPhase.RisingReveal ||
                (_spellSeq == SpellSeqPhase.ActiveHover && _spellCo != null))
                return;
            // Still its card back (a Set that activated and resolved inside one engine call): play the
            // full reveal first — fading the card back never showed the card (owner report 2026-09-23).
            if (_spellSeq == SpellSeqPhase.None && TryPlayActivationThenFade())
                return;
            // Settled lingering card (or a face-up S/T with no sequence) leaving to the GY.
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = StartCoroutine(FadeToGyCo());
        }

        /// <summary>
        /// A Set S/T still showing its card back whose activation already resolved: reveal it from its zone
        /// exactly like a Sync-path activation (disk-turn wait → rise → art → hold) and then fade.
        /// Returns false if this visual has already been revealed / activated.
        /// </summary>
        public bool TryPlayActivationThenFade()
        {
            if (IsMonster || !Application.isPlaying) return false;
            if (SpellActivateSequenceActive)
            {
                _fadeRequested = true;
                return true;
            }

            if (_spellActivateStarted || FaceUp) return false;
            _holdForSeat = false; // a seat hold's materialize would cut into the rise
            _fadeRequested = true;
            _spellActivateStarted = true;
            _activatedFromHand = false;
            _spellSeq = SpellSeqPhase.FaceDownSpawn;
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = StartCoroutine(ActivateAfterDiskTurn(SpellZoneForActivation()));
            return true;
        }

        /// <summary>Hologram manager spawned this as a ghost (engine card already left the field).</summary>
        public void MarkGhost()
        {
            _isGhost = true;
            _fadeRequested = true;
            StillInZone = false;
        }

        /// <summary>Ghost path: play full activate then fade (card already in GY).</summary>
        public void PlayGhostActivationThenFade(CardDatabase db, Vector3 zoneLocal)
        {
            _db = db;
            FaceUp = true;
            IsMonster = false;
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = StartCoroutine(GhostActivateThenFadeCo(zoneLocal));
        }

        /// <summary>Attack-charge glow while this monster is the declared attacker.</summary>
        public void SetAttackCharge(float t01)
        {
            if (!IsMonster) return;
            _attackGlow = Mathf.Clamp01(t01);
            EnsureAttackAura();
            if (_attackAura != null)
            {
                var s = Mathf.Lerp(0.35f, 0.85f, _attackGlow);
                _attackAura.localScale = new Vector3(s, 0.02f, s);
                if (_attackMr != null)
                {
                    var c = PlayerSide
                        ? new Color(1f, 0.85f, 0.25f, 0.25f + _attackGlow * 0.55f)
                        : new Color(1f, 0.4f, 0.25f, 0.25f + _attackGlow * 0.55f);
                    ArFieldMaterials.Apply(_attackMr, c);
                }
            }
        }

        public void ClearAttackCharge()
        {
            _attackGlow = 0f;
            if (_attackAura != null)
            {
                ArObjectUtil.Destroy(_attackAura.gameObject);
                _attackAura = null;
            }
        }

        public void PlayHitFlash()
        {
            ArArenaCombatFx.PlayImpact(transform.position, heavy: true);
            StartCoroutine(HitScalePunch());
        }

        /// <summary>The activation's one Solid Vision beat, anchored on the card's own zone (pits / rings sit on the
        /// field, not in the air where the card presents).</summary>
        void PlaySpellCastAtZone(Vector3 zoneLocal)
        {
            if (!Application.isPlaying) return;
            var world = transform.parent != null ? transform.parent.TransformPoint(zoneLocal) : zoneLocal;
            ArArenaCombatFx.PlaySpellCast(world, PlayerSide, _layer, CardId);
        }

        Vector3 LandingWorld()
        {
            return transform.parent != null
                ? transform.parent.TransformPoint(_targetLocalPos)
                : _targetLocalPos;
        }

        void PlayHoloSpawnFx()
        {
            // A face-up monster materializes in its own Attribute light; a hidden card never hands over its def.
            ArArenaCombatFx.PlayHoloMaterialize(LandingWorld(), PlayerSide, IsMonster, _layer, FaceUp ? Card?.Def : null);
            // The card under the monster rises in with the materialize — after the disk-seat hold
            // (this fires from Update once the hold ended), never before.
            if (!IsMonster || !FaceUp || !Application.isPlaying) return;
            if (Defense)
            {
                if (_plate != null && _plateAlpha.To < 1f)
                {
                    _plateAlpha.Go(1f, SummonPlateRiseSeconds);
                    _plateRise.Go(1f, SummonPlateRiseSeconds);
                }
            }
            else
                BeginSummonCardPlate();
        }

        void PlaySummonFx()
        {
            if (!IsMonster) return;
            ArArenaCombatFx.PlayHoloMaterialize(LandingWorld(), PlayerSide, true, _layer, FaceUp ? Card?.Def : null);
        }

        void EnsureAttackAura()
        {
            if (_attackAura != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "AttackAura";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localScale = new Vector3(0.4f, 0.015f, 0.4f);
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = _layer;
            _attackMr = go.GetComponent<MeshRenderer>();
            ArFieldMaterials.Apply(_attackMr, PlayerSide
                ? new Color(1f, 0.85f, 0.3f, 0.4f)
                : new Color(1f, 0.4f, 0.3f, 0.4f));
            _attackAura = go.transform;
        }

        /// <summary>
        /// Shield glint (anime "braces behind a shield glint"): a thin, slightly curved translucent pane
        /// (centre + two wings angled back around the monster) standing just past the Defense card's
        /// edge on the attacker's side, pulsing gently, with a bright streak sliding across it.
        /// Posed parallel to the braced art each frame, offset toward the opposing side (PoseGroundPieces).
        /// Visibility follows the brace, so it fades in / out with the Defense turn.
        /// </summary>
        void EnsureDefendShield(bool instant)
        {
            if (_defendShield != null)
            {
                if (!instant) _defendPulse = 1f;
                return;
            }

            var root = new GameObject("DefendShield").transform;
            root.SetParent(transform, false);
            root.gameObject.layer = _layer;
            _defendMat = new Material(ArFieldMaterials.GetAdditive(GlintColor(0f))) { name = "DefendGlintPane" };
            _defendStreakMat = new Material(ArFieldMaterials.GetAdditive(new Color(1f, 1f, 1f, 0f)))
                { name = "DefendGlintStreak" };
            var rad = GlintWingDeg * Mathf.Deg2Rad;
            var wx = GlintCenterW * 0.5f + GlintWingW * 0.5f * Mathf.Cos(rad);
            var wz = -GlintWingW * 0.5f * Mathf.Sin(rad); // wings wrap back toward the monster
            _defendMr = MakeGlintQuad(root, "Pane", Vector3.zero, 0f, 0f,
                new Vector3(GlintCenterW, GlintH, 1f), _defendMat).GetComponent<MeshRenderer>();
            MakeGlintQuad(root, "WingR", new Vector3(wx, 0f, wz), GlintWingDeg, 0f,
                new Vector3(GlintWingW, GlintH * 0.94f, 1f), _defendMat);
            MakeGlintQuad(root, "WingL", new Vector3(-wx, 0f, wz), -GlintWingDeg, 0f,
                new Vector3(GlintWingW, GlintH * 0.94f, 1f), _defendMat);
            _defendStreak = MakeGlintQuad(root, "Glint", new Vector3(0f, 0f, 0.004f), 0f, 24f,
                new Vector3(0.06f, GlintH * 0.9f, 1f), _defendStreakMat);
            _defendShield = root;
            _defendPulse = instant ? 0f : 1f;
            PoseGroundPieces();
        }

        Transform MakeGlintQuad(Transform parent, string name, Vector3 local, float yawDeg, float rollDeg,
            Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yawDeg, rollDeg);
            go.transform.localScale = scale;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = _layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            mr.enabled = _shown;
            return go.transform;
        }

        Color GlintColor(float a)
        {
            var c = Color.Lerp(Accent, Color.white, 0.35f);
            c.a = a;
            return c;
        }

        void ClearDefendShield()
        {
            if (_defendShield != null)
            {
                ArObjectUtil.Destroy(_defendShield.gameObject);
                _defendShield = null;
            }

            DestroyOwnedMaterial(ref _defendMat);
            DestroyOwnedMaterial(ref _defendStreakMat);
            _defendMr = null;
            _defendStreak = null;
            _defendPulse = 0f;
        }

        void TickDefendGlint()
        {
            if (_defendShield == null) return;
            if (_defendPulse > 0f)
                _defendPulse = Mathf.Max(0f, _defendPulse - Time.deltaTime);
            var vis = _brace.V * (_plate != null ? _plateAlpha.V : 1f);
            var breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
            var a = Mathf.Clamp01(vis * (0.14f + 0.08f * breathe) + 0.30f * _defendPulse * vis);
            SetMatColor(_defendMat, GlintColor(a));
            _defendShield.localScale = Vector3.one * (1f + 0.025f * breathe + 0.12f * _defendPulse);
            if (_defendStreak != null)
            {
                var phase = Mathf.Repeat(Time.time / 2.4f, 1f);
                _defendStreak.localPosition = new Vector3(Mathf.Lerp(-0.26f, 0.26f, phase), 0f, 0.004f);
                SetMatColor(_defendStreakMat, new Color(1f, 1f, 1f, vis * 0.5f * Mathf.Sin(phase * Mathf.PI)));
            }
        }

        static void SetMatColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        static void DestroyOwnedMaterial(ref Material m)
        {
            if (m != null) ArObjectUtil.Destroy(m);
            m = null;
        }

        Color Accent => PlayerSide
            ? new Color(0.45f, 0.95f, 1f, 1f)
            : new Color(1f, 0.55f, 0.7f, 1f);

        // ── Card plate + braced pose ─────────────────────────────────────────────

        /// <summary>
        /// Face-up Defense display on/off. On: the full card lies flat and turns to landscape under the
        /// art, the art braces, the shield glint rises, a shell model plays State.Defense. Off: the card
        /// turns back to portrait and fades while the art rises; shell back to Idle.
        /// </summary>
        void SetDefenseDisplay(bool def, bool animate)
        {
            if (!IsMonster || !FaceUp) return;
            var poser = _shellRig != null ? _shellRig.GetComponent<ShellPoser>() : null;
            if (poser != null)
                poser.Set(def ? ShellPoser.State.Defense : ShellPoser.State.Idle);

            if (def)
            {
                var fresh = _plate == null;
                EnsurePlate();
                _plateIsDefense = true;
                _plateFadeAt = -1f;
                if (_plate != null) _plate.name = "DefenseCardPlate";
                if (!animate)
                {
                    _plateTurn.Snap(1f);
                    _plateRise.Snap(1f);
                    _plateAlpha.Snap(1f);
                    _brace.Snap(1f);
                }
                else
                {
                    if (fresh)
                    {
                        _plateAlpha.Snap(0f);
                        _plateRise.Snap(0f);
                        _plateTurn.Snap(0f); // fades in while turning portrait → landscape
                    }

                    _plateTurn.Go(1f, PlateTurnSeconds);
                    _plateRise.Go(1f, PlateTurnSeconds);
                    _plateAlpha.Go(1f, PlateTurnSeconds);
                    _brace.Go(1f, BraceSeconds);
                }

                EnsureDefendShield(instant: !animate);
            }
            else
            {
                if (_plate != null && _plateIsDefense)
                {
                    _plateIsDefense = false;
                    if (!animate)
                        DestroyPlate();
                    else
                    {
                        // Turn back to portrait, fading as it turns (the art rises meanwhile).
                        _plateFadeAt = -1f;
                        _plateTurn.Go(0f, PlateTurnSeconds);
                        _plateAlpha.Go(0f, SummonPlateFadeSeconds, PlateFadeDelayOnRaise);
                    }
                }

                if (!animate)
                {
                    _brace.Snap(0f);
                    ClearDefendShield();
                }
                else
                    _brace.Go(0f, BraceSeconds); // glint fades with the brace, then LateUpdate clears it
            }

            _artDirty = true;
        }

        /// <summary>
        /// Attack-position arrival (Normal / Tribute / Special / Flip Summon into Attack): the full card
        /// lies flat in PORTRAIT under the materializing hologram, rises in with it, holds so it can be
        /// read, then fades — leaving the monster standing with no card under it.
        /// </summary>
        void BeginSummonCardPlate()
        {
            if (!IsMonster || !FaceUp || Defense || !Application.isPlaying) return;
            var fresh = _plate == null;
            EnsurePlate();
            if (_plate == null) return;
            _plate.name = "SummonCardPlate";
            _plateIsDefense = false;
            if (fresh)
            {
                _plateAlpha.Snap(0f);
                _plateRise.Snap(0f);
                _plateTurn.Snap(0f);
            }
            else
                _plateTurn.Go(0f, PlateTurnSeconds);

            _plateAlpha.Go(1f, SummonPlateRiseSeconds);
            _plateRise.Go(1f, SummonPlateRiseSeconds);
            _plateFadeAt = Time.unscaledTime + SummonPlateRiseSeconds + SummonPlateHoldSeconds;
        }

        void EnsurePlate()
        {
            if (_plate != null) return;
            // Soft glow border beneath, controller accent (player cyan / opponent pink), unlit.
            var glowGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glowGo.name = "CardPlateGlow";
            glowGo.transform.SetParent(transform, false);
            ArObjectUtil.Destroy(glowGo.GetComponent<Collider>());
            glowGo.layer = _layer;
            var accent = Accent;
            accent.a = 0f;
            _plateGlowMat = new Material(ArFieldMaterials.GetAdditive(accent)) { name = "CardPlateGlow" };
            var soft = SoftRectTexture();
            if (_plateGlowMat.HasProperty("_MainTex")) _plateGlowMat.SetTexture("_MainTex", soft);
            if (_plateGlowMat.HasProperty("_BaseMap")) _plateGlowMat.SetTexture("_BaseMap", soft);
            _plateGlowMat.renderQueue = 3100;
            var glowMr = glowGo.GetComponent<MeshRenderer>();
            glowMr.sharedMaterial = _plateGlowMat;
            ArAnimePresentation.ConfigureHoloRenderer(glowMr);
            glowMr.enabled = _shown;
            _plateGlow = glowGo.transform;

            // Full card (frame + art) — same texture source as RevealSetCardArt, never cropped.
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "DefenseCardPlate";
            go.transform.SetParent(transform, false);
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = _layer;
            _plateMat = new Material(ArFieldMaterials.GetTransparent(new Color(1f, 1f, 1f, 0f)))
                { name = "CardPlateFace" };
            // No art (tokens have a negative hashed CardId; missing art files): no face quad content —
            // the plate reads as its glow border only (was an opaque white card). State machine unchanged.
            var tex = ResolveFullCardTexture();
            _plateHasFace = tex != null;
            if (tex == null) tex = Texture2D.whiteTexture;
            if (_plateMat.HasProperty("_MainTex")) _plateMat.SetTexture("_MainTex", tex);
            if (_plateMat.HasProperty("_BaseMap")) _plateMat.SetTexture("_BaseMap", tex);
            _plateMat.renderQueue = 3110; // over its glow
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _plateMat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            mr.enabled = _shown;
            _plate = go.transform;
            _lastPlateAlpha = -1f;
            PoseGroundPieces();
        }

        void DestroyPlate()
        {
            if (_plate != null) ArObjectUtil.Destroy(_plate.gameObject);
            if (_plateGlow != null) ArObjectUtil.Destroy(_plateGlow.gameObject);
            _plate = null;
            _plateGlow = null;
            DestroyOwnedMaterial(ref _plateMat);
            DestroyOwnedMaterial(ref _plateGlowMat);
            _plateIsDefense = false;
            _plateFadeAt = -1f;
            _lastPlateAlpha = -1f;
            _plateAlpha.Snap(0f);
            _plateRise.Snap(0f);
            _plateTurn.Snap(0f);
        }

        /// <summary>Full card face (frame + art), or null when the card has no art.</summary>
        Texture ResolveFullCardTexture()
        {
            if (_db != null && CardId > 0)
            {
                var full = _db.GetArt(CardId);
                if (full != null && full.texture != null)
                    return full.texture;
            }

            return null;
        }

        void ApplyPlateAlpha()
        {
            var a = _plateAlpha.V;
            if (Mathf.Abs(a - _lastPlateAlpha) < 0.001f) return;
            _lastPlateAlpha = a;
            var face = ArAnimePresentation.Expose(Color.white);
            face.a = _plateHasFace ? a : 0f;
            SetMatColor(_plateMat, face);
            var glow = Accent;
            // Artless plate: the glow alone carries the card outline, so it reads a little stronger.
            glow.a = (_plateHasFace ? 0.55f : 0.8f) * a;
            SetMatColor(_plateGlowMat, glow);
        }

        /// <summary>Zone frame in world space: arena rotation · yaw (0 player / 180 opponent).</summary>
        Quaternion ArenaFrame()
        {
            var parent = transform.parent;
            var yaw = PlayerSide ? 0f : 180f;
            return (parent != null ? parent.rotation : Quaternion.identity) * Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Landscape roll direction (+1 / −1) so the Defense card's top points at this side's M1, exactly
        /// like the disk card (ArZoneLayout.FaceUpDefenseRot: top → disk −X = M1). Player M1 is on arena
        /// −X·PlayerColumnSign (frame = arena); opponent M1 is on arena −X·OppColumnSign, i.e. frame
        /// +X·OppColumnSign (frame yawed 180°). A roll of +90° puts the top on frame −X.
        /// </summary>
        float PlateRollSign() =>
            PlayerSide ? ArPlaymatLayout.PlayerColumnSign : -ArPlaymatLayout.OppColumnSign;

        /// <summary>One root unit in world metres at rest (root scale × arena scale).</summary>
        float WorldUnit()
        {
            var parent = transform.parent;
            var parentScale = parent != null ? parent.lossyScale.x : 1f;
            return Mathf.Max(1e-4f, _targetLocalScale.x * parentScale);
        }

        /// <summary>
        /// Braced monsters stand at the FAR long edge of their landscape card as seen from the stage
        /// camera, so the whole card lies in front of the art (it was hidden behind / under the wide art).
        /// World offset from the zone landing, along the card's short axis (arena frame Z), × brace.
        /// </summary>
        Vector3 BraceOffsetWorld()
        {
            var b = _brace.V;
            if (b <= 0.0001f || !IsMonster || !FaceUp || _plate == null) return Vector3.zero;
            var cam = _billboardCam;
            if (cam == null) return Vector3.zero;
            var fwd = ArenaFrame() * Vector3.forward;
            // The stage camera sits on the player's side: frame +Z edge for the player's monsters, −Z
            // for the opponent's. Hysteresis so an orbiting free-look camera does not flip it at the seam.
            if (_braceAway == 0f) _braceAway = PlayerSide ? 1f : -1f;
            var toLanding = LandingWorld() - cam.transform.position;
            var d = Vector3.Dot(toLanding, fwd) / Mathf.Max(1e-4f, toLanding.magnitude);
            if (d * _braceAway < -0.2f) _braceAway = -_braceAway;
            return fwd * (_braceAway * 0.5f * CardPlateShort * WorldUnit() * b);
        }

        /// <summary>Root at landing + brace offset (idempotent; FaceCamera and LateUpdate both call it).</summary>
        void ApplyBraceOffset()
        {
            if (!IsMonster || !FaceUp || _spawnAnimating || _flipSummoning || _battleFlipping) return;
            if (transform.parent == null) return;
            transform.position = LandingWorld() + BraceOffsetWorld();
        }

        /// <summary>
        /// Plate lives in the ARENA frame (the root billboards toward the camera and must not tilt it).
        /// The plate sits on the zone landing (not the flying root) so a summon card waits on the street
        /// while the hologram arrives; its size is compensated for the root's scale.
        /// Plate rotation = zone frame (yaw 0 player / 180 opponent) · Rx(90) face-up flat · Rz(90·turn·dir):
        /// portrait top points away from the controller; Defense rolls toward M1 exactly like the disk card
        /// (ArZoneLayout.FaceUpDefenseRot = Euler(-90, 0, 90) vs Attack Euler(-90, 0, 0)), dir = PlateRollSign.
        /// The shield glint is posed parallel to the braced ART plane (the root billboards and leans),
        /// offset along the art normal toward the opposing side: in front of an opponent's monster from
        /// the player's view, behind the player's own monster (it braces behind it toward the attacker).
        /// </summary>
        void PoseGroundPieces()
        {
            if (_plate == null && _defendShield == null) return;
            var parent = transform.parent;
            var frame = ArenaFrame();
            var up = frame * Vector3.up;

            if (_plate != null)
            {
                var worldUnit = WorldUnit(); // one root unit at rest
                var rootScale = Mathf.Max(1e-4f, transform.lossyScale.x);
                var comp = worldUnit / rootScale;
                var basePos = parent != null ? parent.TransformPoint(_targetLocalPos) : _targetLocalPos;
                var rise = _plateRise.V;
                var k = Mathf.Lerp(0.82f, 1f, rise);
                var rot = frame * Quaternion.Euler(90f, 0f, 90f * _plateTurn.V * PlateRollSign());
                var y = Mathf.Lerp(CardPlateY - 0.06f, CardPlateY, rise);
                _plate.SetPositionAndRotation(basePos + up * (y * worldUnit), rot);
                _plate.localScale = new Vector3(CardPlateShort * k, CardPlateLong * k, 1f) * comp;
                if (_plateGlow != null)
                {
                    var gy = y - (CardPlateY - CardPlateGlowY);
                    _plateGlow.SetPositionAndRotation(basePos + up * (gy * worldUnit), rot);
                    _plateGlow.localScale = new Vector3(
                        (CardPlateShort + 2f * CardPlateGlowMargin) * k,
                        (CardPlateLong + 2f * CardPlateGlowMargin) * k, 1f) * comp;
                }
            }

            if (_defendShield != null)
            {
                // Parallel to the braced art: root billboard pitch + the art's own lean.
                var s = transform.lossyScale.x;
                var artRot = transform.rotation * Quaternion.Euler(-DefenseLeanDeg * _brace.V, 0f, 0f);
                var artN = artRot * Vector3.forward;
                var artUp = artRot * Vector3.up;
                var fwd = frame * Vector3.forward; // toward the opposing side
                var toward = Vector3.Dot(artN, fwd) >= 0f ? artN : -artN;
                var artBase = transform.TransformPoint(new Vector3(0f, ArtBaseLocalY(), 0f));
                _defendShield.SetPositionAndRotation(
                    artBase + artUp * (GlintY * s) + toward * (GlintOffset * s),
                    Quaternion.LookRotation(toward, artUp));
            }
        }

        /// <summary>
        /// Root-local Y of the art base so the monster stands ON its card (plate top + clearance) in
        /// ARENA-up, whatever the root's billboard pitch (a pitched root's local up is not arena up, so
        /// a root-local plate height sank the feet under the card). 0 when no card lies under it; while
        /// a summon card fades the base follows it down (weight = max(brace, plate alpha)).
        /// </summary>
        float ArtBaseLocalY()
        {
            if (_plate == null) return 0f;
            var w = Mathf.Max(_brace.V, _plateAlpha.V);
            if (w <= 0.0001f) return 0f;
            var arenaUp = ArenaFrame() * Vector3.up;
            var d = Mathf.Max(0.3f, Vector3.Dot(transform.up, arenaUp));
            var rootScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            var plateY = Mathf.Lerp(CardPlateY - 0.06f, CardPlateY, _plateRise.V) + ArtOnPlateClearance;
            var h = Mathf.Max(0f, plateY) * WorldUnit() / rootScale; // plate height in root units
            return h / d * w;
        }

        /// <summary>
        /// Lay out the art for the current brace / rise. 2D artwork: height × lerp(1, 0.88, brace), width
        /// capped at the card's long side while braced (the landscape card's ends stay visible), bottom on
        /// the card top (ArtBaseLocalY), lean back 6° (top away from the camera; the root's +Z faces it),
        /// 20% cool accent tint. Shell / mesh bodies: rise scale + stand on the card (a shell's brace is
        /// ShellPoser.Defense, whose 0.03 crouch drop is cancelled so the feet stay on the card).
        /// Runs every LateUpdate (the base depends on the camera-driven root pitch).
        /// </summary>
        void ApplyArtPose()
        {
            if (!IsMonster || !FaceUp) return;
            var b = _brace.V;
            var r = Mathf.Max(0.02f, _artRise.V);
            var baseY = ArtBaseLocalY();
            if (_art2D && _artFront != null)
            {
                var k = Mathf.Lerp(1f, DefenseCrouchY, b) * r;
                var wCap = _artScale.x > 1e-4f ? Mathf.Min(1f, CardPlateLong / _artScale.x) : 1f;
                var kx = Mathf.Lerp(1f, wCap, b);
                var lean = Quaternion.Euler(-DefenseLeanDeg * b, 0f, 0f);
                var h = _artLift * k;
                var sc = new Vector3(_artScale.x * kx, _artScale.y * k, _artScale.z);
                _artFront.localPosition = new Vector3(0f, baseY, 0f) + lean * new Vector3(0f, h, _artThick * 0.5f);
                _artFront.localRotation = lean;
                _artFront.localScale = sc;
                if (_artBack != null)
                {
                    _artBack.localPosition = new Vector3(0f, baseY, 0f) + lean * new Vector3(0f, h, -_artThick * 0.5f);
                    _artBack.localRotation = lean * Quaternion.Euler(0f, 180f, 0f);
                    _artBack.localScale = sc;
                }

                if (Mathf.Abs(b - _lastTintBrace) > 0.002f && _faceMat != null)
                {
                    _lastTintBrace = b;
                    var cool = Color.Lerp(Accent, new Color(0.5f, 0.7f, 1f, 1f), 0.5f);
                    SetMatColor(_faceMat, ArAnimePresentation.Expose(
                        Color.Lerp(Color.white, cool, DefenseTintBlend * b)));
                }
            }
            else if (_body != null && (_shellRig != null || _body.name == "MonsterModel"))
            {
                _body.localScale = _bodyBaseScale * r;
                var cancelCrouch = _shellRig != null ? 0.03f * b * r : 0f;
                _body.localPosition = _bodyBasePos + new Vector3(0f, baseY + cancelCrouch, 0f);
            }
        }

        /// <summary>Soft-edged white rect (alpha falls off over the outer 4.5% of each side), shared.</summary>
        static Texture2D SoftRectTexture()
        {
            if (_softRectTex != null) return _softRectTex;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "ArCardPlateGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = (x + 0.5f) / n;
                var v = (y + 0.5f) / n;
                var e = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                var a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / CardPlateGlowFeather));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            _softRectTex = tex;
            return tex;
        }

        System.Collections.IEnumerator HitScalePunch()
        {
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.28f;
                var u = Mathf.Clamp01(t);
                var punch = 1f + 0.2f * Mathf.Sin(u * Mathf.PI);
                if (!_spawnAnimating)
                    transform.localScale = _targetLocalScale * punch;
                yield return null;
            }

            if (!_spawnAnimating)
                transform.localScale = _targetLocalScale;
        }

        public void SetTargetLocalPosition(Vector3 localPos)
        {
            // The activate sequence owns pose (present / hover / fade); a Sync or an arm-move relayout
            // must not snap it back to the zone (it ran in LateUpdate, after the coroutine).
            if (!IsMonster && SpellActivateSequenceActive) return;
            _zoneLocal = localPos;
            _hasZone = true;
            _targetLocalPos = ApplyFaceDownHover(localPos);
            if (!IsMonster) _targetLocalRot = ComputePoseRotation();
            // A face-down monster's landscape roll follows the column sign (relayout after a disk re-yaw).
            if (IsMonster && !FaceUp && !_flipSummoning && !_battleFlipping)
                _targetLocalRot = ComputePoseRotation();
            if (!_spawnAnimating && !_flipSummoning && !_battleFlipping)
                transform.localPosition = _targetLocalPos;
        }

        public void ApplyLiveHoloScale()
        {
            if (!IsMonster && SpellActivateSequenceActive) return;
            _targetLocalScale = ArPlaymatLayout.LiveVisualScale;
            if (!_spawnAnimating && !_flipSummoning && !_battleFlipping)
                transform.localScale = _targetLocalScale;
        }

        public void PulseReveal()
        {
            transform.localPosition = _targetLocalPos;
            transform.localRotation = _targetLocalRot;
            transform.localScale = ArPlaymatLayout.LiveVisualScale * 0.12f;
            _spawnStartWorld = transform.position;
            _spawnStartScale = transform.localScale;
            _spawnStartLocalRot = transform.localRotation;
            _spawnAnimating = true;
            _spawnT = 0f;
        }

        /// <summary>
        /// Flip Summon: lift the set card, flip it face-up (still landscape), then
        /// rotate / stand into Attack. Same beats as the disk (toaster flip + position turn).
        /// </summary>
        IEnumerator FlipSummonCo(CardDatabase db)
        {
            _flipSummoning = true;
            _spawnAnimating = false;
            _holdForSeat = false; // the flip is the reveal; a pending seat hold must not re-materialize it mid-flip
            FaceUp = false;
            Defense = true;
            DuelPresentationPacer.HoldFlipSummon(Card != null ? Card.Name : null);
            // The card back may still be inside its seat hold; the flip itself is the reveal.
            SetShown(true);

            var startP = transform.localPosition;
            var startR = transform.localRotation;
            var startS = transform.localScale;
            var yaw = PlayerSide ? 0f : 180f;
            var liftP = new Vector3(startP.x, startP.y + 0.06f, startP.z);
            var endP = new Vector3(_targetLocalPos.x, ArPlaymatLayout.MonsterHoverY, _targetLocalPos.z);
            var endR = Quaternion.Euler(0f, yaw, 0f);
            var endS = ArPlaymatLayout.LiveVisualScale;

            // 1) Lift off the field so the flip reads
            yield return ArDiskMotion.VisibleLerp(transform, startP, liftP, startR, startR,
                startS, startS * 1.04f, ArDiskMotion.FlipSummonLift);

            // 2) Flip over in the set plane — art swaps at the edge-on midpoint
            yield return ArDiskMotion.FlipY(transform, () => RevealSetCardArt(db),
                ArDiskMotion.FlipSummonReveal);

            if (this == null) yield break;
            transform.localRotation = startR;
            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);

            // 3) Rotate / stand into Attack
            yield return ArDiskMotion.VisibleLerp(transform, liftP, endP, startR, endR,
                transform.localScale, endS, ArDiskMotion.FlipSummonTurn);

            FaceUp = true;
            Defense = false;
            _targetLocalPos = endP;
            _targetLocalRot = endR;
            _targetLocalScale = endS;
            SetShown(true);
            RebuildAsFaceUpHolo(db);
            PlaySummonFx();
            // Flip Summon into Attack: the card lies portrait under the monster, then fades.
            BeginSummonCardPlate();
            _flipSummoning = false;
            _spellCo = null;
        }

        /// <summary>
        /// Attacked while set (anime): the sideways set card (already visible if it was shown as the
        /// attack target) flips face-up in place, settles flat on the street where it BECOMES the
        /// Defense card plate, and the monster rises out of it into the braced Defense pose. The end
        /// state is the engine position (face-down monsters are always Defense) — never Attack art.
        /// </summary>
        IEnumerator BattleFlipCo(CardDatabase db)
        {
            _battleFlipping = true;
            _spawnAnimating = false;
            _holdForSeat = false;
            _targetReveal = false;
            FaceUp = false;
            Defense = true;
            DuelPresentationPacer.HoldBattleFlip(Card != null ? Card.Name : null);
            if (_body == null)
            {
                ClearChildren();
                BuildFaceDownSetCard(db);
            }

            // Sideways card back (no face info yet); the flip itself is the reveal.
            SetShown(true);

            var yaw = PlayerSide ? 0f : 180f;
            var endS = ArPlaymatLayout.LiveVisualScale;
            // Plate pose: root at the face-down monster height with Euler(90, yaw, 90·dir) and the set quad
            // (short × long) at identity == the DefenseCardPlate pose (PoseGroundPieces) — seamless swap.
            var plateP = new Vector3(_targetLocalPos.x, FaceDownMonsterY(), _targetLocalPos.z);
            var plateR = Quaternion.Euler(90f, yaw, 90f * PlateRollSign());
            var startP = transform.localPosition;
            var startR = transform.localRotation;

            // 1) Flip over along its long axis — face (full card) swaps in at the edge-on midpoint
            yield return ArDiskMotion.FlipY(transform, () => RevealSetCardArt(db),
                ArDiskMotion.BattleFlipReveal);

            if (this == null) yield break;
            transform.localRotation = startR;
            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);

            // 2) Settle flat on the street: this face-up card is the Defense plate
            yield return ArDiskMotion.VisibleLerp(transform, startP, plateP, startR, plateR,
                transform.localScale, endS, ArDiskMotion.BattleFlipRise * 0.35f);
            if (this == null) yield break;

            // 3) The monster rises out of the card into the braced Defense pose
            FaceUp = true;
            Defense = _destroyAfterFlip || Card == null || !StillInZone ||
                      Card.Position == BattlePosition.Defense;
            _targetLocalPos = new Vector3(_targetLocalPos.x, ArPlaymatLayout.MonsterHoverY, _targetLocalPos.z);
            _targetLocalRot = Quaternion.Euler(0f, yaw, 0f);
            _targetLocalScale = endS;
            SetShown(true);
            RebuildAsFaceUpHolo(db);
            _artRise.Snap(0f);
            _artRise.Go(1f, ArtRiseSeconds);
            _artDirty = true;
            ApplyArtPose();
            PlaySummonFx();
            if (Defense)
                ArArenaCombatFx.PlayDefendShield(transform.position, PlayerSide);
            _battleFlipping = false;
            if (!_destroyAfterFlip)
            {
                _spellCo = null;
                yield break;
            }

            // Destroyed during / by this battle (PlayBattleFlipThenDestroy): let the monster finish rising
            // braced over its card, then break.
            var until = Time.unscaledTime + ArDiskMotion.BattleFlipDestroyHold;
            while (this != null && Time.unscaledTime < until)
                yield return null;
            if (this == null) yield break;
            _spellCo = null;
            ShatterAfterFlip();
        }

        /// <summary>
        /// The monster left the field by battle while face-down (flip + destroy in one engine tick, or
        /// destroyed while its in-zone battle flip is still running). Plays / finishes the arena battle
        /// flip (card → Defense plate → braced monster), holds the braced pose a beat, then shatters.
        /// The shatter spec is TAKEN now: the arena owns this shatter, and the disk card (which checks the
        /// queue after its own flip + leave, ~1.34 s) then flies to the GY instead of winning the race and
        /// leaving this braced monster to vanish without shards.
        /// </summary>
        public void PlayBattleFlipThenDestroy()
        {
            if (this == null) return;
            if (!_destroyAfterFlip)
            {
                _hasFlipDestroySpec = CardShatterPresentation.TryTake(InstanceId, out _flipDestroySpec);
                _destroyAfterFlip = true;
            }

            StillInZone = false;
            // Already flipping in-zone (destroyed within its flip): that BattleFlipCo ends with the hold +
            // shatter (was: early return, leaving a braced monster on an empty zone forever).
            if (_battleFlipping) return;
            _battleFlipping = true;
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = StartCoroutine(BattleFlipCo(_db));
        }

        void ShatterAfterFlip()
        {
            if (_hasFlipDestroySpec)
            {
                _hasFlipDestroySpec = false;
                ArCardShatterFx.PlayAndDestroyVisual(gameObject, _flipDestroySpec, _layer);
            }
            else
                ArCardShatterFx.ConsumeQueuedOrQuietDestroy(gameObject, InstanceId, _layer);
        }

        /// <summary>Keep the set-card body; swap back texture → full face art mid-flip.</summary>
        void RevealSetCardArt(CardDatabase db)
        {
            FaceUp = true;
            Defense = true;
            if (_faceMat == null) return;
            Texture tex = Texture2D.whiteTexture;
            if (db != null && CardId != 0)
            {
                var full = db.GetArt(CardId);
                if (full != null && full.texture != null)
                    tex = full.texture;
            }

            CardArtFocus.ApplyToMaterial(_faceMat, tex, cropToArtwork: false);
            ArFieldMaterials.ForceDoubleSided(_faceMat);
            var col = ArAnimePresentation.HoloFaceColor();
            if (_faceMat.HasProperty("_BaseColor")) _faceMat.SetColor("_BaseColor", col);
            if (_faceMat.HasProperty("_Color")) _faceMat.SetColor("_Color", col);
            if (_faceMr != null)
                _faceMr.sharedMaterial = _faceMat;
        }

        void RebuildAsFaceUpHolo(CardDatabase db)
        {
            ClearChildren();
            BuildBody(db);
            BuildSummonRing();
            EnsurePickCollider();
            transform.localPosition = _targetLocalPos;
            transform.localRotation = _targetLocalRot;
            transform.localScale = _targetLocalScale;
            if (IsMonster && FaceUp && Defense)
                SetDefenseDisplay(true, animate: false);
            BindEffectCallout();
        }

        Quaternion ComputePoseRotation()
        {
            var yaw = IsMonster
                ? (PlayerSide ? 0f : 180f)
                : SpellControllerYaw(PlayerSide);
            if (!FaceUp)
            {
                // Monster set = flat, landscape (anime: the card lies sideways on the field) — exactly
                // the Defense plate pose, so an attack-target reveal / battle flip reads as that card.
                // S/T set = portrait with a slight tilt, same yaw as the upright activate pose so the
                // hinge is a clean pitch stand-up. Either way only the card back shows (RestAsSetCard).
                if (IsMonster)
                    return Quaternion.Euler(90f, yaw, 90f * PlateRollSign());
                SpellLiePose(SpellZone(), out _, out var lieRot);
                return lieRot;
            }

            // Face-up monsters stand upright in BOTH positions; Defense reads through the braced art
            // over its sideways card (DefenseCardPlate), never a rolled-over artwork.
            if (!IsMonster)
            {
                SpellStandPose(SpellZone(), out _, out var standRot);
                return standRot;
            }

            return Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Root height of a face-down monster: its card lies at the Defense plate height.</summary>
        static float FaceDownMonsterY() =>
            ArPlaymatLayout.MonsterHoverY + CardPlateY * ArPlaymatLayout.LiveHoloScale;

        Vector3 ApplyFaceDownHover(Vector3 zoneLocal)
        {
            if (!FaceUp)
                return IsMonster
                    ? new Vector3(zoneLocal.x, FaceDownMonsterY(), zoneLocal.z)
                    : new Vector3(zoneLocal.x, SpellSetLieHeight, zoneLocal.z);
            if (!IsMonster)
            {
                SpellStandPose(zoneLocal, out var standPos, out _);
                return standPos;
            }

            return zoneLocal;
        }

        /// <summary>
        /// Face-up monsters billboard toward the AR camera.
        /// Face-down sets stay flat.
        /// Face-up Spells/Traps keep the pose the activate sequence wrote
        /// (present toward the controller, or settled on-zone if lingering).
        /// </summary>
        public void FaceCamera(Camera cam)
        {
            if (cam != null) _billboardCam = cam;
            if (cam == null || _spawnAnimating || _flipSummoning || _battleFlipping) return;
            if (SpellActivateSequenceActive)
            {
                // Disk-turn wait / rise / present / hover / fade owns pose — do not snap to the zone
                // (Fading used to drop the card 0.4 m and un-lean it as the fade began).
                return;
            }

            if (!FaceUp)
            {
                transform.localRotation = _targetLocalRot;
                transform.localPosition = _targetLocalPos;
                return;
            }

            if (!IsMonster)
            {
                SpellStandPose(SpellZone(), out var standPos, out var standRot);
                transform.localRotation = standRot;
                transform.localPosition = standPos;
                _targetLocalRot = standRot;
                _targetLocalPos = standPos;
                return;
            }

            // Billboard so card local +Z (art normal on Unity quads) faces the camera.
            // LookRotation(-toCam) pointed art AWAY from the camera → backface / blank square.
            var toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 1e-6f) return;
            // Defense stays upright too (braced art); the sideways card lies under it in the arena frame.
            transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            if (transform.parent != null)
                transform.localPosition = _targetLocalPos;
            ApplyBraceOffset();
            ApplyArtPose();
            PoseGroundPieces();
        }

        /// <summary>
        /// Arena-axis yaw of an S/T hologram when no viewer is known (Editor checks): the art reads from local
        /// −Z and the back from +Z, so 180° on the player's side (arena −Z) shows the local duelist the card
        /// back and the opponent the art; 0° on the opponent's side shows the local duelist the art.
        /// </summary>
        public static float SpellControllerYaw(bool playerSide) => playerSide ? 180f : 0f;

        // ── Upright S/T: the hinge (owner 2026-09-29: "The card should simply rotate upward, back facing the
        //    player and artwork facing the opponent"; then "the spell/trap zones need to be straightened out") ──
        // A Set card lies square on its zone, along the arena. Activating, it just tips up about the middle of its
        // edge on its controller's side and stands turned to the local viewer: your own card shows you its back,
        // the opponent's card shows you its art. No rise, no slide toward the camera, no scale-up. (Standing
        // along the arena, a card in a side column read as a thin edge from the player's seat; lying turned to
        // the viewer, the Set cards beside the player lay crooked on their zones.)

        /// <summary>
        /// How far an upright S/T's top tips away from the viewer, so the side facing the viewer (your card's
        /// back, the opponent's art) tilts up toward the eye.
        /// </summary>
        public const float SpellStandLeanDeg = 12f;

        /// <summary>Centre height of a face-down S/T lying on its zone; its hinge edge sits at this height.</summary>
        public const float SpellSetLieHeight = FaceDownHoverHeight + 0.04f;

        /// <summary>Height of the S/T card body in its own units (the root carries the live holo scale).</summary>
        static float SpellCardLocalHeight => CardArtFocus.SpellTrapArtworkScale.y;

        /// <summary>
        /// Lying (Set) and standing poses of a S/T on <paramref name="zone"/> (parent-local). It lies square to the
        /// arena: your own card back-up, top toward the opponent, hinging on its near edge; the opponent's art-up,
        /// top toward you, hinging on its far edge (their side). It stands turned to the horizontal direction
        /// <paramref name="towardViewer"/>, top tipped away, pivoting on the middle of that hinge edge.
        /// </summary>
        static void SpellHingePoses(Vector3 zone, Vector3 towardViewer, bool playerSide, float scale,
            out Vector3 liePos, out Quaternion lieRot, out Vector3 standPos, out Quaternion standRot)
        {
            var f = new Vector3(towardViewer.x, 0f, towardViewer.z);
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.back;
            var lean = SpellStandLeanDeg * Mathf.Deg2Rad;
            var facing = f * Mathf.Cos(lean) + Vector3.up * Mathf.Sin(lean);
            var top = Vector3.up * Mathf.Cos(lean) - f * Mathf.Sin(lean);
            // The back is local +Z and the art local −Z (Unity quads read from −Z).
            standRot = Quaternion.LookRotation(playerSide ? facing : -facing, top);
            // Lying square on the zone: the local duelist's side of the arena is −Z.
            var grid = Vector3.back;
            lieRot = playerSide
                ? Quaternion.LookRotation(Vector3.up, -grid)
                : Quaternion.LookRotation(Vector3.down, grid);
            liePos = new Vector3(zone.x, SpellSetLieHeight, zone.z);
            var half = 0.5f * SpellCardLocalHeight * scale;
            var hingeEdge = liePos + lieRot * new Vector3(0f, -half, 0f);
            standPos = hingeEdge + standRot * new Vector3(0f, half, 0f);
        }

        /// <summary>Zone this S/T's poses are built on (its column, even while it stands off-centre).</summary>
        Vector3 SpellZone() => _hasZone ? _zoneLocal : _targetLocalPos;

        Vector3 SpellZoneForActivation()
        {
            var z = SpellZone();
            return new Vector3(z.x, 0.08f, z.z);
        }

        /// <summary>
        /// Horizontal direction the activated S/T stands square to, in the card's parent (arena) space: the arena's
        /// −Z axis (the local duelist's side), the same for every column, so your own card shows you its back and
        /// the opponent its art, and the opponent's card shows you its art. Owner 2026-10-04: the S/T row is
        /// grouped tight "so that the artwork of all activated spells and traps are facing the opponent". It used to
        /// turn each zone to the stage camera, which swung your side cards' art out toward the sidelines.
        /// </summary>
        static Vector3 ViewFlat(Vector3 zone) => Vector3.back;

        void SpellLiePose(Vector3 zone, out Vector3 pos, out Quaternion rot) =>
            SpellHingePoses(zone, ViewFlat(zone), PlayerSide, ArPlaymatLayout.LiveHoloScale,
                out pos, out rot, out _, out _);

        void SpellStandPose(Vector3 zone, out Vector3 pos, out Quaternion rot) =>
            SpellHingePoses(zone, ViewFlat(zone), PlayerSide, ArPlaymatLayout.LiveHoloScale,
                out _, out _, out pos, out rot);

        /// <summary>Upright pose of an activated S/T on <paramref name="zoneLocal"/>, seen from the local duelist's side.</summary>
        public static Quaternion SpellActivatePresentRotation(bool playerSide)
        {
            SpellHingePoses(Vector3.zero, Vector3.back, playerSide, 1f, out _, out _, out _, out var rot);
            return rot;
        }

        /// <summary>
        /// Centre of an activated S/T standing on <paramref name="zoneLocal"/>, seen from the local duelist's side
        /// (arena −Z): above the zone and shifted toward its controller, since it stands on that edge.
        /// </summary>
        public static Vector3 SpellActivatePresentLocal(Vector3 zoneLocal, bool playerSide)
        {
            SpellHingePoses(zoneLocal, Vector3.back, playerSide, ArPlaymatLayout.LiveHoloScale,
                out _, out _, out var pos, out _);
            return pos;
        }

        IEnumerator ActivateAfterDiskTurn(Vector3 zoneLocal)
        {
            _spellZoneLocal = zoneLocal;
            // The disk ejects the Set card and turns it. The arena image starts
            // once that turn is past halfway, not on the engine frame.
            ActivationBeat.NoteSequenceStarted(InstanceId);
            if (Application.isPlaying)
            {
                // Chain built in one engine call: a later link rises only after the previous link's
                // art has been readable (links revealed in order, not all at once).
                var startAt = Mathf.Max(Time.unscaledTime + SpellActivationPresentation.ArenaStartDelaySeconds,
                    ActivationBeat.RevealStartFloor(InstanceId));
                while (this != null && Time.unscaledTime < startAt)
                    yield return null;
            }

            if (this == null) yield break;
            SetShown(true);
            // The signature Solid Vision FX (Raigeki bolts, Trap Hole pit …) plays once, at the art reveal inside
            // OneShotRiseActivateCo — it used to fire here AND again at the end of the rise.
            yield return SpellActivateSequenceCo(zoneLocal);
        }

        IEnumerator SpellActivateSequenceCo(Vector3 zoneLocal)
        {
            // One-shot and lingering: pop off the pad, present toward the controller.
            // Linger later settles on-zone; one-shots fade from the present pose.
            yield return OneShotRiseActivateCo(zoneLocal);
            _spellCo = null;
        }

        /// <summary>
        /// Activate: the Set card hinges straight up on its zone (<see cref="SpellHingePoses"/>) and holds there
        /// while the effect resolves; a card played from the hand appears already standing.
        /// </summary>
        IEnumerator OneShotRiseActivateCo(Vector3 zoneLocal)
        {
            _spellSeq = SpellSeqPhase.RisingReveal;
            _spellArtRevealed = false;
            if (_body == null)
            {
                var savedFace = FaceUp;
                FaceUp = false;
                ClearChildren();
                BuildBody(_db);
                FaceUp = savedFace;
            }

            var zone = new Vector3(zoneLocal.x, 0f, zoneLocal.z);
            SpellHingePoses(zone, ViewFlat(zone), PlayerSide, ArPlaymatLayout.LiveHoloScale,
                out var liePos, out var lieRot, out var presentPos, out var presentRot);
            var endScale = ArPlaymatLayout.LiveVisualScale;
            var half = 0.5f * SpellCardLocalHeight * ArPlaymatLayout.LiveHoloScale;
            var hingeEdge = liePos + lieRot * new Vector3(0f, -half, 0f);
            var riseDur = Mathf.Max(0.85f, SpellActivationPresentation.RiseSeconds);
            var midReveal = _spellArtRevealed;

            if (_activatedFromHand)
            {
                // Played from the hand: materialize standing, art already out.
                transform.localPosition = presentPos;
                transform.localRotation = presentRot;
                RevealSpellArtUpright(presentPos, presentRot);
                PlaySpellCastAtZone(zoneLocal);
                midReveal = true;
                const float appear = 0.35f;
                var ta = 0f;
                while (ta < appear)
                {
                    ta += Time.deltaTime;
                    var pe = 1f - Mathf.Pow(1f - Mathf.Clamp01(ta / appear), 2f);
                    transform.localScale = Vector3.Lerp(endScale * 0.7f, endScale, pe);
                    yield return null;
                }
            }
            else
            {
                // Lie square on the zone first if the card is not already there (a relayout, or the viewer's
                // side was not known yet when it was Set).
                var fromPos = transform.localPosition;
                var fromRot = transform.localRotation;
                var fromScale = transform.localScale;
                if (Quaternion.Angle(fromRot, lieRot) > 2f || (fromPos - liePos).sqrMagnitude > 0.0004f ||
                    (fromScale - endScale).sqrMagnitude > 1e-4f)
                {
                    const float square = 0.18f;
                    var ts = 0f;
                    while (ts < square)
                    {
                        ts += Time.deltaTime;
                        var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ts / square));
                        transform.localPosition = Vector3.Lerp(fromPos, liePos, e);
                        transform.localRotation = Quaternion.Slerp(fromRot, lieRot, e);
                        transform.localScale = Vector3.Lerp(fromScale, endScale, e);
                        yield return null;
                    }
                }

                // The hinge: turn about the controller-side edge, nothing else moves. The art is revealed at the
                // point ActivationBeat assumes it is readable (RiseArtRevealFraction of the rise).
                var t = 0f;
                while (t < riseDur)
                {
                    t += Time.deltaTime;
                    var u = Mathf.Clamp01(t / riseDur);
                    var e = Mathf.SmoothStep(0f, 1f, u);
                    var r = Quaternion.Slerp(lieRot, presentRot, e);
                    transform.localRotation = r;
                    transform.localPosition = hingeEdge + r * new Vector3(0f, half, 0f);
                    transform.localScale = endScale;
                    if (!midReveal && u >= SpellActivationPresentation.RiseArtRevealFraction)
                    {
                        midReveal = true;
                        RevealSpellArtUpright(presentPos, presentRot);
                        PlaySpellCastAtZone(zoneLocal);
                    }

                    yield return null;
                }
            }

            transform.localPosition = presentPos;
            transform.localRotation = presentRot;
            transform.localScale = endScale;
            _targetLocalScale = endScale;
            _targetLocalPos = presentPos;
            _targetLocalRot = presentRot;
            if (!midReveal)
            {
                RevealSpellArtUpright(presentPos, presentRot);
                PlaySpellCastAtZone(zoneLocal);
            }

            _spellSeq = SpellSeqPhase.ActiveHover;
            RefreshSpellCounters();

            // Hold the present pose on the beat clock: results are shown at ActivationBeat.ResultAt
            // (art readable + ReadBeforeResult), the card stays ReadAfterResult longer, and the
            // total art-readable window is never below MinHover / OpponentReadHover.
            var legacyNeed = PlayerSide
                ? SpellActivationPresentation.MinHoverSeconds
                : SpellActivationPresentation.OpponentReadHoverSeconds;
            var hover = 0f;
            while (true)
            {
                // Negated / destroyed while presenting (chain): shatter at the tagged result beat. A
                // destroyed link that still resolves shows its own effect first (IsShatterReleased).
                if (!StillInZone && CardShatterPresentation.HasQueued(InstanceId) &&
                    ActivationBeat.IsShatterReleased(InstanceId))
                {
                    SpellActivationPresentation.DropActivationEvent(InstanceId);
                    _spellCo = null;
                    ArCardShatterFx.ConsumeQueuedOrQuietDestroy(gameObject, InstanceId, _layer);
                    yield break;
                }

                if (ActivationBeat.TryGetFadeAt(InstanceId, StillInZone && !_isGhost, out var fadeAt))
                {
                    if (Time.unscaledTime >= fadeAt) break;
                }
                else if (hover >= legacyNeed)
                    break;

                hover += Time.deltaTime;
                // Stands still on its hinge edge while the effect resolves (no bob: it is a card, not a float).
                transform.localPosition = presentPos;
                transform.localRotation = presentRot;
                RefreshSpellCounters();
                yield return null;
            }

            var stays = SpellActivationPresentation.TryGet(InstanceId, out var stayEv) && stayEv.StaysOnField;
            bool fade;
            if (_isGhost || !StillInZone)
                fade = true; // the card is no longer on the field (GY / bounced / ghost)
            else if (stays)
                fade = false; // Continuous / Equip / Swords: settle over the zone
            else
                fade = _fadeRequested || SpellActivationPresentation.WantsFadeToGy(InstanceId) ||
                       !SpellActivationPresentation.HasActivation(InstanceId);

            if (fade)
                yield return FadeToGyCo();
            else
            {
                yield return SettleActivatedToZoneCo(zoneLocal);
                // A lingering card is now an ordinary face-up S/T: a later MST / expiry shatters or
                // fades it like any other card (was: quiet vanish, and a disk toaster replay).
                if (stays)
                    SpellActivationPresentation.Complete(InstanceId);
            }
        }

        /// <summary>
        /// The engine undid this Set card's activation (back face-down, activation dropped): stop the
        /// rise / present and lay the card back on its zone again.
        /// </summary>
        void AbortActivationToSet(CardDatabase db)
        {
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = null;
            _spellSeq = SpellSeqPhase.None;
            _spellActivateStarted = false;
            _spellArtRevealed = false;
            _fadeRequested = false;
            _holdForSeat = false;
            FaceUp = false;
            var basePos = new Vector3(_spellZoneLocal.x, ArPlaymatLayout.SpellTrapHoverY, _spellZoneLocal.z);
            _targetLocalPos = ApplyFaceDownHover(basePos);
            _targetLocalRot = ComputePoseRotation();
            _targetLocalScale = ArPlaymatLayout.LiveVisualScale;
            ClearChildren();
            BuildBody(db);
            EnsurePickCollider();
            RestAsSetCard();
        }

        IEnumerator SettleActivatedToZoneCo(Vector3 zoneLocal)
        {
            var fromPos = transform.localPosition;
            var fromRot = transform.localRotation;
            var fromScale = transform.localScale;
            // A card that stays (Continuous / Equip) simply keeps standing where it hinged up.
            SpellStandPose(new Vector3(zoneLocal.x, 0f, zoneLocal.z), out var toPos, out var toRot);
            var toScale = ArPlaymatLayout.LiveVisualScale;
            var dur = SpellActivationPresentation.SettleSeconds;
            var t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                var u = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                transform.localPosition = Vector3.Lerp(fromPos, toPos, u);
                transform.localRotation = Quaternion.Slerp(fromRot, toRot, u);
                transform.localScale = Vector3.Lerp(fromScale, toScale, u);
                yield return null;
            }

            transform.localPosition = toPos;
            transform.localRotation = toRot;
            transform.localScale = toScale;
            _targetLocalScale = toScale;
            _targetLocalPos = toPos;
            _targetLocalRot = toRot;
            _spellSeq = SpellSeqPhase.Settled;
        }

        IEnumerator GhostActivateThenFadeCo(Vector3 zoneLocal)
        {
            _fadeRequested = true;
            yield return SpellActivateSequenceCo(zoneLocal);
        }

        void RevealSpellArtUpright(Vector3 localPos, Quaternion localRot)
        {
            if (_spellArtRevealed) return;
            _spellArtRevealed = true;
            FaceUp = true;
            ClearChildren();
            BuildBody(_db);
            _targetLocalPos = localPos;
            _targetLocalRot = localRot;
            CollectFadeMaterials();
            // No CardFlip3dAnimator here: it found no named face child, spun the whole root 180° (back to
            // the viewer), thinned it for 0.85 s right when the art first became readable, and left the
            // card at ~90% scale. The rise's hinge already reads as the flip.
            BindEffectCallout();
            ActivationBeat.NoteArtRevealed(InstanceId);
        }

        IEnumerator FadeToGyCo()
        {
            _spellSeq = SpellSeqPhase.Fading;
            CollectFadeMaterials();
            var dur = SpellActivationPresentation.FadeToGySeconds;
            var t = 0f;
            var startScale = transform.localScale;
            while (t < dur)
            {
                t += Time.deltaTime;
                var u = Mathf.Clamp01(t / dur);
                var a = 1f - u;
                ApplyFadeAlpha(a * 0.95f);
                transform.localScale = startScale * Mathf.Lerp(1f, 0.55f, u);
                transform.localPosition += Vector3.up * (Time.deltaTime * 0.12f);
                yield return null;
            }

            SpellActivationPresentation.Complete(InstanceId);
            ArObjectUtil.Destroy(gameObject);
        }

        void CollectFadeMaterials()
        {
            _fadeMats.Clear();
            var mrs = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in mrs)
            {
                if (mr == null) continue;
                if (mr.GetComponentInParent<ArEffectCallout>() != null) continue;
                // Instance materials so we don't tint shared assets
                var mats = mr.materials;
                foreach (var m in mats)
                {
                    if (m != null) _fadeMats.Add(m);
                }

                mr.materials = mats;
            }
        }

        void ApplyFadeAlpha(float a)
        {
            a = Mathf.Clamp01(a);
            foreach (var m in _fadeMats)
            {
                if (m == null) continue;
                if (m.HasProperty("_BaseColor"))
                {
                    var c = m.GetColor("_BaseColor");
                    c.a = a;
                    m.SetColor("_BaseColor", c);
                }

                if (m.HasProperty("_Color"))
                {
                    var c = m.GetColor("_Color");
                    c.a = a;
                    m.SetColor("_Color", c);
                }

                // Transparent surface for fade
                if (m.HasProperty("_Surface"))
                    m.SetFloat("_Surface", 1f);
            }
        }

        /// <summary>
        /// Spell counter pips above the hologram.
        ///
        /// The count comes from the live <see cref="CardInstance.Counters"/>, not from
        /// <c>SpellActivationPresentation</c>: that bus only has an entry for a card that registered a
        /// Spell/Trap activation, so an Effect Monster like Magical Marionette or Royal Magical Library
        /// would always read 0 there and never show a pip. The bus is still the authority for a
        /// Spell/Trap mid-activation beat, where the engine can add a counter before the next Sync.
        /// </summary>
        void RefreshSpellCounters(CardInstance card = null)
        {
            var live = card != null ? card : Card;
            var n = !IsMonster && SpellActivationPresentation.HasActivation(InstanceId)
                ? SpellActivationPresentation.GetSpellCounters(InstanceId)
                : (live != null ? live.Counters : 0);
            SetSpellCounterCount(n);
        }

        void SetSpellCounterCount(int n)
        {
            if (n == _spellCounters) return;
            _spellCounters = n;
            RebuildCounterBadge();
        }

        void RebuildCounterBadge()
        {
            if (_counterBadge != null)
            {
                ArObjectUtil.Destroy(_counterBadge.gameObject);
                _counterBadge = null;
            }

            // Counters on a face-down card are hidden information: never draw pips for one.
            if (_spellCounters <= 0 || !FaceUp || _spellSeq == SpellSeqPhase.Fading) return;

            var root = new GameObject("SpellCounters").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, 0.42f, 0f);
            _counterBadge = root;

            var count = Mathf.Min(_spellCounters, 12);
            for (var i = 0; i < count; i++)
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Counter_" + i;
                pip.transform.SetParent(root, false);
                ArObjectUtil.Destroy(pip.GetComponent<Collider>());
                pip.layer = _layer;
                var col = count <= 1 ? 0 : i - (count - 1) * 0.5f;
                pip.transform.localPosition = new Vector3(col * 0.07f, 0f, 0f);
                pip.transform.localScale = Vector3.one * 0.045f;
                ArFieldMaterials.Apply(pip.GetComponent<MeshRenderer>(),
                    new Color(0.35f, 0.85f, 1f, 0.92f));
            }
        }

        void ApplyFaceTexture(CardDatabase db)
        {
            if (_faceMat == null) return;

            if (FaceUp && CardId != 0 && db != null)
            {
                // Illustration crop for arena holos (player + opponent — same path)
                if (IsMonster)
                    CardArtFocus.ApplyMonsterArtwork(_faceMat, db, CardId);
                else
                {
                    var full = db.GetArt(CardId);
                    var tex = full != null ? full.texture : Texture2D.whiteTexture;
                    CardArtFocus.ApplyToMaterial(_faceMat, tex, cropToArtwork: false);
                }

                // If crop path left a missing map, fall back to full face (still better than white square)
                var map = _faceMat.HasProperty("_BaseMap")
                    ? _faceMat.GetTexture("_BaseMap")
                    : (_faceMat.HasProperty("_MainTex") ? _faceMat.GetTexture("_MainTex") : null);
                if (map == null || map == Texture2D.whiteTexture)
                {
                    var full = db.GetArt(CardId);
                    if (full != null && full.texture != null)
                        CardArtFocus.ApplyToMaterial(_faceMat, full.texture, cropToArtwork: IsMonster);
                }

                if (IsMonster)
                    ArFieldMaterials.ForceDoubleSided(_faceMat);
                else
                    ArFieldMaterials.ForceFrontFacesOnly(_faceMat);
                var col = ArAnimePresentation.HoloFaceColor();
                if (_faceMat.HasProperty("_BaseColor")) _faceMat.SetColor("_BaseColor", col);
                if (_faceMat.HasProperty("_Color")) _faceMat.SetColor("_Color", col);

                if (_faceMr != null)
                    _faceMr.sharedMaterial = _faceMat;
                if (!IsMonster)
                    ApplySpellTrapCardBack();
            }
            else
            {
                var back = ResolveCardBackTexture();
                CardArtFocus.ApplyToMaterial(_faceMat, back, cropToArtwork: false);
            }
        }

        float _seatHold;
        bool _holdForSeat;

        /// <summary>True when a sequence that owns visibility started during the disk-seat hold.</summary>
        bool SeatHoldSuperseded() =>
            _flipSummoning || _battleFlipping || _targetReveal || _isGhost ||
            (!IsMonster && (_spellActivateStarted || SpellActivateSequenceActive)) ||
            _spellCo != null;

        /// <summary>
        /// Face-down: the card back lies on its zone pose (monsters sideways), shown and pickable for
        /// targeting, never its face. A later flip / rise starts from here.
        /// </summary>
        void RestAsSetCard()
        {
            SetShown(true);
            _targetReveal = false;
            _spawnAnimating = false;
            _pendingHoloFx = false;
            transform.localPosition = _targetLocalPos;
            transform.localRotation = _targetLocalRot;
            transform.localScale = _targetLocalScale;
        }

        /// <summary>
        /// A face-down Defense monster declared as an attack target: make sure its sideways CARD BACK is up
        /// on its zone (it may still be inside its disk-seat hold) so the charge beams, defend shield and
        /// hit flash have a visible target. It flips at the damage step (battle flip).
        /// </summary>
        public void RevealAsAttackTarget()
        {
            if (!IsMonster || FaceUp || _flipSummoning || _battleFlipping || _isGhost || !StillInZone) return;
            if (_targetReveal && _shown) return;
            _targetReveal = true;
            _holdForSeat = false;
            if (_body == null)
            {
                ClearChildren();
                BuildFaceDownSetCard(_db);
            }

            _targetLocalRot = ComputePoseRotation();
            _targetLocalPos = ApplyFaceDownHover(_targetLocalPos);
            transform.localPosition = _targetLocalPos;
            transform.localRotation = _targetLocalRot;
            transform.localScale = _targetLocalScale;
            SetShown(true);
        }

        /// <summary>
        /// The attack on this face-down monster ended without flipping it (attacker destroyed by a
        /// response, battle skipped / cancelled): it rests on its zone as its card back again.
        /// </summary>
        public void EndAttackTargetReveal()
        {
            if (!_targetReveal) return;
            _targetReveal = false;
            if (FaceUp || _flipSummoning || _battleFlipping || _isGhost) return;
            // The engine flipped it (damage step) — the Sync that follows plays the battle flip from here.
            if (Card != null && Card.FaceUp) return;
            // Left the field: Purge decides (quiet removal for a still-hidden card).
            if (!StillInZone) return;
            RestAsSetCard();
        }

        void SetShown(bool on)
        {
            // Every renderer kind: shell GLBs, stat gauges, callout text meshes and particles must all
            // wait for the disk seat, not only MeshRenderers.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = on;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = on;
            _shown = on;
        }

        /// <summary>True during the disk → street fly-in.</summary>
        public bool IsSpawning => _spawnAnimating;

        /// <summary>Local scale at rest (ignores spawn pop and hit punch).</summary>
        public Vector3 RestingLocalScale => _targetLocalScale;

        /// <summary>Local position at rest: the landing zone while spawning.</summary>
        public Vector3 RestingLocalPosition => _targetLocalPos;

        /// <summary>Re-tint face/back when the Field Spell wash changes (alpha kept for fades).</summary>
        void ApplyFieldWash()
        {
            _washVersion = ArAnimePresentation.FieldWashVersion;
            var col = ArAnimePresentation.HoloFaceColor();
            TintTextured(_faceMat, col);
            if (_backMat != _faceMat) TintTextured(_backMat, col);
        }

        static void TintTextured(Material m, Color col)
        {
            if (m == null) return;
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            // Placeholder accent quads (no art yet) keep their colour.
            if (tex == null || tex == Texture2D.whiteTexture) return;
            if (m.HasProperty("_BaseColor"))
            {
                col.a = m.GetColor("_BaseColor").a;
                m.SetColor("_BaseColor", col);
            }

            if (m.HasProperty("_Color"))
            {
                col.a = m.GetColor("_Color").a;
                m.SetColor("_Color", col);
            }
        }

        void Update()
        {
            if (_holdForSeat)
            {
                _seatHold -= Time.deltaTime;
                if (_seatHold > 0f) return;
                _holdForSeat = false;
                // A flip / activation / attack-target reveal that started inside the hold owns visibility and
                // pose now (Normal Spell set + activated within the slot seat, monster flipped right after its
                // set): spawn-animating here would cut into that sequence.
                if (SeatHoldSuperseded())
                    return;
                // Face-up art or a face-down set's card back (either side): the same materialize.
                SetShown(true);
                _spawnAnimating = true;
                _spawnT = 0f;
                _spawnStartWorld = transform.position;
                _spawnStartScale = transform.localScale;
                _spawnStartLocalRot = transform.localRotation;
                _pendingHoloFx = true;
            }

            // Field Spell wash (ArFieldSpellFloor) re-tints face/back; alpha kept for fades.
            if (_washVersion != ArAnimePresentation.FieldWashVersion)
                ApplyFieldWash();

            // Summon ring spin
            if (_ring != null)
                _ring.Rotate(Vector3.up, 90f * Time.deltaTime, Space.Self);

            if (!_spawnAnimating) return;
            _spawnT += Time.deltaTime / SpawnDuration;
            var t = Mathf.Clamp01(_spawnT);
            var e = 1f - Mathf.Pow(1f - t, 3f);

            if (_pendingHoloFx && t > 0.05f)
            {
                _pendingHoloFx = false;
                PlayHoloSpawnFx();
            }

            var targetWorld = (transform.parent != null
                ? transform.parent.TransformPoint(_targetLocalPos)
                : _targetLocalPos) + BraceOffsetWorld(); // a braced summon lands on its card's far edge
            transform.position = Vector3.Lerp(_spawnStartWorld, targetWorld, e);
            transform.localRotation = Quaternion.Slerp(_spawnStartLocalRot, _targetLocalRot, e);
            // Overshoot pop on spawn
            var scalePop = 1f + 0.18f * Mathf.Sin(e * Mathf.PI);
            transform.localScale = Vector3.Lerp(_spawnStartScale, _targetLocalScale * scalePop, e);

            if (t >= 1f)
            {
                _spawnAnimating = false;
                transform.localPosition = _targetLocalPos;
                transform.localRotation = _targetLocalRot;
                transform.localScale = _targetLocalScale;
                ApplyBraceOffset();
                if (_pendingHoloFx)
                {
                    _pendingHoloFx = false;
                    PlayHoloSpawnFx();
                }
            }
        }

        /// <summary>
        /// Card plate / braced pose / shield glint. Runs after the hologram manager's billboard in most
        /// frames; FaceCamera also re-poses the ground pieces, so the order does not matter.
        /// </summary>
        void LateUpdate()
        {
            _brace.Tick();
            _artRise.Tick();
            _plateAlpha.Tick();
            _plateTurn.Tick();
            _plateRise.Tick();
            if (_plateFadeAt > 0f && Time.unscaledTime >= _plateFadeAt)
            {
                // Summon card read-hold over: fade it, the monster keeps standing.
                _plateFadeAt = -1f;
                _plateAlpha.Go(0f, SummonPlateFadeSeconds);
            }

            // Braced: stand at the card's far edge from the camera (before the art / glint pose).
            ApplyBraceOffset();
            // Every frame for face-up monsters: the on-card base follows the camera-driven root pitch.
            if (_artDirty || (IsMonster && FaceUp))
            {
                _artDirty = false;
                ApplyArtPose();
            }

            if (_plate != null)
            {
                ApplyPlateAlpha();
                if (!_plateAlpha.Active && _plateAlpha.To <= 0f && _plateAlpha.V <= 0.001f &&
                    _plateFadeAt < 0f && !(IsMonster && FaceUp && Defense))
                    DestroyPlate();
            }

            if (_defendShield != null && !(IsMonster && FaceUp && Defense) && !_brace.Active && _brace.V <= 0.001f)
                ClearDefendShield();
            TickDefendGlint();
            PoseGroundPieces();
        }

        void OnDestroy()
        {
            // Materials this visual created (plate, glow, glint). Shared ArFieldMaterials caches are not ours.
            DestroyOwnedMaterial(ref _plateMat);
            DestroyOwnedMaterial(ref _plateGlowMat);
            DestroyOwnedMaterial(ref _defendMat);
            DestroyOwnedMaterial(ref _defendStreakMat);
        }

        void BindEffectCallout()
        {
            ArEffectCallout.Ensure(this);
            if (IsMonster)
            {
                ArMonsterStatGauge.Ensure(this);
                ArFieldTerrainPad.Ensure(this);
            }
        }

        void ClearChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.GetComponent<ArMonsterStatGauge>() != null) continue;
                if (child.GetComponent<ArFieldTerrainPad>() != null) continue;
                ArObjectUtil.Destroy(child);
            }
            // Plate / glow / glint GOs went with the children; drop refs + our materials and reset state
            // (callers re-apply SetDefenseDisplay / BeginSummonCardPlate after the rebuild).
            _plate = null;
            _plateGlow = null;
            DestroyOwnedMaterial(ref _plateMat);
            DestroyOwnedMaterial(ref _plateGlowMat);
            DestroyOwnedMaterial(ref _defendMat);
            DestroyOwnedMaterial(ref _defendStreakMat);
            _defendStreak = null;
            _plateIsDefense = false;
            _plateFadeAt = -1f;
            _lastPlateAlpha = -1f;
            _plateAlpha.Snap(0f);
            _plateTurn.Snap(0f);
            _plateRise.Snap(0f);
            _brace.Snap(0f);
            _artRise.Snap(1f);
            _art2D = false;
            _artFront = null;
            _artBack = null;
            _lastTintBrace = -1f;
            _bodyBaseScale = Vector3.one;
            _bodyBasePos = Vector3.zero;
            _plateHasFace = true;
            _shellRig = null;
            _body = null;
            _ring = null;
            _faceMr = null;
            _faceMat = null;
            _backMr = null;
            _backMat = null;
            _defendShield = null;
            _defendMr = null;
            _attackAura = null;
            _attackMr = null;
        }

        static Texture ResolveCardBackTexture()
        {
            var tex = StreamingSprite.CardBackTexture();
            return tex != null ? tex : Texture2D.grayTexture;
        }

        static Material MakeTint(Color c) => ArFieldMaterials.Get(c);
    }
}
