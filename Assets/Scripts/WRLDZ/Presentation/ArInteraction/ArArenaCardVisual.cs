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
    /// Face-down monsters / S/T → card back flat, parallel to ground, hovering
    /// (never billboarded; never leaks private face art).
    ///
    /// Spell/Trap activation: face-down set → rise → artwork toward the opponent,
    /// card back toward the controller (Yugipedia Duel Disk S/T slots).
    /// Field Spells are wrap walls, not this stand-up. Hover, then fade to GY.
    ///
    /// Future: <see cref="PreferMeshWhenAvailable"/> swaps in dedicated meshes.
    /// </summary>
    public class ArArenaCardVisual : MonoBehaviour
    {
        /// <summary>How high face-down set cards float above the street (meters).</summary>
        public const float FaceDownHoverHeight = 0.12f;

        /// <summary>Hover height for an upright Spell/Trap hologram (activate + linger).</summary>
        public const float SpellActiveHoverHeight = 0.48f;

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

        /// <summary>Spell/Trap is playing or finished the activate rise sequence.</summary>
        public bool SpellActivateSequenceActive => _spellSeq != SpellSeqPhase.None;

        /// <summary>True while fading out after activation → GY.</summary>
        public bool IsFadingOut => _spellSeq == SpellSeqPhase.Fading;

        /// <summary>True while Flip Summon owns pose (do not PulseReveal / billboard).</summary>
        public bool FlipSummoning => _flipSummoning;

        /// <summary>True while any flip sequence owns pose (Flip Summon or battle flip).</summary>
        public bool FlipAnimating => _flipSummoning || _battleFlipping;

        MeshRenderer _faceMr;
        Material _faceMat;
        MeshRenderer _backMr;
        Material _backMat;
        Transform _body;
        Transform _ring;
        int _layer;
        bool _spawnAnimating;
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
        /// <summary>
        /// After a flip, the hologram stays as standing ATK artwork (models later)
        /// even if the engine card is still Defense.
        /// </summary>
        bool _uprightMonsterArt;

        // ── Spell/Trap activation sequence ──────────────────────────
        enum SpellSeqPhase
        {
            None = 0,
            FaceDownSpawn,
            RisingReveal,
            ActiveHover,
            Fading
        }

        SpellSeqPhase _spellSeq;
        bool _spellArtRevealed;
        bool _spellActivateStarted;
        bool _fadeRequested;
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

                _spellArtRevealed = false;
            }
            else
            {
                BuildBody(db);
                BuildSummonRing();
                if (IsMonster && Defense && FaceUp)
                    EnsureDefendShield(instant: true);
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

            if (spellActivate)
            {
                _spellCo = StartCoroutine(SpellActivateSequenceCo(localPos));
                if (Application.isPlaying)
                    ArArenaCombatFx.PlaySpellCast(LandingWorld(), PlayerSide, _layer, CardId);
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
        /// Artwork holograms are not combat targets — no pick collider.
        /// Targetable 3D models will add colliders later when PreferMeshWhenAvailable meshes ship.
        /// </summary>
        public void EnsurePickCollider()
        {
            var col = GetComponent<BoxCollider>();
            if (col != null)
                ArObjectUtil.Destroy(col);
        }

        void BuildBody(CardDatabase db)
        {
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

            // Primary art quad — local +Z toward opponent (S/T) or camera (monsters).
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
            var col = ArAnimePresentation.Expose(Color.white);
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
            var faceScale = IsMonster
                ? new Vector3(CardArtFocus.MonsterArtworkScale.x * 0.72f,
                    CardArtFocus.MonsterArtworkScale.x * 0.72f / CardArtFocus.FullCardAspect, 1f)
                : CardArtFocus.SpellTrapArtworkScale;
            var back = ResolveCardBackTexture();
            // Prefer anime unlit so passthrough AR never drops the back to black
            _faceMat = ArAnimePresentation.MakeFaceMaterial(back, Color.white, "SetCardBack");
            CardArtFocus.ApplyToMaterial(_faceMat, back, cropToArtwork: false);
            ArFieldMaterials.ForceDoubleSided(_faceMat);
            if (_faceMat.HasProperty("_Cull")) _faceMat.SetFloat("_Cull", 0f);

            // A + B quads back-to-back — guarantees visibility regardless of normal
            var a = MakeSetQuad("CardBackA", faceScale, 0f, _faceMat);
            MakeSetQuad("CardBackB", faceScale, 180f, _faceMat);
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

        Transform MakeSetQuad(string name, Vector3 scale, float yFlipDeg, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
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
            if (idChanged)
                _uprightMonsterArt = false;

            // Spell activate sequence owns pose until hover/fade
            if (!IsMonster && _spellSeq != SpellSeqPhase.None && _spellSeq != SpellSeqPhase.Fading)
            {
                RefreshSpellCountersFromBus();
                if (SpellActivationPresentation.WantsFadeToGy(InstanceId))
                    RequestFadeToGy();
                return;
            }

            _targetLocalRot = ComputePoseRotation();
            // Re-apply hover if face flipped (preserve XZ column)
            if (faceChanged)
            {
                var basePos = new Vector3(_targetLocalPos.x,
                    IsMonster ? ArPlaymatLayout.MonsterHoverY : ArPlaymatLayout.SpellTrapHoverY,
                    _targetLocalPos.z);
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
                    if (_spellCo != null) StopCoroutine(_spellCo);
                    _spellCo = StartCoroutine(SpellActivateSequenceCo(
                        new Vector3(_targetLocalPos.x, 0.08f, _targetLocalPos.z)));
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

                // Attacked while set: flip the holo into standing ATK artwork
                if (IsMonster && flippedUp && Defense && !_battleFlipping && Application.isPlaying)
                {
                    _battleFlipping = true;
                    if (_spellCo != null) StopCoroutine(_spellCo);
                    _spellCo = StartCoroutine(BattleFlipCo(db));
                    return;
                }

                ClearChildren();
                _defendShield = null;
                _attackAura = null;
                BuildBody(db);
                BuildSummonRing();
                EnsurePickCollider();
                if (Defense && IsMonster && FaceUp)
                    EnsureDefendShield(instant: true);
                if (flippedUp)
                {
                    PulseReveal();
                    CardFlip3dAnimator.PlayIfNeeded(transform, true, db);
                    PlaySummonFx();
                }
                else if (faceChanged && !FaceUp && !_spawnAnimating)
                {
                    transform.localPosition = _targetLocalPos;
                    transform.localRotation = _targetLocalRot;
                }
            }
            else if (defChanged && IsMonster && FaceUp && !_uprightMonsterArt)
            {
                if (Defense)
                {
                    EnsureDefendShield(instant: false);
                    ArArenaCombatFx.PlayDefendShield(transform.position, PlayerSide);
                }
                else
                    ClearDefendShield();
            }

            RefreshSpellCountersFromBus();
            if (!IsMonster && SpellActivationPresentation.WantsFadeToGy(InstanceId))
                RequestFadeToGy();

            BindEffectCallout();

            if (_spellSeq == SpellSeqPhase.None || _spellSeq == SpellSeqPhase.ActiveHover)
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
                    transform.localRotation = SpellOpponentFacingRotation();
                    transform.localPosition = SpellActiveLocalPos();
                }
            }
        }

        /// <summary>
        /// Zone emptied after resolve. Do not cut the arena rise/read hover short —
        /// the activate sequence fades itself after <see cref="SpellActivationPresentation.MinHoverSeconds"/>.
        /// </summary>
        public void RequestFadeToGy()
        {
            if (_fadeRequested || _spellSeq == SpellSeqPhase.Fading) return;
            _fadeRequested = true;
            // Rise + hover still running: keep the hologram readable, fade at sequence end.
            if (_spellSeq == SpellSeqPhase.RisingReveal ||
                _spellSeq == SpellSeqPhase.ActiveHover)
                return;
            if (_spellSeq == SpellSeqPhase.None)
            {
                if (_spellCo != null) StopCoroutine(_spellCo);
                _spellCo = StartCoroutine(FadeToGyCo());
            }
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

        Vector3 LandingWorld()
        {
            return transform.parent != null
                ? transform.parent.TransformPoint(_targetLocalPos)
                : _targetLocalPos;
        }

        void PlayHoloSpawnFx()
        {
            ArArenaCombatFx.PlayHoloMaterialize(LandingWorld(), PlayerSide, IsMonster, _layer);
        }

        void PlaySummonFx()
        {
            if (!IsMonster) return;
            ArArenaCombatFx.PlayHoloMaterialize(LandingWorld(), PlayerSide, true, _layer);
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

        void EnsureDefendShield(bool instant)
        {
            if (_defendShield != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "DefendShield";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            go.transform.localScale = new Vector3(0.55f, 0.7f, 0.2f);
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = _layer;
            var a = instant ? 0.35f : 0f;
            _defendMr = go.GetComponent<MeshRenderer>();
            ArFieldMaterials.Apply(_defendMr, PlayerSide
                ? new Color(0.35f, 0.8f, 1f, a)
                : new Color(1f, 0.55f, 0.7f, a));
            _defendShield = go.transform;
            if (!instant)
                _defendPulse = 1f;
        }

        void ClearDefendShield()
        {
            if (_defendShield != null)
            {
                ArObjectUtil.Destroy(_defendShield.gameObject);
                _defendShield = null;
            }

            _defendMr = null;
            _defendPulse = 0f;
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
            _targetLocalPos = ApplyFaceDownHover(localPos);
            if (!_spawnAnimating && !_flipSummoning && !_battleFlipping)
                transform.localPosition = _targetLocalPos;
        }

        public void ApplyLiveHoloScale()
        {
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
            FaceUp = false;
            Defense = true;
            DuelPresentationPacer.HoldFlipSummon(Card != null ? Card.Name : null);

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
            _uprightMonsterArt = true;
            _targetLocalPos = endP;
            _targetLocalRot = endR;
            _targetLocalScale = endS;
            RebuildAsFaceUpHolo(db);
            PlaySummonFx();
            _flipSummoning = false;
            _spellCo = null;
        }

        /// <summary>
        /// Attacked while set: flip the face-down holo over, then stand it up as
        /// the same upright artwork a Normal Summon uses (models later).
        /// </summary>
        IEnumerator BattleFlipCo(CardDatabase db)
        {
            _battleFlipping = true;
            _spawnAnimating = false;
            FaceUp = false;
            Defense = true;
            DuelPresentationPacer.HoldBattleFlip(Card != null ? Card.Name : null);

            var startP = transform.localPosition;
            var startR = transform.localRotation;
            var yaw = PlayerSide ? 0f : 180f;
            var endP = new Vector3(_targetLocalPos.x, ArPlaymatLayout.MonsterHoverY, _targetLocalPos.z);
            var endR = Quaternion.Euler(0f, yaw, 0f);
            var endS = ArPlaymatLayout.LiveVisualScale;

            // Flip over in the set plane — art at the edge-on midpoint
            yield return ArDiskMotion.FlipY(transform, () => RevealSetCardArt(db),
                ArDiskMotion.BattleFlipReveal);

            if (this == null) yield break;
            transform.localRotation = startR;
            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);

            // Stand up into Attack-position artwork (not a disk 90° turn)
            yield return ArDiskMotion.VisibleLerp(transform, startP, endP, startR, endR,
                transform.localScale, endS, ArDiskMotion.BattleFlipRise);

            FaceUp = true;
            Defense = false;
            _uprightMonsterArt = true;
            _targetLocalPos = endP;
            _targetLocalRot = endR;
            _targetLocalScale = endS;
            RebuildAsFaceUpHolo(db);
            PlaySummonFx();
            _battleFlipping = false;
            _spellCo = null;
        }

        /// <summary>
        /// Zone already empty (flip + destroy in one engine tick). Play the arena
        /// flip into ATK artwork, then shatter / destroy this hologram.
        /// </summary>
        public void PlayBattleFlipThenDestroy()
        {
            if (_battleFlipping || this == null) return;
            _battleFlipping = true;
            if (_spellCo != null) StopCoroutine(_spellCo);
            _spellCo = StartCoroutine(BattleFlipThenDestroyCo());
        }

        IEnumerator BattleFlipThenDestroyCo()
        {
            yield return BattleFlipCo(_db);
            if (this == null) yield break;
            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);
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
            var col = ArAnimePresentation.Expose(Color.white);
            if (_faceMat.HasProperty("_BaseColor")) _faceMat.SetColor("_BaseColor", col);
            if (_faceMat.HasProperty("_Color")) _faceMat.SetColor("_Color", col);
            if (_faceMr != null)
                _faceMr.sharedMaterial = _faceMat;
        }

        void RebuildAsFaceUpHolo(CardDatabase db)
        {
            ClearChildren();
            _defendShield = null;
            _attackAura = null;
            BuildBody(db);
            BuildSummonRing();
            EnsurePickCollider();
            transform.localPosition = _targetLocalPos;
            transform.localRotation = _targetLocalRot;
            transform.localScale = _targetLocalScale;
            BindEffectCallout();
        }

        Quaternion ComputePoseRotation()
        {
            var yaw = IsMonster
                ? (PlayerSide ? 0f : 180f)
                : SpellControllerYaw(PlayerSide);
            if (!FaceUp)
            {
                // Slight tilt toward camera (not pure flat) so AR stage never edge-on culls sets.
                // Monster set = landscape; S/T set = portrait, same yaw as the
                // upright activate pose so the hinge is a clean pitch stand-up.
                var roll = IsMonster ? 90f : 0f;
                var pitch = IsMonster ? 72f : 88f;
                return Quaternion.Euler(pitch, yaw, roll);
            }

            // Flipped holos (and future models) stand as ATK artwork
            if (IsMonster && _uprightMonsterArt)
                return Quaternion.Euler(0f, yaw, 0f);

            if (IsMonster && Defense)
                return Quaternion.Euler(0f, yaw, 90f);
            if (!IsMonster)
                return SpellOpponentFacingRotation();
            return Quaternion.Euler(0f, yaw, 0f);
        }

        Vector3 ApplyFaceDownHover(Vector3 zoneLocal)
        {
            if (!FaceUp)
                return new Vector3(zoneLocal.x, FaceDownHoverHeight + 0.04f, zoneLocal.z);
            if (!IsMonster)
                return new Vector3(zoneLocal.x, SpellActiveHoverHeight, zoneLocal.z);
            return zoneLocal;
        }

        /// <summary>
        /// Face-up monsters billboard toward the AR camera.
        /// Face-down sets stay flat.
        /// Face-up Spells/Traps stand upright after the Set card pitches up
        /// (Yugipedia Activate / Duel Disk): art toward the opponent, back toward the controller.
        /// </summary>
        public void FaceCamera(Camera cam)
        {
            if (cam == null || _spawnAnimating || _flipSummoning || _battleFlipping) return;
            if (_spellSeq != SpellSeqPhase.None && _spellSeq != SpellSeqPhase.Fading &&
                _spellSeq != SpellSeqPhase.ActiveHover)
            {
                // Rise / reveal sequence owns rotation
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
                transform.localRotation = SpellOpponentFacingRotation();
                transform.localPosition = SpellActiveLocalPos();
                _targetLocalRot = transform.localRotation;
                _targetLocalPos = transform.localPosition;
                return;
            }

            // Billboard so card local +Z (art normal on Unity quads) faces the camera.
            // LookRotation(-toCam) pointed art AWAY from the camera → backface / blank square.
            var toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 1e-6f) return;
            var look = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            if (Defense && IsMonster && !_uprightMonsterArt)
                look *= Quaternion.Euler(0f, 0f, 90f);
            transform.rotation = look;
            if (transform.parent != null)
                transform.localPosition = _targetLocalPos;
        }

        /// <summary>
        /// Yugipedia Duel Disk: S/T slots are on the wearer side of the plate.
        /// The physical card's back faces the duelist; the face points at the
        /// opponent. Arena Solid Vision copies that: local +Z is art, so the
        /// controller yaw is 180° (art → opponent along arena +Z).
        /// </summary>
        public static float SpellControllerYaw(bool playerSide) => playerSide ? 180f : 0f;

        Quaternion SpellOpponentFacingRotation() =>
            Quaternion.Euler(0f, SpellControllerYaw(PlayerSide), 0f);

        Vector3 SpellActiveLocalPos()
        {
            return new Vector3(_targetLocalPos.x, SpellActiveHoverHeight, _targetLocalPos.z);
        }

        IEnumerator SpellActivateSequenceCo(Vector3 zoneLocal)
        {
            // One-shot and lingering (Continuous / Field / Equip / Swords): rise upright,
            // art toward the opponent, back toward the controller. Linger skips the fade.
            yield return OneShotRiseActivateCo(zoneLocal);
            _spellCo = null;
        }

        /// <summary>
        /// Yugipedia Activate: flipped from face-down on the field.
        /// Yugipedia Duel Disk: Set S/T eject and stand face-up.
        /// Keep the Set hologram and pitch it upright — same yaw, so the
        /// controller keeps seeing the card back the whole way up.
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

            var startPos = transform.localPosition;
            var startRot = transform.localRotation;
            var startScale = transform.localScale;
            var hinge = Quaternion.Euler(88f, SpellControllerYaw(PlayerSide), 0f);
            if (Quaternion.Angle(startRot, hinge) > 55f)
                startRot = hinge;

            var endPos = new Vector3(zoneLocal.x, SpellActiveHoverHeight, zoneLocal.z);
            var endRot = SpellOpponentFacingRotation();
            var endScale = ArPlaymatLayout.LiveVisualScale;
            var riseDur = Mathf.Max(0.70f, SpellActivationPresentation.ActivateSequenceSeconds - 0.70f);
            var t = 0f;
            var midReveal = _spellArtRevealed;

            while (t < riseDur)
            {
                t += Time.deltaTime;
                var u = Mathf.Clamp01(t / riseDur);
                var e = 1f - Mathf.Pow(1f - u, 3f);
                transform.localPosition = Vector3.Lerp(startPos, endPos, e);
                transform.localRotation = Quaternion.Slerp(startRot, endRot, e);
                transform.localScale = Vector3.Lerp(startScale, endScale, e);

                if (!midReveal && u >= 0.55f)
                {
                    midReveal = true;
                    RevealSpellArtUpright(zoneLocal);
                }

                yield return null;
            }

            transform.localPosition = endPos;
            transform.localRotation = endRot;
            transform.localScale = endScale;
            _targetLocalScale = endScale;
            _targetLocalPos = endPos;
            _targetLocalRot = endRot;
            if (!midReveal)
                RevealSpellArtUpright(zoneLocal);

            _spellSeq = SpellSeqPhase.ActiveHover;
            RefreshSpellCountersFromBus();
            ArArenaCombatFx.PlaySpellCast(transform.position, PlayerSide, _layer, CardId);

            var hoverNeed = PlayerSide
                ? SpellActivationPresentation.MinHoverSeconds
                : SpellActivationPresentation.OpponentReadHoverSeconds;
            var hover = 0f;
            while (hover < hoverNeed)
            {
                hover += Time.deltaTime;
                var bob = Mathf.Sin(Time.time * 2.4f) * 0.012f;
                transform.localPosition = endPos + Vector3.up * bob;
                transform.localRotation = endRot;
                RefreshSpellCountersFromBus();
                yield return null;
            }

            var stays = SpellActivationPresentation.TryGet(InstanceId, out var stayEv) && stayEv.StaysOnField;
            if (!stays && (_fadeRequested || SpellActivationPresentation.WantsFadeToGy(InstanceId) ||
                           !SpellActivationPresentation.HasActivation(InstanceId)))
                yield return FadeToGyCo();
            else
            {
                _spellSeq = SpellSeqPhase.ActiveHover;
                transform.localPosition = endPos;
                transform.localRotation = endRot;
            }
        }

        IEnumerator GhostActivateThenFadeCo(Vector3 zoneLocal)
        {
            _fadeRequested = true;
            yield return SpellActivateSequenceCo(zoneLocal);
        }

        void RevealSpellArtUpright(Vector3 zoneLocal)
        {
            if (_spellArtRevealed) return;
            _spellArtRevealed = true;
            FaceUp = true;
            ClearChildren();
            BuildBody(_db);
            _targetLocalPos = new Vector3(zoneLocal.x, SpellActiveHoverHeight, zoneLocal.z);
            _targetLocalRot = SpellOpponentFacingRotation();
            CollectFadeMaterials();
            CardFlip3dAnimator.PlayIfNeeded(transform, true, _db);
            BindEffectCallout();
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

        void RefreshSpellCountersFromBus()
        {
            if (IsMonster) return;
            var n = SpellActivationPresentation.GetSpellCounters(InstanceId);
            if (n == _spellCounters) return;
            _spellCounters = n;
            RebuildCounterBadge();
        }

        /// <summary>
        /// Spell counter pips above the hologram — ready for continuous Spells later.
        /// </summary>
        void RebuildCounterBadge()
        {
            if (_counterBadge != null)
            {
                ArObjectUtil.Destroy(_counterBadge.gameObject);
                _counterBadge = null;
            }

            if (_spellCounters <= 0 || _spellSeq == SpellSeqPhase.Fading) return;

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
                var col = ArAnimePresentation.Expose(Color.white);
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

        void Update()
        {
            // Defend shield breathe (shared mat — skip if no pulse & rare tick)
            if (_defendShield != null)
            {
                if (_defendPulse > 0f)
                    _defendPulse = Mathf.Max(0f, _defendPulse - Time.deltaTime);
                var breathe = 1f + 0.06f * Mathf.Sin(Time.time * 3.2f);
                var pop = 1f + 0.15f * _defendPulse;
                _defendShield.localScale = new Vector3(0.55f, 0.7f, 0.2f) * breathe * pop;
            }

            // Summon ring spin
            if (_ring != null)
                _ring.Rotate(Vector3.up, 90f * Time.deltaTime, Space.Self);

            if (!_spawnAnimating) return;
            _spawnT += Time.deltaTime / SpawnDuration;
            var t = Mathf.Clamp01(_spawnT);
            var e = 1f - Mathf.Pow(1f - t, 3f);

            if (_pendingHoloFx && t > 0.38f)
            {
                _pendingHoloFx = false;
                PlayHoloSpawnFx();
            }

            var targetWorld = transform.parent != null
                ? transform.parent.TransformPoint(_targetLocalPos)
                : _targetLocalPos;
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
                if (_pendingHoloFx)
                {
                    _pendingHoloFx = false;
                    PlayHoloSpawnFx();
                }
            }
        }

        void BindEffectCallout()
        {
            ArEffectCallout.Ensure(this);
            if (IsMonster)
                ArMonsterStatGauge.Ensure(this);
        }

        void ClearChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.GetComponent<ArMonsterStatGauge>() != null) continue;
                ArObjectUtil.Destroy(child);
            }
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
