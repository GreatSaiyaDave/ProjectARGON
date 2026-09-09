using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Arm-anchored duel disk: rigid lock to left forearm tracking + official zone pads.
    /// Anime retract/deploy: compact wrist form when duel inactive; blade snaps open for play.
    /// </summary>
    public class ArDuelDiskRig : MonoBehaviour
    {
        public bool IsPlayerSide = true;
        public IArTrackingSource Tracker;
        public int Layer = 28;
        public Color Accent = new(0.25f, 0.9f, 1f);

        public Transform DiskRoot { get; private set; }
        /// <summary>Hinge for the Battle City blade panel (folds when retracted).</summary>
        public Transform BladePivot { get; private set; }
        public Transform ZonesRoot { get; private set; }
        public Transform EnergyRing { get; private set; }
        public Transform Cuff { get; private set; }
        /// <summary>Main deck pile (shuffle + opening draw gesture target).</summary>
        public Transform MainDeckZone { get; private set; }
        public Collider MainDeckCollider { get; private set; }
        /// <summary>Graveyard pile on central body (Battle City disk).</summary>
        public Transform GraveyardZone { get; private set; }
        public Collider GraveyardCollider { get; private set; }
        /// <summary>Extra Deck chamber on central body.</summary>
        public Transform ExtraDeckZone { get; private set; }

        public readonly List<ArDiskZone> Zones = new();
        public ArDiskZone[] MonsterZones = new ArDiskZone[5];
        public ArDiskZone[] SpellTrapZones = new ArDiskZone[5];
        public ArDiskZone FieldSpellZone;
        public ArDiskZone PendulumLeft;
        public ArDiskZone PendulumRight;

        Transform _deckStackVisual;
        readonly List<Transform> _shuffleCards = new();
        Coroutine _dispenseCo;

        MeshRenderer _diskMr;
        Material _diskMat;
        readonly DiskFxDriver _fx = new();
        static Mesh _diskMesh;
        Light _diskLight;
        Coroutine _deployRoutine;
        bool _deployed;
        SpiritDuelerSkin _skin;

        public DiskFxDriver Fx => _fx;
        public bool IsDeployed => _deployed;
        public float BladeOpen01 => _fx.BladeOpen;
        /// <summary>Hub LCD — 4-digit 7-seg in the print-kit counter window.</summary>
        public ArDiskLpCounter LpCounter { get; private set; }
        /// <summary>BATTLE / MAIN 2 / END chips on the blade interior (player disk).</summary>
        public ArDiskPhaseButtons PhaseButtons { get; private set; }

        public static ArDuelDiskRig Create(Transform parent, bool playerSide, int layer, Color accent)
        {
            var go = new GameObject(playerSide ? "PlayerArmDiskRig" : "OppArmDiskRig");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var rig = go.AddComponent<ArDuelDiskRig>();
            rig.IsPlayerSide = playerSide;
            rig.Layer = layer;
            rig.Accent = accent;
            rig._fx.Accent = accent;
            rig._fx.SpinSpeed = playerSide ? 10f : -8f;
            rig.BuildDisk();
            return rig;
        }

        void BuildDisk()
        {
            // transform = left-forearm world pose from tracker
            // DiskRoot floats above the wrist; blade + deck share BladePivot mesh space.
            DiskRoot = new GameObject("DiskRoot").transform;
            DiskRoot.SetParent(transform, false);
            // Deck well = wrist calibration point; plate hovers on an aether tether
            DiskRoot.localPosition = SpiritDuelerSkin.WristCalibratedDiskRoot();
            DiskRoot.localRotation = Quaternion.identity;
            SetLayer(DiskRoot.gameObject);

            // Strap / hub is already sculpted on BattleCityDuelDisk.obj.
            // Do not add a primitive cylinder — it reads as a second "round asset"
            // sitting on the real hub and hides the blade.
            Cuff = new GameObject("WristCuff").transform;
            Cuff.SetParent(DiskRoot, false);
            SetLayer(Cuff.gameObject);

            // ── Blade pivot (folds shut when duel inactive) ──
            BladePivot = new GameObject("BladePivot").transform;
            BladePivot.SetParent(DiskRoot, false);
            // Mesh origin = blade root; deck hub is on +Z of the mesh (wearer side)
            BladePivot.localPosition = new Vector3(0.04f, 0.00f, 0.00f);
            SetLayer(BladePivot.gameObject);

            EnsureDiskMesh();
            if (_diskMesh != null)
            {
                var disk = new GameObject("DiskMesh", typeof(MeshFilter), typeof(MeshRenderer));
                disk.transform.SetParent(BladePivot, false);
                // Mesh + zones share this scale so exterior slots line up with geometry
                var ms = ArPlaymatLayout.DiskMeshVisualScale;
                disk.transform.localPosition = Vector3.zero;
                disk.transform.localRotation = Quaternion.identity;
                disk.transform.localScale = Vector3.one * ms;
                disk.GetComponent<MeshFilter>().sharedMesh = _diskMesh;
                _diskMat = SpiritDuelerSkin.BuildGhostMaterial(Accent);
                _diskMr = disk.GetComponent<MeshRenderer>();
                _diskMr.sharedMaterial = _diskMat;
                SetLayer(disk);
            }
            else
            {
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fallback.name = "DiskFallback";
                fallback.transform.SetParent(BladePivot, false);
                fallback.transform.localScale = new Vector3(0.55f, 0.02f, 0.35f);
                ArObjectUtil.Destroy(fallback.GetComponent<Collider>());
                SetLayer(fallback);
                _diskMr = fallback.GetComponent<MeshRenderer>();
                _diskMat = SpiritDuelerSkin.BuildGhostMaterial(Accent);
                _diskMr.sharedMaterial = _diskMat;
            }

            // Energy ring — empty GO for FX hierarchy (no mesh until pulse needs scale)
            var ringGo = new GameObject("EnergyRing");
            ringGo.transform.SetParent(BladePivot, false);
            ringGo.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            SetLayer(ringGo);
            EnergyRing = ringGo.transform;
            EnergyRing.gameObject.SetActive(false);

            // Soft point light for cards on disk (no permanent glow mesh)
            var lightGo = new GameObject("DiskPointLight", typeof(Light));
            lightGo.transform.SetParent(BladePivot, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            _diskLight = lightGo.GetComponent<Light>();
            _diskLight.type = LightType.Point;
            _diskLight.range = 1.8f;
            _diskLight.color = Accent;
            _diskLight.intensity = 0f;
            _diskLight.cullingMask = (1 << Layer) | 1;

            // Zones share BladePivot with the mesh. Positions are mesh-local, then scaled
            // with DiskMeshVisualScale so they track the enlarged plate.
            ZonesRoot = new GameObject("OfficialZones").transform;
            ZonesRoot.SetParent(BladePivot, false);
            var zoneScale = ArPlaymatLayout.DiskMeshVisualScale;
            ZonesRoot.localPosition = Vector3.zero;
            ZonesRoot.localRotation = Quaternion.identity;
            ZonesRoot.localScale = Vector3.one * zoneScale;
            SetLayer(ZonesRoot.gameObject);

            for (var i = 0; i < 5; i++)
            {
                // Monster Card Slots — top of plate, centered on the sculpted stage
                MonsterZones[i] = ArDiskZone.Create(ZonesRoot, ArDuelZoneKind.Monster, i,
                    ArZoneLayout.MonsterLocal(i), Accent, Layer, IsPlayerSide,
                    localRot: ArZoneLayout.MonsterZoneRotation(i));
                Zones.Add(MonsterZones[i]);
            }

            for (var i = 0; i < 5; i++)
            {
                // Spell Card Slots — under each monster, side nearest the user (Battle City)
                SpellTrapZones[i] = ArDiskZone.Create(ZonesRoot, ArDuelZoneKind.SpellTrap, i,
                    ArZoneLayout.SpellTrapLocal(i), new Color(0.55f, 0.40f, 0.85f), Layer, IsPlayerSide,
                    localRot: ArZoneLayout.SpellTrapZoneRotation(i));
                Zones.Add(SpellTrapZones[i]);
            }

            // Field Card Slot — drawer from the tip edge of the plate
            FieldSpellZone = ArDiskZone.Create(ZonesRoot, ArDuelZoneKind.FieldSpell, 0,
                ArZoneLayout.FieldSpellLocal, new Color(0.3f, 0.85f, 0.45f), Layer, IsPlayerSide,
                localRot: ArZoneLayout.FieldSpellZoneRotation());
            Zones.Add(FieldSpellZone);

            // Battle City V2 has no Pendulum Zones (S1 / anime disk). Leave the
            // anchors null so nothing sits in empty air past M1 / M5.

            RelayoutOfficialZones();
            Debug.Log(
                "[WRLDZ AR] Disk zones · M on plate · ST in dashed indent (10% tip) · " +
                $"M1={ArZoneLayout.MonsterLocal(0)} · ST1={ArZoneLayout.SpellTrapLocal(0)} · " +
                $"Δ={ArZoneLayout.SpellTrapLocal(0) - ArZoneLayout.MonsterLocal(0)} · " +
                $"deckHub={ArZoneLayout.MainDeckLocal}");

            // Deck / GY / Extra on the mesh hub (BladePivot space — not floating on DiskRoot)
            BuildMainDeckZone();
            BuildGraveyardZone();
            BuildExtraDeckZone();
            // Mesh-local like GY — do not inherit OfficialZones scale twice.
            LpCounter = ArDiskLpCounter.Create(ZonesRoot, Layer);
            if (IsPlayerSide)
                PhaseButtons = ArDiskPhaseButtons.Create(ZonesRoot, Layer);

            // Start RETRACTED — anime: disk folded until duel begins
            _fx.SnapRetracted();
            _deployed = false;
            DiskRoot.localPosition = SpiritDuelerSkin.WristCalibratedDiskRoot();
            _fx.CaptureBase(DiskRoot);
            _skin = SpiritDuelerSkin.Attach(this);
            _skin?.SnapPresence(0f);
            ApplyImmediatePose();
            Debug.Log(
                $"[WRLDZ AR] Spirit Dueler ready ({(IsPlayerSide ? "player" : "opp")}) · " +
                $"deck@wrist hoverY={SpiritDuelerSkin.WristHoverY:0.000} · retracted");
        }

        /// <summary>
        /// Re-apply mesh-local zone anchors (S/T mouths especially) so a hot-reload
        /// or redeploy never leaves cards in a buried under-plate pose.
        /// </summary>
        public void RelayoutOfficialZones()
        {
            for (var i = 0; i < 5; i++)
            {
                if (MonsterZones[i] != null)
                {
                    MonsterZones[i].transform.localPosition = ArZoneLayout.MonsterLocal(i);
                    MonsterZones[i].transform.localRotation = ArZoneLayout.MonsterZoneRotation(i);
                }

                if (SpellTrapZones[i] != null)
                {
                    SpellTrapZones[i].transform.localPosition = ArZoneLayout.SpellTrapLocal(i);
                    SpellTrapZones[i].transform.localRotation = ArZoneLayout.SpellTrapZoneRotation(i);
                    SpellTrapZones[i].RefreshFlushLock(ArZoneOrientation.FaceDownSet);
                }
            }

            if (FieldSpellZone != null)
            {
                FieldSpellZone.transform.localPosition = ArZoneLayout.FieldSpellLocal;
                FieldSpellZone.transform.localRotation = ArZoneLayout.FieldSpellZoneRotation();
            }
        }

        void BuildMainDeckZone()
        {
            // Same parent + scale as monster/S-T zones so mesh-local magazine
            // coordinates land on the sculpted hub Deck Holder.
            MainDeckZone = new GameObject("MainDeckHolder").transform;
            var parent = ZonesRoot != null ? ZonesRoot
                : BladePivot != null ? BladePivot : DiskRoot;
            MainDeckZone.SetParent(parent, false);
            MainDeckZone.localPosition = ArZoneLayout.MainDeckLocal;
            MainDeckZone.localRotation = Quaternion.identity;
            MainDeckZone.localScale = Vector3.one;
            SetLayer(MainDeckZone.gameObject);

            BuildDeckCardStack();
            BuildHubZoneLabel(MainDeckZone, "DECK", Accent, new Vector3(0f, 0.04f, 0f));

            var hit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hit.name = "DeckHit";
            hit.transform.SetParent(MainDeckZone, false);
            hit.transform.localScale = new Vector3(
                ArDeckWellCards.Width * 1.1f,
                ArDeckWellCards.PackHeight * 1.2f,
                ArDeckWellCards.Length * 1.1f);
            hit.transform.localPosition = new Vector3(0f, ArDeckWellCards.PackHeight * 0.5f, 0f);
            SetLayer(hit);
            var mr = hit.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            MainDeckCollider = hit.GetComponent<Collider>();
            MainDeckCollider.isTrigger = true;
            SetDeckHighlight(false);
        }

        /// <summary>Identity label for Deck / GY / Extra on the central body hub.</summary>
        void BuildHubZoneLabel(Transform parent, string text, Color accent, Vector3 localPos)
        {
            if (!ArDiskZone.AlwaysShowZoneLabels || parent == null) return;
            var go = new GameObject("ZoneLabel_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SetLayer(go);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = 0.0045f;
            tm.fontStyle = FontStyle.Bold;
            tm.color = Color.Lerp(Color.white, accent, 0.3f);
            if (tm.GetComponent<MeshRenderer>() is { } mr)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        /// <summary>
        /// Visible main deck = stacked card-back assets (not a solid floating slab).
        /// Never throws — pre-duel shuffle depends on this stack existing.
        /// </summary>
        void BuildDeckCardStack()
        {
            try
            {
                // Replace any previous stack this frame — deferred Destroy left a stray pack
                if (MainDeckZone != null)
                {
                    for (var i = MainDeckZone.childCount - 1; i >= 0; i--)
                    {
                        var ch = MainDeckZone.GetChild(i);
                        if (ch != null && ch.name.StartsWith("DeckWellPack"))
                            Object.DestroyImmediate(ch.gameObject);
                    }
                }

                if (_deckStackVisual != null)
                {
                    Object.DestroyImmediate(_deckStackVisual.gameObject);
                    _deckStackVisual = null;
                }

                _shuffleCards.Clear();
                _deckStackVisual = ArDeckWellCards.Build(MainDeckZone, Layer, out var loose);
                _shuffleCards.AddRange(loose);

                Debug.Log(
                    $"[WRLDZ AR] Deck well cards · n={_shuffleCards.Count} · " +
                    $"size={ArDeckWellCards.Width:0.000}x{ArDeckWellCards.Height:0.000} · " +
                    $"hub={MainDeckZone.localPosition}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ AR] Deck stack build failed (fallback cubes): " + ex.Message);
                BuildDeckCardStackFallback();
            }
        }

        void BuildDeckCardStackFallback()
        {
            if (_deckStackVisual == null)
            {
                _deckStackVisual = new GameObject("DeckStack").transform;
                _deckStackVisual.SetParent(MainDeckZone, false);
            }

            _shuffleCards.Clear();
            for (var i = 0; i < 8; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Card" + i;
                c.transform.SetParent(_deckStackVisual, false);
                c.transform.localScale = new Vector3(
                    ArDeckWellCards.Width, 0.002f, ArDeckWellCards.Length);
                c.transform.localPosition = ArDeckWellCards.RestLocal(i);
                ArObjectUtil.Destroy(c.GetComponent<Collider>());
                SetLayer(c);
                c.GetComponent<MeshRenderer>().sharedMaterial =
                    MakeUnlit(i % 2 == 0
                        ? new Color(0.15f, 0.22f, 0.45f, 1f)
                        : new Color(0.18f, 0.28f, 0.55f, 1f));
                _shuffleCards.Add(c.transform);
            }
        }

        static Material _cachedCardBackMat;

        static Material MakeCardBackMaterial()
        {
            if (_cachedCardBackMat != null) return _cachedCardBackMat;
            try
            {
                var spr = StreamingSprite.CardBack()
                          ?? ImagineAssets.CardBackWrldz()
                          ?? YgoCardFrames.CardBack();
                var tex = spr != null && spr.texture != null ? spr.texture : null;
                if (tex != null)
                {
                    _cachedCardBackMat = ArFieldMaterials.CreateUnlitTextureDoubleSided(tex);
                    if (_cachedCardBackMat != null)
                    {
                        _cachedCardBackMat.name = "DiskDeckCardBack";
                        // Ensure full opacity + white multiply so dark backs still read
                        if (_cachedCardBackMat.HasProperty("_BaseColor"))
                            _cachedCardBackMat.SetColor("_BaseColor", Color.white);
                        if (_cachedCardBackMat.HasProperty("_Color"))
                            _cachedCardBackMat.SetColor("_Color", Color.white);
                        return _cachedCardBackMat;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ AR] Card-back material failed: " + ex.Message);
            }

            // Solid blue-purple fallback so pre-duel never shows pure black void
            _cachedCardBackMat = ArFieldMaterials.Get(new Color(0.22f, 0.32f, 0.62f, 1f));
            return _cachedCardBackMat;
        }

        /// <summary>Optional deck pulse — no mesh highlight in empty AR mode.</summary>
        public void SetDeckHighlight(bool on)
        {
            _ = on;
        }

        void BuildGraveyardZone()
        {
            // Yugipedia: Graveyard tray beside Main Deck on the central body
            GraveyardZone = new GameObject("Graveyard").transform;
            GraveyardZone.SetParent(BladePivot != null ? BladePivot : DiskRoot, false);
            var s = ArPlaymatLayout.DiskMeshVisualScale;
            GraveyardZone.localPosition = ArZoneLayout.GraveyardLocal * s;
            GraveyardZone.localRotation = Quaternion.Euler(4f, 0f, 0f);
            GraveyardZone.localScale = Vector3.one * s;
            SetLayer(GraveyardZone.gameObject);
            BuildHubZoneLabel(GraveyardZone, "GY", new Color(0.55f, 0.55f, 0.65f),
                new Vector3(0f, 0.06f, 0f));

            var hit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hit.name = "GyHit";
            hit.transform.SetParent(GraveyardZone, false);
            hit.transform.localScale = new Vector3(
                ArDeckWellCards.Width * 1.35f,
                Mathf.Max(0.012f, ArDeckWellCards.PackHeight * 0.55f),
                ArDeckWellCards.Length * 1.35f);
            hit.transform.localPosition = new Vector3(0f, 0.008f, 0f);
            SetLayer(hit);
            var mr = hit.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            GraveyardCollider = hit.GetComponent<Collider>();
            GraveyardCollider.isTrigger = true;
            var tap = GraveyardZone.gameObject.AddComponent<ArGraveyardHit>();
            tap.Disk = this;
            tap.IsPlayerSide = IsPlayerSide;
        }

        /// <summary>Face-up GY pile on the hub tray (newest on top). Public information.</summary>
        public void SyncGraveyardPile(IReadOnlyList<CardInstance> gy, CardDatabase db)
        {
            if (GraveyardZone == null) return;
            Transform pile = GraveyardZone.Find("GyPile");
            if (pile == null)
            {
                pile = new GameObject("GyPile").transform;
                pile.SetParent(GraveyardZone, false);
                pile.localPosition = Vector3.zero;
                pile.localRotation = Quaternion.identity;
                pile.localScale = Vector3.one;
                SetLayer(pile.gameObject);
            }

            for (var i = pile.childCount - 1; i >= 0; i--)
                ArObjectUtil.Destroy(pile.GetChild(i).gameObject);

            if (gy == null || gy.Count == 0) return;
            var show = Mathf.Min(4, gy.Count);
            for (var i = 0; i < show; i++)
            {
                var card = gy[gy.Count - show + i];
                if (card == null) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "GyCard_" + card.InstanceId;
                go.transform.SetParent(pile, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(
                    ArDeckWellCards.Width, ArDeckWellCards.Length, 1f);
                go.transform.localPosition = new Vector3(
                    (i - (show - 1) * 0.5f) * 0.003f,
                    0.0015f + i * 0.0011f,
                    (i - (show - 1) * 0.5f) * 0.002f);
                ArObjectUtil.Destroy(go.GetComponent<Collider>());
                SetLayer(go);
                var tex = Texture2D.whiteTexture;
                if (db != null)
                {
                    var art = db.GetArt(card.CardId);
                    if (art != null && art.texture != null) tex = art.texture;
                }

                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = ArFieldMaterials.CreateUnlitTextureDoubleSided(tex);
            }
        }

        void BuildExtraDeckZone()
        {
            // Battle City V2 has one deck magazine. Extra is an invisible engine
            // hook only — never a second well on the hub.
            ExtraDeckZone = new GameObject("ExtraDeck").transform;
            ExtraDeckZone.SetParent(BladePivot != null ? BladePivot : DiskRoot, false);
            ExtraDeckZone.localPosition = ArZoneLayout.ExtraDeckLocal;
            ExtraDeckZone.localRotation = Quaternion.identity;
            ExtraDeckZone.localScale = Vector3.one;
            SetLayer(ExtraDeckZone.gameObject);
        }

        /// <summary>Rebuild stack if empty (pre-duel must never no-op).</summary>
        public void EnsureDeckStackVisual()
        {
            if (MainDeckZone == null) BuildMainDeckZone();
            // Drop destroyed refs
            _shuffleCards.RemoveAll(t => t == null);
            if (_shuffleCards.Count == 0)
                BuildDeckCardStack();
            // Always show stack during pre-duel (never leave inactive)
            if (_deckStackVisual != null)
                _deckStackVisual.gameObject.SetActive(true);
            if (MainDeckZone != null)
                MainDeckZone.gameObject.SetActive(true);
        }

        /// <summary>
        /// Pre-duel: temporarily enlarge deck so riffle is obvious in the AR RT view.
        /// </summary>
        public void SetDeckStackPresentationScale(float scale)
        {
            if (_deckStackVisual != null)
                // Never grow past the well — shuffle emphasis is motion, not size
                _deckStackVisual.localScale = Vector3.one * Mathf.Clamp(scale, 0.5f, 1.06f);
        }

        /// <summary>
        /// Pre-duel hand shuffle: controlled split → cascade riffle → square-up.
        /// Reads as two neat packets interlacing, not random card scatter.
        /// </summary>
        public IEnumerator PlayShuffleRoutine(float duration = 1.75f)
        {
            EnsureDeckStackVisual();
            if (_shuffleCards.Count == 0)
            {
                Debug.LogWarning("[WRLDZ AR] Shuffle skipped — no deck cards — hard rebuild");
                BuildDeckCardStack();
            }

            if (_shuffleCards.Count == 0)
            {
                Debug.LogError("[WRLDZ AR] Shuffle aborted — deck stack still empty after rebuild");
                yield return new WaitForSecondsRealtime(0.4f);
                yield break;
            }

            // Readable in phone / Editor RT without looking chaotic
            SetDeckStackPresentationScale(1.35f);

            var n = _shuffleCards.Count;
            var mid = n / 2;
            const float packetSep = 0.010f;
            const float bridgeLift = 0.006f;
            const float riffleDrop = 0.004f;

            Vector3 RestPos(int i) => ArDeckWellCards.RestLocal(i);

            Quaternion RestRot(int i) =>
                Quaternion.Euler(0f, (i - n * 0.5f) * 0.35f, 0f);

            // Packet poses while held apart (slight fan like thumbs on edges)
            Vector3 PacketPos(int i, bool left)
            {
                var rest = RestPos(i);
                var side = left ? -1f : 1f;
                return rest + new Vector3(side * packetSep, bridgeLift, 0f);
            }

            Quaternion PacketRot(bool left) =>
                Quaternion.Euler(6f, left ? -4f : 4f, left ? 8f : -8f);

            // Capture starts
            var startPos = new Vector3[n];
            var startRot = new Quaternion[n];
            for (var i = 0; i < n; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                startPos[i] = c.localPosition;
                startRot[i] = c.localRotation;
            }

            Debug.Log(
                $"[WRLDZ AR] Hand shuffle start · cards={n} · {duration:0.00}s · " +
                $"packets L={mid} R={n - mid} · deckWorld={MainDeckWorldCenter}");
            PlayFx(DiskFxEvent.LegalZonePulse, duration);

            // Phase weights (sum ≈ 1): split · riffle · square · settle
            // Longer riffle = the "controlled" moment people recognize
            var dSplit = duration * 0.22f;
            var dRiffle = duration * 0.48f;
            var dSquare = duration * 0.20f;
            var dSettle = duration * 0.10f;

            // ── 1) Split: stack → two neat packets ──
            {
                var t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < dSplit)
                {
                    var u = Mathf.Clamp01((Time.unscaledTime - t0) / dSplit);
                    var e = SmoothStep(u);
                    for (var i = 0; i < n; i++)
                    {
                        var c = _shuffleCards[i];
                        if (c == null) continue;
                        var left = i < mid;
                        c.localPosition = Vector3.Lerp(startPos[i], PacketPos(i, left), e);
                        c.localRotation = Quaternion.Slerp(startRot[i], PacketRot(left), e);
                    }

                    yield return null;
                }
            }

            // Snap packets clean before riffle
            for (var i = 0; i < n; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                var left = i < mid;
                c.localPosition = PacketPos(i, left);
                c.localRotation = PacketRot(left);
            }

            // ── 2) Cascade riffle: cards peel from each packet into a bridge, interleave ──
            // Each card has a release time; left/right alternate as they drop into the pile.
            {
                var releaseOrder = BuildRiffleReleaseOrder(n, mid);
                var t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < dRiffle)
                {
                    var u = Mathf.Clamp01((Time.unscaledTime - t0) / dRiffle);
                    for (var i = 0; i < n; i++)
                    {
                        var c = _shuffleCards[i];
                        if (c == null) continue;
                        var left = i < mid;
                        var from = PacketPos(i, left);
                        var fromR = PacketRot(left);

                        // When this card releases in the cascade (0..1 across the phase)
                        var releaseU = releaseOrder[i];
                        // Stretch: card travels over ~18% of the riffle window after release
                        var local = Mathf.Clamp01((u - releaseU) / 0.18f);
                        if (u < releaseU)
                        {
                            // Still in packet — slight thumb pressure (tiny inward drift)
                            var press = Mathf.Clamp01(u / Mathf.Max(0.01f, releaseU)) * 0.004f;
                            c.localPosition = from + new Vector3(left ? press : -press, 0f, 0f);
                            c.localRotation = fromR;
                            continue;
                        }

                        var e = SmoothStep(local);
                        // Interleaved rest index: alternate L/R by release sequence rank
                        var destIdx = RankInRelease(releaseOrder, i);
                        var to = RestPos(destIdx) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * riffleDrop);
                        // Bridge arc: cards bow up in the middle while flying in
                        var bridge = Mathf.Sin(e * Mathf.PI) * bridgeLift * 1.4f;
                        var midX = Mathf.Lerp(from.x, 0f, e);
                        to = new Vector3(midX, to.y + bridge * (1f - e), to.z);

                        // Gentle edge tilt while cascading (not wild spin)
                        var tiltZ = Mathf.Lerp(left ? 8f : -8f, 0f, e);
                        var tiltX = Mathf.Lerp(6f, 0f, e) + Mathf.Sin(e * Mathf.PI) * 12f;
                        var toR = Quaternion.Euler(tiltX, (destIdx - n * 0.5f) * 0.35f, tiltZ);

                        c.localPosition = Vector3.Lerp(from, to, e);
                        c.localRotation = Quaternion.Slerp(fromR, toR, e);
                    }

                    yield return null;
                }
            }

            // After riffle: cards sit at interleaved heights — capture before square/settle
            var postRifflePos = new Vector3[n];
            var postRiffleRot = new Quaternion[n];
            var order = BuildRiffleReleaseOrder(n, mid);
            for (var i = 0; i < n; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                var destIdx = RankInRelease(order, i);
                c.localPosition = RestPos(destIdx);
                c.localRotation = RestRot(destIdx);
                postRifflePos[i] = c.localPosition;
                postRiffleRot[i] = c.localRotation;
            }

            // ── 3) Square-up: push ends in, tap edges (controlled double-tap feel) ──
            {
                var t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < dSquare)
                {
                    var u = Mathf.Clamp01((Time.unscaledTime - t0) / dSquare);
                    // Two compress pulses
                    var pulse = Mathf.Sin(u * Mathf.PI * 2f) * (1f - u) * 0.012f;
                    for (var i = 0; i < n; i++)
                    {
                        var c = _shuffleCards[i];
                        if (c == null) continue;
                        var destIdx = RankInRelease(order, i);
                        var rest = RestPos(destIdx);
                        // Squeeze toward center X, tiny Y settle
                        c.localPosition = rest + new Vector3(pulse * (destIdx % 2 == 0 ? 1f : -1f),
                            -Mathf.Abs(pulse) * 0.3f, 0f);
                        c.localRotation = Quaternion.Slerp(
                            postRiffleRot[i], RestRot(destIdx), SmoothStep(u));
                    }

                    yield return null;
                }
            }

            // Capture after square for smooth settle into final stack order
            for (var i = 0; i < n; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                postRifflePos[i] = c.localPosition;
                postRiffleRot[i] = c.localRotation;
            }

            // ── 4) Settle: glide into final neat stack (stable order for draw VFX) ──
            {
                var t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < dSettle)
                {
                    var u = SmoothStep(Mathf.Clamp01((Time.unscaledTime - t0) / dSettle));
                    for (var i = 0; i < n; i++)
                    {
                        var c = _shuffleCards[i];
                        if (c == null) continue;
                        c.localPosition = Vector3.Lerp(postRifflePos[i], RestPos(i), u);
                        c.localRotation = Quaternion.Slerp(postRiffleRot[i], RestRot(i), u);
                    }

                    yield return null;
                }
            }

            // Hard snap final neat stack
            for (var i = 0; i < n; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                c.localPosition = RestPos(i);
                c.localRotation = RestRot(i);
            }

            SetDeckStackPresentationScale(1f);
            SnapDeckCardsHome();
            Debug.Log("[WRLDZ AR] Hand shuffle complete");
        }

        /// <summary>Every well card back in the pack — kills leftover shuffle / draw lifts.</summary>
        public void SnapDeckCardsHome()
        {
            SetDeckStackPresentationScale(1f);
            for (var i = 0; i < _shuffleCards.Count; i++)
            {
                var c = _shuffleCards[i];
                if (c == null) continue;
                c.localPosition = ArDeckWellCards.RestLocal(i);
                c.localRotation = Quaternion.Euler(0f, (i - _shuffleCards.Count * 0.5f) * 0.35f, 0f);
                c.localScale = Vector3.one;
                SetDeckCardAlpha(c, 1f);
            }
        }

        /// <summary>Release times 0..1 for cascade: alternate L packet / R packet from thumbs.</summary>
        static float[] BuildRiffleReleaseOrder(int n, int mid)
        {
            var release = new float[n];
            if (n <= 0) return release;

            // Peel from top of each packet (highest index first — thumbs release tops)
            var leftTop = mid - 1;
            var rightTop = n - 1;
            var leftCount = Mathf.Max(0, mid);
            var rightCount = Mathf.Max(0, n - mid);
            var seq = new List<int>(n);
            var step = 0;
            while (leftCount > 0 || rightCount > 0)
            {
                var takeLeft = leftCount > 0 &&
                               (rightCount == 0 || step % 2 == 0 || leftCount > rightCount);
                if (takeLeft && leftCount > 0 && leftTop >= 0)
                {
                    seq.Add(leftTop);
                    leftTop--;
                    leftCount--;
                }
                else if (rightCount > 0 && rightTop >= mid)
                {
                    seq.Add(rightTop);
                    rightTop--;
                    rightCount--;
                }
                else if (leftCount > 0 && leftTop >= 0)
                {
                    seq.Add(leftTop);
                    leftTop--;
                    leftCount--;
                }
                else
                    break;

                step++;
            }

            for (var s = 0; s < seq.Count; s++)
            {
                var idx = seq[s];
                if (idx < 0 || idx >= n) continue;
                release[idx] = seq.Count <= 1 ? 0f : (s / (float)(seq.Count - 1)) * 0.82f;
            }

            return release;
        }

        /// <summary>Where card i lands in the interleaved pile (0 = bottom).</summary>
        static int RankInRelease(float[] release, int cardIndex)
        {
            var rank = 0;
            var mine = release[cardIndex];
            for (var j = 0; j < release.Length; j++)
            {
                if (j == cardIndex) continue;
                if (release[j] < mine - 1e-5f ||
                    (Mathf.Abs(release[j] - mine) < 1e-5f && j < cardIndex))
                    rank++;
            }

            return rank;
        }

        static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>World-space center of the main deck for hand proximity tests.</summary>
        public Vector3 MainDeckWorldCenter =>
            MainDeckZone != null
                ? MainDeckZone.position + MainDeckZone.up * (ArDeckWellCards.PackHeight * 0.5f)
                : transform.position;

        public float MainDeckReachRadius => 0.16f;

        /// <summary>
        /// World pose of the magazine mouth — follows the disk wherever it is.
        /// Draw flights sample this every frame so a moving arm still peels cleanly.
        /// </summary>
        public bool TryGetDrawOriginWorldPose(out Vector3 pos, out Quaternion rot, out Vector3 worldScale)
        {
            EnsureDeckStackVisual();
            if (MainDeckZone != null)
            {
                var ls = MainDeckZone.lossyScale;
                pos = MainDeckZone.TransformPoint(new Vector3(0f, ArDeckWellCards.PackHeight + 0.0012f, 0f));
                rot = MainDeckZone.rotation;
                worldScale = new Vector3(
                    ArDeckWellCards.Width * ls.x,
                    ArDeckWellCards.Thickness * ls.y,
                    ArDeckWellCards.Length * ls.z);
                return true;
            }

            return TryGetTopDeckWorldPose(out pos, out rot, out worldScale);
        }

        /// <summary>
        /// World pose of the top card in the disk deck magazine.
        /// Used to dispense draws from the disk, not from a floating world point.
        /// </summary>
        public bool TryGetTopDeckWorldPose(out Vector3 pos, out Quaternion rot, out Vector3 worldScale)
        {
            EnsureDeckStackVisual();
            // i=0 is the top / draw card
            Transform top = null;
            for (var i = 0; i < _shuffleCards.Count; i++)
            {
                if (_shuffleCards[i] == null) continue;
                top = _shuffleCards[i];
                break;
            }

            if (top != null)
            {
                pos = top.position + top.up * 0.003f;
                rot = top.rotation;
                var ls = top.lossyScale;
                worldScale = new Vector3(
                    ArDeckWellCards.Width * ls.x,
                    ArDeckWellCards.Thickness * ls.y,
                    ArDeckWellCards.Length * ls.z);
                return true;
            }

            if (MainDeckZone != null)
            {
                pos = MainDeckWorldCenter + MainDeckZone.up * 0.006f;
                rot = MainDeckZone.rotation;
                var ls = MainDeckZone.lossyScale;
                worldScale = new Vector3(
                    ArDeckWellCards.Width * ls.x,
                    ArDeckWellCards.Thickness * ls.y,
                    ArDeckWellCards.Length * ls.z);
                return true;
            }

            pos = transform.position;
            rot = transform.rotation;
            worldScale = Vector3.one * 0.08f;
            return false;
        }

        /// <summary>Lift + fade the top well card so a draw reads as leaving the disk.</summary>
        public void PulseDispenseTopCard()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            if (_dispenseCo != null)
                StopCoroutine(_dispenseCo);
            SnapDeckCardsHome();
            _dispenseCo = StartCoroutine(DispenseTopCardCo());
        }

        IEnumerator DispenseTopCardCo()
        {
            Transform top = null;
            for (var i = 0; i < _shuffleCards.Count; i++)
            {
                if (_shuffleCards[i] == null) continue;
                top = _shuffleCards[i];
                break;
            }

            if (top == null) yield break;
            var startP = ArDeckWellCards.RestLocal(0);
            var startR = Quaternion.Euler(0f, (0 - _shuffleCards.Count * 0.5f) * 0.35f, 0f);
            var startS = Vector3.one;
            top.localPosition = startP;
            top.localRotation = startR;
            top.localScale = startS;
            var lift = startP + new Vector3(0f, 0.030f, 0f);
            var u = 0f;
            var dur = ArDiskMotion.DrawLift;
            while (u < 1f && top != null)
            {
                u += Time.unscaledDeltaTime / dur;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                top.localPosition = Vector3.Lerp(startP, lift, e);
                top.localRotation = startR * Quaternion.Euler(-28f * e, 0f, 6f * e);
                top.localScale = Vector3.Lerp(startS, startS * 0.96f, e);
                SetDeckCardAlpha(top, 1f - e);
                yield return null;
            }

            if (top == null)
            {
                _dispenseCo = null;
                yield break;
            }

            SnapDeckCardsHome();
            _dispenseCo = null;
        }

        static void SetDeckCardAlpha(Transform card, float a)
        {
            if (card == null) return;
            foreach (var mr in card.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr?.material == null) continue;
                if (mr.material.HasProperty("_BaseColor"))
                {
                    var c = mr.material.GetColor("_BaseColor");
                    c.a = a;
                    mr.material.SetColor("_BaseColor", c);
                }

                if (mr.material.HasProperty("_Color"))
                {
                    var c = mr.material.color;
                    c.a = a;
                    mr.material.color = c;
                }
            }
        }

        void ApplyImmediatePose()
        {
            _fx.Tick(0f, DiskRoot, _diskMat, BladePivot, ZonesRoot, EnergyRing);
            if (_diskLight != null)
                _diskLight.intensity = 0f;
        }

        void OnDisable()
        {
            if (_dispenseCo != null)
            {
                StopCoroutine(_dispenseCo);
                _dispenseCo = null;
            }

            SnapDeckCardsHome();
        }

        void LateUpdate()
        {
            // Rigid lock to left forearm (Yugipedia: strap around left arm)
            if (Tracker != null && IsPlayerSide && Tracker.TryGetLeftForearm(out var pose))
            {
                // Orient disk so blade points outward along forearm axis
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else if (Tracker != null && !IsPlayerSide && Tracker.TryGetOpponentLeftForearm(out var opose))
            {
                transform.SetPositionAndRotation(opose.position, opose.rotation);
            }

            // Pass null disk mat — body color is SpiritDuelerSkin only (do not fight zone cards).
            _fx.Tick(Time.deltaTime, DiskRoot, null, BladePivot, ZonesRoot, EnergyRing);
            if (_diskLight != null)
            {
                var presence = _skin != null ? _skin.Presence : 1f;
                var open = _fx.BladeOpen;
                _diskLight.intensity = presence * (0.22f + open * (0.55f + _fx.EnergyPulse * 1.4f));
                _diskLight.range = 0.7f + open * 1.5f;
                _diskLight.color = Accent;
            }
        }

        public void PlayFx(DiskFxEvent evt, float duration = -1f)
        {
            if (evt == DiskFxEvent.BladeDeploy)
                _deployed = true;
            if (evt == DiskFxEvent.BladeRetract)
                _deployed = false;
            _fx.Play(evt, duration);
        }

        public void SetAccent(Color accent)
        {
            Accent = accent;
            _fx.Accent = accent;
            if (_diskLight != null) _diskLight.color = accent;
            _skin?.SetAccent(accent);
        }

        /// <summary>Instant Duel / skip cinematic — no swing.</summary>
        public void SnapVisibleDeployed()
        {
            _skin?.SnapPresence(1f);
            _fx.SnapDeployed();
            _deployed = true;
            PhaseButtons?.SetDeployed(true);
            RelayoutOfficialZones();
        }

        /// <summary>Ghost cuff appears folded (Zone Mode / pre-duel). Does not open the blade.</summary>
        public void FadeInRetracted(float delay = 0f)
        {
            if (_deployRoutine != null) StopCoroutine(_deployRoutine);
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                _fx.SnapRetracted();
                _skin?.SnapPresence(1f);
                _deployed = false;
                return;
            }

            _deployRoutine = StartCoroutine(FadeInRetractedRoutine(delay));
        }

        /// <summary>
        /// Retracted → Deployed: fade in if needed, then Battle City blade swing.
        /// </summary>
        public void DeployForDuel(float delay = 0f)
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                _deployed = true;
                PhaseButtons?.SetDeployed(true);
                _skin?.SnapPresence(1f);
                _fx.Play(DiskFxEvent.BladeDeploy);
                return;
            }

            if (_deployRoutine != null)
                StopCoroutine(_deployRoutine);
            _deployRoutine = StartCoroutine(DeployRoutine(delay));
        }

        /// <summary>Fold disk shut (leave duel / inactive AR arm).</summary>
        public void Retract(float delay = 0f) => Retract(delay, fadeOut: false);

        /// <summary>Fold then dissipate (Map / leave AR).</summary>
        public void RetractThenFadeOut(float delay = 0f) => Retract(delay, fadeOut: true);

        void Retract(float delay, bool fadeOut)
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                _deployed = false;
                LpCounter?.PowerOff();
                PhaseButtons?.SetDeployed(false);
                _fx.Play(DiskFxEvent.BladeRetract);
                _skin?.SnapPresence(fadeOut ? 0f : 1f);
                return;
            }

            if (_deployRoutine != null)
                StopCoroutine(_deployRoutine);
            _deployRoutine = StartCoroutine(RetractRoutine(delay, fadeOut));
        }

        IEnumerator FadeInRetractedRoutine(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            _fx.SnapRetracted();
            _deployed = false;
            PhaseButtons?.SetDeployed(false);
            _skin?.FadePresence(1f, SpiritDuelerSkin.FadeInSeconds);
            yield return new WaitForSecondsRealtime(SpiritDuelerSkin.FadeInSeconds);
            _deployRoutine = null;
        }

        IEnumerator DeployRoutine(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            RelayoutOfficialZones();
            if (_skin != null && _skin.Presence < 0.85f)
            {
                _skin.FadePresence(1f, SpiritDuelerSkin.FadeInSeconds);
                yield return new WaitForSecondsRealtime(0.28f);
            }

            _deployed = true;
            PhaseButtons?.SetDeployed(true);
            _fx.Play(DiskFxEvent.BladeDeploy);
            yield return new WaitForSecondsRealtime(0.9f);
            if (_fx.BladeOpen < 0.85f)
                _fx.SnapDeployed();

            Debug.Log($"[WRLDZ AR] {(IsPlayerSide ? "Player" : "Opp")} disk DEPLOY open={_fx.BladeOpen:0.00}");
            _deployRoutine = null;
        }

        IEnumerator RetractRoutine(float delay, bool fadeOut)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            _deployed = false;
            LpCounter?.PowerOff();
            PhaseButtons?.SetDeployed(false);
            _fx.Play(DiskFxEvent.BladeRetract);
            yield return new WaitForSecondsRealtime(0.72f);
            if (fadeOut)
            {
                _skin?.FadePresence(0f, SpiritDuelerSkin.FadeOutSeconds);
                yield return new WaitForSecondsRealtime(SpiritDuelerSkin.FadeOutSeconds);
            }

            Debug.Log($"[WRLDZ AR] {(IsPlayerSide ? "Player" : "Opp")} disk RETRACT fadeOut={fadeOut}");
            _deployRoutine = null;
        }

        public void SetZonesHot(bool hot, ArDuelZoneKind? only = null)
        {
            foreach (var z in Zones)
            {
                if (only.HasValue && z.Kind != only.Value)
                {
                    z.SetHot(false);
                    continue;
                }

                z.SetHot(hot);
            }
        }

        /// <summary>Show engine-legal empty pads for the selected hand card.</summary>
        public void ApplyLegalHighlights(System.Collections.Generic.List<LegalIntentService.LegalSlot> slots,
            ArDiskZone hover = null)
        {
            foreach (var z in Zones)
            {
                var on = slots != null && SlotMatches(slots, z);
                z.SetLegalHighlight(on, hover == z);
            }
        }

        public void ClearLegalHighlights()
        {
            foreach (var z in Zones)
                z.SetLegalHighlight(false);
        }

        public ArDiskZone FindZone(ArDuelZoneKind kind, int index)
        {
            foreach (var z in Zones)
                if (z.Kind == kind && z.Index == index)
                    return z;
            return null;
        }

        static bool SlotMatches(System.Collections.Generic.List<LegalIntentService.LegalSlot> slots, ArDiskZone z)
        {
            var kind = ToRules(z.Kind);
            foreach (var s in slots)
            {
                if (s.Kind != kind) continue;
                if (kind == WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell ||
                    kind == WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft ||
                    kind == WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight)
                    return true;
                if (s.Index == z.Index) return true;
            }

            return false;
        }

        static WRLDZ.Duel.Rules.RulesZoneKind ToRules(ArDuelZoneKind k) => k switch
        {
            ArDuelZoneKind.Monster => WRLDZ.Duel.Rules.RulesZoneKind.Monster,
            ArDuelZoneKind.SpellTrap => WRLDZ.Duel.Rules.RulesZoneKind.SpellTrap,
            ArDuelZoneKind.FieldSpell => WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell,
            ArDuelZoneKind.PendulumLeft => WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft,
            ArDuelZoneKind.PendulumRight => WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight,
            _ => WRLDZ.Duel.Rules.RulesZoneKind.Monster
        };

        public ArDiskZone FindNearestValid(Vector3 worldPoint, float maxRadius, CardInstance card,
            DuelEngine engine, DuelistState who, bool preferSet)
        {
            ArDiskZone best = null;
            // Use the larger of zone snap radius and caller forgiveness (was: zone-only,
            // which ignored generous release radii and made drops feel sticky/missy).
            var bestD = Mathf.Max(0.05f, maxRadius);
            foreach (var z in Zones)
            {
                if (!z.IsPlayerSide && IsPlayerSide) continue;
                var d = Vector3.Distance(z.transform.position, worldPoint);
                var allow = Mathf.Max(z.SnapRadius * 1.75f, maxRadius);
                if (d > allow || d > bestD) continue;
                var legal = ArSnapRules.QueryLegal(engine, who, card, z);
                if (!legal.Any)
                {
                    // Still allow the caller's preferSet path (Shift Set, etc.)
                    var v = ArSnapRules.Validate(engine, who, card, z, preferSet);
                    if (!v.Ok) continue;
                }
                bestD = d;
                best = z;
            }

            return best;
        }

        /// <summary>Mirror engine field onto zone occupant refs.</summary>
        public void SyncOccupantsFrom(DuelistState who)
        {
            if (who == null) return;
            for (var i = 0; i < 5; i++)
            {
                if (MonsterZones[i] != null)
                    MonsterZones[i].Occupant = who.MonsterZones[i]?.Occupant;
                if (SpellTrapZones[i] != null)
                    SpellTrapZones[i].Occupant = who.SpellTrapZones[i]?.Occupant;
            }

            if (FieldSpellZone != null)
                FieldSpellZone.Occupant = who.FieldSpellZone?.Occupant;
            if (PendulumLeft != null)
                PendulumLeft.Occupant = who.PendulumZones != null && who.PendulumZones.Length > 0
                    ? who.PendulumZones[0]?.Occupant
                    : null;
            if (PendulumRight != null)
                PendulumRight.Occupant = who.PendulumZones != null && who.PendulumZones.Length > 1
                    ? who.PendulumZones[1]?.Occupant
                    : null;
        }

        static void EnsureDiskMesh()
        {
            if (_diskMesh == null)
            {
                var stream = System.IO.Path.Combine(Application.streamingAssetsPath,
                    SpiritDuelerDiskView.StreamingRelativePath);
                if (System.IO.File.Exists(stream))
                    _diskMesh = ObjMeshLoader.LoadFromFile(stream, "BattleCityDuelDisk");
            }

            if (_diskMesh != null)
                _diskMesh.RecalculateNormals();
        }

        static Material MakeDiskMat(Color accent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "ArDiskRigMat" };
            var baseCol = new Color(0.1f, 0.14f, 0.2f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accent * 0.55f);
            }

            return mat;
        }

        static Material MakeUnlit(Color c) => ArFieldMaterials.Get(c);

        void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject);
        }
    }
}
