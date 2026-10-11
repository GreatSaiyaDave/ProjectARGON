using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FlipMassEffects" (v142) runtime (Tools/CardCompile/README.md):
    /// <list type="bullet">
    /// <item>Flip record (<see cref="EffectActionKind.RecordThisCardFlipped"/>): the FLIP marks this copy as flipped
    /// for its current field presence (face-down stamp + field-entry stamp). It ends when the card is turned
    /// face-down or leaves the field other than to the GY (ygopro RESETS_STANDARD without RESET_LEAVE/TOGRAVE).</item>
    /// <item>Jigen Bakudan (<see cref="EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk"/>): the record
    /// counts its controller's Standby Phases (<see cref="TickStandby"/>); at the first one the mandatory trigger
    /// Tributes it (cost), destroys every monster that player controls and burns the opponent for half the total
    /// ATK of the monsters destroyed. The registered effect is used up at that Standby Phase either way.</item>
    /// <item>Spear Cretin (<see cref="EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy"/>): sent to the GY
    /// with a live record, each player targets 1 monster in their own GY and Special Summons it in face-up Attack
    /// or face-down Defense Position (their choice).</item>
    /// <item>Cyber Jar (<see cref="EffectActionKind.CyberJarStyle"/>): destroy every monster on the field (their
    /// "sent to the GY" triggers wait until Cyber Jar has resolved), then the turn player and the other reveal
    /// their top N cards, Special Summon the revealed Level-N-or-lower monsters in face-up Attack or face-down
    /// Defense Position (their choice; with too few Monster Zones they choose which, the rest go to the GY) and
    /// add every other revealed card to their hand.</item>
    /// <item>Morphing Jar #2 (<see cref="EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon"/>): shuffle
    /// every monster on the field into the Deck, each player excavates until as many monsters as they shuffled into
    /// their Main Deck, Special Summons the excavated Level-N-or-lower monsters face-down Defense (too few zones:
    /// they choose which) and sends the other excavated cards to the GY.</item>
    /// </list>
    /// A choice the human player makes goes through one pick (<see cref="PendingActivation"/> with
    /// <see cref="EffectTargetKind.MonsterInYourDeckToSummon"/> for revealed / excavated cards,
    /// <see cref="EffectTargetKind.MonsterInEitherGy"/> for GY monsters): each tap records one Special Summon (the
    /// UI's "Summon ATK" = face-up Attack, "Summon DEF" = face-down Defense, and its zone); CANCEL makes the remaining
    /// choices automatically, since the Summons are mandatory. The AI decides at once. While one of these choices is
    /// open, later held triggers and other mass Summons of this batch wait for it (<see cref="RunInOrder"/>); a
    /// player's choice that comes up while an unrelated prompt is open is made automatically, and an open choice that
    /// something else replaces or clears is made automatically first (<see cref="SettleDisplacedPick"/>), so no
    /// revealed or excavated card is ever lost. Every Special Summon of one resolution happens together, after the
    /// choices.
    /// <para>Simultaneous sends (<see cref="Simultaneously"/>): mass destructions, both battling monsters destroyed and
    /// a Summon's Tributes hold the "sent to the GY" triggers of the cards they send until the whole event has
    /// happened (Spear Cretin sees every monster destroyed with it in the GY).</para>
    /// </summary>
    public static class FlipMassEffectsRuntime
    {
        // ───────────────────── Flip record ─────────────────────

        sealed class FlipRecord
        {
            public int FaceDownStamp;
            public int FieldEntryStamp;
            /// <summary>Standby Phases of this card's controller that began while the record was live.</summary>
            public int ControllerStandbyPhases;
        }

        static readonly ConditionalWeakTable<CardInstance, FlipRecord> s_flipped =
            new ConditionalWeakTable<CardInstance, FlipRecord>();

        /// <summary>
        /// FLIP marker (<see cref="EffectActionKind.RecordThisCardFlipped"/>): only while this card is still
        /// face-up in a Monster Zone (Jigen Bakudan Lua: IsRelateToEffect and IsFaceup).
        /// </summary>
        public static void RecordFlipped(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !card.FaceUp) return;
            var controller = engine.ControllerOf(card);
            if (controller == null || !controller.TryFindMonster(card, out _)) return;
            s_flipped.Remove(card);
            s_flipped.Add(card, new FlipRecord
            {
                FaceDownStamp = card.FaceDownStamp,
                FieldEntryStamp = card.FieldEntryStamp
            });
        }

        /// <summary>The record of <paramref name="card"/>, if it is still the same field presence and face-up since.</summary>
        static bool TryLiveRecord(CardInstance card, out FlipRecord record)
        {
            record = null;
            if (card == null || !s_flipped.TryGetValue(card, out var r)) return false;
            if (r.FaceDownStamp != card.FaceDownStamp || r.FieldEntryStamp != card.FieldEntryStamp)
            {
                s_flipped.Remove(card);
                return false;
            }

            record = r;
            return true;
        }

        /// <summary>This copy was flipped face-up during its current (or just-ended, if now in the GY) field presence.</summary>
        public static bool WasFlipped(CardInstance card) => TryLiveRecord(card, out _);

        /// <summary>
        /// Start of <paramref name="turnPlayer"/>'s Standby Phase (not called when the phase is skipped): +1 on the
        /// Standby count of each live record on a monster they control.
        /// </summary>
        public static void TickStandby(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            foreach (var m in turnPlayer.MonstersOnField())
                if (m != null && TryLiveRecord(m, out var record))
                    record.ControllerStandbyPhases++;
        }

        // ───────────────────── Jigen Bakudan ─────────────────────

        /// <summary>
        /// Jigen Bakudan's registered Standby trigger (Lua e1 of s.operation): mandatory, at the first Standby Phase
        /// of its controller after the flip. Cost: Tribute it (Mask of Restrict: cannot be activated). Destroy all
        /// monsters its controller controls; the opponent takes half the total ATK (rounded down) of the monsters
        /// the destruction removed.
        /// </summary>
        public static void ResolveStandbyTributeDestroyBurn(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null) return;
            // Never flipped during this field presence: the effect was never registered.
            if (!TryLiveRecord(source, out var record)) return;
            if (record.ControllerStandbyPhases != 1)
            {
                // Past the first Standby Phase (the effect expired there while it could not activate).
                if (record.ControllerStandbyPhases > 1) s_flipped.Remove(source);
                return;
            }

            // Used up at this Standby Phase whether or not its cost can be paid (RESET_PHASE+PHASE_STANDBY).
            s_flipped.Remove(source);
            if (!source.FaceUp || !who.TryFindMonster(source, out _)) return;
            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                engine.Log($"{source.Name}: it cannot be Tributed — its Standby Phase effect is not activated.");
                return;
            }

            engine.Log($"{source.Name}: Standby Phase — Tribute it to destroy all monsters {who.Name} controls.");
            ReleaseEvents.Record(engine, source, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(who, source, sentBy: source);
            engine.Log($"Cost: Tribute {source.Name}.");

            // One destruction: the destroyed monsters' "sent to the GY" triggers activate after the damage.
            Simultaneously(engine, () =>
            {
                var total = 0;
                var destroyed = 0;
                foreach (var m in who.MonstersOnField().Where(x => x != null).ToList())
                {
                    if (engine.ControllerOf(m) == null) continue;
                    var atk = m.CurrentAtk;
                    TextEffectRuntime.DestroyCard(engine, m, source);
                    if (engine.ControllerOf(m) != null) continue; // not destroyed (unaffected)
                    total += atk;
                    destroyed++;
                }

                var damage = total / 2;
                engine.Log($"{source.Name}: {destroyed} monster(s) destroyed, total ATK {total} — {damage} damage.");
                var opp = engine.OpponentOf(who);
                if (damage > 0 && opp != null)
                    engine.ApplyEffectDamage(opp, damage, source.Name);
            });
            engine.NotifyPublic();
        }

        // ───────────────────── Simultaneous sends: held field → GY triggers ─────────────────────

        sealed class HeldTrigger
        {
            public DuelistState Owner;
            public CardInstance Card;
            public bool Destroyed;
            public bool DestroyedByBattle;
            public CardInstance BattleDestroyer;
        }

        sealed class HoldState
        {
            public int Depth;
            public readonly List<HeldTrigger> Items = new List<HeldTrigger>();
        }

        static readonly ConditionalWeakTable<DuelEngine, HoldState> s_hold =
            new ConditionalWeakTable<DuelEngine, HoldState>();

        /// <summary>
        /// Called first by <see cref="MonsterEffects.OnSentFromFieldToGy"/>: while a simultaneous event is happening
        /// (<see cref="Simultaneously"/>: a mass destruction, both battling monsters destroyed, a Summon's Tributes
        /// or materials), the "sent to the GY" triggers of the cards it sends wait and activate after it. Returns
        /// false when nothing is being held.
        /// </summary>
        public static bool TryHoldSentToGy(DuelEngine engine, DuelistState owner, CardInstance card, bool destroyed,
            bool destroyedByBattle, CardInstance battleDestroyer)
        {
            if (engine == null || !s_hold.TryGetValue(engine, out var hold) || hold.Depth <= 0) return false;
            hold.Items.Add(new HeldTrigger
            {
                Owner = owner,
                Card = card,
                Destroyed = destroyed,
                DestroyedByBattle = destroyedByBattle,
                BattleDestroyer = battleDestroyer
            });
            return true;
        }

        /// <summary>
        /// Runs <paramref name="body"/> as one event that sends cards from the field to the GY at the same time (Dark
        /// Hole, Raigeki, Mirror Force, both battling monsters destroyed, a Ritual Summon's Tributes, …). Every "sent
        /// to the GY" trigger it causes waits until the whole event has happened, then they activate in order — so a
        /// flipped Spear Cretin destroyed together with other monsters sees all of them in the GY. A nested event
        /// leaves its triggers to the outermost one. The body runs unchanged when <paramref name="engine"/> is null.
        /// </summary>
        public static void Simultaneously(DuelEngine engine, Action body)
        {
            if (body == null) return;
            if (engine == null)
            {
                body();
                return;
            }

            List<HeldTrigger> held;
            BeginHold(engine);
            // GuardiansAndEquipEvents batch: Equip Spells that leave the field in this event leave together (Morale Boost).
            var equipLeave = GuardiansAndEquipEventsRuntime.EnterEquipLeaveEvent(engine);
            try
            {
                body();
            }
            finally
            {
                held = EndHold(engine);
                equipLeave.Dispose();
            }

            ReplayHeld(engine, held);
        }

        static void BeginHold(DuelEngine engine) => s_hold.GetOrCreateValue(engine).Depth++;

        static List<HeldTrigger> EndHold(DuelEngine engine)
        {
            var hold = s_hold.GetOrCreateValue(engine);
            if (hold.Depth > 0) hold.Depth--;
            if (hold.Depth > 0) return new List<HeldTrigger>(); // an outer hold replays them
            var items = new List<HeldTrigger>(hold.Items);
            hold.Items.Clear();
            return items;
        }

        /// <summary>
        /// The held triggers go on one Chain (SimultaneousTriggers batch, SEGOC: the turn player's first, so the
        /// opponent's resolve first; each player orders their own). An open choice of this batch still goes first:
        /// the Chain waits for it.
        /// </summary>
        static void ReplayHeld(DuelEngine engine, List<HeldTrigger> held)
        {
            if (held == null || held.Count == 0) return;
            SimultaneousTriggersRuntime.RunHeld(engine, held.Select(h => (h.Owner, h.Card, (Action)(() =>
                MonsterEffects.OnSentFromFieldToGy(engine, h.Owner, h.Card, h.Destroyed, h.DestroyedByBattle,
                    h.BattleDestroyer)))).ToList());
        }

        /// <summary>
        /// Runs <paramref name="steps"/> (held triggers, waiting mass Summons) in order. While a choice of this batch
        /// is open for the player, the remaining steps wait for it: they are handed to its plan and run when it has
        /// finished, so a later trigger's own prompt cannot replace the open choice (the engine keeps one
        /// <see cref="DuelEngine.PendingActivation"/> at a time).
        /// </summary>
        static void RunInOrder(DuelEngine engine, List<Action> steps)
        {
            if (steps == null) return;
            for (var i = 0; i < steps.Count; i++)
            {
                if (engine.GameOver) return;
                if (TryOpenPlan(engine, out var open))
                {
                    open.After.AddRange(steps.GetRange(i, steps.Count - i));
                    return;
                }

                steps[i]();
            }
        }

        /// <summary>The plan whose choice is open for the player right now, if any.</summary>
        static bool TryOpenPlan(DuelEngine engine, out Plan plan)
        {
            plan = null;
            var open = engine?.PendingActivation;
            return open != null && s_picks.TryGetValue(open, out plan);
        }

        // ───────────────────── Mass Special Summon plans ─────────────────────

        enum PlanKind
        {
            /// <summary>Cyber Jar: revealed cards; position choice; the other revealed cards to the hand.</summary>
            RevealToHand,
            /// <summary>Morphing Jar #2: excavated cards; face-down Defense; the other excavated cards to the GY.</summary>
            ExcavateToGrave,
            /// <summary>Spear Cretin: 1 target from each player's own GY; position choice.</summary>
            OwnGyTarget,
            /// <summary>
            /// ForcedActivationAndSubstitution batch (v163) — Major Riot: monsters in the player's own hand, face-down
            /// Defense only, at most <see cref="Seat.Limit"/>; the cards not chosen stay in the hand.
            /// </summary>
            OwnHandFaceDown,
        }

        sealed class Pick
        {
            public CardInstance Card;
            public BattlePosition Position;
            public bool FaceUp;
            public int Zone = -1;
        }

        sealed class Seat
        {
            public DuelistState Who;
            /// <summary>Monsters that may still be chosen for a Special Summon.</summary>
            public readonly List<CardInstance> Candidates = new List<CardInstance>();
            public readonly List<Pick> Summons = new List<Pick>();
            public readonly List<CardInstance> ToHand = new List<CardInstance>();
            public readonly List<CardInstance> ToGrave = new List<CardInstance>();
            /// <summary>How many more Special Summons this player can make (free zones; 1 for a GY target).</summary>
            public int PicksLeft;
            /// <summary>ForcedActivationAndSubstitution batch (v163): at most this many Summons (-1 = the free zones only).</summary>
            public int Limit = -1;
        }

        sealed class Plan
        {
            public PlanKind Kind;
            public CardInstance Source;
            /// <summary>Face-up Attack or face-down Defense, the summoning player's choice (else face-down Defense).</summary>
            public bool PositionChoice;
            /// <summary>
            /// NegationsAndGySummons batch (Gilasaurus): "can Special Summon" — the choosing player may decline (CANCEL
            /// summons nothing); the AI always takes its best monster.
            /// </summary>
            public bool Optional;
            /// <summary>
            /// NegationsAndGySummons batch (Gilasaurus, Lua POS_FACEUP): the Summon is face-up, in Attack or Defense
            /// Position ("Summon DEF" = face-up Defense Position), never face-down.
            /// </summary>
            public bool FaceUpOnly;
            public readonly List<Seat> Seats = new List<Seat>();
            /// <summary>The human player's seat while their pick is open.</summary>
            public Seat Chooser;
            /// <summary>
            /// What waits for this plan to finish, in order: the held "sent to the GY" triggers of the same event and
            /// any other mass Summon of this batch that came up while the player's choice was open.
            /// </summary>
            public readonly List<Action> After = new List<Action>();
            public EffectClause Clause;

            public Seat AddSeat(DuelistState who)
            {
                var seat = new Seat { Who = who };
                Seats.Add(seat);
                return seat;
            }
        }

        static readonly ConditionalWeakTable<PendingActivation, Plan> s_picks =
            new ConditionalWeakTable<PendingActivation, Plan>();

        static IEnumerable<DuelistState> FirstThenOther(DuelEngine engine, DuelistState first)
        {
            if (first != null) yield return first;
            var other = engine.OpponentOf(first);
            if (other != null) yield return other;
        }

        static List<CardInstance> FieldMonsters(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            foreach (var who in new[] { engine.Player, engine.Opponent })
                if (who != null)
                    list.AddRange(who.MonstersOnField().Where(m => m != null));
            return list;
        }

        static int FreeZones(DuelistState who) =>
            who?.MonsterZones == null ? 0 : who.MonsterZones.Count(z => z != null && z.IsEmpty);

        static List<CardInstance> TakeTop(DuelEngine engine, DuelistState who, int n)
        {
            var list = new List<CardInstance>();
            for (var i = 0; i < n && who.Deck.Count > 0; i++)
            {
                var id = who.Deck[0];
                who.Deck.RemoveAt(0);
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        static string Names(IEnumerable<CardInstance> cards) => string.Join(", ", cards.Select(c => c.Name));

        /// <summary>A Main Deck monster with a Level from 1 to <paramref name="levelMax"/> (0 = any Level).</summary>
        static bool IsLevelMonster(CardInstance c, int levelMax)
        {
            if (c?.Def == null || !c.Def.IsMonster || c.Def.IsExtraDeck) return false;
            var level = c.PrintedLevel;
            return level >= 1 && (levelMax <= 0 || level <= levelMax);
        }

        /// <summary>
        /// The engine's Special Summon gates for a summon by a resolving effect (DuelEngine.SpecialSummonToField):
        /// hard Nomi, a monster summoned only by its own procedure, a "while X is face-up" gate, and a named-card
        /// Semi-Nomi. A monster that fails them "cannot be Special Summoned" here (Lua IsCanBeSpecialSummoned).
        /// </summary>
        internal static bool CanBeSpecialSummonedByThis(DuelistState who, CardInstance card)
        {
            if (card?.Def == null) return false;
            var text = card.OfficialText;
            if (PsctGrammar.BlocksSpecialSummon(text)) return false;
            if (OfficialEffectRegistry.HasInherentHandTribute(card.CardId)) return false;
            var gate = PsctGrammar.SummonGateNamed(text);
            if (!string.IsNullOrEmpty(gate) && !FieldSpellEffects.ControlsFaceUpNamed(who, gate)) return false;
            return TributeSummonsRuntime.SemiNomiSummonAllowed(card, out _);
        }

        // Nomi / Semi-Nomi / Ritual rules for hand, Deck and GY copies: SpecialSummonLegality (EngineHygiene, v160).

        /// <summary>
        /// A revealed / excavated card: a copy in the Deck was never properly summoned, so a Ritual Monster, a Nomi
        /// and a Semi-Nomi cannot be Special Summoned by this effect (ygopro revive limit: IsCanBeSpecialSummoned
        /// without ignoring the Summoning conditions). "Cannot be Normal Summoned or Set" alone (Rare Metal Dragon)
        /// and conditional Normal Summon locks (Mystic Swordsman LV4, Cave Dragon) do not stop it.
        /// </summary>
        /// <remarks>
        /// EngineHygiene batch (v160): the one engine-aware check (<see cref="SpecialSummonLegality.CanBeSpecialSummoned"/>:
        /// also Jowgen / Jam Breeding Machine and summon costs). Chain Energy's hand toll is asked by the callers, which
        /// know whether it was already paid.
        /// </remarks>
        internal static bool HandOrDeckCopyCanBeSpecialSummoned(DuelistState who, CardInstance c, bool faceUp = true) =>
            who != null && SpecialSummonLegality.CanBeSpecialSummoned(who.Engine, who, c, null,
                who.Hand != null && who.Hand.Contains(c) ? SummonOrigin.Hand : SummonOrigin.Deck, checkHandToll: false,
                faceUp: faceUp);

        static bool DeckCopyCanBeSpecialSummoned(DuelistState who, CardInstance c) =>
            who != null && SpecialSummonLegality.CanBeSpecialSummoned(who.Engine, who, c, null, SummonOrigin.Deck);

        /// <summary>
        /// A GY monster this effect can target and Special Summon (the effect's own card excluded). Lua
        /// IsCanBeSpecialSummoned(e,0,tp,false,false): a Ritual or Extra Deck monster only once it was properly
        /// Summoned (<see cref="CardInstance.ProcedureCompleted"/>, set by a Ritual / Fusion Summon — a Ritual
        /// Monster discarded or destroyed from the hand never is).
        /// </summary>
        static bool IsGyTarget(DuelEngine engine, DuelistState who, CardInstance source, CardInstance g) =>
            g?.Def != null && g != source && g.Def.IsMonster &&
            // EngineHygiene batch (v160): the one engine-aware check — also a "Must first be" procedure monster never
            // properly summoned, Jowgen / Jam Breeding Machine and summon costs (v159 audit: Spear Cretin, Gilasaurus).
            SpecialSummonLegality.CanBeSpecialSummoned(engine, who, g, source, SummonOrigin.Graveyard);

        /// <summary>AI: face-down Defense for a Flip monster or one with more DEF than ATK, else face-up Attack.</summary>
        static Pick AiPick(Plan plan, CardInstance c)
        {
            if (plan.FaceUpOnly)
            {
                var d = c.Def;
                return new Pick
                {
                    Card = c,
                    Position = d != null && d.def > d.atk ? BattlePosition.Defense : BattlePosition.Attack,
                    FaceUp = true
                };
            }

            if (!plan.PositionChoice)
                return new Pick { Card = c, Position = BattlePosition.Defense, FaceUp = false };
            var def = c.Def;
            var flip = def?.type != null && def.type.IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0;
            var hide = def == null || flip || def.def > def.atk;
            return hide
                ? new Pick { Card = c, Position = BattlePosition.Defense, FaceUp = false }
                : new Pick { Card = c, Position = BattlePosition.Attack, FaceUp = true };
        }

        static int Worth(CardInstance c) => c?.Def == null ? 0 : Math.Max(c.Def.atk, c.Def.def);

        /// <summary>The automatic choice for a seat: the best monsters (AI), in order (the human's CANCEL).</summary>
        static void AutoDecide(Plan plan, Seat seat, bool inOrder)
        {
            var pool = inOrder
                ? seat.Candidates.ToList()
                : seat.Candidates.OrderByDescending(Worth).ThenBy(c => c.Name).ToList();
            foreach (var c in pool)
            {
                if (seat.PicksLeft <= 0) break;
                seat.Summons.Add(inOrder && plan.PositionChoice
                    ? new Pick { Card = c, Position = BattlePosition.Attack, FaceUp = true }
                    : AiPick(plan, c));
                seat.Candidates.Remove(c);
                seat.PicksLeft--;
            }

            CloseSeat(plan, seat);
        }

        /// <summary>No more picks: the undecided revealed / excavated monsters go to the GY; GY targets stay.</summary>
        static void CloseSeat(Plan plan, Seat seat)
        {
            if (plan.Kind != PlanKind.OwnGyTarget && plan.Kind != PlanKind.OwnHandFaceDown)
                seat.ToGrave.AddRange(seat.Candidates);
            seat.Candidates.Clear();
            seat.PicksLeft = 0;
        }

        static bool NeedsHumanChoice(Plan plan, Seat seat) =>
            seat.Candidates.Count > 0 && seat.PicksLeft > 0 &&
            (plan.PositionChoice || seat.Candidates.Count > seat.PicksLeft);

        /// <summary>
        /// The choices as they stand when the Special Summons are decided: each player's free Monster Zones, and for a
        /// GY target (Spear Cretin) the monsters in their own GY right now.
        /// </summary>
        static void Prepare(DuelEngine engine, Plan plan)
        {
            foreach (var seat in plan.Seats)
            {
                if (plan.Kind == PlanKind.OwnHandFaceDown)
                {
                    // Major Riot: the hand as it is now; as many as went to the hand, as zones allow.
                    seat.Candidates.RemoveAll(c => !seat.Who.Hand.Contains(c));
                    seat.PicksLeft = seat.Candidates.Count > 0
                        ? Math.Min(FreeZones(seat.Who), seat.Limit >= 0 ? seat.Limit : int.MaxValue)
                        : 0;
                    continue;
                }

                if (plan.Kind != PlanKind.OwnGyTarget)
                {
                    if (seat.Candidates.Count > 0) seat.PicksLeft = FreeZones(seat.Who);
                    continue;
                }

                seat.Candidates.Clear();
                seat.Candidates.AddRange(seat.Who.Graveyard.Where(g => IsGyTarget(engine, seat.Who, plan.Source, g)));
                if (seat.Candidates.Count == 0)
                {
                    engine.Log($"{seat.Who.Name} has no monster in their GY to select.");
                    continue;
                }

                seat.PicksLeft = FreeZones(seat.Who) > 0 ? Math.Max(1, plan.Clause?.Amount ?? 1) : 0;
                if (seat.PicksLeft == 0)
                    engine.Log($"{seat.Who.Name} has no free Monster Zone — nothing is Special Summoned for them.");
            }
        }

        static void DecideAndRun(DuelEngine engine, Plan plan)
        {
            // Another of these effects has the player's choice open: this one waits for it, so its Summons cannot
            // take the zones or the GY monsters that choice still offers.
            if (TryOpenPlan(engine, out var open) && open != plan)
            {
                engine.Log($"{plan.Source?.Name}: waits until the choice for {open.Source?.Name} has been made.");
                open.After.Add(() => DecideAndRun(engine, plan));
                return;
            }

            Prepare(engine, plan);
            foreach (var seat in plan.Seats)
            {
                if (seat.Candidates.Count == 0) continue;
                if (seat.PicksLeft <= 0)
                {
                    if (plan.Kind == PlanKind.OwnHandFaceDown)
                        engine.Log($"{seat.Who.Name} has no free Monster Zone — nothing is Special Summoned for them.");
                    else if (plan.Kind != PlanKind.OwnGyTarget)
                        engine.Log($"{seat.Who.Name} has no free Monster Zone — {Names(seat.Candidates)} to the GY.");
                    CloseSeat(plan, seat);
                    continue;
                }

                if (seat.Who.IsPlayer && plan.Chooser == null && NeedsHumanChoice(plan, seat))
                {
                    if (engine.PendingActivation == null)
                    {
                        plan.Chooser = seat;
                        continue;
                    }

                    engine.Log($"{plan.Source?.Name}: another choice is pending — {seat.Who.Name}'s choice is made automatically.");
                }

                AutoDecide(plan, seat, inOrder: false);
            }

            if (plan.Chooser != null)
                OpenPick(engine, plan);
            else
                Finish(engine, plan);
        }

        static string PromptFor(Plan plan, Seat seat)
        {
            switch (plan.Kind)
            {
                case PlanKind.OwnGyTarget when plan.FaceUpOnly && plan.Optional:
                    return $"{plan.Source?.Name}: you can select 1 monster in your GY to Special Summon, then Summon " +
                           "ATK (face-up Attack Position) or Summon DEF (face-up Defense Position). CANCEL summons " +
                           "nothing.";
                case PlanKind.OwnGyTarget:
                    return "select 1 monster in your GY to Special Summon, then Summon ATK (face-up Attack " +
                           "Position) or Summon DEF (face-down Defense Position). CANCEL selects your highest-ATK " +
                           "monster in face-up Attack Position.";
                case PlanKind.OwnHandFaceDown:
                    return $"choose {seat.PicksLeft} monster(s) in your hand to Special Summon in face-down Defense " +
                           "Position. CANCEL takes your strongest ones.";
                case PlanKind.RevealToHand:
                {
                    var room = seat.PicksLeft < seat.Candidates.Count
                        ? $" You have room for {seat.PicksLeft}; the others are sent to the GY."
                        : "";
                    return $"Special Summon your revealed monsters ({seat.Candidates.Count} left): tap one, then " +
                           $"Summon ATK (face-up Attack Position) or Summon DEF (face-down Defense Position).{room} " +
                           "CANCEL summons the rest in face-up Attack Position.";
                }
                default:
                    return $"choose {seat.PicksLeft} of the excavated monsters to Special Summon in face-down " +
                           "Defense Position; the others are sent to the GY. CANCEL takes them in excavation order.";
            }
        }

        static void OpenPick(DuelEngine engine, Plan plan)
        {
            var seat = plan.Chooser;
            var pending = new PendingActivation
            {
                Controller = seat.Who,
                Card = plan.Source,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = plan.Kind == PlanKind.OwnGyTarget
                    ? EffectTargetKind.MonsterInEitherGy
                    : plan.Kind == PlanKind.OwnHandFaceDown
                        ? EffectTargetKind.MonsterInHandOrDeck
                        : EffectTargetKind.MonsterInYourDeckToSummon,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = Math.Min(seat.PicksLeft, seat.Candidates.Count),
                PromptOverride = PromptFor(plan, seat)
            };
            pending.LegalTargets.AddRange(seat.Candidates);
            s_picks.Add(pending, plan);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
        }

        /// <summary>
        /// <see cref="SpellTrapEffects.PendingTargetIsSpecialSummon"/> for this batch's picks: true (the UI asks
        /// Attack/Defense and a zone) for a position choice, false for a face-down-only pick. Returns false for any
        /// other pending activation.
        /// </summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = false;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            asksPlacement = plan.PositionChoice;
            return true;
        }

        /// <summary>This batch owns the open pick (tests and the AI read it; the pick itself is a normal target tap).</summary>
        public static bool IsMassSummonPick(PendingActivation pending) =>
            pending != null && s_picks.TryGetValue(pending, out _);

        /// <summary>
        /// A tap on one of the offered monsters: records its Special Summon — "Summon DEF" (the UI's pending
        /// Defense) is face-down Defense, anything else face-up Attack, or face-down Defense for a face-down-only
        /// effect — and its zone. When the player has no pick left, everything is summoned together. Returns false
        /// for any other pending activation.
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            var seat = plan.Chooser;
            var pick = engine.ResolveLegalEffectTarget(target);
            var chosenPosition = engine.PendingSpecialSummonPosition;
            var chosenZone = engine.PendingSpecialSummonZone;
            engine.ClearPendingSpecialSummonPlacement();
            if (seat == null || pick == null || !seat.Candidates.Contains(pick))
            {
                engine.Log("Not one of the offered monsters.");
                return true;
            }

            seat.Candidates.Remove(pick);
            pending.LegalTargets.Remove(pick);
            if ((plan.Kind == PlanKind.OwnGyTarget &&
                 (!seat.Who.Graveyard.Contains(pick) || !IsGyTarget(engine, seat.Who, plan.Source, pick))) ||
                (plan.Kind == PlanKind.OwnHandFaceDown && !seat.Who.Hand.Contains(pick)))
            {
                // It left the GY / hand (or became unsummonable) while the choice was open: offer the others.
                engine.Log($"{plan.Source?.Name}: {pick.Name} can no longer be selected.");
                if (seat.Candidates.Count > 0)
                {
                    pending.TargetPicksRemaining = Math.Min(seat.PicksLeft, seat.Candidates.Count);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                ok = true; // nothing left to select: the choice is over
                s_picks.Remove(pending);
                engine.ClearPendingActivation();
                CloseSeat(plan, seat);
                Finish(engine, plan);
                return true;
            }

            var faceDownDefense = !plan.FaceUpOnly &&
                                  (!plan.PositionChoice || chosenPosition == BattlePosition.Defense);
            var defense = faceDownDefense || (plan.FaceUpOnly && chosenPosition == BattlePosition.Defense);
            var picked = new Pick
            {
                Card = pick,
                Position = defense ? BattlePosition.Defense : BattlePosition.Attack,
                FaceUp = !faceDownDefense,
                Zone = chosenZone
            };
            seat.Summons.Add(picked);
            seat.PicksLeft--;
            engine.Log($"{plan.Source?.Name}: {pick.Name} will be Special Summoned in {PositionText(picked)}");
            ok = true;
            if (seat.PicksLeft > 0 && seat.Candidates.Count > 0)
            {
                pending.TargetPicksRemaining = Math.Min(seat.PicksLeft, seat.Candidates.Count);
                pending.PromptOverride = PromptFor(plan, seat);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            CloseSeat(plan, seat);
            Finish(engine, plan);
            return true;
        }

        /// <summary>
        /// CANCEL on this batch's pick: the Summons are mandatory, so the rest is decided automatically — the other
        /// revealed monsters in face-up Attack Position (Cyber Jar), the excavated monsters in excavation order
        /// (Morphing Jar #2), the highest-ATK GY monster in face-up Attack Position (Spear Cretin). Returns false
        /// for any other pending activation.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var plan)) return false;
            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            var seat = plan.Chooser;
            if (seat != null)
            {
                engine.Log($"{plan.Source?.Name}: the remaining choice is made automatically.");
                if (plan.Kind == PlanKind.OwnGyTarget && plan.Optional)
                {
                    // "can Special Summon": CANCEL declines.
                    engine.Log($"{plan.Source?.Name}: {seat.Who.Name} does not Special Summon a monster.");
                    seat.Summons.Clear();
                    CloseSeat(plan, seat);
                }
                else if (plan.Kind == PlanKind.OwnGyTarget)
                {
                    var best = seat.Candidates.OrderByDescending(c => c.Def?.atk ?? 0).ThenBy(c => c.Name)
                        .FirstOrDefault();
                    if (best != null && seat.PicksLeft > 0)
                    {
                        seat.Summons.Add(new Pick { Card = best, Position = BattlePosition.Attack, FaceUp = true });
                        seat.PicksLeft--;
                    }

                    CloseSeat(plan, seat);
                }
                else
                    // Major Riot: the strongest monsters (the AI's choice); the others: in reveal / excavation order.
                    AutoDecide(plan, seat, inOrder: plan.Kind != PlanKind.OwnHandFaceDown);
            }

            Finish(engine, plan);
            return true;
        }

        /// <summary>
        /// <see cref="DuelEngine.SetPendingActivation"/> / <see cref="DuelEngine.ClearPendingActivation"/>: something
        /// other than this batch is replacing or clearing its open choice (the engine keeps one pending activation).
        /// The revealed / excavated cards exist only in the plan, so the remaining choice is made automatically
        /// (as CANCEL does) and the plan finishes first — no card is lost and no Summon is skipped.
        /// </summary>
        public static void SettleDisplacedPick(DuelEngine engine)
        {
            for (var guard = 0; guard < 16 && TryOpenPlan(engine, out var plan); guard++)
            {
                engine.Log($"{plan.Source?.Name}: another action replaced the open choice.");
                TryCancelPick(engine);
            }
        }

        /// <summary>
        /// Every Special Summon of the plan together, then the cards to the hand / GY, then what waited for it (the
        /// held triggers, other mass Summons).
        /// </summary>
        static void Finish(DuelEngine engine, Plan plan)
        {
            foreach (var seat in plan.Seats)
            foreach (var s in seat.Summons)
            {
                if (engine.GameOver) return;
                Summon(engine, plan, seat, s);
            }

            foreach (var seat in plan.Seats)
            {
                if (seat.ToHand.Count > 0)
                {
                    seat.Who.Hand.AddRange(seat.ToHand);
                    engine.Log($"{seat.Who.Name} adds {Names(seat.ToHand)} to their hand.");
                }

                if (seat.ToGrave.Count > 0)
                {
                    seat.Who.Graveyard.AddRange(seat.ToGrave);
                    // SimpleTriggersAndSwarm batch: every move into a GY stamps the card (GyEntryStamps), so its "sent to
                    // the GY" (from anywhere) trigger sees the excavated / revealed cards sent here.
                    foreach (var g in seat.ToGrave)
                        GyEntryStamps.Entered(engine, g);
                    engine.Log($"{seat.Who.Name} sends {Names(seat.ToGrave)} to the GY.");
                }
            }

            var after = plan.After.ToList();
            plan.After.Clear();
            RunInOrder(engine, after);
            engine.FlushQueuedSummonResponses();
            engine.NotifyPublic();
        }

        static void Summon(DuelEngine engine, Plan plan, Seat seat, Pick s)
        {
            var who = seat.Who;
            var card = s.Card;
            if (plan.Kind == PlanKind.OwnGyTarget)
            {
                if (!who.Graveyard.Contains(card))
                {
                    engine.Log($"{plan.Source?.Name}: {card.Name} is no longer in the GY — it is not Special Summoned.");
                    return;
                }

                who.Graveyard.Remove(card);
                engine.NextSpecialSummonFromGraveyard = true;
                if (engine.SpecialSummonToField(who, card, s.Position, s.FaceUp, SummonKind.SpecialSummon, s.Zone))
                    engine.Log($"{plan.Source?.Name}: {who.Name} Special Summons {card.Name} from the GY in " +
                               PositionText(s));
                else
                {
                    engine.NextSpecialSummonFromGraveyard = false;
                    who.Graveyard.Add(card);
                    engine.Log($"{card.Name} could not be Special Summoned — it stays in the GY.");
                }

                return;
            }

            if (plan.Kind == PlanKind.OwnHandFaceDown)
            {
                if (!who.Hand.Contains(card))
                {
                    engine.Log($"{plan.Source?.Name}: {card.Name} is no longer in the hand — it is not Special Summoned.");
                    return;
                }

                who.Hand.Remove(card);
                engine.MarkSpecialSummonFromHand(card); // Chain Energy toll (RuleLocks batch)
                if (engine.SpecialSummonToField(who, card, s.Position, s.FaceUp, SummonKind.SpecialSummon, s.Zone))
                    engine.Log($"{plan.Source?.Name}: {who.Name} Special Summons a monster from the hand in " +
                               PositionText(s));
                else
                {
                    who.Hand.Add(card);
                    engine.Log($"{card.Name} could not be Special Summoned — it stays in the hand.");
                }

                return;
            }

            if (engine.SpecialSummonToField(who, card, s.Position, s.FaceUp, SummonKind.SpecialSummon, s.Zone))
                engine.Log($"{plan.Source?.Name}: {who.Name} Special Summons " +
                           (s.FaceUp ? $"{card.Name} in face-up Attack Position." : "a monster in face-down Defense Position."));
            else
            {
                who.Graveyard.Add(card);
                GyEntryStamps.Entered(engine, card, GyEntryReason.Rule); // BattleDestroyedGyRecovery (v162)
                engine.Log($"{card.Name} could not be Special Summoned — sent to the GY.");
            }
        }

        static string PositionText(Pick s) =>
            !s.FaceUp
                ? "face-down Defense Position."
                : s.Position == BattlePosition.Defense
                    ? "face-up Defense Position."
                    : "face-up Attack Position.";

        // ───────────────────── Cyber Jar ─────────────────────

        /// <summary>
        /// Cyber Jar (Lua c34124316): destroy all monsters on the field (their GY triggers are held), then the turn
        /// player and then the other reveal the top <see cref="EffectClause.Amount"/> cards of their Deck (as many
        /// as they have). Each player Special Summons their revealed Level <see cref="EffectClause.SummonLevelMax"/>
        /// or lower monsters that can be Special Summoned, in face-up Attack or face-down Defense Position; with
        /// too few zones they choose which and the others go to the GY. Every other revealed card goes to its
        /// owner's hand.
        /// </summary>
        public static void ResolveCyberJar(DuelEngine engine, DuelistState flipController, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || clause == null) return;
            var reveal = clause.Amount > 0 ? clause.Amount : 5;
            engine.Log($"{source?.Name ?? "Cyber Jar"}: destroy all monsters on the field.");
            List<HeldTrigger> held;
            BeginHold(engine);
            try
            {
                foreach (var m in FieldMonsters(engine))
                    if (engine.ControllerOf(m) != null)
                        TextEffectRuntime.DestroyCard(engine, m, source);
            }
            finally
            {
                held = EndHold(engine);
            }

            var plan = new Plan
            {
                Kind = PlanKind.RevealToHand,
                Source = source,
                PositionChoice = true,
                Clause = clause
            };
            // The destroyed monsters' triggers activate after Cyber Jar has resolved.
            if (held.Count > 0)
                plan.After.Add(() => ReplayHeld(engine, held));
            foreach (var who in FirstThenOther(engine, engine.TurnPlayer ?? flipController))
            {
                var seat = plan.AddSeat(who);
                var revealed = TakeTop(engine, who, reveal);
                if (revealed.Count == 0)
                {
                    engine.Log($"{who.Name} has no cards in their Deck to reveal.");
                    continue;
                }

                engine.Log($"{who.Name} reveals: {Names(revealed)}.");
                foreach (var c in revealed)
                {
                    if (IsLevelMonster(c, clause.SummonLevelMax) && DeckCopyCanBeSpecialSummoned(who, c))
                        seat.Candidates.Add(c);
                    else
                        seat.ToHand.Add(c);
                }
            }

            DecideAndRun(engine, plan);
        }

        // ───────────────────── Morphing Jar #2 ─────────────────────

        /// <summary>
        /// Morphing Jar #2 (Lua c79106360): shuffle every monster on the field into its owner's Deck (not one that
        /// is about to be destroyed by the battle that flipped this card, nor one unaffected by it); Extra Deck
        /// monsters go to the Extra Deck and Tokens vanish, and neither counts. Each player who shuffled monsters
        /// into their Main Deck excavates from the top until they have excavated that many monsters, Special Summons
        /// the excavated Level <see cref="EffectClause.SummonLevelMax"/> or lower monsters in face-down Defense
        /// Position (too few zones: they choose which) and sends the other excavated cards to the GY.
        /// </summary>
        public static void ResolveShuffleExcavateSummon(DuelEngine engine, DuelistState flipController,
            CardInstance source, EffectClause clause)
        {
            if (engine == null || flipController == null || clause == null) return;
            engine.Log($"{source?.Name ?? "Morphing Jar #2"}: shuffle all monsters on the field into the Deck.");
            var counts = new Dictionary<DuelistState, int>();
            foreach (var m in FieldMonsters(engine))
            {
                var controller = engine.ControllerOf(m);
                if (controller == null) continue;
                if (m == engine.FlipSourceBattleDestroyPending)
                {
                    engine.Log($"{m.Name} is being destroyed by battle — it is not shuffled into the Deck.");
                    continue;
                }

                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                {
                    engine.Log($"{m.Name} is unaffected by {source?.Name}.");
                    continue;
                }

                var owner = m.ControlOwner ?? controller;
                var toMainDeck = !m.IsToken && m.Def != null && !m.Def.IsExtraDeck;
                if (!engine.ReturnCardToDeck(m) || !toMainDeck) continue;
                counts.TryGetValue(owner, out var n);
                counts[owner] = n + 1;
            }

            foreach (var kv in counts)
                engine.Rng.Shuffle(kv.Key.Deck);

            var plan = new Plan
            {
                Kind = PlanKind.ExcavateToGrave,
                Source = source,
                PositionChoice = false,
                Clause = clause
            };
            foreach (var who in FirstThenOther(engine, flipController))
            {
                var seat = plan.AddSeat(who);
                counts.TryGetValue(who, out var need);
                if (need <= 0) continue;
                var excavated = new List<CardInstance>();
                var monsters = 0;
                while (monsters < need && who.Deck.Count > 0)
                {
                    var c = TakeTop(engine, who, 1)[0];
                    excavated.Add(c);
                    if (c.Def != null && c.Def.IsMonster) monsters++;
                }

                engine.Log($"{who.Name} excavates: {Names(excavated)}.");
                foreach (var c in excavated)
                {
                    if (IsLevelMonster(c, clause.SummonLevelMax) && DeckCopyCanBeSpecialSummoned(who, c))
                        seat.Candidates.Add(c);
                    else
                        seat.ToGrave.Add(c);
                }
            }

            DecideAndRun(engine, plan);
        }

        // ───────────────────── Spear Cretin ─────────────────────

        /// <summary>
        /// Spear Cretin (Lua c58551308 e2): mandatory "sent to the GY" trigger, only for a copy flipped during the
        /// field presence that just ended. Its owner, then the other player, each target 1 monster in their own GY
        /// (Spear Cretin itself excluded; nothing when they have none), then each target is Special Summoned to its
        /// owner's field in face-up Attack or face-down Defense Position (that player's choice), if they have a free
        /// Monster Zone.
        /// </summary>
        public static void ResolveEachPlayerSummonFromOwnGy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || source == null || clause == null) return;
            if (!TryLiveRecord(source, out _)) return; // not flipped: the trigger does not exist
            s_flipped.Remove(source);
            var gyOwner = engine.Player != null && engine.Player.Graveyard.Contains(source)
                ? engine.Player
                : engine.Opponent != null && engine.Opponent.Graveyard.Contains(source)
                    ? engine.Opponent
                    : who;
            engine.Log($"{source.Name}: sent to the GY after being flipped — each player selects 1 monster in " +
                       "their GY to Special Summon.");
            var plan = new Plan
            {
                Kind = PlanKind.OwnGyTarget,
                Source = source,
                PositionChoice = clause.SummonFaceUpAttackOrFaceDownDefense,
                Clause = clause
            };
            // Each player's GY targets and free zones are read when the choice is made (Prepare).
            foreach (var player in FirstThenOther(engine, gyOwner))
                plan.AddSeat(player);

            DecideAndRun(engine, plan);
        }

        /// <summary>
        /// NegationsAndGySummons batch — Gilasaurus (Lua s.operation): "your opponent can Special Summon 1 monster from
        /// their GY". The opponent of <paramref name="who"/> may select 1 monster in their own GY that can be Special
        /// Summoned (Necrovalley applies) and Special Summon it face-up in Attack or Defense Position, if they have a
        /// free Monster Zone. A human picks it (CANCEL = none); the AI takes its best monster.
        /// </summary>
        public static void ResolveOpponentMaySummonOneFromOwnGy(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null || source == null) return;
            engine.Log($"{source.Name}: {opp.Name} can Special Summon 1 monster from their GY.");
            var plan = new Plan
            {
                Kind = PlanKind.OwnGyTarget,
                Source = source,
                PositionChoice = true,
                FaceUpOnly = true,
                Optional = true,
                Clause = clause
            };
            plan.AddSeat(opp);
            DecideAndRun(engine, plan);
        }

        // ───────────────────── Major Riot ─────────────────────

        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163) — Major Riot (Lua c9074847 s.operation, after its returns):
        /// each player with a count in <paramref name="counts"/>, <paramref name="activator"/> first, Special Summons
        /// from their own hand that many monsters (Lua s.spfilter: Level <see cref="EffectClause.SummonLevelMax"/> or
        /// lower, can be Normal Summoned/Set, can be Special Summoned; fewer when the hand or the free zones run out)
        /// in face-down Defense Position, all at once. Mandatory: a human chooses among more candidates than picks
        /// (CANCEL takes the strongest), the AI takes its best monsters.
        /// </summary>
        public static void ResolveEachPlayerSummonsFromHandFaceDown(DuelEngine engine, DuelistState activator,
            CardInstance source, EffectClause clause, IDictionary<DuelistState, int> counts)
        {
            if (engine == null || activator == null || counts == null) return;
            var levelMax = clause != null && clause.SummonLevelMax > 0 ? clause.SummonLevelMax : 0;
            var plan = new Plan
            {
                Kind = PlanKind.OwnHandFaceDown,
                Source = source,
                PositionChoice = false,
                Clause = clause
            };
            foreach (var player in FirstThenOther(engine, activator))
            {
                if (!counts.TryGetValue(player, out var n) || n <= 0) continue;
                var seat = plan.AddSeat(player);
                seat.Limit = n;
                seat.Candidates.AddRange(player.Hand.Where(c =>
                    IsLevelMonster(c, levelMax) && TributeSummonsRuntime.IsNormalSummonableCard(c) &&
                    // Face-down Defense only (Lua POS_FACEDOWN_DEFENSE): face-up-only rules (Rivalry of Warlords) allow it.
                    HandOrDeckCopyCanBeSpecialSummoned(player, c, faceUp: false)));
                engine.Log(seat.Candidates.Count == 0
                    ? $"{source?.Name}: {player.Name} has no monster in their hand that can be Special Summoned."
                    : $"{source?.Name}: {player.Name} Special Summons up to {n} monster(s) from their hand in " +
                      "face-down Defense Position.");
            }

            if (plan.Seats.Count == 0) return;
            DecideAndRun(engine, plan);
        }
    }
}
