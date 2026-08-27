using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Where the spatial duel is presented.
    /// Lab default: PhoneCamera on S23 Ultra, EditorSim on PC.
    /// Product north star: LensesXr (OpenXR) when hardware exists — same stage.
    /// </summary>
    public enum ArPresentationMode
    {
        /// <summary>Unity Editor / no camera — RT stage only (daily PC iterate).</summary>
        EditorSim = 0,
        /// <summary>Phone rear camera passthrough + stage (S23 Ultra primary demo).</summary>
        PhoneCamera = 1,
        /// <summary>HMD passthrough (any OpenXR / Android XR) — later product path.</summary>
        LensesXr = 2,
        /// <summary>Salvage optical-see-through (combiner glass + OLED holos). No webcam.</summary>
        SalvageOst = 3
    }

    /// <summary>
    /// Resolves active presentation mode for the lab (S23 + PC).
    /// Never claims LensesXr unless an XR display subsystem is actually running.
    /// </summary>
    public static class ArPresentationTarget
    {
        static ArPresentationMode? _override;

        /// <summary>Force a mode for demos (null = auto-detect).</summary>
        public static void SetOverride(ArPresentationMode? mode) => _override = mode;

        public static ArPresentationMode Current
        {
            get
            {
                if (_override.HasValue) return _override.Value;

                // Live salvage session (duel only). The Prefer flag arms the next duel;
                // it must not flip the overworld into landscape.
                if (ArSalvageLensesSession.Instance != null &&
                    ArSalvageLensesSession.Instance.SessionRunning)
                    return ArPresentationMode.SalvageOst;

                // ── Editor lab: ALWAYS sim (or optional webcam) ──
                // Never auto-pick LensesXr in Editor — that hides the RT viewport and
                // leaves an empty Game view. Quest path is device builds + explicit override.
                if (Application.isEditor)
                {
                    if (PreferEditorCamera && HasCameraDevice())
                        return ArPresentationMode.PhoneCamera;
                    return ArPresentationMode.EditorSim;
                }

                // Real OpenXR HMD session (Quest, etc.)
                if (IsXrSessionLikelyActive())
                    return ArPresentationMode.LensesXr;

                // S23 / any Android phone → phone AR path (camera may open async)
                if (Application.isMobilePlatform)
                    return ArPresentationMode.PhoneCamera;

                // Standalone player with camera (rare)
                if (HasCameraDevice())
                    return ArPresentationMode.PhoneCamera;

                return ArPresentationMode.EditorSim;
            }
        }

        /// <summary>When true, Editor uses webcam if available (dev toggle for AR preview).</summary>
        public static bool PreferEditorCamera { get; set; }

        public static bool IsLenses => Current == ArPresentationMode.LensesXr;
        public static bool IsSalvageOst => Current == ArPresentationMode.SalvageOst;
        /// <summary>Wearable path: OpenXR HMD or salvage combiner. Life-size stage.</summary>
        public static bool IsWearable => IsLenses || IsSalvageOst;
        static string PhoneArStatus()
        {
            var arf = ArFoundationSession.Instance;
            if (arf != null && arf.SessionRunning)
                return arf.FloorLocked ? "ARCORE · 6DOF floor" : "ARCORE · scanning floor";
            return "PHONE AR · S23 camera";
        }

        public static bool IsPhone => Current == ArPresentationMode.PhoneCamera;
        public static bool IsSim => Current == ArPresentationMode.EditorSim;

        static bool HasCameraDevice()
        {
            try
            {
                return WebCamTexture.devices != null && WebCamTexture.devices.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static bool IsXrSessionLikelyActive()
        {
            try
            {
#if ENABLE_VR || UNITY_XR
                if (UnityEngine.XR.XRSettings.enabled &&
                    !string.IsNullOrEmpty(UnityEngine.XR.XRSettings.loadedDeviceName) &&
                    UnityEngine.XR.XRSettings.loadedDeviceName != "None")
                    return true;
#endif
                var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays)
                {
                    if (d != null && d.running)
                        return true;
                }
            }
            catch
            {
                // XR assemblies not present — fine for phone lab
            }

            return false;
        }

        public static string StatusLabel()
        {
            return Current switch
            {
                ArPresentationMode.LensesXr => "LENSES XR · passthrough duel",
                ArPresentationMode.SalvageOst => "SALVAGE OST · combiner holos",
                ArPresentationMode.PhoneCamera => PhoneArStatus(),
                _ => "PC EDITOR · spatial stage"
            };
        }
    }
}
