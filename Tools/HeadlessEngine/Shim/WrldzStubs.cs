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
        public static string ErazPieceId(string bandId) =>
            string.IsNullOrEmpty(bandId) ? "" : "eraz." + bandId.Trim() + ".piece";
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
    // Headless stand-in for the lab Lua script index (the real store sits beside the native core,
    // which is excluded). Nothing is indexed headlessly, so the PERF-05 suite check skips itself.
    public static class OcgScriptStore
    {
        public static int IndexedCount => 0;
        public static bool HasCardScript(int cardId) => false;
    }

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
    // AR duel-disk / hologram staging hooks. No-ops headlessly. The timing constants are copied
    // verbatim from the real SpellActivationPresentation (DuelPresentationPacer uses them in const
    // expressions); the local-lineage activation beat (ActivationBeat) is inert here.
    public static class ArDiskMotion
    {
        public const float ToasterEject = 0.55f;
        public const float ToasterFlip = 0.70f;
        public const float ArenaAfterSetFlip = ToasterEject + ToasterFlip * 0.5f;
    }

    public static class SpellActivationPresentation
    {
        public const float ActivateSequenceSeconds = 1.65f;
        public const float RiseSeconds = ActivateSequenceSeconds - 0.55f;
        public const float RiseArtRevealFraction = 0.32f + 0.68f * 0.18f;
        public const float ArenaStartDelaySeconds = ArDiskMotion.ArenaAfterSetFlip;
        public const float ArtReadableDelaySeconds = ArenaStartDelaySeconds + RiseSeconds * RiseArtRevealFraction;
        public const float ReadBeforeResultSeconds = 1.4f;
        public const float OpponentReadBeforeResultSeconds = 1.8f;
        public const float ReadAfterResultSeconds = 2.0f;
        public const float OpponentReadAfterResultSeconds = 2.8f;
        public const float MinHoverSeconds = 4.5f;
        public const float OpponentReadHoverSeconds = 6.5f;
        public const float FadeToGySeconds = 0.75f;
        public const float BeatHardCapSeconds = 20f;
        public const float PlayerBeatSeconds = ArtReadableDelaySeconds +
            (ReadBeforeResultSeconds + ReadAfterResultSeconds > MinHoverSeconds
                ? ReadBeforeResultSeconds + ReadAfterResultSeconds : MinHoverSeconds) + FadeToGySeconds;
        public const float OpponentBeatSeconds = ArtReadableDelaySeconds +
            (OpponentReadBeforeResultSeconds + OpponentReadAfterResultSeconds > OpponentReadHoverSeconds
                ? OpponentReadBeforeResultSeconds + OpponentReadAfterResultSeconds : OpponentReadHoverSeconds) + FadeToGySeconds;

        // The engine branches on HasActivation (a cancelled / bounced activation), so keep a minimal model.
        static readonly System.Collections.Generic.HashSet<int> _active = new();
        public static void RegisterActivation(int instanceId, int cardId, string cardName,
            bool playerSide, int zoneIndex, bool staysOnField, bool flatOnBoard = false, bool isTrap = false)
        { lock (_active) _active.Add(instanceId); }
        public static void QueueFadeToGy(int instanceId) { }
        public static void MarkResolved(int instanceId) { }
        public static void CancelActivationPresentation(int instanceId) { lock (_active) _active.Remove(instanceId); }
        public static void AbandonActivation(int instanceId) { lock (_active) _active.Remove(instanceId); }
        public static bool HasActivation(int instanceId) { lock (_active) return _active.Contains(instanceId); }
        public static bool WantsFadeToGy(int instanceId) => false;
        public static bool IsFlatOnBoard(int instanceId) => false;
        public static void EnqueueGhostIfNeeded(int instanceId) { }
        public static void Clear() { lock (_active) _active.Clear(); }
    }

    public static class ActivationBeat
    {
        public static void Clear() { }
        public static void NoteChainLink(int instanceId, int linkNumber) { }
        public static void NoteNegated(int instanceId) { }
        public static void SetSource(int instanceId) { }
        public static void BeginResolve(int instanceId) { }
        public static void MarkResolved(int instanceId) { }
        public static void NoteResult(int resultInstanceId) { }
        public static void NoteSummonTrigger(WRLDZ.Duel.CardInstance summoned, int zoneIndex, bool playerSide) { }
        public static void NoteAttackTrigger(WRLDZ.Duel.CardInstance attacker) { }
        public static void NoteAttackEnded() { }
        public static bool AnyResultPending => false;
        public static float PendingResultGate => 0f;
        public static float HoldUntilUnscaled => 0f;
        public static string HoldReason() => null;
        public static bool BlocksAdvance(out string cardName, out float secondsLeft)
        { cardName = null; secondsLeft = 0f; return false; }
    }

    /// <summary>Referenced only by a presentation-layout assertion inside InteractionRegressionTests.
    /// Headless returns the input, so that check fails honestly (stub-expected).</summary>
    public static class ArArenaCardVisual
    {
        public static UnityEngine.Vector3 SpellActivatePresentLocal(UnityEngine.Vector3 zone, bool playerSide) => zone;
    }
}
