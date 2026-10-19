using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CallsDiceAndSetTriggers" (v163) runtime. Reads compiled programs only — no card-id branches. Every
    /// player choice goes through <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick"/> (declaration tokens in
    /// the ordinary card picker, or the chooser's own monsters), addressed to whichever player makes it, also the card's
    /// controller's opponent; the AI (or a prompt that cannot open now) decides at once. Reveals go to the log.
    /// <list type="bullet">
    /// <item><b>Destroyed while Set</b> (Statue of the Wicked, Dark Coffin; <see cref="EffectClause.RequiresDestroyedWhileFaceDown"/>):
    /// TextEffectRuntime.DestroyCard and the legacy Heavy Storm record each Spell/Trap Card destroyed on the field with
    /// its position (<see cref="NoteSpellTrapDestroying"/>), keyed to the GY trip it is about to make
    /// (<see cref="CardInstance.GyEntryStamp"/>); the mandatory "sent from the field to the GY" trigger applies only
    /// while that record holds (<see cref="WasDestroyedFaceDownThisTrip"/>), whoever destroyed it. Neither card has an
    /// Activate clause, so neither can be activated. Dark Coffin: the opponent picks an available option (2 option
    /// tokens) — 1 random card of their hand is discarded, or they select 1 monster they control and it is destroyed by
    /// Dark Coffin's effect. The AI destroys its least valuable monster when that is worth less than 1000 ATK/DEF or its
    /// hand is empty, else discards.</item>
    /// <item><b>Cobra Jar</b>: its FLIP Token clause carries <see cref="EffectClause.TokenBattleDestroyedDamageToOpponent"/>;
    /// each Token it Summons is bound to the opponent of the resolving player (<see cref="BindTokenBattleBurn"/>), and
    /// DuelEngine.DestroyMonster reports every destroyed Token (<see cref="OnTokenDestroyed"/>): destroyed by battle, the
    /// bound player takes the damage, even after Cobra Jar is gone.</item>
    /// <item><b>Dice Jar</b>: one die-roll event of 2 dice (the controller's, then the opponent's) through
    /// <see cref="CoinDiceInterruptsRuntime.RollDice"/>, so Dice Re-Roll redoes both; equal rolls are rolled again (a new
    /// event). The lower roller takes the other roll x Amount, or MaxRollDamage when the higher roll is a 6.</item>
    /// <item><b>Ordeal of a Traveler</b>: queued with Fairy Box / Jam Defender by
    /// BattleWindowsRuntime.FireOpponentAttackDeclaredTriggers (<see cref="StartOrdeal"/>). Optional: a human controller
    /// picks "Activate" or "Do not activate" (CANCEL declines); the AI always activates. It needs a card in its
    /// controller's hand. At resolution, with the hand not empty and the attacker still on the field, 1 card of the
    /// hand is taken at random (engine.Rng.PickIndex); the attacking player calls Monster / Spell / Trap (the AI calls
    /// the type most common among the defender's cards, like Ominous Fortunetelling); the card is revealed in the log
    /// and a wrong call returns the attacker to the hand. The attack declaration then goes on.</item>
    /// <item><b>Reversal Quiz</b>: cost <see cref="EffectClause.CostSendAllOtherHandAndFieldCardsToGy"/>, paid as the card
    /// is placed (SpellTrapEffects.BeginDeferredChainActivation → <see cref="PayActivationCost"/>), as one event. At
    /// resolution the controller calls the type of the top Deck card (the AI calls the type most common in its own
    /// Deck), it is revealed in the log, a right call exchanges both players' current LP (set: no damage, gain or
    /// LP-change trigger), then the Deck is shuffled. An empty Deck: nothing happens.</item>
    /// <item><b>Reasoning</b>: the opponent declares a Level 1-12 (12 Level tokens). The AI declares the Level most
    /// common among the activator's public monsters that can be Normal Summoned/Set (GY, face-up field, face-up
    /// banished), else 4. The Deck is excavated down to the nearest monster that can be Normal Summoned/Set
    /// (TributeSummonsRuntime.IsNormalSummonableDef); a different Level, a free zone and a legal Special Summon:
    /// it is Special Summoned face-up (a human picks Attack / Defense, the AI Attack unless its DEF is higher) and the
    /// cards above it go to the GY; otherwise every excavated card goes to the GY. No such monster: the Deck is
    /// revealed in the log and shuffled.</item>
    /// </list>
    /// </summary>
    public static class CallsDiceAndSetTriggersRuntime
    {
        /// <summary>ActivationCondition (Reversal Quiz): another card in your hand or on your field to send as the cost.</summary>
        public const string OtherCardInHandOrOnField = "OtherCardInHandOrOnField";

        /// <summary>
        /// ActivationCondition (Reasoning): a free Monster Zone, the player can Special Summon, and a monster that can be
        /// Normal Summoned/Set is in the Deck.
        /// </summary>
        public const string CanExcavateSummonableMonster = "CanExcavateSummonableMonster";

        /// <summary><see cref="CardDef.type"/> of a Level declaration token (Reasoning).</summary>
        public const string LevelTokenKind = "Declaration (Level)";

        /// <summary><see cref="CardDef.type"/> of an option token (Dark Coffin's options, Ordeal's Yes/No, a position).</summary>
        public const string OptionTokenKind = "Declaration (Option)";

        const int MaxRerolls = 64;

        sealed class DestroyRecord
        {
            /// <summary>The <see cref="CardInstance.GyEntryStamp"/> of the GY trip this destruction makes.</summary>
            public int Stamp;
            public bool FaceDown;
            /// <summary>TlmBattleShieldsAndBurnZero batch: the player whose card destroyed it (Lua rp), when known.</summary>
            public DuelistState Destroyer;
        }

        sealed class TokenBurn
        {
            public DuelistState Victim;
            public int Amount;
            public CardInstance Source;
            public string SourceName;
        }

        sealed class OrdealRun
        {
            public CardInstance Card;
            public DuelistState Who;
            public DuelistState Caller;
            public CardInstance Attacker;
            public int AttackerEntry;
            public Action<DuelEngine> Resume;
            /// <summary>A human's prompt opened: the declaration goes on through <see cref="Resume"/> at the end.</summary>
            public bool Paused;
        }

        static readonly ConditionalWeakTable<CardInstance, DestroyRecord> s_destroyed = new();
        static readonly ConditionalWeakTable<CardInstance, TokenBurn> s_tokenBurns = new();

        // ───────────────────────────── helpers ─────────────────────────────

        static CardInstance Token(string kind, string name, int value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = name,
                type = kind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = value,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        static List<CardInstance> TypeTokens() =>
            ContinuousTrapsAndSpiritSupportRuntime.TypeNames.Select(ContinuousTrapsAndSpiritSupportRuntime.TypeToken).ToList();

        static int CallOf(CardInstance chosen) =>
            Array.IndexOf(ContinuousTrapsAndSpiritSupportRuntime.TypeNames, chosen?.Def?.name ?? "");

        static string TypeName(int i) =>
            i >= 0 && i < ContinuousTrapsAndSpiritSupportRuntime.TypeNames.Length
                ? ContinuousTrapsAndSpiritSupportRuntime.TypeNames[i]
                : "?";

        /// <summary>
        /// <paramref name="who"/> chooses 1 of <paramref name="choices"/> (AskCardPick); <paramref name="next"/> gets the
        /// choice. True when a human's prompt is open now and <paramref name="next"/> runs once it is answered.
        /// </summary>
        static bool Ask(DuelEngine engine, DuelistState who, CardInstance source, Func<List<CardInstance>> choices,
            EffectTargetKind kind, string prompt, Func<List<CardInstance>, CardInstance> ai, Action<CardInstance> next)
        {
            var done = false;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, choices, kind, prompt, ai, c =>
            {
                done = true;
                next(c);
            });
            return !done;
        }

        static bool InGy(DuelEngine engine, CardInstance card) =>
            (engine?.Player?.Graveyard.Contains(card) ?? false) || (engine?.Opponent?.Graveyard.Contains(card) ?? false);

        static int Value(CardInstance m) =>
            m == null ? 0 : Math.Max(Math.Max(m.CurrentAtk, m.CurrentDef), Math.Max(m.Def?.atk ?? 0, m.Def?.def ?? 0));

        // ───────────────────────────── destroyed while Set ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.DestroyCard / SpellTrapEffects' legacy Heavy Storm: <paramref name="card"/>, a card in a
        /// Spell &amp; Trap Zone (or the Field Zone), is about to be destroyed and sent to the GY. <paramref name="destroyer"/>:
        /// the player whose card destroys it (TlmBattleShieldsAndBurnZero batch, Kozaky's Self-Destruct Button), when known.
        /// </summary>
        public static void NoteSpellTrapDestroying(CardInstance card, bool wasFaceUp, DuelistState destroyer = null)
        {
            if (card == null) return;
            s_destroyed.Remove(card);
            s_destroyed.Add(card, new DestroyRecord
            {
                Stamp = card.GyEntryStamp + 1, FaceDown = !wasFaceUp, Destroyer = destroyer
            });
        }

        /// <summary>
        /// TlmBattleShieldsAndBurnZero batch: the player who destroyed <paramref name="card"/> while it was face-down, on its
        /// latest GY trip (no later trip since; it may have left the GY after its trigger activated), or null.
        /// </summary>
        public static DuelistState DestroyedFaceDownBy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_destroyed.TryGetValue(card, out var r)) return null;
            return r.FaceDown && card.GyEntryStamp == r.Stamp ? r.Destroyer : null;
        }

        /// <summary>
        /// <paramref name="card"/> is in a GY because it was destroyed while face-down on the field, on its latest trip
        /// there (Lua REASON_DESTROY, IsPreviousLocation(LOCATION_ONFIELD), IsPreviousPosition(POS_FACEDOWN)).
        /// </summary>
        public static bool WasDestroyedFaceDownThisTrip(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_destroyed.TryGetValue(card, out var r)) return false;
            return r.FaceDown && card.GyEntryStamp == r.Stamp && InGy(engine, card);
        }

        // ───────────────────────────── Dark Coffin ─────────────────────────────

        /// <summary><see cref="EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster"/>.</summary>
        public static void ResolveDarkCoffin(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var canDiscard = opp.Hand.Count > 0;
            var canDestroy = opp.MonstersOnField().Any();
            if (!canDiscard && !canDestroy)
            {
                engine.Log($"{source?.Name}: {opp.Name} has no card in hand and no monster — nothing happens.");
                return;
            }

            if (canDiscard && canDestroy)
            {
                var options = new List<CardInstance>
                {
                    Token(OptionTokenKind, "Discard 1 random card from your hand", 0),
                    Token(OptionTokenKind, "Destroy 1 monster you control", 1)
                };
                Ask(engine, opp, source, () => options, EffectTargetKind.None,
                    "your opponent's card effect — choose 1: discard 1 random card from your hand, or select 1 " +
                    "monster you control and destroy it.",
                    list => list.ElementAtOrDefault(AiCoffinDestroys(opp) ? 1 : 0),
                    chosen =>
                    {
                        if (chosen?.Def?.level == 1) CoffinDestroy(engine, who, opp, source);
                        else CoffinDiscard(engine, who, opp, source);
                    });
                return;
            }

            if (canDiscard) CoffinDiscard(engine, who, opp, source);
            else CoffinDestroy(engine, who, opp, source);
        }

        static bool AiCoffinDestroys(DuelistState opp)
        {
            var cheapest = opp.MonstersOnField().Where(m => m != null).OrderBy(Value).FirstOrDefault();
            if (cheapest == null) return false;
            return opp.Hand.Count == 0 || cheapest.IsToken || Value(cheapest) < 1000;
        }

        static void CoffinDiscard(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source)
        {
            if (opp.Hand.Count == 0)
            {
                engine.Log($"{source?.Name}: {opp.Name} has no card in hand to discard.");
                return;
            }

            var i = engine.Rng.PickIndex(opp.Hand.Count);
            if (i < 0 || i >= opp.Hand.Count) i = 0;
            var card = opp.Hand[i];
            // EenDarkWorldDiscards batch: a discard by this card's effect (Electric Snake, the Dark World triggers).
            DiscardTriggersRuntime.BeginScope(engine);
            CardInstance gone;
            try
            {
                gone = MonsterEffects.DiscardFromHandByInstance(opp, card, DiscardCause.Effect, who);
                if (gone != null) DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, who);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            engine.Log($"{source?.Name}: {opp.Name} discards 1 random card ({gone?.Name ?? card?.Name}).");
            engine.NotifyPublic();
        }

        static void CoffinDestroy(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source)
        {
            Ask(engine, opp, source, () => opp.MonstersOnField().Where(m => m != null).ToList(),
                EffectTargetKind.YourMonster,
                "your opponent's card effect — select 1 monster you control to destroy.",
                list => list.OrderBy(Value).FirstOrDefault(),
                chosen =>
                {
                    if (chosen == null || !opp.TryFindMonster(chosen, out _))
                    {
                        engine.Log($"{source?.Name}: no monster to destroy.");
                        return;
                    }

                    engine.Log($"{source?.Name}: {opp.Name} selects {chosen.Name} — it is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, chosen, source, false, who);
                    engine.NotifyPublic();
                });
        }

        // ───────────────────────────── Cobra Jar ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.SummonTokens: <paramref name="token"/> was Summoned by a clause with
        /// <see cref="EffectClause.TokenBattleDestroyedDamageToOpponent"/>; when it is destroyed by battle, the opponent of
        /// <paramref name="summoner"/> (the resolving player) takes <paramref name="amount"/> damage.
        /// </summary>
        public static void BindTokenBattleBurn(DuelEngine engine, CardInstance token, DuelistState summoner, int amount,
            CardInstance source)
        {
            var victim = engine?.OpponentOf(summoner);
            if (token == null || victim == null || amount <= 0) return;
            s_tokenBurns.Remove(token);
            s_tokenBurns.Add(token, new TokenBurn
            {
                Victim = victim,
                Amount = amount,
                Source = source,
                SourceName = source?.Name ?? token.Name
            });
        }

        /// <summary>DuelEngine.DestroyMonster: a Token was destroyed (it leaves play; its binding ends).</summary>
        public static void OnTokenDestroyed(DuelEngine engine, CardInstance token, bool destroyedByBattle)
        {
            if (engine == null || token == null || !s_tokenBurns.TryGetValue(token, out var b)) return;
            s_tokenBurns.Remove(token);
            if (!destroyedByBattle || engine.GameOver || b.Victim == null) return;
            engine.Log($"{b.SourceName}: {token.Name} was destroyed by battle — {b.Victim.Name} takes {b.Amount} damage.");
            engine.ApplyEffectDamage(b.Victim, b.Amount, b.SourceName, b.Source);
        }

        // ───────────────────────────── Dice Jar ─────────────────────────────

        /// <summary><see cref="EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage"/>.</summary>
        public static void ResolveDiceJar(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var per = clause != null && clause.Amount > 0 ? clause.Amount : 500;
            var six = clause != null && clause.MaxRollDamage > 0 ? clause.MaxRollDamage : 6000;
            RollBoth(engine, who, opp, source, per, six, 0);
        }

        static void RollBoth(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source, int per,
            int six, int rerolls)
        {
            if (engine.GameOver) return;
            engine.Log($"{source?.Name}: {who.Name} and {opp.Name} each roll a six-sided die.");
            // dice[0] = the controller's die, dice[1] = the opponent's (Lua TossDice(tp, 1, 1)): one event, so a Dice
            // Re-Roll redoes both. Good for the controller: not lower than the opponent.
            CoinDiceInterruptsRuntime.RollDice(engine, who, source, 2, d => d[0] >= d[1], d =>
            {
                var mine = d[0];
                var theirs = d[1];
                engine.Log($"{source?.Name}: {who.Name} rolls {mine}, {opp.Name} rolls {theirs}.");
                if (engine.GameOver) return;
                if (mine == theirs)
                {
                    if (rerolls >= MaxRerolls)
                    {
                        engine.Log($"{source?.Name}: the rolls keep matching — nothing happens.");
                        return;
                    }

                    engine.Log($"{source?.Name}: the rolls are the same — both players roll again.");
                    RollBoth(engine, who, opp, source, per, six, rerolls + 1);
                    return;
                }

                var loser = mine < theirs ? who : opp;
                var high = Math.Max(mine, theirs);
                var damage = high == 6 ? six : high * per;
                engine.Log($"{source?.Name}: {loser.Name} rolled lower and takes {damage} damage.");
                engine.ApplyEffectDamage(loser, damage, source?.Name, source);
                engine.NotifyPublic();
            });
        }

        // ───────────────────────────── Ordeal of a Traveler ─────────────────────────────

        /// <summary>
        /// BattleWindowsRuntime.RunDeclarationTriggers: the defender's face-up Ordeal of a Traveler at an opponent's attack
        /// declaration. True when a human's prompt paused the declaration; <paramref name="resume"/> continues it once
        /// the effect is over. False when it is over now (or did not activate).
        /// </summary>
        internal static bool StartOrdeal(DuelEngine engine, CardInstance ordeal, DuelistState who,
            Action<DuelEngine> resume)
        {
            var attacker = engine?.DeclaredAttacker;
            var caller = engine?.DeclaredAttackingPlayer;
            if (attacker == null || caller == null || who == null || ordeal == null || ReferenceEquals(caller, who))
                return false;
            // Lua target chk: you have a card in your hand.
            if (who.Hand.Count == 0) return false;
            var run = new OrdealRun
            {
                Card = ordeal,
                Who = who,
                Caller = caller,
                Attacker = attacker,
                AttackerEntry = attacker.FieldEntryStamp,
                Resume = resume
            };
            if (engine.IsHumanControlled(who))
            {
                var choices = new List<CardInstance>
                {
                    Token(OptionTokenKind, $"Activate {ordeal.Name}", 1),
                    Token(OptionTokenKind, "Do not activate", 0)
                };
                if (Ask(engine, who, ordeal, () => choices, EffectTargetKind.None,
                        $"{attacker.Name} declared an attack — activate this card's effect? Your opponent calls the type " +
                        "of 1 random card in your hand; a wrong call returns the attacking monster to the hand. " +
                        "CANCEL = do not activate.",
                        list => list.LastOrDefault(),
                        chosen =>
                        {
                            if (chosen?.Def?.level == 1)
                                ResolveOrdeal(engine, run);
                            else
                            {
                                engine.Log($"{ordeal.Name}: {who.Name} does not activate its effect.");
                                EndOrdeal(engine, run);
                            }
                        }))
                    run.Paused = true;
                return run.Paused;
            }

            ResolveOrdeal(engine, run);
            return run.Paused;
        }

        static bool AttackerStillThere(DuelEngine engine, OrdealRun run)
        {
            var a = run.Attacker;
            var holder = engine.ControllerOf(a);
            return holder != null && holder.TryFindMonster(a, out _) && a.FieldEntryStamp == run.AttackerEntry;
        }

        static void ResolveOrdeal(DuelEngine engine, OrdealRun run)
        {
            engine.Log($"{run.Card.Name}: {run.Who.Name} activates its effect.");
            if (run.Who.Hand.Count == 0)
            {
                engine.Log($"{run.Card.Name}: {run.Who.Name} has no card in hand — no effect.");
                EndOrdeal(engine, run);
                return;
            }

            if (!AttackerStillThere(engine, run))
            {
                engine.Log($"{run.Card.Name}: the attacking monster is no longer on the field — no effect.");
                EndOrdeal(engine, run);
                return;
            }

            var i = engine.Rng.PickIndex(run.Who.Hand.Count);
            if (i < 0 || i >= run.Who.Hand.Count) i = 0;
            var card = run.Who.Hand[i];
            engine.Log($"{run.Card.Name}: {run.Caller.Name} takes 1 random card from {run.Who.Name}'s hand and calls " +
                       "its type.");
            if (Ask(engine, run.Caller, run.Card, TypeTokens, EffectTargetKind.None,
                    "call the type of the card taken at random from your opponent's hand (Monster, Spell or Trap). A " +
                    "wrong call returns your attacking monster to the hand.",
                    list => list.ElementAtOrDefault(ContinuousTrapsAndSpiritSupportRuntime.AiCall(engine, run.Who)) ??
                            list.FirstOrDefault(),
                    chosen => FinishOrdeal(engine, run, card, CallOf(chosen))))
                run.Paused = true;
        }

        static void FinishOrdeal(DuelEngine engine, OrdealRun run, CardInstance card, int call)
        {
            if (call < 0) call = ContinuousTrapsAndSpiritSupportRuntime.AiCall(engine, run.Who);
            var kind = ContinuousTrapsAndSpiritSupportRuntime.CardTypeIndex(card?.Def);
            engine.Log($"{run.Card.Name}: {run.Caller.Name} calls {TypeName(call)}; the card is {card?.Name} " +
                       $"({TypeName(kind)}).");
            if (kind == call)
                engine.Log($"{run.Card.Name}: right call — no effect.");
            else if (!AttackerStillThere(engine, run))
                engine.Log($"{run.Card.Name}: wrong call, but the attacking monster is no longer on the field.");
            else if (ContinuousProtections.IsUnaffectedBy(engine, run.Attacker, run.Card))
                engine.Log($"{run.Attacker.Name} is unaffected by {run.Card.Name}.");
            else
            {
                engine.Log($"{run.Card.Name}: wrong call — {run.Attacker.Name} returns to the hand.");
                engine.ReturnCardToHand(run.Attacker);
            }

            EndOrdeal(engine, run);
        }

        static void EndOrdeal(DuelEngine engine, OrdealRun run)
        {
            engine.NotifyPublic();
            if (run.Paused) run.Resume?.Invoke(engine);
        }

        // ───────────────────────────── Reversal Quiz ─────────────────────────────

        static List<CardInstance> OtherCards(DuelistState who, CardInstance card)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            list.AddRange(who.Hand.Where(h => h != null && !ReferenceEquals(h, card)));
            list.AddRange(who.MonstersOnField().Where(m => !ReferenceEquals(m, card)));
            list.AddRange(who.SpellTrapsOnField().Where(s => !ReferenceEquals(s, card)));
            return list;
        }

        /// <summary>
        /// SpellTrapEffects.BeginDeferredChainActivation (an untargeted activation, as the card is placed): the cost
        /// <see cref="EffectClause.CostSendAllOtherHandAndFieldCardsToGy"/> — every other card in the hand and on the
        /// field goes to the GY together (a cost; the hand cards are sent, not discarded).
        /// </summary>
        public static void PayActivationCost(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog)
        {
            if (engine == null || who == null || card == null || prog == null) return;
            if (!prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && c.CostSendAllOtherHandAndFieldCardsToGy))
                return;
            var hand = who.Hand.Where(h => h != null && !ReferenceEquals(h, card)).ToList();
            var field = OtherCards(who, card).Where(c => !hand.Contains(c)).ToList();
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var h in hand)
                        MonsterEffects.SendFromHandToGraveByInstance(who, h);
                    foreach (var f in field)
                        if (ReferenceEquals(engine.ControllerOf(f), who))
                            engine.SendCardToGrave(who, f);
                });
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }

            engine.Log($"Cost: {who.Name} sends {hand.Count} card(s) from the hand and {field.Count} from the field " +
                       "to the GY.");
        }

        /// <summary>AI call for its own top Deck card: the type most common in its Deck (Monster on a tie).</summary>
        static int AiOwnDeckCall(DuelEngine engine, DuelistState who)
        {
            var counts = new int[3];
            foreach (var id in who.Deck)
            {
                var i = ContinuousTrapsAndSpiritSupportRuntime.CardTypeIndex(engine.Database?.Get(id));
                if (i >= 0) counts[i]++;
            }

            var best = 0;
            for (var i = 1; i < 3; i++)
                if (counts[i] > counts[best]) best = i;
            return best;
        }

        /// <summary><see cref="EffectActionKind.CallTopDeckCardTypeRightExchangesLp"/>.</summary>
        public static void ResolveReversalQuiz(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            if (who.Deck.Count == 0)
            {
                engine.Log($"{source?.Name}: {who.Name} has no card in the Deck — nothing happens.");
                return;
            }

            Ask(engine, who, source, TypeTokens, EffectTargetKind.None,
                "call the type of the card on top of your Deck (Monster, Spell or Trap). A right call exchanges your " +
                "LP with your opponent's.",
                list => list.ElementAtOrDefault(AiOwnDeckCall(engine, who)) ?? list.FirstOrDefault(),
                chosen => FinishReversalQuiz(engine, who, source, CallOf(chosen)));
        }

        static void FinishReversalQuiz(DuelEngine engine, DuelistState who, CardInstance source, int call)
        {
            if (who.Deck.Count == 0) return;
            if (call < 0) call = AiOwnDeckCall(engine, who);
            var top = engine.Database?.Get(who.Deck[0]);
            var kind = ContinuousTrapsAndSpiritSupportRuntime.CardTypeIndex(top);
            engine.Log($"{source?.Name}: {who.Name} calls {TypeName(call)}; the top card of the Deck is " +
                       $"{top?.name ?? "?"} ({TypeName(kind)}).");
            var opp = engine.OpponentOf(who);
            if (kind == call && opp != null)
            {
                var mine = who.LifePoints;
                who.LifePoints = opp.LifePoints;
                opp.LifePoints = mine;
                engine.Log($"{source?.Name}: right call — {who.Name} and {opp.Name} exchange LP " +
                           $"({who.Name} {who.LifePoints}, {opp.Name} {opp.LifePoints}).");
            }
            else
                engine.Log($"{source?.Name}: wrong call — the LP stay.");

            engine.Rng.Shuffle(who.Deck);
            engine.Log($"{who.Name} shuffles the Deck.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Reasoning ─────────────────────────────

        static bool SummonableDeckCard(DuelEngine engine, int id) =>
            TributeSummonsRuntime.IsNormalSummonableDef(engine?.Database?.Get(id));

        /// <summary>
        /// AI declaration: the Level most common among the activator's public monsters that can be Normal Summoned/Set
        /// (GY, face-up on the field, face-up banished); 4 when none is known (the lowest Level on a tie).
        /// </summary>
        static int AiDeclaredLevel(DuelistState activator)
        {
            var counts = new int[13];
            void Count(CardInstance c)
            {
                if (c?.Def == null || !TributeSummonsRuntime.IsNormalSummonableDef(c.Def)) return;
                var lv = c.Def.level;
                if (lv >= 1 && lv <= 12) counts[lv]++;
            }

            if (activator != null)
            {
                foreach (var c in activator.Graveyard) Count(c);
                foreach (var c in activator.MonstersOnField()) if (c.FaceUp) Count(c);
                foreach (var c in activator.Banished) if (c != null && c.FaceUp) Count(c);
            }

            var best = 4;
            for (var lv = 1; lv <= 12; lv++)
                if (counts[lv] > counts[best]) best = lv;
            return best;
        }

        /// <summary><see cref="EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster"/>.</summary>
        public static void ResolveReasoning(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            if (RuleLocksRuntime.AnySpecialSummonLocked(engine, who, null, out var why))
            {
                engine.Log($"{source?.Name}: {why} — nothing happens.");
                return;
            }

            var levels = Enumerable.Range(1, 12).Select(n => Token(LevelTokenKind, $"Level {n}", n)).ToList();
            Ask(engine, opp, source, () => levels, EffectTargetKind.None,
                "declare a monster Level from 1 to 12. Your opponent excavates down to the first monster that can be " +
                "Normal Summoned/Set: if it has the declared Level every excavated card goes to the GY, otherwise it " +
                "is Special Summoned.",
                list => list.ElementAtOrDefault(AiDeclaredLevel(who) - 1) ?? list.FirstOrDefault(),
                chosen => Excavate(engine, who, source, chosen?.Def?.level is int n && n >= 1 && n <= 12
                    ? n
                    : AiDeclaredLevel(who)));
        }

        static void Mill(DuelEngine engine, DuelistState who, int n)
        {
            if (n <= 0) return;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                TextEffectRuntime.SendTopOfDeckToGy(engine, who, n, byEffectOf: who);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }
        }

        static void Excavate(DuelEngine engine, DuelistState who, CardInstance source, int level)
        {
            var opp = engine.OpponentOf(who);
            engine.Log($"{source?.Name}: {opp?.Name} declares Level {level}.");
            var idx = -1;
            for (var i = 0; i < who.Deck.Count; i++)
            {
                if (!SummonableDeckCard(engine, who.Deck[i])) continue;
                idx = i;
                break;
            }

            if (idx < 0)
            {
                var all = string.Join(", ", who.Deck.Select(id => engine.Database?.Get(id)?.name ?? "?"));
                engine.Log($"{source?.Name}: no monster that can be Normal Summoned/Set — {who.Name} reveals the Deck " +
                           $"({all}) and shuffles it.");
                engine.Rng.Shuffle(who.Deck);
                engine.NotifyPublic();
                return;
            }

            var excavated = string.Join(", ", who.Deck.Take(idx + 1).Select(id => engine.Database?.Get(id)?.name ?? "?"));
            engine.Log($"{source?.Name}: {who.Name} excavates {idx + 1} card(s): {excavated}.");
            var id0 = who.Deck[idx];
            var def = engine.Database?.Get(id0);
            var monster = engine.CreateCardInstance(id0);
            string refusal = null;
            if (def == null || def.level == level)
                refusal = $"{def?.name} is Level {level}, the declared Level";
            else if (engine.FirstEmpty(who.MonsterZones) < 0)
                refusal = "no Monster Zone is free";
            else if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, monster, source, SummonOrigin.Deck,
                         requireZone: true))
                refusal = $"{def.name} cannot be Special Summoned";
            if (refusal != null)
            {
                engine.Log($"{source?.Name}: {refusal} — every excavated card goes to the GY.");
                Mill(engine, who, idx + 1);
                engine.NotifyPublic();
                return;
            }

            var aiPos = (def.atk) >= (def.def) ? BattlePosition.Attack : BattlePosition.Defense;
            if (!engine.IsHumanControlled(who))
            {
                SummonExcavated(engine, who, source, idx, monster, aiPos);
                return;
            }

            var positions = new List<CardInstance>
            {
                Token(OptionTokenKind, "Face-up Attack Position", 0),
                Token(OptionTokenKind, "Face-up Defense Position", 1)
            };
            Ask(engine, who, source, () => positions, EffectTargetKind.None,
                $"Special Summon {def.name} — choose its position.",
                list => list.ElementAtOrDefault(aiPos == BattlePosition.Defense ? 1 : 0),
                chosen => SummonExcavated(engine, who, source, idx, monster,
                    chosen?.Def?.level == 1 ? BattlePosition.Defense : BattlePosition.Attack));
        }

        static void SummonExcavated(DuelEngine engine, DuelistState who, CardInstance source, int idx,
            CardInstance monster, BattlePosition pos)
        {
            if (idx < 0 || idx >= who.Deck.Count || who.Deck[idx] != monster.CardId)
            {
                engine.Log($"{source?.Name}: the excavated cards changed — nothing is Special Summoned.");
                return;
            }

            who.Deck.RemoveAt(idx);
            if (!engine.SpecialSummonToField(who, monster, pos, true))
            {
                who.Deck.Insert(idx, monster.CardId);
                engine.Log($"{source?.Name}: {monster.Name} could not be Special Summoned — every excavated card goes to " +
                           "the GY.");
                Mill(engine, who, idx + 1);
                engine.NotifyPublic();
                return;
            }

            engine.Log($"{source?.Name}: {monster.Name} is Special Summoned in face-up " +
                       $"{(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position; the other excavated cards go " +
                       "to the GY.");
            Mill(engine, who, idx);
            engine.NotifyPublic();
        }

        // ───────────────────────────── activation conditions, AI ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False for another batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case OtherCardInHandOrOnField:
                    ok = OtherCards(who, card).Count > 0;
                    reason = "Needs another card in your hand or on your field to send to the GY.";
                    return true;
                case CanExcavateSummonableMonster:
                    ok = engine != null && who != null && engine.FirstEmpty(who.MonsterZones) >= 0 &&
                         !RuleLocksRuntime.AnySpecialSummonLocked(engine, who, null, out _) &&
                         who.Deck.Exists(id => SummonableDeckCard(engine, id));
                    reason = "Needs a free Monster Zone, the ability to Special Summon and a monster in your Deck " +
                             "that can be Normal Summoned/Set.";
                    return true;
            }

            return false;
        }

        /// <summary>
        /// SimpleAi (Main Phase 1): Reasoning whenever it is legal; Reversal Quiz only while the AI has at most half of
        /// the opponent's LP and 3 or fewer other cards to send.
        /// </summary>
        public static bool AiWantsToActivateFromHand(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null || !ai.Hand.Contains(card)) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var act = prog.ClausesFor(EffectTiming.Activate);
            if (act.Exists(c => c != null && c.Action == EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster))
                return true;
            if (act.Exists(c => c != null && c.Action == EffectActionKind.CallTopDeckCardTypeRightExchangesLp))
            {
                var opp = engine.OpponentOf(ai);
                return opp != null && ai.LifePoints * 2 <= opp.LifePoints && OtherCards(ai, card).Count <= 3;
            }

            return false;
        }
    }
}
