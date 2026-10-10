using System;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Everything a match's rules may depend on besides the decks and the players' decisions: the RNG
    /// seed, the ERAZ text band and the rule flags. Rules code reads these from
    /// <see cref="DuelEngine.Config"/> only — never from PlayerPrefs, AppSession or a device setting —
    /// so a server (or a replay) given the same config reproduces the same duel.
    /// Unity: <c>Core/DuelBootstrap</c> fills it from <c>ArDuelMatchConfig</c> + the device prefs at duel
    /// start. Server: filled from the match ticket (BACKEND_DESIGN §7.2 <c>seed</c>, <c>rule_flags</c>).
    /// </summary>
    [Serializable]
    public sealed class DuelMatchConfig
    {
        /// <summary>
        /// Shuffle / coin / die seed. Null: the engine draws a fresh one on every
        /// <see cref="DuelEngine.StartDuel(WRLDZ.Data.CardDatabase, WRLDZ.Data.DeckFile, WRLDZ.Data.DeckFile)"/>
        /// (restarts reshuffle). The seed actually used is <see cref="DuelRng.Seed"/>.
        /// </summary>
        public int? Seed;

        /// <summary>ERAZ band whose official text snapshot compiles card effects; null/empty = cards_db.json.</summary>
        public string EraId;

        /// <summary>
        /// Targeting offers include the activating Spell/Trap even when another legal S/T exists
        /// (self-target is always legal; this only changes the default offer). Ticket flag
        /// <c>allowSpellTrapSelfTarget</c>; Unity copies the device pref in.
        /// </summary>
        public bool AllowSpellTrapSelfTarget;

        /// <summary>Result pays rewards or stakes (tiers T1/T2). Forces the LLM effect compiler off.</summary>
        public bool Rewarded;

        /// <summary>
        /// Lab / tooling only: a compile miss during this duel may ask the LLM effect compiler. Ignored when
        /// <see cref="Rewarded"/> — card behaviour in a rewarded duel never depends on a live model call.
        /// </summary>
        public bool AllowAiEffectCompile;

        /// <summary>Write the review JSONL / transcript under persistentDataPath on game over (client default).</summary>
        public bool ExportReviewLog = true;

        public bool EffectiveAllowAiEffectCompile => AllowAiEffectCompile && !Rewarded;

        public DuelMatchConfig Clone() => (DuelMatchConfig)MemberwiseClone();
    }
}
