using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PhaseTimersAndDeckTop" (v163) runtime: Standby / End Phase timers, turn counts and FLIP Deck-to-top
    /// effects keyed on "Necrovalley". Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item>Bottomless Shifting Sand (c76532077): the End Phase of its controller's opponent's turn destroys every
    /// face-up monster tied for the highest ATK at once (<see cref="DestroyHighestAtkFaceUpMonsters"/>); in its
    /// controller's Standby Phase a continuous check (no Chain Link, before the Standby triggers) destroys it while they
    /// hold 4 or fewer cards (<see cref="OnStandbyPhase"/>).</item>
    /// <item>Mirage of Nightmare (c41482598): the opponent's Standby Phase draws up to 4 and records how many were to be
    /// drawn on this field presence (<see cref="ResolveMirageDraw"/>); its controller's next Standby Phase discards that
    /// many at random, or the whole hand (<see cref="ResolveMirageDiscard"/>).</item>
    /// <item>Different Dimension Capsule (c11961740): banish 1 chosen Deck card face-down; the Capsule stays on the field
    /// and counts its controller's Standby Phases (<see cref="OnStandbyPhase"/>); in the 2nd it is destroyed and the card,
    /// if it is still banished, goes to the hand. No Pyro Clock turn count (its Lua registers no 1082946 effect).</item>
    /// <item>Pyro Clock of Destiny (c1082946): one shared turn-count interface (<see cref="TurnCounts"/>) over the counts
    /// whose Lua registers effect code 1082946 — Swords of Revealing Light, Lightforce Sword, Steel Scorpion and Deck
    /// Devastation Virus. Each count runs the same one-step method its own phase tick runs.</item>
    /// <item>A Cat of Ill Omen / An Owl of Luck: <see cref="EffectActionKind.PlaceNamedFromDeckOnTop"/> with a card kind
    /// (<see cref="ResolvePlaceKindOnTop"/>): choose the card, then, while "Necrovalley" is on the field, the controller may
    /// add it to the hand; otherwise the Deck is shuffled and the card placed on top.</item>
    /// <item>Royal Tribute (c72405967): both players discard every monster in their hands as one event
    /// (<see cref="ResolveRoyalTribute"/>).</item>
    /// </list>
    /// Human choices go through <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick"/> /
    /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskOptionalCardPick"/>, which hold the Chain / Standby Phase.
    /// </summary>
    public static class PhaseTimersAndDeckTopRuntime
    {
        /// <summary>ActivationCondition (Pyro Clock of Destiny): some effect with a turn count exists.</summary>
        public const string TurnCountExists = "TurnCountExists";

        /// <summary>ActivationCondition (Royal Tribute): a card other than this one in either player's hand.</summary>
        public const string CardOtherThanThisInEitherHand = "CardOtherThanThisInEitherHand";

        /// <summary>Mirage of Nightmare's record: the number to discard, for one field presence.</summary>
        sealed class MirageRecord
        {
            public int EntryStamp;
            public int Count;
        }

        /// <summary>Different Dimension Capsule's delayed effect (Lua e1, label and reset count).</summary>
        sealed class CapsuleRecord
        {
            public CardInstance Capsule;
            public int EntryStamp;
            public DuelistState Controller;
            public CardInstance Banished;
            /// <summary>Lua label: the operation ran once already (the next run is the 2nd Standby Phase).</summary>
            public bool RanOnce;
            /// <summary>Own Standby Phases passed (Lua RESET_PHASE+PHASE_STANDBY+RESET_SELF_TURN, 2).</summary>
            public int StandbysPassed;
        }

        /// <summary>The card that holds a turn count kept on another object (a banished card, a player).</summary>
        sealed class Holder
        {
            public object Key;
            public CardInstance Card;
            public DuelistState Owner;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, MirageRecord> Mirage = new();
            public readonly List<CapsuleRecord> Capsules = new();
            public readonly List<Holder> Holders = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── helpers ─────────────────────────────

        static bool FaceUpSpellTrapOf(DuelistState who, CardInstance card) =>
            who != null && card != null && card.FaceUp &&
            (who.TryFindSpellTrap(card, out _) || ReferenceEquals(who.FieldSpellZone?.Occupant, card));

        /// <summary>A face-up card whose effects are negated (its own flag, or Imperial Order / Royal Decree / Jinzo).</summary>
        static bool Negated(DuelEngine engine, CardInstance card) =>
            card != null && (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null);

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == action);
        }

        /// <summary>One proxy card per distinct card in <paramref name="who"/>'s Deck that <paramref name="ok"/> accepts.</summary>
        static List<CardInstance> DeckProxies(DuelEngine engine, DuelistState who, Func<CardDef, bool> ok)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id)) continue;
                if (!engine.Database.TryGet(id, out var def) || def == null || (ok != null && !ok(def))) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        /// <summary>Clause predicate for TextEffectRuntime.IsContinuousTrapPlacementOnly: Bottomless Shifting Sand's effects
        /// apply from the face-up card, not from its activation.</summary>
        public static bool IsFaceUpTriggerOnly(EffectClause c) =>
            c != null &&
            ((c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk) ||
             (c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost));

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            switch (c.ActivationCondition)
            {
                case TurnCountExists:
                    ok = TurnCounts(engine).Count > 0;
                    reason = "Needs an effect that counts turns (Swords of Revealing Light, Lightforce Sword, …).";
                    return true;
                case CardOtherThanThisInEitherHand:
                {
                    var opp = engine.OpponentOf(who);
                    ok = (who.Hand?.Any(h => h != null && !ReferenceEquals(h, card)) ?? false) ||
                         (opp?.Hand?.Any(h => h != null && !ReferenceEquals(h, card)) ?? false);
                    reason = "Needs a card in either player's hand.";
                    return true;
                }
            }

            return false;
        }

        // ───────────────────────────── Standby Phase (continuous effects) ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggers, <paramref name="who"/>'s Standby Phase, before the Standby triggers (these
        /// are continuous effects, applied as the phase starts; not Chain Links): Bottomless Shifting Sand's hand check and
        /// Different Dimension Capsule's count.
        /// </summary>
        public static void OnStandbyPhase(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || engine.GameOver) return;
            ShiftingSandStandbyCheck(engine, who);
            if (engine.GameOver) return;
            CapsuleStandby(engine, who);
        }

        /// <summary>
        /// Bottomless Shifting Sand (Lua e3: EVENT_PHASE+PHASE_STANDBY continuous, turn player = controller, hand ≤ 4):
        /// destroy this card. A negated copy does nothing.
        /// </summary>
        static void ShiftingSandStandbyCheck(DuelEngine engine, DuelistState who)
        {
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                if (st == null || !st.FaceUp) continue;
                var clause = ClauseOf(st, EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost);
                if (clause == null || Negated(engine, st)) continue;
                var limit = clause.Amount > 0 ? clause.Amount : 4;
                var hand = who.Hand?.Count ?? 0;
                if (hand > limit) continue;
                engine.Log($"{st.Name}: {who.Name} holds {hand} card(s) in the Standby Phase ({limit} or less) — it is destroyed.");
                TextEffectRuntime.DestroyCard(engine, st, st, byPlayer: who);
                if (engine.GameOver) return;
            }
        }

        // ───────────────────────────── Bottomless Shifting Sand ─────────────────────────────

        /// <summary>
        /// End Phase of the opponent's turn (Lua s.desop): every face-up monster on the field whose ATK is the highest is
        /// destroyed, all at once (GetMaxGroup: every tied monster). Nothing face-up: nothing happens.
        /// </summary>
        public static void DestroyHighestAtkFaceUpMonsters(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            if (!FaceUpSpellTrapOf(who, source))
            {
                engine.Log($"{source?.Name ?? "Effect"} is no longer face-up on the field — nothing is destroyed.");
                return;
            }

            var faceUp = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null)
                    faceUp.AddRange(side.MonstersOnField().Where(m => m != null && m.FaceUp));
            if (faceUp.Count == 0)
            {
                engine.Log($"{source.Name}: no face-up monster on the field.");
                return;
            }

            var top = faceUp.Max(m => m.CurrentAtk);
            var victims = faceUp.Where(m => m.CurrentAtk == top).ToList();
            engine.Log($"{source.Name}: destroy the face-up monster(s) with the highest ATK ({top}): " +
                       string.Join(", ", victims.Select(v => v.Name)) + ".");
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var v in victims)
                    if (engine.ControllerOf(v) != null)
                        TextEffectRuntime.DestroyCard(engine, v, source, byPlayer: who);
            });
        }

        // ───────────────────────────── Mirage of Nightmare ─────────────────────────────

        /// <summary>
        /// Opponent's Standby Phase (Lua s.drop): while <paramref name="who"/> holds fewer than N cards, draw N minus the hand
        /// size and record that number on this field presence (a new activation is a new presence, which clears it).
        /// </summary>
        public static void ResolveMirageDraw(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Hand == null || source == null || clause == null || engine.GameOver) return;
            if (!FaceUpSpellTrapOf(who, source)) return;
            var want = clause.Amount > 0 ? clause.Amount : 4;
            var n = want - who.Hand.Count;
            if (n <= 0)
            {
                engine.Log($"{source.Name}: {who.Name} already holds {who.Hand.Count} cards — no draw.");
                return;
            }

            StateOf(engine).Mirage[source] = new MirageRecord { EntryStamp = source.FieldEntryStamp, Count = n };
            engine.Log($"{source.Name}: {who.Name} draws until {want} cards in hand ({n}); they discard {n} at random " +
                       "in their next Standby Phase.");
            engine.Draw(who, n, silent: false);
        }

        /// <summary>The number Mirage of Nightmare's next discard is waiting for on this presence (0 = none).</summary>
        public static int MirageRecordedCount(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null || !s_state.TryGetValue(engine, out var st)) return 0;
            return st.Mirage.TryGetValue(source, out var r) && r.EntryStamp == source.FieldEntryStamp ? r.Count : 0;
        }

        /// <summary>
        /// Controller's Standby Phase with a record (Lua s.dctg clears it as the effect activates, s.dcop): discard that
        /// many cards at random, or the whole hand, as one discard event (REASON_EFFECT + REASON_DISCARD).
        /// </summary>
        public static void ResolveMirageDiscard(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.Hand == null || source == null || engine.GameOver) return;
            var n = MirageRecordedCount(engine, source);
            if (n <= 0) return;
            StateOf(engine).Mirage.Remove(source);
            if (!FaceUpSpellTrapOf(who, source)) return;
            engine.Log($"{source.Name}: {who.Name} discards {Math.Min(n, who.Hand.Count)} card(s) at random.");
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                for (var i = 0; i < n && who.Hand.Count > 0; i++)
                {
                    var idx = engine.Rng.PickIndex(who.Hand.Count);
                    if (idx < 0 || idx >= who.Hand.Count) idx = 0;
                    var gone = MonsterEffects.DiscardFromHandByInstance(who, who.Hand[idx], DiscardCause.Effect, who);
                    if (gone == null) continue;
                    engine.Log($"{who.Name} discards {gone.Name} at random ({source.Name}).");
                    DiscardTriggersRuntime.NoteDiscard(engine, who, gone, who);
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }
        }

        // ───────────────────────────── Different Dimension Capsule ─────────────────────────────

        /// <summary>
        /// Resolution (Lua s.activate): with this card still on the field, its controller chooses 1 card in their Deck and
        /// banishes it face-down; the Capsule stays on the field and waits for its controller's 2nd Standby Phase. Nothing
        /// banished (an empty Deck): it goes to the GY like any Normal Spell (CancelToGrave).
        /// </summary>
        public static void ResolveCapsule(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            if (!FaceUpSpellTrapOf(who, source))
            {
                engine.Log($"{source.Name} is no longer on the field — nothing is banished.");
                return;
            }

            var stamp = source.FieldEntryStamp;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => DeckProxies(engine, who, null),
                EffectTargetKind.CardInYourDeck,
                "choose 1 card in your Deck to banish face-down. It is added to your hand in your 2nd Standby Phase.",
                AiCapsulePick,
                picked => FinishCapsule(engine, who, source, stamp, picked));
        }

        static CardInstance AiCapsulePick(List<CardInstance> list) =>
            list.Where(c => c?.Def != null && c.Def.IsMonster).OrderByDescending(c => c.Def.atk).FirstOrDefault() ??
            list.FirstOrDefault();

        static void FinishCapsule(DuelEngine engine, DuelistState who, CardInstance source, int stamp, CardInstance picked)
        {
            var idx = picked == null || who.Deck == null ? -1 : who.Deck.IndexOf(picked.CardId);
            if (idx < 0 || !FaceUpSpellTrapOf(who, source) || source.FieldEntryStamp != stamp)
            {
                engine.Log($"{source.Name}: nothing is banished.");
                if (FaceUpSpellTrapOf(who, source))
                    engine.SendCardToGrave(who, source);
                return;
            }

            who.Deck.RemoveAt(idx);
            var card = engine.CreateCardInstance(picked.CardId);
            card.FaceUp = false;
            card.BanishedTurnNumber = engine.TurnNumber;
            who.Banished.Add(card);
            StateOf(engine).Capsules.Add(new CapsuleRecord
            {
                Capsule = source,
                EntryStamp = stamp,
                Controller = who,
                Banished = card
            });
            engine.Log($"{source.Name}: {who.Name} banishes a card from their Deck face-down.");
        }

        /// <summary>The face-down card Different Dimension Capsule is holding for <paramref name="capsule"/>, or null.</summary>
        public static CardInstance CapsuleBanishedCard(DuelEngine engine, CardInstance capsule)
        {
            if (engine == null || capsule == null || !s_state.TryGetValue(engine, out var st)) return null;
            return st.Capsules.FirstOrDefault(r => ReferenceEquals(r.Capsule, capsule))?.Banished;
        }

        /// <summary>
        /// Its controller's Standby Phase (Lua e1, continuous, range Spell &amp; Trap Zone, reset after 2 own Standby
        /// Phases or when the Capsule leaves the field): the 1st time it only counts; the 2nd time the Capsule is destroyed
        /// and the banished card, if it is still that banished face-down card, is added to the hand. While negated the
        /// operation does not run, but the Standby Phase still counts toward the reset.
        /// </summary>
        static void CapsuleStandby(DuelEngine engine, DuelistState who)
        {
            if (!s_state.TryGetValue(engine, out var st) || st.Capsules.Count == 0) return;
            foreach (var r in st.Capsules.ToList())
            {
                if (engine.GameOver) return;
                if (!FaceUpSpellTrapOf(r.Controller, r.Capsule) || r.Capsule.FieldEntryStamp != r.EntryStamp)
                {
                    st.Capsules.Remove(r);
                    continue;
                }

                if (!ReferenceEquals(r.Controller, who)) continue;
                if (!Negated(engine, r.Capsule))
                {
                    if (r.RanOnce)
                    {
                        st.Capsules.Remove(r);
                        engine.Log($"{r.Capsule.Name}: {who.Name}'s 2nd Standby Phase after its activation — it is destroyed.");
                        TextEffectRuntime.DestroyCard(engine, r.Capsule, r.Capsule, byPlayer: who);
                        ReturnBanishedToHand(engine, who, r);
                        continue;
                    }

                    r.RanOnce = true;
                    engine.Log($"{r.Capsule.Name}: {who.Name}'s 1st Standby Phase after its activation.");
                }

                r.StandbysPassed++;
                if (r.StandbysPassed >= 2)
                {
                    st.Capsules.Remove(r);
                    engine.Log($"{r.Capsule.Name}: its effect has ended (it was negated in the 2nd Standby Phase).");
                }
            }
        }

        static void ReturnBanishedToHand(DuelEngine engine, DuelistState who, CapsuleRecord r)
        {
            var card = r.Banished;
            if (card == null || who.Banished == null || !who.Banished.Contains(card) || card.FaceUp)
            {
                engine.Log($"{r.Capsule.Name}: the banished card is no longer there — nothing is added to the hand.");
                return;
            }

            who.Banished.Remove(card);
            card.FaceUp = true;
            card.Position = BattlePosition.Attack;
            if (!who.Hand.Contains(card))
                who.Hand.Add(card);
            engine.Log($"{r.Capsule.Name}: {card.Name} is added to {who.Name}'s hand.");
        }

        // ───────────────────────────── Pyro Clock of Destiny: turn counts ─────────────────────────────

        /// <summary>
        /// One running effect that counts turns (ygopro effect code 1082946): the card that holds it, whose effect it is,
        /// and its one turn step. <see cref="Choices"/>: the monsters a Steel Scorpion count runs on (one is chosen by
        /// <see cref="Owner"/>); null when there is nothing to choose.
        /// </summary>
        public sealed class TurnCount
        {
            public CardInstance Holder;
            public DuelistState Owner;
            /// <summary>The player helped when this count ends (the AI moves forward its opponent's lock, its own countdown).</summary>
            public DuelistState EndingHelps;
            public List<CardInstance> Choices;
            public Action<CardInstance> Advance;
        }

        /// <summary>
        /// TextEffectRuntime (Lightforce Sword, Deck Devastation Virus): <paramref name="holder"/> holds the turn count kept
        /// on <paramref name="key"/> (the banished card, the player whose draws are checked); <paramref name="owner"/> activated it.
        /// </summary>
        public static void NoteTurnCountHolder(DuelEngine engine, object key, CardInstance holder, DuelistState owner)
        {
            if (engine == null || key == null || holder == null) return;
            var list = StateOf(engine).Holders;
            list.RemoveAll(h => ReferenceEquals(h.Key, key));
            list.Add(new Holder { Key = key, Card = holder, Owner = owner });
        }

        static Holder HolderOf(DuelEngine engine, object key) =>
            engine != null && s_state.TryGetValue(engine, out var st)
                ? st.Holders.LastOrDefault(h => ReferenceEquals(h.Key, key))
                : null;

        /// <summary>
        /// Every running turn count Pyro Clock of Destiny can move forward (Lua IsHasEffect(1082946) over every location):
        /// Swords of Revealing Light on either field, Lightforce Sword's face-down banished cards, Steel Scorpion's marked
        /// monsters and Deck Devastation Virus's draw check. Each Advance runs the step the count's own phase tick runs.
        /// </summary>
        public static List<TurnCount> TurnCounts(DuelEngine engine)
        {
            var list = new List<TurnCount>();
            if (engine == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                var other = engine.OpponentOf(side);
                // Swords of Revealing Light: counts the End Phases of its controller's opponent's turns.
                foreach (var swords in side.SpellTrapsOnField().ToList())
                {
                    if (!DuelEngine.IsSwordsCountdown(swords)) continue;
                    var owner = side;
                    var card = swords;
                    list.Add(new TurnCount
                    {
                        Holder = card, Owner = owner, EndingHelps = other,
                        Advance = _ => engine.AdvanceSwordsCountdown(owner, card)
                    });
                }

                // Lightforce Sword: the face-down banished card counts its owner's Standby Phases back to the hand.
                foreach (var banished in side.Banished?.ToList() ?? new List<CardInstance>())
                {
                    if (!TextEffectRuntime.IsBanishedReturnCountdown(banished)) continue;
                    var h = HolderOf(engine, banished);
                    var pile = side;
                    var card = banished;
                    list.Add(new TurnCount
                    {
                        Holder = h?.Card ?? card, Owner = h?.Owner ?? other, EndingHelps = pile,
                        Advance = _ => TextEffectRuntime.AdvanceBanishedReturnCountdown(engine, pile, card)
                    });
                }

                // Deck Devastation Virus: the victim's draws are checked until the count ends.
                if (side.CrushCardLingerEndsRemaining > 0)
                {
                    var h = HolderOf(engine, side);
                    var victim = side;
                    if (h?.Card != null)
                        list.Add(new TurnCount
                        {
                            Holder = h.Card, Owner = h.Owner ?? other, EndingHelps = victim,
                            Advance = _ => TextEffectRuntime.AdvanceVirusLinger(engine, victim)
                        });
                }
            }

            // Steel Scorpion: one count per marked monster; its owner chooses which one moves forward.
            foreach (var (source, controller, targets) in BattlePhaseEndRuntime.LiveDelayedDestroys(engine))
            {
                var scorpion = source;
                list.Add(new TurnCount
                {
                    Holder = scorpion, Owner = controller, EndingHelps = controller, Choices = targets,
                    Advance = t => BattlePhaseEndRuntime.AdvanceDelayedDestroy(engine, scorpion, t)
                });
            }

            // DcrSpellTrapUtility batch: Final Countdown's player countdown (its card holds the count wherever it is).
            list.AddRange(DcrSpellTrapUtilityRuntime.TurnCounts(engine));

            return list;
        }

        /// <summary>
        /// Pyro Clock of Destiny's resolution (Lua s.activate): <paramref name="who"/> chooses 1 card with a turn count, then
        /// (Steel Scorpion) its owner chooses the marked monster, and that count's step runs once.
        /// </summary>
        public static void ResolvePyroClock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            if (TurnCounts(engine).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no turn count to move forward.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => Holders(engine),
                EffectTargetKind.None,
                "choose the card whose turn count moves forward by 1 turn.",
                list => AiHolder(engine, who, list),
                holder => AdvanceHolder(engine, who, source, holder));
        }

        static List<CardInstance> Holders(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            foreach (var t in TurnCounts(engine))
                if (t.Holder != null && !list.Contains(t.Holder))
                    list.Add(t.Holder);
            return list;
        }

        static CardInstance AiHolder(DuelEngine engine, DuelistState ai, List<CardInstance> list)
        {
            var counts = TurnCounts(engine);
            return list.FirstOrDefault(h => counts.Any(t => ReferenceEquals(t.Holder, h) && ReferenceEquals(t.EndingHelps, ai)))
                   ?? list.FirstOrDefault();
        }

        static void AdvanceHolder(DuelEngine engine, DuelistState who, CardInstance source, CardInstance holder)
        {
            if (holder == null || engine.GameOver) return;
            // A card holding 2+ counts (one Lightforce Sword activated twice) moves its oldest count (residual: no option prompt).
            var count = TurnCounts(engine).FirstOrDefault(t => ReferenceEquals(t.Holder, holder));
            if (count == null)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {holder.Name}'s turn count has already ended.");
                return;
            }

            engine.Log($"{source?.Name ?? "Effect"}: {holder.Name}'s turn count moves forward by 1 turn.");
            if (count.Choices == null || count.Choices.Count == 0)
            {
                count.Advance(null);
                return;
            }

            var chooser = count.Owner ?? who;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, chooser, holder,
                () => TurnCounts(engine).FirstOrDefault(t => ReferenceEquals(t.Holder, holder))?.Choices ??
                      new List<CardInstance>(),
                EffectTargetKind.FaceUpMonsterOnField,
                "choose the monster whose count moves forward by 1 turn.",
                list => list.FirstOrDefault(),
                target =>
                {
                    var now = TurnCounts(engine).FirstOrDefault(t => ReferenceEquals(t.Holder, holder));
                    if (target != null && now?.Choices != null && now.Choices.Contains(target))
                        now.Advance(target);
                });
        }

        // ───────────────────────────── A Cat of Ill Omen / An Owl of Luck ─────────────────────────────

        static bool MatchesKind(CardDef def, string kind) =>
            def != null && kind switch
            {
                "Trap" => def.IsTrap,
                "FieldSpell" => def.IsFieldSpell,
                _ => false
            };

        /// <summary>
        /// FLIP (Lua s.operation): <paramref name="who"/> chooses 1 card of <see cref="EffectClause.DeckPickCardKind"/> in their
        /// Deck. While <see cref="EffectClause.AddToHandInsteadIfNamedOnField"/> is on the field (Duel.IsEnvironment: face-up,
        /// its activation resolved; a negated copy counts) they may add it to the hand instead (the opponent sees it);
        /// otherwise the Deck is shuffled and the card placed on top. No such card: nothing happens.
        /// </summary>
        public static void ResolvePlaceKindOnTop(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Deck == null || clause == null || engine.GameOver) return;
            var kind = clause.DeckPickCardKind;
            if (DeckProxies(engine, who, d => MatchesKind(d, kind)).Count == 0)
            {
                engine.Log($"{source?.Name ?? "Effect"}: no {(kind == "FieldSpell" ? "Field Spell" : kind)} in {who.Name}'s Deck.");
                return;
            }

            var targetKind = kind == "FieldSpell" ? EffectTargetKind.FieldSpellInYourDeck : EffectTargetKind.CardInYourDeck;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source,
                () => DeckProxies(engine, who, d => MatchesKind(d, kind)),
                targetKind,
                $"choose 1 {(kind == "FieldSpell" ? "Field Spell" : kind)} in your Deck to place on top of your Deck.",
                list => list.FirstOrDefault(),
                picked => AfterKindPick(engine, who, source, clause, targetKind, picked));
        }

        static void AfterKindPick(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            EffectTargetKind targetKind, CardInstance picked)
        {
            if (picked == null || who.Deck == null || !who.Deck.Contains(picked.CardId)) return;
            var env = clause.AddToHandInsteadIfNamedOnField;
            if (string.IsNullOrEmpty(env) || !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, env))
            {
                PlaceOnTop(engine, who, source, picked);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskOptionalCardPick(engine, who, source,
                () => who.Deck.Contains(picked.CardId) ? new List<CardInstance> { picked } : new List<CardInstance>(),
                targetKind,
                $"\"{env}\" is on the field: tap {picked.Name} to add it to your hand instead, or CANCEL to place it on " +
                "top of your Deck.",
                list => list.FirstOrDefault(),
                yes =>
                {
                    if (yes != null)
                    {
                        if (MonsterEffects.AddCardIdFromDeckToHand(engine, who, picked.CardId, source?.Name ?? "Effect"))
                            engine.Log($"{engine.OpponentOf(who)?.Name} sees {picked.Name}.");
                        return;
                    }

                    PlaceOnTop(engine, who, source, picked);
                });
        }

        static void PlaceOnTop(DuelEngine engine, DuelistState who, CardInstance source, CardInstance picked)
        {
            var idx = who.Deck.IndexOf(picked.CardId);
            if (idx < 0) return;
            who.Deck.RemoveAt(idx);
            engine.Rng.Shuffle(who.Deck);
            who.Deck.Insert(0, picked.CardId);
            engine.Log($"{source?.Name ?? "Effect"}: {who.Name} shuffles the Deck and places {picked.Name} on top.");
        }

        // ───────────────────────────── Royal Tribute ─────────────────────────────

        /// <summary>
        /// Resolution (Lua s.handesop): every monster in both hands is discarded at once (REASON_EFFECT + REASON_DISCARD),
        /// so "discarded by your opponent's card effect" triggers of the opponent's monsters fire.
        /// </summary>
        public static void ResolveRoyalTribute(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                foreach (var side in new[] { who, opp })
                {
                    if (side?.Hand == null) continue;
                    foreach (var h in side.Hand.Where(c => c?.Def != null && c.Def.IsMonster).ToList())
                    {
                        var gone = MonsterEffects.DiscardFromHandByInstance(side, h, DiscardCause.Effect, who);
                        if (gone == null) continue;
                        engine.Log($"{side.Name} discards {gone.Name} ({source?.Name ?? "Effect"}).");
                        DiscardTriggersRuntime.NoteDiscard(engine, side, gone, who);
                    }
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }
        }
    }
}
