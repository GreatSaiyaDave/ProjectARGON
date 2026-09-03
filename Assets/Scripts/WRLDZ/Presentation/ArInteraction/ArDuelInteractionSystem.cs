using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Root orchestrator for the AR duel interaction stack (anime dual-board model):
    /// <list type="bullet">
    /// <item><b>Card Field</b> — left-arm disks (yours and opponent)</item>
    /// <item><b>Hologram Field</b> — empty-air midfield projections (no playmat mesh)</item>
    /// <item>Hand volume, drag-snap, anime director, engine sync</item>
    /// </list>
    /// Disks mount retracted; <see cref="DeployDisksForDuel"/> plays anime blade deploy.
    /// Lenses: <see cref="UseExternalTracker"/> with <see cref="XrWorldArmTracker"/> at 1:1 m.
    /// </summary>
    public class ArDuelInteractionSystem : MonoBehaviour
    {
        public const int DefaultLayer = 28;

        public IArTrackingSource Tracker { get; private set; }
        public ArDuelDiskRig PlayerDisk { get; private set; }
        /// <summary>Opponent's physical left-arm disk (shared stage).</summary>
        public ArDuelDiskRig OppDisk { get; private set; }
        /// <summary>Physical cards on both arm disks.</summary>
        public ArCardFieldController CardField { get; private set; }
        public ArHandVolume HandVolume { get; private set; }
        /// <summary>AI / P2 hand at the far end of the street (backs toward you).</summary>
        public ArHandVolume OppHandVolume { get; private set; }
        public ArCardDragSystem Drag { get; private set; }
        /// <summary>Empty-air midfield holograms (hologram field — no visible board).</summary>
        public ArArenaHologramManager Arena { get; private set; }
        public ArAnimeSequenceDirector Anime { get; private set; }
        public ArDuelSyncBridge SyncBridge { get; private set; }

        /// <summary>True when disks/arena use real-world meters (Quest / lenses).</summary>
        public bool LifeSize { get; private set; }

        Camera _stageCam;
        RectTransform _viewport;
        DuelEngine _engine;
        CardDatabase _db;
        bool _built;
        SimulatedArmTracker _simTracker;
        Transform _stageRoot;

        /// <summary>
        /// Build interaction graph under stageRoot. Stage camera renders to RT; viewport is the UI RawImage.
        /// </summary>
        public static ArDuelInteractionSystem Mount(
            Transform stageRoot,
            Camera stageCamera,
            RectTransform arViewport,
            Transform lifetimeHost = null)
        {
            var host = new GameObject("ArDuelInteractionSystem");
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);
            else
                host.transform.SetParent(stageRoot, false);

            var sys = host.AddComponent<ArDuelInteractionSystem>();
            sys._stageCam = stageCamera;
            sys._viewport = arViewport;
            sys._stageRoot = stageRoot;
            sys.Build(stageRoot);
            return sys;
        }

        void Build(Transform stageRoot)
        {
            if (_built) return;
            _built = true;
            _stageRoot = stageRoot;

            var match = AppSession.Instance != null ? AppSession.Instance.PendingArMatch : null;
            var sep = match != null ? match.SeparationMeters : ArDuelMatchConfig.DefaultStandM;
            // Prefer auto surface-scan depth when present (PvE + PvP)
            if (match != null && match.AutoSurfaceScan && match.SurfaceDepthM >= ArDuelMatchConfig.MinSeparationM)
                sep = match.SurfaceDepthM;
            // Editor never uses OpenXR trackers (Game view has no HMD).
            // Salvage OST *does* run in Editor (mouse-look combiner preview).
            var wantSalvage = ArPresentationTarget.IsSalvageOst
                              || ArSalvageLensesSession.PreferSalvageOst;
            var wantArCore = !wantSalvage && ArFoundationSession.IsBuilt;
            var wantLenses = !wantSalvage && !wantArCore && !Application.isEditor &&
                             (ArLensesSession.ShouldUseLensesPath() || ArPresentationTarget.IsLenses);

            if (wantSalvage)
            {
                LifeSize = true;
                var salvage = ArSalvageLensesSession.Ensure();
                salvage.BeginSession(sep);
                if (salvage.Tracker != null)
                {
                    Tracker = salvage.Tracker;
                    salvage.ApplySeparation(sep);
                }
            }
            else if (wantArCore)
            {
                LifeSize = true;
                var arf = ArFoundationSession.Instance;
                if (arf != null)
                {
                    arf.BeginSession(sep);
                    if (arf.Tracker != null)
                    {
                        Tracker = arf.Tracker;
                        arf.ApplySeparation(sep);
                    }
                }
            }
            else if (wantLenses)
            {
                LifeSize = true;
                var session = ArLensesSession.Ensure();
                session.BeginSession(sep);
                if (session.Tracker != null)
                {
                    Tracker = session.Tracker;
                    session.ApplySeparation(sep);
                }
            }

            if (Tracker == null)
            {
                LifeSize = false;
                _simTracker = gameObject.AddComponent<SimulatedArmTracker>();
                _simTracker.stageRoot = stageRoot;
                _simTracker.stageCamera = _stageCam != null ? _stageCam.transform : null;
                // Human ready stance: moderate stage compression, arms near body (not floor props).
                _simTracker.StageUnitsPerMeter = Application.isEditor ? 0.46f : 0.48f;
                // Disks sit outside the holo volume with a visible air gap to the nearest row.
                _simTracker.ArmClearanceFromMid = Application.isEditor ? 2.55f : 2.35f;
                _simTracker.WristHeight = Application.isEditor ? 0.52f : 0.50f;
                _simTracker.OppWristHeight = Application.isEditor ? 0.58f : 0.56f;
                _simTracker.WristLateral = 0.22f;
                _simTracker.EnableIdleMotion = true;
                // Default standing separation if match missing
                if (sep < 1.2f) sep = ArDuelMatchConfig.DefaultStandM;
                _simTracker.ApplyPlayerSeparationMeters(sep);
                Tracker = _simTracker;
            }

            // Layer: lenses use Default (0) so HMD camera sees disks; phone keeps 28 + RT cam
            var layer = LifeSize ? 0 : DefaultLayer;

            // Your Spirit Dueler (left arm)
            PlayerDisk = ArDuelDiskRig.Create(stageRoot, playerSide: true, layer,
                new Color(0.25f, 0.9f, 1f));
            PlayerDisk.Tracker = Tracker;

            // Opponent's physical Spirit Dueler on their left arm (shared stage)
            OppDisk = ArDuelDiskRig.Create(stageRoot, playerSide: false, layer,
                new Color(1f, 0.35f, 0.55f));
            OppDisk.Tracker = Tracker;

            // Life-size: scale disk mesh to wearable physical size (~0.35 m blade)
            if (LifeSize)
            {
                ScaleDiskForLifeSize(PlayerDisk);
                ScaleDiskForLifeSize(OppDisk);
            }

            HandVolume = ArHandVolume.Create(stageRoot, layer, new Color(0.55f, 0.95f, 1f));
            HandVolume.Tracker = Tracker;
            HandVolume.SourceDeck = PlayerDisk;
            OppHandVolume = ArHandVolume.Create(stageRoot, layer, new Color(1f, 0.45f, 0.62f),
                opponent: true);
            OppHandVolume.Tracker = Tracker;
            OppHandVolume.SourceDeck = OppDisk;
            // Life-size still needs a generous pick target on phone/editor RT
            if (LifeSize)
            {
                HandVolume.CardScale = 0.16f;
                OppHandVolume.CardScale = 0.16f;
            }
            else
            {
                HandVolume.CardScale = 0.24f; // EditorSim / phone viewport — easy to hit
                OppHandVolume.CardScale = 0.20f;
            }

            CardField = ArCardFieldController.Create(stageRoot, PlayerDisk, OppDisk, layer);

            Arena = ArArenaHologramManager.Create(stageRoot, layer);
            Arena.Tracker = Tracker;
            Arena.PlayerDisk = PlayerDisk;
            Arena.OppDisk = OppDisk;
            Arena.PreferDynamicModels = false;

            Drag = gameObject.AddComponent<ArCardDragSystem>();
            Drag.Bind(_stageCam, _viewport, HandVolume, PlayerDisk, null);
            Drag.OppDisk = OppDisk;
            Drag.Arena = Arena;
            if (_viewport != null)
            {
                try { Drag.Hud = ArDragActionHud.Create(_viewport); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WRLDZ AR] Drag HUD skipped: " + ex.Message);
                }
            }

            var freeLook = _stageCam != null ? _stageCam.GetComponent<ArFreeLookCamera>() : null;
            if (freeLook != null)
            {
                freeLook.Drag = Drag;
                freeLook.Viewport = _viewport;
            }

            Anime = gameObject.AddComponent<ArAnimeSequenceDirector>();
            Anime.StageCamera = _stageCam;
            Anime.Arena = Arena;
            Anime.PlayerDisk = PlayerDisk;
            Anime.OppDisk = OppDisk;

            SyncBridge = gameObject.AddComponent<ArDuelSyncBridge>();
            SyncBridge.Arena = Arena;
            SyncBridge.CardField = CardField;
            SyncBridge.PlayerDisk = PlayerDisk;
            SyncBridge.OppDisk = OppDisk;

            if (_viewport != null && !LifeSize)
            {
                var raw = _viewport.GetComponent<RawImage>();
                if (raw != null)
                {
                    raw.raycastTarget = true;
                    ArViewportInput.Attach(raw, Drag, _stageCam);
                }
            }

            // Start hidden until deploy / pre-duel draw; never leave permanently off
            SetHandVolumeVisible(false);

            // Finish OpenXR floor bind after arena exists (ARCore / salvage already bound)
            if (LifeSize && !wantSalvage && !wantArCore)
                ArLensesBootstrap.BindDuelToLenses(this, stageRoot);

            Debug.Log(
                $"[WRLDZ AR] Dual boards mounted — CardField(disks) + " +
                $"HologramField(empty air) · sep={sep:0.0}m · lifeSize={LifeSize} · " +
                $"disks retracted · {(match != null ? match.SummaryLine() : "default")}");
        }

        /// <summary>Swap in OpenXR 1:1 tracker after mount (or re-bind).</summary>
        public void UseExternalTracker(IArTrackingSource external, bool lifeSize)
        {
            if (external == null) return;
            Tracker = external;
            LifeSize = lifeSize;
            if (PlayerDisk != null) PlayerDisk.Tracker = Tracker;
            if (OppDisk != null) OppDisk.Tracker = Tracker;
            if (HandVolume != null)
            {
                HandVolume.Tracker = Tracker;
                HandVolume.SourceDeck = PlayerDisk;
            }
            if (OppHandVolume != null)
            {
                OppHandVolume.Tracker = Tracker;
                OppHandVolume.SourceDeck = OppDisk;
            }
            if (Arena != null) Arena.Tracker = Tracker;
            if (lifeSize)
            {
                ScaleDiskForLifeSize(PlayerDisk);
                ScaleDiskForLifeSize(OppDisk);
            }

            Debug.Log($"[WRLDZ AR] External tracker · lifeSize={lifeSize} · {external.GetType().Name}");
        }

        static void ScaleDiskForLifeSize(ArDuelDiskRig rig)
        {
            if (rig?.DiskRoot == null) return;
            // Battle City disk ~ 0.4 m across on the arm
            rig.DiskRoot.localScale = Vector3.one * 0.38f;
        }

        public void BindEngine(DuelEngine engine, CardDatabase db)
        {
            var engineChanged = !ReferenceEquals(_engine, engine);
            _engine = engine;
            _db = db;
            if (Drag != null)
            {
                Drag.Engine = engine;
                Drag.StageCamera = _stageCam;
                Drag.ViewportRect = _viewport;
                Drag.Hand = HandVolume;
                Drag.PlayerDisk = PlayerDisk;
                Drag.OppDisk = OppDisk;
                Drag.Arena = Arena;
            }

            if (engineChanged)
            {
                Anime?.Bind(engine, _stageCam);
                SyncBridge?.Bind(engine, db);
                // Opening cinematic owns deploy when engine is in pre-duel sequence
                if (engine != null && engine.OpeningSequenceActive)
                {
                    SetHandVolumeVisible(false);
                    // Keep retracted until PreDuelCinematic runs
                }
                else
                    DeployDisksForDuel();
            }

            SyncNow();
        }

        /// <summary>
        /// Battle City deploy sequence: player disk snaps open, opponent follows,
        /// hand volume fades in. Call when duel becomes active in AR.
        /// </summary>
        public void DeployDisksForDuel()
        {
            PlayerDisk?.DeployForDuel(0.05f);
            OppDisk?.DeployForDuel(0.35f);
            StartCoroutine(RevealHandAfterDeploy(0.55f));
            Debug.Log("[WRLDZ AR] Disk deploy sequence — BladeDeploy");
        }

        /// <summary>Deploy blades only (pre-duel) — hand stays hidden until opening draw.</summary>
        public void DeployForDuelOnly(float playerDelay = 0.05f)
        {
            SetHandVolumeVisible(false);
            PlayerDisk?.DeployForDuel(playerDelay);
            Debug.Log("[WRLDZ AR] Disk deploy only (pre-duel, no hand yet)");
        }

        public void SetHandVolumeVisible(bool on)
        {
            if (HandVolume != null)
                HandVolume.gameObject.SetActive(on);
            if (OppHandVolume != null)
                OppHandVolume.gameObject.SetActive(on);
        }

        /// <summary>Fold both disks (leave duel / AR inactive arm).</summary>
        public void RetractDisks()
        {
            PlayerDisk?.Retract(0f);
            OppDisk?.Retract(0.12f);
            SetHandVolumeVisible(false);
            Debug.Log("[WRLDZ AR] Disks retracting");
        }

        IEnumerator RevealHandAfterDeploy(float delay)
        {
            if (!Application.isPlaying)
            {
                SetHandVolumeVisible(true);
                yield break;
            }

            yield return new WaitForSecondsRealtime(delay);
            SetHandVolumeVisible(true);
            if (_engine != null && _db != null)
            {
                HandVolume?.SyncHand(_engine.Player, _db);
                OppHandVolume?.SyncHand(_engine.Opponent, _db);
            }
        }

        public void SyncNow()
        {
            if (_engine == null || _db == null) return;
            // If field cards exist but blade still folded, force deploy so pad cards are visible
            if (PlayerDisk != null && !PlayerDisk.IsDeployed && HasAnyFieldCard(_engine))
                DeployDisksForDuel();

            // Hand volume (cards still in hand — backs/faces as floating holos)
            if (HandVolume != null)
            {
                if (!HandVolume.gameObject.activeSelf &&
                    _engine.Player != null && _engine.Player.HandCount > 0 &&
                    !_engine.OpeningSequenceActive)
                    SetHandVolumeVisible(true);
                if (HandVolume.gameObject.activeInHierarchy)
                    HandVolume.SyncHand(_engine.Player, _db);
            }

            if (OppHandVolume != null)
            {
                if (!OppHandVolume.gameObject.activeSelf &&
                    _engine.Opponent != null && _engine.Opponent.HandCount > 0 &&
                    !_engine.OpeningSequenceActive)
                    OppHandVolume.gameObject.SetActive(true);
                if (OppHandVolume.gameObject.activeInHierarchy)
                    OppHandVolume.SyncHand(_engine.Opponent, _db);
            }
            // Card Field first so disk zone transforms hold current pads for spawn origins
            CardField?.SyncFromEngine(_engine, _db);
            // Arena: large card assets fly out from disk zones into midfield columns
            Arena?.SyncFromEngine(_engine, _db);

            if (_engine.Player != null)
            {
                PlayerDisk?.LpCounter?.Sync(_engine.Player.LifePoints, PlayerDisk.IsDeployed);
                PlayerDisk?.SyncGraveyardPile(_engine.Player.Graveyard, _db);
            }

            if (_engine.Opponent != null)
            {
                OppDisk?.LpCounter?.Sync(_engine.Opponent.LifePoints, OppDisk.IsDeployed);
                OppDisk?.SyncGraveyardPile(_engine.Opponent.Graveyard, _db);
            }
            if (_engine.Player != null && _engine.Opponent != null)
                Arena?.SyncLifePoints(_engine.Player.LifePoints, _engine.Opponent.LifePoints);
        }

        /// <summary>Engine-legal empty zones for a selected/dragged hand card.</summary>
        public void ShowLegalPlacements(CardInstance card, ArDiskZone hover = null,
            DuelistState who = null)
        {
            if (_engine == null || card == null)
            {
                ClearLegalPlacements();
                return;
            }

            who ??= _engine.Player;
            var slots = LegalIntentService.LegalSlots(_engine, who, card);
            ShowLegalPlacements(slots, hover);
            Debug.Log($"[WRLDZ AR] Legal zones · {card.Name} · {slots.Count} slot(s)");
        }

        /// <summary>Highlight a pre-filtered slot list (after Summon / Set is chosen).</summary>
        public void ShowLegalPlacements(List<LegalIntentService.LegalSlot> slots, ArDiskZone hover = null)
        {
            if (Drag != null) Drag.HighlightsActive = slots != null && slots.Count > 0;
            PlayerDisk?.ApplyLegalHighlights(slots, hover);
            // Arena is cinematic Solid Vision — never a drop / tap target.
            Arena?.ClearLegalHighlights();
            OppDisk?.ClearLegalHighlights();
        }

        /// <summary>
        /// Occupied-zone blink for the local responder's legal Set cards.
        /// Opponent disk and arena never show which face-down is live.
        /// </summary>
        public void ShowResponseActivations(List<LegalIntentService.LegalSlot> slots)
        {
            if (Drag != null) Drag.HighlightsActive = slots != null && slots.Count > 0;
            PlayerDisk?.ApplyLegalHighlights(slots);
            OppDisk?.ClearLegalHighlights();
            Arena?.ClearLegalHighlights();
        }

        public void ClearLegalPlacements()
        {
            if (Drag != null) Drag.HighlightsActive = false;
            PlayerDisk?.ClearLegalHighlights();
            OppDisk?.ClearLegalHighlights();
            Arena?.ClearLegalHighlights();
        }

        static bool HasAnyFieldCard(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return false;
            static bool Side(DuelistState who)
            {
                if (who == null) return false;
                foreach (var z in who.MonsterZones)
                    if (z?.Occupant != null) return true;
                foreach (var z in who.SpellTrapZones)
                    if (z?.Occupant != null) return true;
                if (who.FieldSpellZone?.Occupant != null) return true;
                return false;
            }

            return Side(engine.Player) || Side(engine.Opponent);
        }

        void OnDestroy()
        {
            Anime?.Unbind();
            SyncBridge?.Unbind();
        }
    }
}
