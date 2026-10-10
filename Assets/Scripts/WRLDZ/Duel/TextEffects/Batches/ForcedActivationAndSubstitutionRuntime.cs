using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ForcedActivationAndSubstitution batch (v163) — Spirit Elimination (Lua c69832741 + aux.SpElimFilter in
    /// cards_specific_functions.lua). Its resolution registers a player effect on its activator until the end of the turn
    /// (EFFECT_FLAG_PLAYER_TARGET, SetTargetRange(1,0), RESET_PHASE+PHASE_END). While it applies, every effect or cost
    /// that would banish a MONSTER from that player's GY must banish a monster that player controls instead (the
    /// substitute is chosen like the GY monster would have been: by the player banishing, as a cost or a target).
    /// Spells and Traps in the GY are not affected. When the requirement names a property (an Attribute or a Type:
    /// <c>aux.SpElimFilter(c,true)</c>) the substitute must be face-up and have it; otherwise a face-down monster
    /// qualifies too. One shared helper, read by every banish-from-GY path of the engine:
    /// <list type="bullet">
    /// <item>costs <see cref="EffectClause.BanishFromGyCount"/> (TextEffectRuntime.CollectBanishGy: Bazoo, Freed, the
    /// Spirit hand Summons, Dark Necrofear, Lekunga) and the banish hand procedures
    /// (EngineHygieneRuntime.ProcedureBanishCandidates: Spirit of Flames, Gigantes, Inferno);</item>
    /// <item>targets of a banishing clause in a GY zone whose Lua target filter uses SpElimFilter
    /// (<see cref="SubstitutesTargetsIn"/>; TextEffectRuntime.CollectTargets: Soul Release, Gravedigger Ghoul, Kycoo,
    /// Witch Doctor of Chaos, Book of Life's second target; not Disappear);</item>
    /// <item>Skull Lair's cost and Soul Demolition's targets (FaceUpQuickEffectsRuntime);</item>
    /// <item>Fiend Comedian's "banish all cards in your opponent's GY" (SimpleSpellTrapsRuntime).</item>
    /// </list>
    /// The substitutes are banished by <see cref="DuelEngine.BanishCard"/>, which moves a field monster like any other
    /// (they "remain removed from play"). Locks on cards in the GY (Necrovalley, Kycoo) do not apply to them.
    /// </summary>
    public static class SpiritEliminationRuntime
    {
        sealed class State
        {
            /// <summary>The player affected and the turn their Spirit Elimination resolved.</summary>
            public readonly Dictionary<DuelistState, int> Turn = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>The <see cref="EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn"/> arm.</summary>
        public static void Apply(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            StateOf(engine).Turn[who] = engine.TurnNumber;
            engine.Log($"{source?.Name ?? "Spirit Elimination"}: until the end of this turn, a monster {who.Name} " +
                       "controls is banished instead of each monster that would be banished from their GY.");
        }

        /// <summary><paramref name="player"/> is under Spirit Elimination this turn.</summary>
        public static bool Affects(DuelEngine engine, DuelistState player) =>
            engine != null && player != null && s_state.TryGetValue(engine, out var st) &&
            st.Turn.TryGetValue(player, out var turn) && turn == engine.TurnNumber;

        /// <summary>
        /// The monsters <paramref name="player"/> controls that can stand in for monsters in their GY, weakest first
        /// (<paramref name="except"/> excluded; face-up only when <paramref name="mustBeFaceUp"/>; property check on
        /// the monster as it is now).
        /// </summary>
        public static List<CardInstance> Substitutes(DuelEngine engine, DuelistState player, CardInstance except,
            bool mustBeFaceUp, Func<CardInstance, bool> matches = null)
        {
            var list = new List<CardInstance>();
            if (engine == null || player == null) return list;
            foreach (var m in player.MonstersOnField())
            {
                if (m == null || m == except) continue;
                if (mustBeFaceUp && !m.FaceUp) continue;
                if (matches != null && !matches(m)) continue;
                list.Add(m);
            }

            return list.OrderBy(m => m.FaceUp ? m.CurrentAtk : 0).ToList();
        }

        /// <summary>
        /// A candidate or target list built from GYs: for each affected <paramref name="gyOwners"/> player, their GY
        /// monsters leave the list and the monsters they control (<see cref="Substitutes"/>) join it. A target list
        /// (<paramref name="targeting"/>) leaves out a monster that cannot be targeted on the field (Lord of D.).
        /// </summary>
        public static void SubstituteInList(DuelEngine engine, List<CardInstance> list, CardInstance except,
            bool mustBeFaceUp, Func<CardInstance, bool> matches, bool targeting, params DuelistState[] gyOwners)
        {
            if (engine == null || list == null || gyOwners == null) return;
            foreach (var owner in gyOwners.Where(o => o != null).Distinct())
            {
                if (!Affects(engine, owner)) continue;
                list.RemoveAll(c => c?.Def != null && c.Def.IsMonster && owner.Graveyard.Contains(c));
                foreach (var s in Substitutes(engine, owner, except, mustBeFaceUp, matches))
                    if (!list.Contains(s) && !(targeting && engine.IsDragonTargetProtected(s)))
                        list.Add(s);
            }
        }

        /// <summary>
        /// The banishing cost or effect is a property of "this card" on the field (it gains ATK, or compares with its
        /// ATK): the card itself cannot be its own substitute (Lua Bazoo / Freed exclude e:GetHandler()).
        /// </summary>
        public static bool SourceMustStay(EffectClause c) =>
            c != null && (c.RequiresHigherAtkThanSource || SimultaneousTriggersRuntime.NeedsSourceFaceUp(c));

        /// <summary>A target zone that reads one or both GYs (monsters or any cards).</summary>
        public static bool IsGyZone(EffectZoneFilter zone) =>
            zone is EffectZoneFilter.EitherGyMonsters or EffectZoneFilter.EitherGyCards
                or EffectZoneFilter.OppGyMonsters or EffectZoneFilter.OppGyCards
                or EffectZoneFilter.ControllerGyMonsters or EffectZoneFilter.ControllerGyCards;

        /// <summary>
        /// A targeted banish in this GY zone is substituted (Lua <c>aux.SpElimFilter</c> in the target filter): the
        /// monster-only GY zones (Witch Doctor of Chaos, Gravedigger Ghoul, Kycoo, Book of Life's second target) and
        /// "cards in any GY" (Soul Release). Not "1 card in your opponent's GY" (Disappear, Lua c24623598 selects
        /// <c>Card.IsAbleToRemove</c> in LOCATION_GRAVE only), nor a "cards in your GY" zone no SpElimFilter card uses.
        /// </summary>
        public static bool SubstitutesTargetsIn(EffectZoneFilter zone) =>
            zone is EffectZoneFilter.EitherGyMonsters or EffectZoneFilter.OppGyMonsters
                or EffectZoneFilter.ControllerGyMonsters or EffectZoneFilter.EitherGyCards;

        /// <summary>
        /// The source card a targeted GY banish leaves out of its substitutes: only the source whose effect needs it on
        /// the field (<see cref="SourceMustStay"/>); otherwise none (Lua Witch Doctor of Chaos / Soul Release select with
        /// exclusion nil, so Witch Doctor can target itself).
        /// </summary>
        public static CardInstance TargetSubstituteExcept(EffectClause c, CardInstance source) =>
            SourceMustStay(c) ? source : null;

        /// <summary>The players whose GY a GY zone filter reads (<paramref name="who"/> = the clause's controller).</summary>
        public static DuelistState[] GyOwnersOfZone(EffectZoneFilter zone, DuelistState who, DuelistState opp) =>
            zone switch
            {
                EffectZoneFilter.OppGyMonsters or EffectZoneFilter.OppGyCards => new[] { opp },
                EffectZoneFilter.ControllerGyMonsters or EffectZoneFilter.ControllerGyCards => new[] { who },
                _ => new[] { who, opp }
            };

        /// <summary>
        /// The legacy Spell/Trap target mirror (SpellTrapEffects.CollectLegalTargets, MonsterInEitherGy / CardInEitherGy):
        /// the same substitution as TextEffectRuntime.CollectTargets when <paramref name="card"/>'s targeted clause banishes
        /// its GY target.
        /// </summary>
        public static void SubstituteLegacyGyTargets(DuelEngine engine, DuelistState who, CardInstance card,
            List<CardInstance> list)
        {
            if (engine == null || who == null || list == null) return;
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            var c = prog?.ClauseList.FirstOrDefault(x =>
                x != null && x.RequiresTargetChoice && x.Action == EffectActionKind.Banish && IsGyZone(x.Zone));
            if (c == null || !SubstitutesTargetsIn(c.Zone)) return;
            SubstituteInList(engine, list, TargetSubstituteExcept(c, card), false, null, true,
                GyOwnersOfZone(c.Zone, who, engine.OpponentOf(who)));
        }

        /// <summary>True when the card is in a GY now (a substitute on the field is not).</summary>
        public static bool InAGraveyard(DuelEngine engine, CardInstance card) =>
            card != null && engine != null &&
            ((engine.Player?.Graveyard?.Contains(card) ?? false) ||
             (engine.Opponent?.Graveyard?.Contains(card) ?? false));
    }

    /// <summary>
    /// ForcedActivationAndSubstitution batch (v163) — Major Riot (Lua c9074847). Normal Trap, EFFECT_TYPE_ACTIVATE on
    /// EVENT_TO_HAND: s.condition = <c>rp~=tp</c> and a card of the activator's (IsControler(tp) in the hand = sent to
    /// their hand) left their Monster Zone; s.target needs 1 monster on the field that can return to the hand.
    /// <list type="bullet">
    /// <item><b>The event.</b> <see cref="DuelEngine.ReturnCardToHand"/> takes the reason player (the player whose card
    /// effect returns it) and calls <see cref="NoteReturnedToHand"/> for a monster that left a Monster Zone for a hand.
    /// Once play is idle, <c>DuelEngine.OfferMonstersReturnedWindow</c> offers each duelist (turn player first) whose
    /// monster an opponent's effect returned the <see cref="ResponseTiming.MonstersReturnedToHandByOpponent"/> window;
    /// returns of an earlier turn or phase, or in the Damage Step, were missed.</item>
    /// <item><b>The resolution</b> (<see cref="EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown"/>):
    /// every monster on the field returns to its owner's hand (Extra Deck monsters to the Extra Deck, Tokens vanish);
    /// then each player, activator first, Special Summons from their own hand as many monsters as went to their hand
    /// (Lua s.spfilter: Level 4 or lower, can be Normal Summoned/Set, can be Special Summoned; fewer when their hand
    /// or zones run out) in face-down Defense Position, all at once
    /// (<see cref="FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromHandFaceDown"/>: a human chooses, the AI takes its
    /// best monsters).</item>
    /// </list>
    /// </summary>
    public static class MajorRiotRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: in the <see cref="ResponseTiming.MonstersReturnedToHandByOpponent"/>
        /// window, with a monster on the field that can return to the hand.
        /// </summary>
        public const string YourMonstersReturnedToHandByOpponent = "YourMonstersReturnedToHandByOpponent";

        sealed class Rec
        {
            public DuelistState HandOwner;
            public DuelistState By;
            public int Turn;
            public DuelPhase Phase;
            public bool InDamageStep;
        }

        sealed class State
        {
            public readonly List<Rec> Records = new();
            public readonly List<DuelistState> Offered = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>
        /// DuelEngine.ReturnCardToHand: a monster left a Monster Zone for <paramref name="handOwner"/>'s hand because of
        /// <paramref name="by"/>'s card effect (null = unknown or not an effect; nothing is recorded).
        /// </summary>
        public static void NoteReturnedToHand(DuelEngine engine, DuelistState handOwner, CardInstance card,
            DuelistState by)
        {
            if (engine == null || handOwner == null || card == null || by == null || by == handOwner) return;
            StateOf(engine).Records.Add(new Rec
            {
                HandOwner = handOwner,
                By = by,
                Turn = engine.TurnNumber,
                Phase = engine.Phase,
                InDamageStep = engine.BattleStep == BattleStep.DamageStep
            });
        }

        public static bool HasPending(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.Records.Count > 0;

        /// <summary>The window round is over (or the duel ended): every recorded return is gone.</summary>
        public static void ClearAll(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Records.Clear();
            st.Offered.Clear();
        }

        /// <summary>
        /// DuelEngine.OfferMonstersReturnedWindow: <paramref name="victim"/> may now get the window once for the returns
        /// recorded since the last round. Returns of an earlier turn or phase, or in the Damage Step (a Normal Trap
        /// cannot be activated there), were missed and are dropped.
        /// </summary>
        public static bool BeginWindow(DuelEngine engine, DuelistState victim)
        {
            if (engine == null || victim == null || !s_state.TryGetValue(engine, out var st)) return false;
            st.Records.RemoveAll(r => r.Turn != engine.TurnNumber || r.Phase != engine.Phase || r.InDamageStep);
            if (!st.Records.Exists(r => r.HandOwner == victim) || st.Offered.Contains(victim)) return false;
            st.Offered.Add(victim);
            return true;
        }

        /// <summary>Activation condition <see cref="YourMonstersReturnedToHandByOpponent"/>.</summary>
        public static bool WindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only when 1 or more of your monsters are returned from the field to your hand by your " +
                     "opponent's effect.";
            var pr = engine?.PendingResponse;
            if (pr == null || who == null || pr.Timing != ResponseTiming.MonstersReturnedToHandByOpponent ||
                pr.Responder != who || !s_state.TryGetValue(engine, out var st) ||
                !st.Records.Exists(r => r.HandOwner == who))
                return false;
            if (!MonstersOnField(engine).Any())
            {
                reason = "There is no monster on the field to return to the hand.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>The program is a Trap whose activation condition is this window (Major Riot).</summary>
        public static bool AnswersWindow(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.Activate &&
                c.ActivationCondition == YourMonstersReturnedToHandByOpponent);

        static IEnumerable<CardInstance> MonstersOnField(DuelEngine engine)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null)
                    foreach (var m in side.MonstersOnField())
                        if (m != null)
                            yield return m;
        }

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond): Major Riot when the opponent's field is the stronger one — more monsters,
        /// or as many with more total ATK.
        /// </summary>
        public static bool AiWantsToAnswer(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            if (!AnswersWindow(CompiledEffectCache.GetOrCompile(card.Def))) return false;
            if (!WindowOpen(engine, ai, out _)) return false;
            var opp = engine.OpponentOf(ai);
            if (opp == null) return false;
            var mine = ai.MonstersOnField().Where(m => m != null).ToList();
            var theirs = opp.MonstersOnField().Where(m => m != null).ToList();
            if (theirs.Count == 0) return false;
            int Atk(List<CardInstance> l) => l.Sum(m => m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0);
            return theirs.Count > mine.Count || (theirs.Count == mine.Count && Atk(theirs) > Atk(mine));
        }

        /// <summary>
        /// The <see cref="EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown"/> arm (Lua
        /// s.operation): return every monster on the field to the hand, then each player Special Summons from their
        /// hand as many monsters as went to their hand, in face-down Defense Position.
        /// </summary>
        public static void Resolve(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null) return;
            var monsters = MonstersOnField(engine).ToList();
            if (monsters.Count == 0)
            {
                engine.Log($"{source?.Name}: there is no monster on the field — no effect.");
                return;
            }

            var counts = new Dictionary<DuelistState, int>();
            TextEffectRuntime.BeginMonsterReturnToOwnerHandBatch();
            try
            {
                foreach (var m in monsters)
                {
                    if (engine.ControllerOf(m) == null) continue;
                    if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                    {
                        engine.Log($"{m.Name} is unaffected by {source?.Name}.");
                        continue;
                    }

                    if (!engine.ReturnCardToHand(m, by: who)) continue;
                    // Lua og:FilterCount(IsLocation(LOCATION_HAND) and IsControler(p)): only cards now in a hand count.
                    foreach (var side in new[] { engine.Player, engine.Opponent })
                        if (side?.Hand != null && side.Hand.Contains(m))
                            counts[side] = (counts.TryGetValue(side, out var n) ? n : 0) + 1;
                }
            }
            finally
            {
                TextEffectRuntime.EndMonsterReturnToOwnerHandBatch(engine);
            }

            if (engine.GameOver) return;
            FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromHandFaceDown(engine, who, source, clause, counts);
        }
    }
}
