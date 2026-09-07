// Headless stand-ins for the AR presentation layer and the session/GPS glue.
// The rules engine ("referee") calls into these hooks so the AR duel disk /
// arena holograms ("TV") can react; headlessly they are inert. Kept OUTSIDE
// the Unity Assets/ tree so Unity uses the real implementations.
using System;

namespace WRLDZ.Core
{
    // Mirrors Core/ArDuelMatchConfig.cs enum. The real config type drags in GPS
    // (MapEnvironment); headlessly we only need the launch-kind + era band.
    public enum ArDuelLaunchKind
    {
        CreateMenu = 0, TearZone = 1, Hub = 2, Practice = 3, LabTest = 4,
        NearbyChallenge = 5, NpcStreet = 6, TearBoss = 7, RaidBoss = 8, StoryEra = 9,
    }

    public class ArDuelMatchConfig
    {
        public string ErazBandId = WRLDZ.Duel.Rules.ErazFormat.Original;
        public ArDuelLaunchKind Launch;
        public string apiKey;

        public static ArDuelMatchConfig DefaultQuick() =>
            new ArDuelMatchConfig { ErazBandId = WRLDZ.Duel.Rules.ErazFormat.Original, Launch = ArDuelLaunchKind.Practice };
    }

    // Minimal account store used by ErazProgress.GrantBadge (not exercised by the
    // rules suites). PlayerProgress/PlayerInventory are the real Data types.
    public static class LocalAccountStore
    {
        public class Account
        {
            public WRLDZ.Data.PlayerProgress progress;
            public WRLDZ.Data.PlayerInventory inventory;
            public void EnsureProgress() { progress ??= new WRLDZ.Data.PlayerProgress(); }
            public void EnsureInventory() { inventory ??= new WRLDZ.Data.PlayerInventory(); }
        }
    }

    public static class ArtifactService
    {
        public static string ErazId(string bandId) => "eraz_" + (bandId ?? "");
        public static void Grant(LocalAccountStore.Account acc, string defId, int qty) { }
        public static void Grant(WRLDZ.Data.PlayerProgress p, WRLDZ.Data.PlayerInventory inv, string defId, int qty) { }
    }

    // Headless stand-in for the session MonoBehaviour. No live instance exists,
    // so Instance is null and era resolution falls back to the default band.
    public class AppSession
    {
        public static AppSession Instance => null;
        public static AppSession Ensure() => Instance;
        public ArDuelMatchConfig PendingArMatch => null;

        // Preserve the real rule: Hub / overworld CREATE require the Original badge.
        public static bool RequiresOriginalBadgeForLaunch(ArDuelLaunchKind launch) =>
            launch == ArDuelLaunchKind.Hub || launch == ArDuelLaunchKind.CreateMenu;
    }
}

namespace WRLDZ.Duel.Ocg
{
    // Headless stand-in for the AGPL native-OCG lab host (excluded from build).
    // The lab path is never active headlessly, so IsActive is false and the
    // engine always uses its own rules ("referee"), never the oracle.
    public sealed class OcgLabDuelHost
    {
        public static OcgLabDuelHost Current => null;
        public static bool IsActive => Current != null;
        public WRLDZ.Duel.DuelCommandResult TryExecute(WRLDZ.Duel.DuelistState who, WRLDZ.Duel.DuelIntent intent) => null;
    }
}

namespace WRLDZ.Presentation
{
    // Referenced by CardDatabase art helpers (never called in headless mode).
    public static class CardArtFocus
    {
        public static UnityEngine.Rect ArtworkNormRect => new UnityEngine.Rect(0.1f, 0.2f, 0.8f, 0.6f);
        public static bool IsPreCroppedIllustration(UnityEngine.Texture2D tex) => false;
        public static bool LooksLikeFullCardScan(UnityEngine.Texture2D tex) => false;
    }
}

namespace WRLDZ.Presentation.ArInteraction
{
    // AR duel-disk / hologram staging hooks. No-ops headlessly.
    public static class SpellActivationPresentation
    {
        public static void RegisterActivation(int instanceId, int cardId, string cardName,
            bool playerSide, int zoneIndex, bool staysOnField, bool flatOnBoard = false) { }
        public static void QueueFadeToGy(int instanceId) { }
        public static bool WantsFadeToGy(int instanceId) => false;
        public static bool IsFlatOnBoard(int instanceId) => false;
        public static void EnqueueGhostIfNeeded(int instanceId) { }
        public static void Clear() { }
    }
}
