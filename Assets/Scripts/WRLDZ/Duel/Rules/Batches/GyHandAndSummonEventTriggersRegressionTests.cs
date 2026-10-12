using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// GyHandAndSummonEventTriggers batch: Night Assailant, Soul Reversal, Emissary of the Afterlife, Backfire, Blessings of
    /// the Nile, "7", The Kick Man and Beckoning Light (<see cref="GyHandAndSummonEventTriggersAtoms"/>,
    /// <see cref="GyHandAndSummonEventTriggersRuntime"/>). Compile shapes, near misses, and mini-duels through
    /// TryActivateSpellTrap (Card Destruction, Dark Hole, Mystical Space Typhoon, the cards themselves), SpecialSummonToField,
    /// TryNormalSummon and the AI.
    /// </summary>
    public static class GyHandAndSummonEventTriggersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int NightAssailant = 16226786;
        const int SoulReversal = 78864369;
        const int Emissary = 75043725;
        const int Backfire = 82705573;
        const int Blessings = 30653113;
        const int Seven = 67048711;
        const int KickMan = 90407382;
        const int BeckoningLight = 16255442;

        const int ManEaterBug = 54652250;     // Flip Effect 450/600
        const int ArmedNinja = 9076207;       // Flip Effect 300/300
        const int DragonPiper = 55763552;     // Flip Effect, FIRE
        const int Celtic = 91152256;          // Normal Lv4 EARTH 1400/1200
        const int FlameManipulator = 34460851; // Normal Lv3 FIRE 900/1000
        const int Burstinatrix = 58932616;    // Normal Lv3 FIRE 1200/800
        const int PetitDragon = 75356564;     // Normal Lv2 WIND 600/700
        const int MysticalElf = 15025844;     // Normal Lv4 LIGHT 800/2000
        const int Gradius = 10992251;         // Normal Lv4 LIGHT 1200/800
        const int Sparkman = 20721928;        // Normal Lv4 LIGHT 1600/1400
        const int CardDestruction = 72892473;
        const int DarkHole = 53129443;
        const int Mst = 5318639;
        const int AxeOfDespair = 40619825;    // +1000 ATK
        const int Pendant = 65169794;         // Black Pendant +500 ATK

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GyHandAndSummonEventTriggers: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            NightAssailantDuels(db, pDeck, aDeck, Check);
            SoulReversalDuels(db, pDeck, aDeck, Check);
            EmissaryDuels(db, pDeck, aDeck, Check);
            BackfireDuels(db, pDeck, aDeck, Check);
            BlessingsDuels(db, pDeck, aDeck, Check);
            SevenDuels(db, pDeck, aDeck, Check);
            KickManDuels(db, pDeck, aDeck, Check);
            BeckoningLightDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            Wipe(engine);
            return engine;
        }

        static void Wipe(DuelEngine engine)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }
        }

        static CardInstance ToGy(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            foreach (var id in ids) who.Deck.Add(id);
        }

        static void Settle(DuelEngine engine)
        {
            Console.WriteLine("[TEST] Settle called");
            DrainCombat(engine);
            FieldSpellEffects.RefreshBoard(engine);
            engine.NotifyPublic();
            // Ensure hand-to-GY triggers fire (GyHandAndSummonEventTriggers batch)
            TextEffects.GyHandAndSummonEventTriggersRuntime.FireQueuedHandSends(engine);
            Console.WriteLine("[TEST] Settle done");
            DrainCombat(engine);
        }

        /// <summary>Activate <paramref name="card"/> for the player and pick <paramref name="target"/> if asked.</summary>
        static bool Activate(DuelEngine engine, CardInstance card, bool fromHand, CardInstance target,
            out List<CardInstance> offered)
        {
            offered = null;
            var p = engine.Player;
            if (!engine.TryActivateSpellTrap(p, card, fromHand)) return false;
            if (target != null && engine.IsAwaitingEffectTarget)
            {
                offered = engine.PendingActivation.LegalTargets.ToList();
                if (!engine.TrySelectEffectTarget(target)) return false;
            }

            Settle(engine);
            return true;
        }

        static string Names(IEnumerable<CardInstance> list) =>
            list == null ? "(no prompt)" : string.Join(",", list.Select(c => c?.Name));

        static CompiledCardProgram CompileText(string text, bool monster) =>
            CardTextEffectCompiler.Compile(monster
                ? new CardDef
                {
                    id = 990181, name = "Near-miss Assailant", type = "Effect Monster", frameType = "effect", race = "Fiend",
                    attribute = "DARK", level = 3, atk = 1000, def = 1000, desc = text
                }
                : new CardDef
                {
                    id = 990182, name = "Near-miss Spell", type = "Spell Card", frameType = "spell", race = "Continuous",
                    desc = text
                });

        static bool FromThisBatch(EffectClause c) =>
            c != null && (c.Timing == EffectTiming.SentFromHandToGy ||
                          c.TargetCardKind == GyHandAndSummonEventTriggersRuntime.FlipKind ||
                          c.Action == EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck ||
                          c.Action == EffectActionKind.InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy ||
                          c.Action == EffectActionKind.GainLpEachTimeDiscardedByOpponentEffect ||
                          c.Action == EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed ||
                          c.Action == EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard ||
                          (c.Action == EffectActionKind.EquipTargetedGyEquipSpellToThis && c.RequiresThisSpecialSummoned));

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id, out bool implemented)
            {
                var def = db.Get(id);
                implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                return def != null ? CardTextEffectCompiler.Compile(def) : null;
            }

            {
                var p = P(NightAssailant, out var impl);
                var c = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.SentFromHandToGy);
                Check("Night Assailant: FLIP destroy + sent from the hand → target 1 Flip monster in your GY (not its name) to hand",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    p.ClauseList.Exists(x => x.Timing == EffectTiming.Flip && x.Action == EffectActionKind.Destroy) &&
                    c != null && c.Action == EffectActionKind.AddFromGyToHand && c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                    c.TargetCardKind == GyHandAndSummonEventTriggersRuntime.FlipKind && c.ExceptThisCard &&
                    c.RequiresTargetChoice && c.IsPsctTarget && !c.IsOptional, Describe(p));
            }
            {
                var p = P(SoulReversal, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Soul Reversal: target 1 Flip monster in your GY, place it on top of the Deck",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.PlaceTargetOnTopOfDeck && c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                    c.TargetCardKind == GyHandAndSummonEventTriggersRuntime.FlipKind && c.RequiresTargetChoice &&
                    TargetingTruthRuntime.ClauseTargets(c), Describe(p));
            }
            {
                var p = P(Emissary, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Emissary of the Afterlife: sent from the field → each player adds a Level 3 or lower Normal Monster",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.SentFromFieldToGy &&
                    c.Action == EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck && c.Amount == 3 &&
                    !c.IsOptional && !c.RequiresTargetChoice, Describe(p));
            }
            {
                var p = P(Backfire, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Backfire: FIRE monster destroyed → 500 damage marker",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy &&
                    c.AttributeFilter == "FIRE" && c.Amount == 500, Describe(p));
            }
            {
                var p = P(Blessings, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Blessings of the Nile: discarded by the opponent's card effect → 1000 LP marker",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.GainLpEachTimeDiscardedByOpponentEffect && c.Amount == 1000, Describe(p));
            }
            {
                var p = P(Seven, out var impl);
                var act = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.Activate);
                var gain = p?.ClauseList.FirstOrDefault(x => x.Timing == EffectTiming.SentFromFieldToGy);
                Check("\"7\": activation with 3 face-up copies → draw 3, destroy all; sent from the field → 700 LP",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 && act != null &&
                    act.Action == EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed && act.NamedCard == "7" &&
                    act.ConditionAmount == 3 && act.Amount == 3 && gain != null &&
                    gain.Action == EffectActionKind.GainLifePoints && gain.Amount == 700 && !gain.IsOptional, Describe(p));
            }
            {
                var p = P(KickMan, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("The Kick Man: Special Summoned, you can equip 1 appropriate Equip Spell from your GY",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.ThisCardSummoned &&
                    c.Action == EffectActionKind.EquipTargetedGyEquipSpellToThis && c.RequiresThisSpecialSummoned &&
                    !c.RequiresThisNormalOrSpecialSummoned && c.IsOptional && c.IsPsctTarget, Describe(p));
            }
            {
                var p = P(BeckoningLight, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Beckoning Light: discard the hand, then add that many LIGHT monsters from the GY",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard &&
                    c.AttributeFilter == "LIGHT" &&
                    c.ActivationCondition == GyHandAndSummonEventTriggersRuntime.HandAndAttributeMonstersInGy &&
                    !TargetingTruthRuntime.ClauseTargets(c), Describe(p));
            }
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string text, bool monster)[]
            {
                // Sent from the field, not the hand.
                ("When this card is sent from the field to the Graveyard: Target 1 Flip Effect Monster in your Graveyard, " +
                 "except this card; return that target to the hand.", true),
                // Any monster, not a Flip monster.
                ("When this card is sent from the hand to the Graveyard: Target 1 monster in your Graveyard, except this card; " +
                 "return that target to the hand.", true),
                // The opponent's GY.
                ("Return 1 Flip Effect Monster from your opponent's Graveyard to the top of your Deck.", false),
                // The bottom of the Deck.
                ("Return 1 Flip Effect Monster from your Graveyard to the bottom of your Deck.", false),
                // Only you add, not each player.
                ("When this card is sent from the field to the Graveyard: Add 1 Level 3 or lower Normal Monster from your Deck " +
                 "to your hand.", true),
                // Destroyed by battle only / damage to you.
                ("If a face-up FIRE monster(s) you control is destroyed by battle: Inflict 500 damage to your opponent.", false),
                ("If a face-up FIRE monster(s) you control is destroyed and sent to the GY: Inflict 500 damage to you.", false),
                // Discarded by your own card's effect.
                ("Each time card(s) are discarded from your hand to the Graveyard by the effect of a card you control, " +
                 "increase your Life Points by 1000 points.", false),
                // A different count, and not "all".
                ("When there are 3 face-up \"7\" cards on your side of the field, draw 3 cards from your Deck. Then destroy " +
                 "1 \"7\" card.", false),
                // Normal Summoned, not Special Summoned.
                ("When this card is Normal Summoned, you can equip 1 appropriate Equip Spell Card from your Graveyard to this " +
                 "card.", true),
                // A flat number, not "for each".
                ("Discard your entire hand, then add 1 LIGHT monster from your Graveyard to your hand.", false),
            };
            var leaked = misses.Where(n =>
            {
                var p = CompileText(n.text, n.monster);
                return p != null && p.ClauseList.Exists(FromThisBatch);
            }).Select(n => n.text).ToList();
            Check("Near misses: look-alike sentences never compile to this batch's clauses", leaked.Count == 0,
                string.Join(" || ", leaked));
        }

        // ───────────────────────────── Night Assailant ─────────────────────────────

        static void NightAssailantDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Card Destruction discards its own Night Assailant (sent from the hand, by its owner's effect):
            // the trigger targets a Flip monster in the GY — another Night Assailant and a non-Flip monster are not offered.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var bug = ToGy(e, p, ManEaterBug);
                var ninja = ToGy(e, p, ArmedNinja);
                var otherAssailant = ToGy(e, p, NightAssailant);
                var celtic = ToGy(e, p, Celtic);
                var assailant = PutInHand(e, p, NightAssailant);
                var cd = PutInHand(e, p, CardDestruction);
                var activated = Activate(e, cd, true, null, out _);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    e.TrySelectEffectTarget(ninja);
                }

                Settle(e);
                var offeredOk = offered != null && offered.Contains(bug) && offered.Contains(ninja) &&
                                !offered.Contains(otherAssailant) && !offered.Contains(celtic) && !offered.Contains(assailant);
                Check("Night Assailant (player): discarded from the hand → a Flip monster in the GY returns to the hand",
                    reached && activated && offeredOk && p.Hand.Contains(ninja) && !p.Graveyard.Contains(ninja) &&
                    p.Graveyard.Contains(bug) && p.Graveyard.Contains(assailant),
                    $"reached={reached} act={activated} offered={Names(offered)} hand=[{Names(p.Hand)}]");
            }

            // The AI's Night Assailant discarded by the player's Card Destruction: the AI picks a Flip monster itself.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bug = ToGy(e, o, ManEaterBug);
                var celtic = ToGy(e, o, Celtic);
                PutInHand(e, o, NightAssailant);
                PutInHand(e, p, Celtic);
                var cd = PutInHand(e, p, CardDestruction);
                var activated = Activate(e, cd, true, null, out _);
                Check("Night Assailant (AI): discarded by the opponent's effect → its Flip monster returns to its hand",
                    reached && activated && o.Hand.Contains(bug) && o.Graveyard.Contains(celtic) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={activated} oHand=[{Names(o.Hand)}] oGy=[{Names(o.Graveyard)}]");
            }

            // Destroyed on the field (not sent from the hand): no trigger; the Flip monster stays in the GY.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var bug = ToGy(e, p, ManEaterBug);
                PlaceMonster(e, p, NightAssailant, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                Check("Night Assailant: destroyed on the field is not \"sent from the hand\" — nothing returns",
                    reached && activated && p.Graveyard.Contains(bug) && !p.Hand.Contains(bug) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={activated} hand=[{Names(p.Hand)}]");
            }
        }

        // ───────────────────────────── Soul Reversal ─────────────────────────────

        static void SoulReversalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var bug = ToGy(e, p, ManEaterBug);
                var ninja = ToGy(e, p, ArmedNinja);
                var celtic = ToGy(e, p, Celtic);
                var piper = ToGy(e, o, DragonPiper);
                var sr = PutInHand(e, p, SoulReversal);
                var activated = Activate(e, sr, true, ninja, out var offered);
                var offeredOk = offered != null && offered.Contains(bug) && offered.Contains(ninja) &&
                                !offered.Contains(celtic) && !offered.Contains(piper);
                Check("Soul Reversal: only your GY's Flip monsters are offered; the target goes on top of the Deck",
                    reached && activated && offeredOk && !p.Graveyard.Contains(ninja) && p.Deck.Count > 0 &&
                    p.Deck[0] == ArmedNinja && p.Graveyard.Contains(bug) && o.Graveyard.Contains(piper),
                    $"reached={reached} act={activated} offered={Names(offered)} top={(p.Deck.Count > 0 ? p.Deck[0] : 0)}");
            }

            // No Flip monster in your GY (only in the opponent's): Soul Reversal cannot be activated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, Celtic);
                ToGy(e, e.Opponent, DragonPiper);
                var sr = PutInHand(e, p, SoulReversal);
                var can = e.CanActivateSpellTrap(p, sr, fromHand: true);
                Check("Soul Reversal: no Flip monster in your GY — it cannot be activated", reached && !can,
                    $"reached={reached} can={can}");
            }
        }

        // ───────────────────────────── Emissary of the Afterlife ─────────────────────────────

        static void EmissaryDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Emissary destroyed by Dark Hole: the player picks among its Level 3 or lower Normal Monsters (not
            // the Level 4 one, not the Effect Monster); the AI picks its own best one; both are added.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Celtic, FlameManipulator, ManEaterBug, PetitDragon);
                SetDeck(o, Celtic, PetitDragon, Burstinatrix);
                PlaceMonster(e, p, Emissary, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    var petit = offered.FirstOrDefault(c => c.CardId == PetitDragon);
                    e.TrySelectEffectTarget(petit);
                }

                Settle(e);
                var offeredOk = offered != null && offered.Count == 2 && offered.Exists(c => c.CardId == PetitDragon) &&
                                offered.Exists(c => c.CardId == FlameManipulator);
                Check("Emissary of the Afterlife: each player adds a Level 3 or lower Normal Monster from their own Deck",
                    reached && activated && offeredOk && p.Hand.Exists(c => c.CardId == PetitDragon) &&
                    !p.Deck.Contains(PetitDragon) && p.Deck.Contains(FlameManipulator) &&
                    o.Hand.Exists(c => c.CardId == Burstinatrix) && !o.Deck.Contains(Burstinatrix) &&
                    o.Deck.Contains(PetitDragon) && o.Deck.Contains(Celtic),
                    $"reached={reached} act={activated} offered={Names(offered)} pHand=[{Names(p.Hand)}] oHand=[{Names(o.Hand)}]");
            }

            // The AI's Emissary: the AI picks first; the player then picks from its own Deck; a player with none adds nothing.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Celtic, ManEaterBug);
                SetDeck(o, FlameManipulator, PetitDragon);
                PlaceMonster(e, o, Emissary, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                Check("Emissary of the Afterlife (AI): it adds its best one; the player has none and adds nothing",
                    reached && activated && o.Hand.Count == 1 && o.Hand[0].CardId == FlameManipulator &&
                    p.Hand.Count == 0 && p.Deck.Count == 2 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={activated} pHand=[{Names(p.Hand)}] oHand=[{Names(o.Hand)}]");
            }
        }

        // ───────────────────────────── Backfire ─────────────────────────────

        static void BackfireDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Dark Hole destroys two face-up FIRE monsters the player controls: one event, 500 damage once. The opponent's FIRE
            // monster does not count.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, p, Backfire, 0);
                PlaceMonster(e, p, FlameManipulator, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Burstinatrix, 1, BattlePosition.Attack, true);
                PlaceMonster(e, o, FlameManipulator, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                Check("Backfire: two face-up FIRE monsters destroyed together — 500 damage to the opponent, once",
                    reached && activated && o.LifePoints == 7500 && p.LifePoints == 8000,
                    $"reached={reached} act={activated} pLP={p.LifePoints} oLP={o.LifePoints}");
            }

            // A face-down FIRE monster and a face-up non-FIRE monster destroyed: no damage.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, p, Backfire, 0);
                PlaceMonster(e, p, FlameManipulator, 0, BattlePosition.Defense, false);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                Check("Backfire: a face-down FIRE monster or a non-FIRE monster destroyed — no damage",
                    reached && activated && o.LifePoints == 8000, $"reached={reached} act={activated} oLP={o.LifePoints}");
            }

            // The opponent's Backfire watches the opponent's monsters: the player's destroyed FIRE monster does not trigger it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, o, Backfire, 0);
                PlaceMonster(e, p, FlameManipulator, 0, BattlePosition.Attack, true);
                var dh = PutInHand(e, p, DarkHole);
                var activated = Activate(e, dh, true, null, out _);
                Check("Backfire: the opponent's FIRE monster is not \"you control\" — no damage",
                    reached && activated && p.LifePoints == 8000 && o.LifePoints == 8000,
                    $"reached={reached} act={activated} pLP={p.LifePoints} oLP={o.LifePoints}");
            }
        }

        // ───────────────────────────── Blessings of the Nile ─────────────────────────────

        static void BlessingsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Card Destruction: the opponent discards 2 cards by the player's card effect (+1000 once); the player's
            // own Blessings sees only its own effect's discards (no LP).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, p, Blessings, 0);
                FaceUpSpell(e, o, Blessings, 0);
                PutInHand(e, p, Celtic);
                PutInHand(e, o, Celtic);
                PutInHand(e, o, PetitDragon);
                var cd = PutInHand(e, p, CardDestruction);
                var activated = Activate(e, cd, true, null, out _);
                Check("Blessings of the Nile: discarded by the opponent's card effect — 1000 LP once; your own effect — none",
                    reached && activated && o.LifePoints == 9000 && p.LifePoints == 8000,
                    $"reached={reached} act={activated} pLP={p.LifePoints} oLP={o.LifePoints}");
            }
        }

        // ───────────────────────────── "7" ─────────────────────────────

        static void SevenDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The third "7" is activated with two face-up copies: draw 3, then every face-up "7" on the field is destroyed
            // (the opponent's too); each one gains its controller 700 LP.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var s1 = FaceUpSpell(e, p, Seven, 0);
                var s2 = FaceUpSpell(e, p, Seven, 1);
                var so = FaceUpSpell(e, o, Seven, 0);
                var s3 = PutInHand(e, p, Seven);
                var deckBefore = p.Deck.Count;
                var activated = Activate(e, s3, true, null, out _);
                var gone = new[] { s1, s2, s3 }.All(s => p.Graveyard.Contains(s)) && o.Graveyard.Contains(so);
                Check("\"7\": the third copy's activation draws 3, then destroys every face-up \"7\" (700 LP each)",
                    reached && activated && p.Hand.Count == 3 && p.Deck.Count == deckBefore - 3 && gone &&
                    p.LifePoints == 8000 + 2100 && o.LifePoints == 8700,
                    $"reached={reached} act={activated} hand={p.Hand.Count} deck={deckBefore}->{p.Deck.Count} gone={gone} " +
                    $"pLP={p.LifePoints} oLP={o.LifePoints}");
            }

            // Only two copies: nothing happens; one destroyed by Mystical Space Typhoon gains 700.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var s1 = FaceUpSpell(e, p, Seven, 0);
                var s2 = PutInHand(e, p, Seven);
                var activated = Activate(e, s2, true, null, out _);
                var onField = p.TryFindSpellTrap(s1, out _) && p.TryFindSpellTrap(s2, out _) && s2.FaceUp;
                var noDraw = p.Hand.Count == 0;
                var mst = PutInHand(e, p, Mst);
                var mstOk = Activate(e, mst, true, s1, out _);
                Check("\"7\": with fewer than 3 copies its activation does nothing; sent from the field it gains 700 LP",
                    reached && activated && onField && noDraw && mstOk && p.Graveyard.Contains(s1) && p.LifePoints == 8700,
                    $"reached={reached} act={activated} onField={onField} hand={p.Hand.Count} mst={mstOk} LP={p.LifePoints}");
            }
        }

        // ───────────────────────────── The Kick Man ─────────────────────────────

        static void KickManDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Special Summoned for the player: an appropriate Equip Spell from the GY is equipped to it.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var axe = ToGy(e, p, AxeOfDespair);
                var pendant = ToGy(e, p, Pendant);
                var kick = e.CreateCardInstance(KickMan);
                var summoned = e.SpecialSummonToField(p, kick, BattlePosition.Attack, true);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    e.TrySelectEffectTarget(axe);
                }

                Settle(e);
                Check("The Kick Man (player): Special Summoned — the chosen Equip Spell is equipped from the GY",
                    reached && summoned && offered != null && offered.Contains(axe) && offered.Contains(pendant) &&
                    axe.EquippedTo == kick && kick.CurrentAtk == 2300 && p.Graveyard.Contains(pendant),
                    $"reached={reached} ss={summoned} offered={Names(offered)} eq={axe.EquippedTo?.Name} atk={kick.CurrentAtk}");
            }

            // The AI's Kick Man, Special Summoned: it equips its best Equip Spell.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var axe = ToGy(e, o, AxeOfDespair);
                var pendant = ToGy(e, o, Pendant);
                var kick = e.CreateCardInstance(KickMan);
                var summoned = e.SpecialSummonToField(o, kick, BattlePosition.Attack, true);
                Settle(e);
                Check("The Kick Man (AI): Special Summoned — equips the best Equip Spell from its GY",
                    reached && summoned && axe.EquippedTo == kick && o.Graveyard.Contains(pendant),
                    $"reached={reached} ss={summoned} eq={axe.EquippedTo?.Name}");
            }

            // Normal Summoned: no trigger.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var axe = ToGy(e, p, AxeOfDespair);
                var kick = PutInHand(e, p, KickMan);
                var summoned = e.TryNormalSummon(p, kick, asSet: false);
                var prompted = e.IsAwaitingEffectTarget;
                Settle(e);
                Check("The Kick Man: a Normal Summon does not equip anything",
                    reached && summoned && !prompted && p.Graveyard.Contains(axe) && axe.EquippedTo == null,
                    $"reached={reached} ns={summoned} prompted={prompted}");
            }
        }

        // ───────────────────────────── Beckoning Light ─────────────────────────────

        static void BeckoningLightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // 2 cards in hand, 3 LIGHT monsters in the GY: the hand is discarded, then the player picks exactly 2.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var elf = ToGy(e, p, MysticalElf);
                var gradius = ToGy(e, p, Gradius);
                var spark = ToGy(e, p, Sparkman);
                var celticGy = ToGy(e, p, Celtic);
                var h1 = PutInHand(e, p, Celtic);
                var h2 = PutInHand(e, p, ManEaterBug);
                var bl = PlaceSetTrap(e, p, BeckoningLight, 0);
                var activated = e.TryActivateSpellTrap(p, bl, fromHand: false);
                DrainCombat(e);
                var picks = 0;
                List<CardInstance> firstOffer = null;
                for (var i = 0; i < 4 && e.IsAwaitingEffectTarget; i++)
                {
                    var offer = e.PendingActivation.LegalTargets.ToList();
                    firstOffer ??= offer;
                    var want = offer.Contains(spark) ? spark : offer.Contains(gradius) ? gradius : offer.FirstOrDefault();
                    if (!e.TrySelectEffectTarget(want)) break;
                    picks++;
                    DrainCombat(e);
                }

                Settle(e);
                Check("Beckoning Light: discard the hand (2), then add exactly 2 LIGHT monsters of your choice",
                    reached && activated && picks == 2 && firstOffer != null && firstOffer.Count == 3 &&
                    !firstOffer.Contains(celticGy) && p.Hand.Count == 2 && p.Hand.Contains(spark) && p.Hand.Contains(gradius) &&
                    p.Graveyard.Contains(elf) && p.Graveyard.Contains(h1) && p.Graveyard.Contains(h2),
                    $"reached={reached} act={activated} picks={picks} offer={Names(firstOffer)} hand=[{Names(p.Hand)}]");
            }

            // 3 cards in hand but only 2 LIGHT monsters in the GY: it cannot be activated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, MysticalElf);
                ToGy(e, p, Gradius);
                PutInHand(e, p, Celtic);
                PutInHand(e, p, Celtic);
                PutInHand(e, p, ManEaterBug);
                var bl = PlaceSetTrap(e, p, BeckoningLight, 0);
                var can = e.CanActivateSpellTrap(p, bl, fromHand: false);
                p.Hand.Clear();
                var canEmpty = e.CanActivateSpellTrap(p, bl, fromHand: false);
                Check("Beckoning Light: fewer LIGHT monsters than cards in hand, or an empty hand — it cannot be activated",
                    reached && !can && !canEmpty, $"reached={reached} can={can} canEmpty={canEmpty}");
            }
        }
    }
}
