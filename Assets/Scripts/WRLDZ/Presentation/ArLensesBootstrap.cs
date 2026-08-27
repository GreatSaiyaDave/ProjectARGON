using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Lenses-first bootstrap for Quest 3 / OpenXR / Android XR.
    /// Starts <see cref="ArLensesSession"/>, binds floor arena + arm disks at 1:1 meters.
    /// Safe no-op when no HMD is present (phone/Editor keep their paths).
    /// </summary>
    public class ArLensesBootstrap : MonoBehaviour
    {
        [Tooltip("Editor only: use host webcam to preview phone AR path.")]
        public bool preferEditorCamera;

        [Header("Optional overrides (auto-filled from ArLensesSession)")]
        public Transform xrCamera;
        public Transform xrLeftController;
        public Transform floorAnchor;
        public Transform arenaAnchor;

        [Header("Force lenses path (for lab on Quest)")]
        public bool forceLensesModeWhenXrPresent = true;

        void Awake()
        {
            WrldzLab.Apply();
            ArPresentationTarget.PreferEditorCamera = preferEditorCamera;

            if (forceLensesModeWhenXrPresent && ArLensesSession.IsXrDisplayRunning())
                ArPresentationTarget.SetOverride(ArPresentationMode.LensesXr);

            var mode = ArPresentationTarget.Current;
            Debug.Log(
                $"[WRLDZ] Lab bootstrap · mode={mode} · {ArPresentationTarget.StatusLabel()} · " +
                $"device={SystemInfo.deviceModel}");

            if (mode == ArPresentationMode.LensesXr || ArLensesSession.IsXrDisplayRunning())
            {
                var sep = AppSession.Instance?.PendingArMatch?.SeparationMeters
                          ?? ArDuelMatchConfig.DefaultStandM;
                var session = ArLensesSession.Ensure();
                session.BeginSession(sep);
                PullRefsFromSession(session);
                Debug.Log("[WRLDZ] Lenses path — ArLensesSession started.");
            }
            else
            {
                Debug.Log("[WRLDZ] No HMD — PhoneCamera / EditorSim. Quest 3 OpenXR for life-size duels.");
            }
        }

        void PullRefsFromSession(ArLensesSession session)
        {
            if (session == null) return;
            if (xrCamera == null && session.Head != null) xrCamera = session.Head;
            if (xrLeftController == null) xrLeftController = session.LeftController;
            if (floorAnchor == null) floorAnchor = session.FloorAnchor;
            if (arenaAnchor == null) arenaAnchor = session.ArenaAnchor;
        }

        /// <summary>
        /// Re-parent duel stage pieces for life-size MR:
        /// arena → floor midfield, disks driven by <see cref="XrWorldArmTracker"/>.
        /// </summary>
        public void TryBindSpatialStage(Transform stageRoot, Transform playerDisk, Transform arena)
        {
            var session = ArLensesSession.Instance ?? ArLensesSession.Ensure();
            PullRefsFromSession(session);

            if (!ArLensesSession.ShouldUseLensesPath() && !ArPresentationTarget.IsLenses)
            {
                Debug.Log("[WRLDZ] TryBindSpatialStage skipped — not in lenses mode.");
                return;
            }

            if (session != null)
            {
                var sep = AppSession.Instance?.PendingArMatch?.SeparationMeters ?? session.SeparationMeters;
                session.BeginSession(sep);
                PullRefsFromSession(session);
            }

            // Floor-anchored midfield arena (life-size meters)
            var floor = arenaAnchor != null ? arenaAnchor : floorAnchor;
            if (floor != null && arena != null)
            {
                arena.SetParent(floor, false);
                arena.localPosition = Vector3.zero;
                arena.localRotation = Quaternion.identity;
                arena.localScale = Vector3.one;
                Debug.Log("[WRLDZ Lenses] Arena → floor midfield (1:1 m).");
            }

            // Stage root sits on floor so all holos are world-scale
            if (floorAnchor != null && stageRoot != null)
            {
                // Keep stage as child of session floor for consistent world lock
                if (stageRoot.parent != floorAnchor)
                {
                    var worldPos = stageRoot.position;
                    stageRoot.SetParent(floorAnchor, true);
                    // Prefer local zero so arena layout is relative to feet/midfield
                    stageRoot.localPosition = new Vector3(0f, 0f, 0f);
                    stageRoot.localRotation = Quaternion.identity;
                    stageRoot.localScale = Vector3.one;
                }

                Debug.Log("[WRLDZ Lenses] Stage root floor-locked.");
            }

            if (xrLeftController != null && playerDisk != null)
                Debug.Log("[WRLDZ Lenses] Player disk driven by left controller tracker (not re-parented).");

            if (xrCamera != null)
                Debug.Log("[WRLDZ Lenses] HMD camera active — life-size spatial duel.");
        }

        /// <summary>Ensure session + floor bind for an interaction system already built.</summary>
        public static void BindDuelToLenses(ArDuelInteractionSystem interaction, Transform stageRoot)
        {
            if (interaction == null) return;
            var session = ArLensesSession.Ensure();
            var sep = AppSession.Instance?.PendingArMatch?.SeparationMeters
                      ?? ArDuelMatchConfig.DefaultStandM;
            session.BeginSession(sep);

            // Swap tracker to 1:1 XR world tracker
            if (session.Tracker != null)
            {
                interaction.UseExternalTracker(session.Tracker, lifeSize: true);
            }

            // Parent arena holograms to midfield floor anchor
            if (session.ArenaAnchor != null && interaction.Arena != null)
            {
                interaction.Arena.transform.SetParent(session.ArenaAnchor, false);
                interaction.Arena.transform.localPosition = Vector3.zero;
                interaction.Arena.transform.localRotation = Quaternion.identity;
                interaction.Arena.transform.localScale = Vector3.one;
            }

            var boot = Object.FindAnyObjectByType<ArLensesBootstrap>();
            boot?.TryBindSpatialStage(stageRoot,
                interaction.PlayerDisk != null ? interaction.PlayerDisk.transform : null,
                interaction.Arena != null ? interaction.Arena.transform : null);

            // Pre-duel cinematic will deploy if OpeningSequenceActive; otherwise deploy now
            if (interaction != null)
            {
                // Leave deploy to PreDuelCinematic when engine is still in opening sequence
            }

            Debug.Log("[WRLDZ Lenses] Duel bound — arm disks + floor arena @ 1:1 m");
        }
    }
}
