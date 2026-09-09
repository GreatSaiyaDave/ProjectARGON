namespace WRLDZ.Duel
{
    /// <summary>
    /// Anime TCG move intent — produced by tap UI or verbal announce.
    /// Always executed through <see cref="DuelCommandService"/> so legality is shared.
    /// </summary>
    public enum DuelIntentKind
    {
        None = 0,
        NormalSummonAtk,
        SetMonsterDef,
        SetSpellTrap,
        Activate,
        FlipSummon,
        ChangePosition,
        EnterBattlePhase,
        EnterMainPhase2,
        Attack,
        DirectAttack,
        SpecialSummon,
        PassResponse,
        EndTurn,
        ClearTributes,
        CancelTarget,
        ConfirmLpZero
    }

    /// <summary>Structured command. Card names/instances optional depending on kind.</summary>
    public sealed class DuelIntent
    {
        public DuelIntentKind Kind;
        public CardInstance Card;
        public CardInstance Target;
        public string RawText;
        public string CardNameHint;
        public string TargetNameHint;
        /// <summary>Preferred field column (0–4). −1 = first empty (voice / legacy).</summary>
        public int ZoneIndex = -1;

        /// <summary>How the intent was produced (for logs / anime flavor).</summary>
        public string Source = "ui"; // "ui" | "voice" | "type" | "ai"

        public static DuelIntent Of(DuelIntentKind kind, CardInstance card = null, CardInstance target = null,
            string raw = null, string source = "ui", int zoneIndex = -1)
        {
            return new DuelIntent
            {
                Kind = kind,
                Card = card,
                Target = target,
                RawText = raw,
                Source = source,
                ZoneIndex = zoneIndex
            };
        }
    }

    public sealed class DuelCommandResult
    {
        public bool Ok;
        public string Message;
        public DuelIntent Intent;

        public static DuelCommandResult Success(DuelIntent intent, string msg = null) =>
            new() { Ok = true, Intent = intent, Message = msg ?? "OK" };

        public static DuelCommandResult Fail(DuelIntent intent, string msg) =>
            new() { Ok = false, Intent = intent, Message = msg ?? "Illegal move." };
    }
}
