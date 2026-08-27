using System.Collections.Generic;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Combat-reaction windows. Duration = action animation until IMPACT — not a paused menu.
    /// Player taps or announces while the monster is mid-animation; miss the hit = too late.
    /// </summary>
    public enum ResponseTiming
    {
        None = 0,

        /// <summary>
        /// Attack animation in flight — react before impact.
        /// Legal: Mirror Force, Negate Attack, Waboku, set Quick-Play (e.g. MST).
        /// </summary>
        AttackDeclared,

        /// <summary>
        /// Summon appear animation — react before the form locks in.
        /// Legal: Trap Hole (ATK ≥ 1000), set Quick-Play free-chain.
        /// </summary>
        MonsterSummoned,

        /// <summary>
        /// Damage calculation (Damage Step) — effects that say "during damage calculation".
        /// Official example: Kuriboh (discard from hand; take no battle damage from that battle).
        /// Konami text: "During damage calculation, if your opponent's monster attacks (Quick Effect)…"
        /// </summary>
        DamageCalculation,

        /// <summary>
        /// Open game state on the opponent's turn (Main or Battle start).
        /// Legal: compiled "Activate only during your opponent's turn" traps (Absolute End).
        /// </summary>
        OpponentOpenState,

        /// <summary>
        /// After LP damage is applied (battle or effect).
        /// Legal: "when you take damage to your Life Points" traps (Numinous Healer).
        /// </summary>
        YouTakeDamage
    }

    /// <summary>
    /// Open combat reaction (one responder; activate or let impact land).
    /// Dual input: tap + voice. Timer = <see cref="ReactionSeconds"/> from the source card's anim.
    /// </summary>
    public class PendingResponse
    {
        public ResponseTiming Timing;
        public DuelistState Responder;
        public string Prompt;
        /// <summary>Unscaled time when the animation / window started.</summary>
        public float OpenedUnscaledTime;

        /// <summary>
        /// Seconds until impact (tied to combat anim of the card that acted).
        /// Typical attack charge ≈ 5s for heavy monsters.
        /// </summary>
        public float ReactionSeconds = CombatAnimTimings.DefaultAttackImpact;

        /// <summary>Anime motion line while the animation plays.</summary>
        public string MotionLine;

        // AttackDeclared
        public CardInstance Attacker;
        public CardInstance AttackTarget; // null = direct attack
        public DuelistState AttackingPlayer;

        // MonsterSummoned
        public CardInstance Summoned;
        public DuelistState Summoner;

        /// <summary>Cards the responder may activate before impact.</summary>
        public readonly List<CardInstance> LegalCards = new();
    }
}
