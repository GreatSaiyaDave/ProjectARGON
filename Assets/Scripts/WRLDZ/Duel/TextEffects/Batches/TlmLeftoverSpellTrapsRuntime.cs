using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmLeftoverSpellTraps" runtime (atoms: <see cref="TlmLeftoverSpellTrapsAtoms"/>). Reads compiled clauses
    /// only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Grave Lure</b> (<see cref="EffectActionKind.TurnOpponentDeckTopFaceUpSendToGyWhenDrawn"/>): the opponent's
    /// top Deck card is shown, turned face-up in the Deck and the Deck shuffled; the copy carries
    /// <see cref="DeckCopyInfo.SentToGyWhenDrawnBy"/>. <c>DuelEngine.Draw</c> reports each draw (<see cref="NoteDrawn"/>):
    /// that copy is sent to the GY at once, or, while a Chain resolves, after the resolving Chain Link
    /// (<see cref="FlushDrawnToGy"/>, from <c>DuelEngine.ResolveChainStack</c>; Lua EVENT_CHAIN_SOLVED) if it is still in
    /// the hand. A copy that leaves the Deck another way loses the mark with its Deck metadata.</item>
    /// <item><b>Pikeru's Second Sight</b> (<see cref="EffectActionKind.OpponentShowsDrawnCardsUntilTheirSecondEndPhase"/>): a
    /// player effect of the activator; each card the opponent draws is shown to the activator (the duel log, as the
    /// other "look at" effects: <see cref="FlipDownAndPeekRuntime.LogLook"/>) until the end of the opponent's 2nd End
    /// Phase counted from the activation (the current one included when activated during their turn).</item>
    /// <item><b>Level Conversion Lab</b> (<see cref="EffectActionKind.ShowHandMonsterRollDieLevelBecomesResultOrSendToGy"/>):
    /// at resolution the activator selects 1 monster in their hand (AskCardPick, not a target), shows it, rolls a die
    /// through <see cref="CoinDiceInterruptsRuntime.RollDie"/>. 1: it is sent to the GY. 2-6: its Level becomes the result
    /// until the End Phase (<see cref="ApplyLevelChanges"/>, from <c>SimpleTargetUtilityRuntime.ApplyLevelStamps</c> on
    /// each board refresh) — in the hand, and on the field after a Summon from the hand this turn (ygopro
    /// RESETS_STANDARD_PHASE_END minus RESET_TOFIELD); it ends if the card goes to the GY, leaves the field, or is turned
    /// face-down after being face-up on the field. The set Level overrides the Cost Down stamps live at resolution; a Cost
    /// Down that resolves later still applies on top of it (ygopro CHANGE_LEVEL / UPDATE_LEVEL timestamp order).</item>
    /// <item><b>Shifting Shadows</b> (<see cref="EffectActionKind.ShuffleYourFaceDownMainZoneMonsters"/>): a face-up field
    /// ignition routed by <see cref="PerCountBurnsRuntime"/> (<see cref="IsShadowsIgnition"/> /
    /// <see cref="CanUseShadowsIgnition"/> / <see cref="ActivateShadowsIgnition"/> / <see cref="TryResolveShadowsLink"/>):
    /// once per turn per copy, pay the LP (cost), needs 2+ face-down monsters in your Main Monster Zones; at resolution
    /// they are rearranged at random (<see cref="FlipDownAndPeekRuntime.ShuffleFaceDownMainZoneMonsters"/>).</item>
    /// <item><b>Mesmeric Control</b> (<see cref="EffectActionKind.OpponentCannotChangeBattlePositionsDuringTheirNextTurn"/>):
    /// a player lock through the End Phase of the next turn, read by <see cref="ManualPositionChangeLocked"/> (from
    /// <c>PositionLocksRuntime.ManualPositionChangeLocked</c>: DuelEngine.CanChangePosition / CanFlipSummon).</item>
    /// </list>
    /// </summary>
    public static class TlmLeftoverSpellTrapsRuntime
    {
        /// <summary>A player effect that lasts through <see cref="LastTurn"/> (Pikeru's Second Sight, Mesmeric Control).</summary>
        sealed class PlayerEffect
        {
            public DuelistState Owner;
            public CardInstance Source;
            public int LastTurn;
        }

        /// <summary>A Grave Lure copy drawn while a Chain Link resolved: sent once that link is over.</summary>
        sealed class DrawnCopy
        {
            public DuelistState Holder;
            public CardInstance Card;
            public DuelistState By;
            public CardInstance Source;
        }

        sealed class State
        {
            public readonly List<PlayerEffect> Sights = new();
            public readonly List<PlayerEffect> PositionLocks = new();
            public readonly List<DrawnCopy> DrawnDuringChain = new();
        }

        /// <summary>Level Conversion Lab's "Level becomes N" on one card.</summary>
        sealed class LevelChange
        {
            public int Turn;
            public int Level;
            /// <summary><see cref="CardInstance.FieldEntryStamp"/> when the change was applied (in the hand).</summary>
            public int EntryStampInHand;
            public int GyEntryStamp;
            /// <summary><see cref="CardInstance.FaceDownStamp"/> once it was seen face-up on the field; -1 before.</summary>
            public int FaceDownStampFaceUp = -1;
            /// <summary>
            /// The hand Level reduction (Cost Down stamps) live when the Level was set. ygopro card::get_level applies
            /// CHANGE_LEVEL and UPDATE_LEVEL in timestamp order and CHANGE_LEVEL resets only the older single updates, so
            /// the stamps up to here are overridden and any added later still apply on top of the set Level.
            /// </summary>
            public int ReductionAtChange;
        }

        sealed class ShadowsUse
        {
            public int Turn;
            public int EntryStamp;
        }

        sealed class ShadowsHold
        {
            public DuelistState Who;
            public CardInstance Card;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, LevelChange> s_levels = new();
        static readonly ConditionalWeakTable<CardInstance, ShadowsUse> s_shadowsUsed = new();
        static readonly ConditionalWeakTable<ChainLink, ShadowsHold> s_shadowsLinks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static bool InThisDuel(DuelEngine engine, DuelistState who) =>
            who != null && (ReferenceEquals(who, engine.Player) || ReferenceEquals(who, engine.Opponent));

        // ───────────────────────────── Grave Lure ─────────────────────────────

        /// <summary>
        /// Grave Lure resolves: the top card of the opponent's Deck is shown to both players, turned face-up in that Deck
        /// and marked to go to the GY when drawn; then the opponent's Deck is shuffled (the metadata moves with the copy).
        /// </summary>
        public static void ApplyGraveLure(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            var name = source?.Name ?? "Grave Lure";
            if (opp?.Deck == null || opp.Deck.Count == 0)
            {
                engine?.Log($"{name}: {opp?.Name ?? "the opponent"} has no card in their Deck — no effect.");
                return;
            }

            var id = opp.Deck[0];
            var info = opp.Deck.TakeAt(0) ?? new DeckCopyInfo();
            info.FaceUp = true;
            info.SentToGyWhenDrawnBy = who;
            info.SentToGyWhenDrawnSource = source;
            opp.Deck.Insert(0, id, info);
            var topName = engine.Database != null && engine.Database.TryGet(id, out var def) && def != null
                ? def.name
                : $"card #{id}";
            engine.Log($"{name}: the top card of {opp.Name}'s Deck is {topName} — it is turned face-up in the Deck.");
            engine.Rng.Shuffle(opp.Deck);
            engine.Log($"{name}: {opp.Name} shuffles their Deck. When they draw {topName}, it is sent to the GY.");
        }

        /// <summary>
        /// <c>DuelEngine.Draw</c>, for each card drawn (after the copy's own draw trigger and Crush Card Virus): Pikeru's
        /// Second Sight shows it to the drawer's opponent; a Grave Lure copy goes to the GY now, or after the resolving
        /// Chain Link while a Chain resolves (Lua: Duel.IsChainSolving → EVENT_CHAIN_SOLVED).
        /// </summary>
        public static void NoteDrawn(DuelEngine engine, DuelistState who, CardInstance card, DeckCopyInfo info)
        {
            if (engine == null || who == null || card == null) return;
            ShowToSecondSight(engine, who, card);
            var by = info?.SentToGyWhenDrawnBy;
            if (by == null) return;
            if (!engine.IsResolvingChain)
            {
                SendDrawnToGy(engine, who, card, by, info.SentToGyWhenDrawnSource);
                return;
            }

            engine.Log($"{card.Name} was drawn face-up — it is sent to the GY after this Chain Link resolves.");
            StateOf(engine).DrawnDuringChain.Add(new DrawnCopy
            {
                Holder = who,
                Card = card,
                By = by,
                Source = info.SentToGyWhenDrawnSource
            });
        }

        /// <summary>
        /// <c>DuelEngine.ResolveChainStack</c>, before each Chain Link resolves and when the Chain is over: the Grave Lure
        /// copies drawn during the previous link go to the GY if they are still in their holder's hand.
        /// </summary>
        public static void FlushDrawnToGy(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.DrawnDuringChain.Count == 0) return;
            var due = st.DrawnDuringChain.ToList();
            st.DrawnDuringChain.Clear();
            foreach (var d in due)
            {
                if (engine.GameOver) return;
                SendDrawnToGy(engine, d.Holder, d.Card, d.By, d.Source);
            }
        }

        static void SendDrawnToGy(DuelEngine engine, DuelistState holder, CardInstance card, DuelistState by,
            CardInstance source)
        {
            if (holder?.Hand == null || card == null || !holder.Hand.Contains(card)) return;
            engine.Log($"{source?.Name ?? "Grave Lure"}: {holder.Name} drew the face-up {card.Name} — it is sent to the GY.");
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                engine.SendCardToGrave(holder, card, sentBy: source);
                // A send by a card effect, not a discard (Lua REASON_EFFECT, reason player = Grave Lure's controller).
                DiscardTriggersRuntime.NoteSentFromHand(engine, holder, card, by);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }
        }

        // ───────────────────────────── Pikeru's Second Sight ─────────────────────────────

        /// <summary>
        /// Pikeru's Second Sight resolves: register the activator's effect. RESET_OPPO_TURN with count 2 ends it at the
        /// opponent's 2nd End Phase: the current turn counts when it is the opponent's (their turns are T and T+2), else
        /// their next two turns (T+1 and T+3).
        /// </summary>
        public static void RegisterSecondSight(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var last = engine.TurnNumber + (ReferenceEquals(engine.TurnPlayer, opp) ? 2 : 3);
            StateOf(engine).Sights.Add(new PlayerEffect { Owner = who, Source = source, LastTurn = last });
            engine.Log($"{source?.Name ?? "Pikeru's Second Sight"}: until {opp.Name}'s second End Phase, {opp.Name} shows " +
                       $"{who.Name} every card they draw.");
        }

        static void ShowToSecondSight(DuelEngine engine, DuelistState drawer, CardInstance card)
        {
            if (!s_state.TryGetValue(engine, out var st) || st.Sights.Count == 0) return;
            st.Sights.RemoveAll(s => s.LastTurn < engine.TurnNumber || !InThisDuel(engine, s.Owner));
            var viewer = engine.OpponentOf(drawer);
            var sight = st.Sights.FirstOrDefault(s => ReferenceEquals(s.Owner, viewer));
            if (sight == null) return;
            FlipDownAndPeekRuntime.LogLook(engine, viewer, sight.Source, $"the card {drawer.Name} draws", new[] { card });
        }

        // ───────────────────────────── Level Conversion Lab ─────────────────────────────

        static List<CardInstance> HandMonsters(DuelistState who) =>
            who?.Hand?.Where(h => h?.Def != null && h.Def.IsMonster).ToList() ?? new List<CardInstance>();

        /// <summary>
        /// Level Conversion Lab resolves: the activator selects 1 monster in their hand (a human picks among 2+, the AI takes
        /// its highest Level), it is shown, then the die is rolled.
        /// </summary>
        public static void StartLevelConversion(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var name = source?.Name ?? "Level Conversion Lab";
            if (HandMonsters(who).Count == 0)
            {
                engine.Log($"{name}: {who.Name} has no monster in their hand — no effect.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => HandMonsters(who),
                Kind = EffectTargetKind.MonsterInYourHand,
                Prompt = $"{name}: select 1 monster in your hand to show your opponent, then a die is rolled.",
                AiChoose = list => list.OrderByDescending(m => m.Level).ThenByDescending(m => m.Def?.atk ?? 0)
                    .FirstOrDefault(),
                HoldsChain = engine.IsResolvingChain,
                Finish = pick => ShowAndRoll(engine, who, source, pick)
            });
        }

        static void ShowAndRoll(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            var name = source?.Name ?? "Level Conversion Lab";
            if (engine == null || engine.GameOver || pick == null || !who.Hand.Contains(pick)) return;
            var opp = engine.OpponentOf(who);
            engine.Log($"{name}: {who.Name} shows {pick.Name} (Level {pick.Level}) to {opp?.Name ?? "the opponent"}.");
            CoinDiceInterruptsRuntime.RollDie(engine, who, source, r => r != 1,
                r => ApplyRoll(engine, who, source, pick, r));
        }

        static void ApplyRoll(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick, int roll)
        {
            var name = source?.Name ?? "Level Conversion Lab";
            if (engine == null || engine.GameOver) return;
            if (!who.Hand.Contains(pick))
            {
                engine.Log($"{name}: {pick.Name} is no longer in {who.Name}'s hand — no effect.");
                return;
            }

            if (roll == 1)
            {
                engine.Log($"{name}: the die shows 1 — {pick.Name} is sent to the GY.");
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    engine.SendCardToGrave(who, pick, sentBy: source);
                    DiscardTriggersRuntime.NoteSentFromHand(engine, who, pick, who);
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
                }

                engine.NotifyPublic();
                return;
            }

            s_levels.Remove(pick);
            s_levels.Add(pick, new LevelChange
            {
                Turn = engine.TurnNumber,
                Level = roll,
                EntryStampInHand = pick.FieldEntryStamp,
                GyEntryStamp = pick.GyEntryStamp,
                ReductionAtChange = ReductionAtResolution(engine, who, pick)
            });
            ApplyOne(engine, pick, onField: false);
            engine.Log($"{name}: the die shows {roll} — {pick.Name}'s Level becomes {roll} until the end of this turn.");
            engine.NotifyPublic();
        }

        /// <summary>
        /// <c>SimpleTargetUtilityRuntime.ApplyLevelStamps</c> (every board refresh, after the modifiers were cleared): set
        /// each live "Level becomes N" on its card in a hand or a Main Monster Zone; drop the ones that ended.
        /// </summary>
        public static void ApplyLevelChanges(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who.Hand != null)
                    foreach (var h in who.Hand)
                        ApplyOne(engine, h, onField: false);
                if (who.MonsterZones == null) continue;
                foreach (var z in who.MonsterZones)
                    ApplyOne(engine, z?.Occupant, onField: true);
            }
        }

        static void ApplyOne(DuelEngine engine, CardInstance c, bool onField)
        {
            if (c?.Def == null || !s_levels.TryGetValue(c, out var lc)) return;
            if (!StillApplies(engine, c, lc, onField))
            {
                s_levels.Remove(c);
                return;
            }

            if (c.PrintedLevel > 0)
                c.LevelModifier = lc.Level - c.PrintedLevel - (LiveReduction(engine, c) - lc.ReductionAtChange);
        }

        /// <summary>The card's live Cost Down stamp this turn (<c>SimpleTargetUtilityRuntime</c>); 0 when none.</summary>
        static int LiveReduction(DuelEngine engine, CardInstance c) =>
            c.LevelReducedUntilTurn == engine.TurnNumber ? c.LevelReducedAmount : 0;

        /// <summary>
        /// The stamp the card carries at the Lab's resolution, counting the hand-wide one a card drawn after a Cost Down
        /// inherits on the next sweep (<c>SimpleTargetUtilityRuntime.Sweep</c>) even if no sweep ran since the draw.
        /// </summary>
        static int ReductionAtResolution(DuelEngine engine, DuelistState who, CardInstance c)
        {
            if (c.LevelReducedUntilTurn == engine.TurnNumber) return c.LevelReducedAmount;
            return who != null && who.LevelReducedHandUntilTurn == engine.TurnNumber ? who.LevelReducedHandAmount : 0;
        }

        static bool StillApplies(DuelEngine engine, CardInstance c, LevelChange lc, bool onField)
        {
            if (lc.Turn != engine.TurnNumber || !c.Def.IsMonster) return false;
            // Sent to the GY and back (RESET_TOGRAVE).
            if (c.GyEntryStamp != lc.GyEntryStamp) return false;
            if (!onField)
                return c.FieldEntryStamp == lc.EntryStampInHand; // went to the field and back to the hand: RESET_TOHAND
            // Summoned / Set from the hand once (RESET_TOFIELD is excluded); a second field entry means it left (RESET_LEAVE).
            if (c.FieldEntryStamp != lc.EntryStampInHand + 1) return false;
            if (!c.FaceUp) return lc.FaceDownStampFaceUp < 0 || c.FaceDownStamp == lc.FaceDownStampFaceUp;
            if (lc.FaceDownStampFaceUp < 0) lc.FaceDownStampFaceUp = c.FaceDownStamp;
            // Turned face-down after it was face-up on the field (RESET_TURN_SET).
            return c.FaceDownStamp == lc.FaceDownStampFaceUp;
        }

        // ───────────────────────────── Shifting Shadows ─────────────────────────────

        static EffectClause ShadowsClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.FaceUpFieldIgnition)
                .FirstOrDefault(c => c != null && c.Action == EffectActionKind.ShuffleYourFaceDownMainZoneMonsters);

        /// <summary>The card's face-up ignition is Shifting Shadows' paid rearrangement.</summary>
        public static bool IsShadowsIgnition(CompiledCardProgram prog) => ShadowsClause(prog) != null;

        static int FaceDownMainZoneMonsters(DuelistState who)
        {
            if (who?.MonsterZones == null) return 0;
            var n = 0;
            for (var i = 0; i < who.MonsterZones.Length && i < TcgRules.MonsterZones; i++)
            {
                var occ = who.MonsterZones[i]?.Occupant;
                if (occ != null && !occ.FaceUp) n++;
            }

            return n;
        }

        /// <summary>
        /// <c>PerCountBurnsRuntime.CanActivateFaceUpFieldIgnition</c>, after its open-state / own Main Phase / negation checks:
        /// once per turn per copy (SetCountLimit(1); a copy that left the field and came back is a new card), the LP cost
        /// can be paid, and 2 or more face-down monsters are in your Main Monster Zones.
        /// </summary>
        public static bool CanUseShadowsIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            var c = ShadowsClause(prog);
            if (engine == null || who == null || card == null || c == null)
            {
                reason = "Not a Shifting Shadows effect.";
                return false;
            }

            if (s_shadowsUsed.TryGetValue(card, out var used) && used.Turn == engine.TurnNumber &&
                used.EntryStamp == card.FieldEntryStamp)
            {
                reason = "This card's effect was already used this turn.";
                return false;
            }

            if (who.LifePoints < c.PayLpAmount)
            {
                reason = $"Needs {c.PayLpAmount} LP to pay.";
                return false;
            }

            var need = Math.Max(2, c.ConditionAmount);
            if (FaceDownMainZoneMonsters(who) < need)
            {
                reason = $"Needs {need} or more face-down monsters in your Main Monster Zones.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// <c>PerCountBurnsRuntime.TryFaceUpFieldIgnition</c>, on its seated Chain Link: count the use and pay the LP (cost).
        /// The monsters are rearranged when the link resolves.
        /// </summary>
        public static void ActivateShadowsIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, ChainLink link)
        {
            var c = ShadowsClause(prog);
            if (engine == null || who == null || card == null || c == null || link == null) return;
            s_shadowsUsed.Remove(card);
            s_shadowsUsed.Add(card, new ShadowsUse { Turn = engine.TurnNumber, EntryStamp = card.FieldEntryStamp });
            s_shadowsLinks.Remove(link);
            s_shadowsLinks.Add(link, new ShadowsHold { Who = who, Card = card });
            engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
            var amount = Math.Max(0, c.PayLpAmount);
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP → {who.Name} at {Math.Max(0, who.LifePoints - amount)} LP.");
        }

        /// <summary>
        /// <c>PerCountBurnsRuntime.TryResolveFaceUpFieldIgnitionLink</c>: false for any other link. Shifting Shadows' link
        /// rearranges its controller's face-down monsters in the Main Monster Zones at random (Lua s.operation has no
        /// relate check, so it applies even if the card left the field), unless its effect is negated as it resolves.
        /// </summary>
        public static bool TryResolveShadowsLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || !s_shadowsLinks.TryGetValue(link, out var st)) return false;
            s_shadowsLinks.Remove(link);
            var card = st.Card;
            var who = st.Who;
            var name = card?.Name ?? "Shifting Shadows";
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, card);
            var stillOnField = card != null && who != null && who.TryFindSpellTrap(card, out _);
            if (negatedBy != null || (stillOnField && card.IsNegated))
            {
                engine.Log($"{name}: its effect is negated{(negatedBy != null ? $" by {negatedBy.Name}" : "")}.");
                engine.NotifyPublic();
                return true;
            }

            if (FaceDownMainZoneMonsters(who) < 2)
                engine.Log($"{name}: {who?.Name} has fewer than 2 face-down monsters — nothing is rearranged.");
            else
                FlipDownAndPeekRuntime.ShuffleFaceDownMainZoneMonsters(engine, who, card);
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Mesmeric Control ─────────────────────────────

        /// <summary>
        /// Mesmeric Control resolves: the activator's opponent cannot change the battle positions of the monsters they
        /// control by hand until the end of the next turn (RESET_PHASE|PHASE_END, 2 counts this turn's End Phase and the
        /// next one).
        /// </summary>
        public static void RegisterPositionLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            StateOf(engine).PositionLocks.Add(new PlayerEffect
            {
                Owner = who,
                Source = source,
                LastTurn = engine.TurnNumber + 1
            });
            engine.Log($"{source?.Name ?? "Mesmeric Control"}: during {opp.Name}'s next turn, they cannot change the battle " +
                       "positions of their monsters, except with a card effect.");
        }

        /// <summary>
        /// <c>PositionLocksRuntime.ManualPositionChangeLocked</c> (DuelEngine.CanChangePosition / CanFlipSummon /
        /// IsBattlePositionLocked): a Mesmeric Control of <paramref name="card"/>'s controller's opponent is live.
        /// </summary>
        public static bool ManualPositionChangeLocked(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st) || st.PositionLocks.Count == 0)
                return false;
            st.PositionLocks.RemoveAll(l => l.LastTurn < engine.TurnNumber || !InThisDuel(engine, l.Owner));
            var ctrl = engine.ControllerOf(card);
            if (ctrl == null) return false;
            var opp = engine.OpponentOf(ctrl);
            return st.PositionLocks.Exists(l => ReferenceEquals(l.Owner, opp));
        }
    }
}
