using System.Collections.Generic;
using WRLDZ.Duel.Rules;

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
        YouTakeDamage,

        /// <summary>Response to the most recently activated Chain Link.</summary>
        ChainResponse,

        /// <summary>
        /// SummonNegation batch (v141): the summon-negation window. The monster is in its zone, but its
        /// Normal / Tribute / Flip Summon (or Special Summon by procedure) has not succeeded yet; only
        /// "When a monster(s) would be Summoned" Counter Traps answer it (Solemn Judgment / Horn of Heaven).
        /// Pass lets the Summon succeed (then its "when Summoned" effects and the MonsterSummoned window).
        /// </summary>
        SummonNegation,

        /// <summary>
        /// SpellTrapDestroyedResponse batch (v142): 1 or more of the responder's Spell / Trap Cards were just
        /// destroyed and sent from the field to the GY by a card effect their opponent controls. Offered once
        /// nothing is resolving (DuelEngine.OfferSpellTrapDestroyedWindow); only Traps whose activation
        /// condition is that event answer it (Gust / Driving Snow). Pass: play continues.
        /// </summary>
        SpellTrapDestroyedByOpponent,

        /// <summary>
        /// HandDisruption batch (v142): the responder's opponent just drew a card(s) outside either Draw Phase.
        /// Only "Activate only when your opponent draws a card(s) outside of either Draw Phase" cards answer it
        /// (Appropriate). Offered once the draw's chain has resolved (TextEffects.HandDisruptionRuntime);
        /// Pass lets play continue. Appended — never insert above.
        /// </summary>
        OpponentDrewOutsideDrawPhase,

        /// <summary>
        /// BattleWindows batch: the end of the Damage Step, after damage calculation and battle destruction (ygopro
        /// EVENT_DAMAGE_STEP_END). Offered to the turn player, then the opponent, only when they have a legal answer
        /// (Destruction Punch; Michizure for a monster sent from the field to their GY in that Damage Step). Pass:
        /// the Damage Step ends. Appended — never insert above.
        /// </summary>
        EndOfDamageStep,

        /// <summary>
        /// BattleWindows batch: 1 or more monsters were just sent from the field to the responder's GY (ygopro
        /// EVENT_TO_GRAVE, previous location Monster Zone). Offered once play is idle (DuelEngine Notify), like
        /// <see cref="SpellTrapDestroyedByOpponent"/>; only Michizure-type Traps answer it. Pass: play continues.
        /// Appended — never insert above.
        /// </summary>
        MonsterSentFromFieldToYourGy,

        /// <summary>
        /// EquipModel batch: a monster was just equipped with an Equip Card (ygopro EVENT_EQUIP: an Equip Spell or
        /// Union equipped, a monster becoming an Equip Card, an Equip Card moved). Offered once play is idle (DuelEngine
        /// Notify), turn player first, like <see cref="MonsterSentFromFieldToYourGy"/>; only Traps whose activation
        /// condition is that event (Armored Glass) answer it. Pass: play continues. Appended — never insert above.
        /// </summary>
        MonsterEquipped,
        /// <summary>
        /// EngineHygiene batch (v160): the responder just discarded 1 or more cards from their hand (TextEffects.DiscardEvents,
        /// ygopro EVENT_DISCARD). Offered once play is idle (DuelEngine Notify); only Forced Requisition answers it.
        /// Pass: play continues. Appended — never insert above.
        /// </summary>
        YouDiscardedFromHand,
        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163): the responder's monster(s) were just returned from the field to
        /// their hand by the opponent's card effect (TextEffects.MajorRiotRuntime, ygopro EVENT_TO_HAND with rp ~= tp).
        /// Offered once play is idle (DuelEngine Notify), turn player first; only Major Riot answers it. Pass: play
        /// continues. Appended — never insert above.
        /// </summary>
        MonstersReturnedToHandByOpponent,
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
        /// Seconds the responder has to activate. Default is the player-facing
        /// 5s heads-up; expiry auto-passes. Cinematic impact length is separate.
        /// </summary>
        public float ReactionSeconds = CombatAnimTimings.DefaultResponseSeconds;

        /// <summary>Anime motion line while the animation plays.</summary>
        public string MotionLine;

        // AttackDeclared
        public CardInstance Attacker;
        public CardInstance AttackTarget; // null = direct attack
        public DuelistState AttackingPlayer;

        /// <summary>Chain Link being answered when Timing is ChainResponse.</summary>
        public ChainLink ChainTarget;

        /// <summary>
        /// ChainResponse re-offered to a human controller of the last link after the opponent passed: only
        /// chain-answer effects (Counter Traps, monster chain Quick Effects) are offered, not free-chain
        /// Traps / Quick-Plays, so a player is not prompted (timed) to chain onto their own activation
        /// every time. The AI is offered everything and decides itself.
        /// </summary>
        public bool ChainAnswersOnly;

        // MonsterSummoned
        public CardInstance Summoned;
        public DuelistState Summoner;

        /// <summary>Cards the responder may activate before impact.</summary>
        public readonly List<CardInstance> LegalCards = new();
    }
}
