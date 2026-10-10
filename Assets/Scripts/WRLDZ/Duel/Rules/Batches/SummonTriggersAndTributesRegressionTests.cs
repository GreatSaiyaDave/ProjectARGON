using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SummonTriggersAndTributes" (v163, PGD): Byser Shock, King Tiger Wanghu, Moisture Creature, Dark Dust
    /// Spirit, Gravekeeper's Chief (the "only 1 face-up" rule and its Necrovalley immunity), Jowls of Dark Demise and
    /// Metamorphosis. Compile shapes, mini-duels through the real entry points (TryNormalSummon, TryFlipSummon,
    /// SpecialSummonToField, TryActivateSpellTrap, TrySelectEffectTarget, TryAttack) and near misses.
    /// Runtime: <see cref="SummonTriggersAndTributesRuntime"/>.
    /// </summary>
    public static class SummonTriggersAndTributesRegressionTests
    {
        const int ByserShock = 17597059;
        const int KingTiger = 83986578;       // Beast Lv4 1700
        const int Moisture = 75285069;        // Fairy Lv9
        const int DarkDust = 89111398;        // Spirit Zombie Lv6 2200/1800
        const int Chief = 62473983;           // Spellcaster Lv5
        const int Jowls = 5257687;            // Flip Lv2
        const int Metamorphosis = 46411259;

        const int Necrovalley = 47355498;
        const int GkSpy = 24317029;           // "Gravekeeper's" Lv4
        const int ChangeOfHeart = 4031928;
        const int MirrorForce = 44095762;
        const int PotOfGreed = 55144522;
        const int Celtic = 91152256;          // Lv4 1400
        const int MysticalElf = 15025844;     // Lv4 800/2000
        const int GiantSoldier = 13039848;    // Lv3 1300/2000
        const int SummonedSkull = 70781052;   // Lv6 2500
        const int DarkMagician = 46986414;    // Lv7 2500
        const int GaiaChampion = 66889139;    // Fusion Lv7 2600
        const int ThousandDragon = 41462083;  // Fusion Lv7 2400
        const int DarkFlareKnight = 13722870; // Fusion Lv6, no summoning condition
        const int FlameWingman = 35809262;    // Fusion Lv6, "Must be Fusion Summoned"
        const int TorrentialTribute = 53582587;
        const int AmazonessTiger = 10979723;  // "You can only control 1 "Amazoness Tiger"" (no "face-up")

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("SummonTriggersAndTributes: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);
            ByserShockDuels(db, pDeck, aDeck, Check);
            KingTigerDuels(db, pDeck, aDeck, Check);
            MoistureDuels(db, pDeck, aDeck, Check);
            DarkDustDuels(db, pDeck, aDeck, Check);
            UniqueRuleDuels(db, pDeck, aDeck, Check);
            NecrovalleyDuels(db, pDeck, aDeck, Check);
            JowlsDuels(db, pDeck, aDeck, Check);
            MetamorphosisDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "SummonTriggersAndTributes probe", type = type,
                frameType = type.StartsWith("Spell") ? "spell" : "effect",
                race = type.StartsWith("Spell") ? "Normal" : "Warrior",
                attribute = type.StartsWith("Spell") ? null : "EARTH",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static DuelEngine Clean(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.Player.Graveyard.Clear();
            engine.Opponent.Graveyard.Clear();
            return engine;
        }

        /// <summary>The Player's Main Phase 1 on turn 3+ with an empty board, hands and GYs.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            return Clean(engine);
        }

        /// <summary>The opponent's (AI's) Main Phase 1 with an empty board, hands and GYs.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            return Clean(engine);
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static CardInstance Down(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        static CardInstance InGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance FieldSpell(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        static bool PickIfAwaiting(DuelEngine e, CardInstance pick)
        {
            if (!e.IsAwaitingEffectTarget) return true;
            var legal = e.PendingActivation.LegalTargets.FirstOrDefault(t => ReferenceEquals(t, pick)) ??
                        e.PendingActivation.LegalTargets.FirstOrDefault(t => t.CardId == pick.CardId);
            return legal != null && e.TrySelectEffectTarget(legal);
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            CompiledCardProgram Prog(int id) => db.TryGet(id, out var d) ? CardTextEffectCompiler.Compile(d) : null;
            bool Implemented(int id) => db.TryGet(id, out var d) && CardEffectStatus.Classify(d) == CardEffectStatusKind.Implemented;

            var byser = Prog(ByserShock);
            Check("Byser Shock: any Summon → return every Set card on the field (one clause, no target)",
                byser != null && byser.FullyCompiled && Implemented(ByserShock) && byser.ClauseList.Count == 1 &&
                byser.ClauseList[0].Timing == EffectTiming.ThisCardSummoned &&
                byser.ClauseList[0].Action == EffectActionKind.ReturnAllSetCardsOnFieldToHand &&
                !byser.ClauseList[0].RequiresTargetChoice && !byser.ClauseList[0].RequiresThisNormalOrFlipSummoned &&
                !byser.ClauseList[0].RequiresThisSpecialSummoned, Describe(byser));

            var tiger = Prog(KingTiger);
            Check("King Tiger Wanghu: a face-up host marker, ATK ceiling 1400",
                tiger != null && tiger.FullyCompiled && Implemented(KingTiger) && tiger.ClauseList.Count == 1 &&
                tiger.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                tiger.ClauseList[0].Action == EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned &&
                tiger.ClauseList[0].ConditionAmount == 1400, Describe(tiger));

            var moist = Prog(Moisture);
            Check("Moisture Creature: Tribute Summon by its 3-Tribute procedure → destroy the opponent's Spells/Traps",
                moist != null && moist.FullyCompiled && Implemented(Moisture) && moist.ClauseList.Count == 1 &&
                moist.ClauseList[0].Timing == EffectTiming.ThisCardSummoned &&
                moist.ClauseList[0].Action == EffectActionKind.DestroyAllOppSpellTraps &&
                moist.ClauseList[0].RequiresThisTributeSummoned &&
                moist.ClauseList[0].RequiresThisTributeSummonedByTributingExactly == 3, Describe(moist));

            var dust = Prog(DarkDust);
            Check("Dark Dust Spirit: Spirit return + NS/flip → destroy all other face-up monsters (no Activate clause)",
                dust != null && dust.FullyCompiled && Implemented(DarkDust) && dust.ClauseList.Count == 2 &&
                dust.ClauseList.Exists(c => c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand) &&
                dust.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardSummoned &&
                                            c.Action == EffectActionKind.DestroyAllFaceUpMonstersOnField &&
                                            c.ExceptThisCard && c.RequiresThisNormalOrFlipSummoned &&
                                            c.AlsoWhenFlippedFaceUp && !c.IsOptional) &&
                !dust.ClauseList.Exists(c => c.Timing == EffectTiming.Activate), Describe(dust));

            var chief = Prog(Chief);
            Check("Gravekeeper's Chief: the unique rule, the Necrovalley immunity and the Tribute Summon revival",
                chief != null && chief.FullyCompiled && Implemented(Chief) && chief.ClauseList.Count == 3 &&
                chief.ClauseList.Exists(c => c.Action == EffectActionKind.YouCanOnlyControlOneFaceUpNamed &&
                                             c.NamedCard == "Gravekeeper's Chief") &&
                chief.ClauseList.Exists(c => c.Action == EffectActionKind.YourGyUnaffectedByNamedCard &&
                                             c.NamedCard == "Necrovalley" && string.IsNullOrEmpty(c.UnaffectedByNamedCard)) &&
                chief.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardSummoned &&
                                             c.Action == EffectActionKind.SpecialSummonFromGy &&
                                             c.RequiresThisTributeSummoned && c.IsOptional), Describe(chief));

            var jowls = Prog(Jowls);
            Check("Jowls of Dark Demise: FLIP take control of any opponent's monster + direct attack grant",
                jowls != null && jowls.FullyCompiled && Implemented(Jowls) && jowls.ClauseList.Count == 1 &&
                jowls.ClauseList[0].Timing == EffectTiming.Flip &&
                jowls.ClauseList[0].Action == EffectActionKind.TakeControlUntilEndPhase &&
                jowls.ClauseList[0].Zone == EffectZoneFilter.OppMonsters && jowls.ClauseList[0].IsPsctTarget &&
                jowls.ClauseList[0].TakenMonsterCanAttackDirectlyThisTurn, Describe(jowls));

            var meta = Prog(Metamorphosis);
            Check("Metamorphosis: Tribute 1 (Level recorded) → Extra Deck Fusion of that Level, no target",
                meta != null && meta.FullyCompiled && Implemented(Metamorphosis) && meta.ClauseList.Count == 1 &&
                meta.ClauseList[0].Action == EffectActionKind.SpecialSummonFusionFromExtra &&
                meta.ClauseList[0].RequiresTributeCount == 1 && meta.ClauseList[0].MatchTributedLevel &&
                !meta.ClauseList[0].RequiresTargetChoice, Describe(meta));

            Check("Resolution kinds are mapped (no UniqueException)",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.ReturnAllSetCardsOnFieldToHand }) ==
                EffectResolutionKind.ReturnToHand &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned }) ==
                EffectResolutionKind.Destroy &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.YouCanOnlyControlOneFaceUpNamed }) ==
                EffectResolutionKind.Protection &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.YourGyUnaffectedByNamedCard }) ==
                EffectResolutionKind.Protection);
        }

        // ───────────────────────── near misses ─────────────────────────

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
                p != null && p.ClauseList.Exists(c => c != null && f(c));

            var byser = CompileText(90163001, "Effect Monster",
                "If this card is Summoned: Return all Set cards your opponent controls to the hand.");
            Check("Near-miss: a one-sided Set return is not Byser Shock's clause",
                !Has(byser, c => c.Action == EffectActionKind.ReturnAllSetCardsOnFieldToHand), Describe(byser));

            var tiger = CompileText(90163002, "Effect Monster",
                "When a monster(s) with 1400 or less ATK is Normal Summoned or Special Summoned: Destroy those monsters " +
                "with 1500 or less ATK. This card must be face-up on the field to activate and to resolve this effect.");
            Check("Near-miss: King Tiger's sentence with two different ATK ceilings does not compile",
                !Has(tiger, c => c.Action == EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned),
                Describe(tiger));

            var moist = CompileText(90163003, "Effect Monster",
                "If you Tribute Summon this monster by Tributing 3 monsters on the field, destroy all Spell and Trap " +
                "Cards on the field.");
            Check("Near-miss: a both-sides Moisture Creature wipe is not compiled to the opponent-only clause",
                !Has(moist, c => c.RequiresThisTributeSummonedByTributingExactly > 0), Describe(moist));

            var dust = CompileText(90163004, "Effect Monster",
                "When this card is Normal Summoned or flipped face-up: Destroy all other face-up monsters your opponent " +
                "controls.");
            Check("Near-miss: an opponent-only \"other face-up\" wipe is not Dark Dust Spirit's clause",
                !Has(dust, c => c.Action == EffectActionKind.DestroyAllFaceUpMonstersOnField && c.ExceptThisCard),
                Describe(dust));

            Check("Near-miss: Amazoness Tiger's \"only control 1\" (no \"face-up\") is not this batch's rule",
                db.TryGet(AmazonessTiger, out var amz) &&
                !Has(CardTextEffectCompiler.Compile(amz), c => c.Action == EffectActionKind.YouCanOnlyControlOneFaceUpNamed),
                Describe(amz != null ? CardTextEffectCompiler.Compile(amz) : null));

            var immune = CompileText(90163005, "Effect Monster", "Your GY is unaffected by \"Gravekeeper's Servant\".");
            Check("Near-miss: \"unaffected by\" another card name is not the Necrovalley immunity",
                !Has(immune, c => c.Action == EffectActionKind.YourGyUnaffectedByNamedCard), Describe(immune));

            var jowls = CompileText(90163006, "Flip Effect Monster",
                "FLIP: Take control of 1 monster on your opponent's side of the field until the end of the turn that " +
                "this card's effect is activated.");
            Check("Near-miss: the take-control sentence without the direct attack is not Jowls' clause",
                !Has(jowls, c => c.TakenMonsterCanAttackDirectlyThisTurn), Describe(jowls));

            var meta = CompileText(90163007, "Spell Card",
                "Tribute 1 monster. Special Summon 1 Fusion Monster from your Extra Deck with a higher Level than the " +
                "Tributed monster.");
            Check("Near-miss: a different-Level Metamorphosis is not FullyCompiled",
                meta == null || !meta.FullyCompiled ||
                !Has(meta, c => c.Action == EffectActionKind.SpecialSummonFusionFromExtra && c.MatchTributedLevel),
                Describe(meta));
        }

        // ───────────────────────── Byser Shock ─────────────────────────

        static void ByserShockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var tribute = Up(e, p, Celtic, 0);
            var ownSet = Down(e, p, MysticalElf, 1);
            var ownSetSpell = PlaceSetTrap(e, p, PotOfGreed, 0);
            var oppSet = Down(e, o, Celtic, 0);
            var oppSkull = Up(e, o, SummonedSkull, 1);
            var oppTrap = PlaceSetTrap(e, o, MirrorForce, 0);
            var byser = PutInHand(e, p, ByserShock);
            e.PendingTributes.Add(tribute);
            var ok = e.TryNormalSummon(p, byser, asSet: false);
            PassAll(e);
            Check("Byser Shock: Tribute Summoned → every Set card on both fields returns to its owner's hand",
                reached && ok && OnField(p, byser) && p.Hand.Contains(ownSet) && p.Hand.Contains(ownSetSpell) &&
                o.Hand.Contains(oppSet) && o.Hand.Contains(oppTrap) && OnField(o, oppSkull) &&
                !p.TryFindSpellTrap(ownSetSpell, out _) && !o.TryFindSpellTrap(oppTrap, out _),
                $"reached={reached} ok={ok} byser={OnField(p, byser)} elf={p.Hand.Contains(ownSet)} " +
                $"pot={p.Hand.Contains(ownSetSpell)} oppSet={o.Hand.Contains(oppSet)} mf={o.Hand.Contains(oppTrap)}");

            // Special Summon fires it too (EVENT_SPSUMMON_SUCCESS).
            var e2 = PlayerMain(db, pDeck, aDeck, out var reached2);
            var oppSet2 = Down(e2, e2.Opponent, Celtic, 0);
            var byser2 = e2.CreateCardInstance(ByserShock);
            var ss = e2.SpecialSummonToField(e2.Player, byser2, BattlePosition.Attack, true);
            PassAll(e2);
            Check("Byser Shock: Special Summoned → the Set monster returns too",
                reached2 && ss && e2.Opponent.Hand.Contains(oppSet2) && OnField(e2.Player, byser2),
                $"reached={reached2} ss={ss} hand={e2.Opponent.Hand.Contains(oppSet2)}");
        }

        // ───────────────────────── King Tiger Wanghu ─────────────────────────

        static void KingTigerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Up(e, e.Opponent, KingTiger, 0);
                var celtic = PutInHand(e, p, Celtic);
                var ns = e.TryNormalSummon(p, celtic, asSet: false);
                PassAll(e);
                var destroyedNs = !OnField(p, celtic) && p.Graveyard.Contains(celtic);
                // A Special Summon of a monster above the ceiling stays.
                var skull = e.CreateCardInstance(SummonedSkull);
                var ss = e.SpecialSummonToField(p, skull, BattlePosition.Attack, true);
                PassAll(e);
                // A Special Summon at the ceiling is destroyed.
                var elf = e.CreateCardInstance(MysticalElf);
                var ss2 = e.SpecialSummonToField(p, elf, BattlePosition.Attack, true);
                PassAll(e);
                Check("King Tiger Wanghu: the opponent's Normal / Special Summons of 1400 or less ATK are destroyed",
                    reached && ns && destroyedNs && ss && OnField(p, skull) && ss2 && !OnField(p, elf) &&
                    p.Graveyard.Contains(elf),
                    $"reached={reached} ns={ns} destroyedNs={destroyedNs} ss={ss} skull={OnField(p, skull)} ss2={ss2} " +
                    $"elf={OnField(p, elf)}");
            }

            {
                // A Flip Summon is not answered; a Set is not a Summon; its own controller's Summons are.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var tiger = Up(e, p, KingTiger, 0);
                var set = Down(e, p, Celtic, 1);
                var flip = e.TryFlipSummon(p, set);
                PassAll(e);
                var elf = PutInHand(e, p, MysticalElf);
                var setNs = e.TryNormalSummon(p, elf, asSet: true);
                PassAll(e);
                var giant = e.CreateCardInstance(GiantSoldier);
                var ss = e.SpecialSummonToField(p, giant, BattlePosition.Attack, true);
                PassAll(e);
                Check("King Tiger Wanghu: Flip Summon and Set ignored; its controller's own Special Summon destroyed",
                    reached && flip && OnField(p, set) && set.FaceUp && setNs && OnField(p, elf) && ss &&
                    !OnField(p, giant) && OnField(p, tiger),
                    $"reached={reached} flip={flip} set={OnField(p, set)} setNs={setNs} elf={OnField(p, elf)} ss={ss} " +
                    $"giant={OnField(p, giant)}");
            }

            {
                // Face-down (Set) King Tiger does nothing.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Down(e, e.Opponent, KingTiger, 0);
                var celtic = PutInHand(e, p, Celtic);
                var ns = e.TryNormalSummon(p, celtic, asSet: false);
                PassAll(e);
                Check("King Tiger Wanghu: must be face-up on the field",
                    reached && ns && OnField(p, celtic), $"reached={reached} ns={ns}");
            }

            {
                // Review finding: a second Summon queued while the first one's response window is open must not
                // drop the first monster's answer (the window's summoned card is overwritten by the queued one).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tiger = Up(e, p, KingTiger, 0);
                PlaceSetTrap(e, p, TorrentialTribute, 0);
                var a = e.CreateCardInstance(Celtic);
                var ssA = e.SpecialSummonToField(o, a, BattlePosition.Attack, true);
                var windowOpen = e.IsAwaitingResponse;
                var b = e.CreateCardInstance(MysticalElf);
                var ssB = e.SpecialSummonToField(o, b, BattlePosition.Attack, true);
                var bQueued = OnField(o, b);
                PassAll(e);
                Check("King Tiger Wanghu: a Summon queued behind an open summon window does not drop the first answer",
                    reached && ssA && windowOpen && ssB && bQueued && !OnField(o, a) && !OnField(o, b) &&
                    o.Graveyard.Contains(a) && o.Graveyard.Contains(b) && OnField(p, tiger),
                    $"reached={reached} ssA={ssA} window={windowOpen} ssB={ssB} bQueued={bQueued} a={OnField(o, a)} " +
                    $"b={OnField(o, b)} tiger={OnField(p, tiger)} awaiting={e.IsAwaitingResponse}");
            }
        }

        // ───────────────────────── Moisture Creature ─────────────────────────

        static void MoistureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var t1 = Up(e, p, Celtic, 0);
                var t2 = Up(e, p, MysticalElf, 1);
                var t3 = Up(e, p, GiantSoldier, 2);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                var field = FieldSpell(e, o, Necrovalley);
                var mc = PutInHand(e, p, Moisture);
                e.PendingTributes.AddRange(new[] { t1, t2, t3 });
                var ok = e.TryNormalSummon(p, mc, asSet: false);
                PassAll(e);
                Check("Moisture Creature: 3 tapped Tributes → Tribute Summoned that way, the opponent's Spells/Traps " +
                      "(incl. the Field Spell) are destroyed",
                    reached && ok && OnField(p, mc) && p.Graveyard.Contains(t1) && p.Graveyard.Contains(t2) &&
                    p.Graveyard.Contains(t3) && o.Graveyard.Contains(mf) && o.Graveyard.Contains(field) &&
                    o.FieldSpellZone.Occupant == null,
                    $"reached={reached} ok={ok} mc={OnField(p, mc)} mf={o.Graveyard.Contains(mf)} " +
                    $"field={o.Graveyard.Contains(field)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var t1 = Up(e, p, Celtic, 0);
                var t2 = Up(e, p, MysticalElf, 1);
                var t3 = Up(e, p, GiantSoldier, 2);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                var mc = PutInHand(e, p, Moisture);
                e.PendingTributes.AddRange(new[] { t1, t2 });
                var ok = e.TryNormalSummon(p, mc, asSet: false);
                PassAll(e);
                Check("Moisture Creature: 2 Tributes is an ordinary Tribute Summon — no destruction",
                    reached && ok && OnField(p, mc) && OnField(p, t3) && o.TryFindSpellTrap(mf, out _),
                    $"reached={reached} ok={ok} mc={OnField(p, mc)} t3={OnField(p, t3)}");
            }

            {
                // The AI uses the procedure when the player controls a Spell/Trap, and not otherwise.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                Up(e, o, Celtic, 0);
                Up(e, o, MysticalElf, 1);
                Up(e, o, GiantSoldier, 2);
                var mf = PlaceSetTrap(e, p, MirrorForce, 0);
                var mc = PutInHand(e, o, Moisture);
                var ok = e.TryNormalSummon(o, mc, asSet: false);
                PassAll(e);
                var aiThree = ok && OnField(o, mc) && o.MonstersOnField().Count() == 1 && p.Graveyard.Contains(mf);

                var e2 = OpponentMain(db, pDeck, aDeck, out var reached2);
                var o2 = e2.Opponent;
                Up(e2, o2, Celtic, 0);
                Up(e2, o2, MysticalElf, 1);
                Up(e2, o2, GiantSoldier, 2);
                var mc2 = PutInHand(e2, o2, Moisture);
                var ok2 = e2.TryNormalSummon(o2, mc2, asSet: false);
                PassAll(e2);
                var aiTwo = ok2 && OnField(o2, mc2) && o2.MonstersOnField().Count() == 2;
                Check("Moisture Creature: the AI Tributes 3 only when there is a Spell/Trap to destroy",
                    reached && reached2 && aiThree && aiTwo,
                    $"reached={reached}/{reached2} aiThree={aiThree} aiTwo={aiTwo} ok={ok}/{ok2}");
            }
        }

        // ───────────────────────── Dark Dust Spirit ─────────────────────────

        static void DarkDustDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tribute = Up(e, p, Celtic, 0);
                var elf = Up(e, p, MysticalElf, 1);
                var skull = Up(e, o, SummonedSkull, 0);
                var oppSet = Down(e, o, Celtic, 1);
                var dds = PutInHand(e, p, DarkDust);
                e.PendingTributes.Add(tribute);
                var ok = e.TryNormalSummon(p, dds, asSet: false);
                PassAll(e);
                Check("Dark Dust Spirit: Normal (Tribute) Summoned → every other face-up monster is destroyed, Sets stay",
                    reached && ok && OnField(p, dds) && !OnField(p, elf) && p.Graveyard.Contains(elf) &&
                    !OnField(o, skull) && o.Graveyard.Contains(skull) && OnField(o, oppSet),
                    $"reached={reached} ok={ok} dds={OnField(p, dds)} elf={OnField(p, elf)} skull={OnField(o, skull)} " +
                    $"set={OnField(o, oppSet)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var dds = Down(e, p, DarkDust, 0);
                var skull = Up(e, o, SummonedSkull, 0);
                var flip = e.TryFlipSummon(p, dds);
                PassAll(e);
                Check("Dark Dust Spirit: Flip Summoned → the other face-up monsters are destroyed",
                    reached && flip && OnField(p, dds) && !OnField(o, skull),
                    $"reached={reached} flip={flip} skull={OnField(o, skull)}");
            }
        }

        // ───────────────────────── "You can only control 1 face-up" ─────────────────────────

        static void UniqueRuleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                Up(e, p, Chief, 0);
                Up(e, p, Celtic, 1);
                var second = PutInHand(e, p, Chief);
                var summonCheck = SummonProcedures.CheckNormalOrTribute(e, p, second, asSet: false);
                var setCheck = SummonProcedures.CheckNormalOrTribute(e, p, second, asSet: true);
                var ns = e.TryNormalSummon(p, second, asSet: false);
                Check("Unique rule: no Normal (Tribute) Summon of a second face-up Gravekeeper's Chief; a Set is legal",
                    reached && !summonCheck.Legal && setCheck.Legal && !ns && p.Hand.Contains(second),
                    $"reached={reached} summon={summonCheck.Legal}:{summonCheck.Reason} set={setCheck.Legal} ns={ns}");

                var set = Down(e, p, Chief, 2);
                var canFlip = e.CanFlipSummon(p, set);
                var gyCopy = InGy(e, p, Chief);
                var canSs = SpecialSummonLegality.CanBeSpecialSummoned(e, p, gyCopy, null, SummonOrigin.Graveyard);
                p.Graveyard.Remove(gyCopy);
                var ss = e.SpecialSummonToField(p, gyCopy, BattlePosition.Attack, true);
                Check("Unique rule: no Flip Summon and no face-up Special Summon of a second copy",
                    !canFlip && !canSs && !ss && !OnField(p, gyCopy),
                    $"canFlip={canFlip} canSs={canSs} ss={ss}");

                // Flipped face-up by an effect anyway: the one that came face-up later is destroyed.
                var first = p.MonstersOnField().First(m => m.CardId == Chief && m.FaceUp);
                e.ApplyEffectPositionChange(set, BattlePosition.Attack, true);
                e.NotifyPublic();
                PassAll(e);
                Check("Unique rule: a copy flipped face-up by an effect is destroyed, the first stays",
                    OnField(p, first) && !OnField(p, set) && p.Graveyard.Contains(set),
                    $"first={OnField(p, first)} flipped={OnField(p, set)}");
            }

            {
                // Control change: the copy that arrives is destroyed.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = Up(e, p, Chief, 0);
                var theirs = Up(e, o, Chief, 0);
                Up(e, o, SummonedSkull, 1);
                var took = e.TryTakeControl(p, theirs);
                e.NotifyPublic();
                PassAll(e);
                Check("Unique rule: a copy taken by a control change is destroyed (to its owner's GY)",
                    reached && took && OnField(p, mine) && !OnField(p, theirs) && !OnField(o, theirs) &&
                    o.Graveyard.Contains(theirs),
                    $"reached={reached} took={took} mine={OnField(p, mine)} theirs p={OnField(p, theirs)} " +
                    $"o={OnField(o, theirs)} oGy={o.Graveyard.Contains(theirs)} pGy={p.Graveyard.Contains(theirs)}");

                // Each player may control their own face-up copy.
                var e2 = PlayerMain(db, pDeck, aDeck, out _);
                Up(e2, e2.Opponent, Chief, 0);
                Up(e2, e2.Player, Celtic, 0);
                var mine2 = PutInHand(e2, e2.Player, Chief);
                e2.PendingTributes.Add(e2.Player.MonsterZones[0].Occupant);
                var ns = e2.TryNormalSummon(e2.Player, mine2, asSet: false);
                PassAll(e2);
                Check("Unique rule: the opponent's face-up copy does not block your own",
                    ns && OnField(e2.Player, mine2) && e2.Opponent.MonstersOnField().Any(m => m.CardId == Chief),
                    $"ns={ns}");
            }
        }

        // ───────────────────────── Gravekeeper's Chief vs Necrovalley ─────────────────────────

        static void NecrovalleyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FieldSpell(e, o, Necrovalley);
                var chief = Up(e, p, Chief, 0);
                var mineGy = InGy(e, p, Celtic);
                var theirGy = InGy(e, o, Celtic);
                var lockedMineBefore = ContinuousProtections.CannotTargetCardInGy(e, null, mineGy);
                var lockedTheirs = ContinuousProtections.CannotTargetCardInGy(e, null, theirGy);
                var banishLockMine = ContinuousProtections.CannotBanishFromGraveyard(e, p);
                var banishLockTheirs = ContinuousProtections.CannotBanishFromGraveyard(e, o);
                e.BanishCard(o, theirGy);
                e.BanishCard(p, mineGy);
                var theirsStays = o.Graveyard.Contains(theirGy);
                var mineBanished = !p.Graveyard.Contains(mineGy);
                Check("Necrovalley vs Gravekeeper's Chief: its controller's GY is unaffected, the opponent's is locked",
                    reached && !lockedMineBefore && lockedTheirs && !banishLockMine && banishLockTheirs &&
                    theirsStays && mineBanished,
                    $"reached={reached} lockMine={lockedMineBefore} lockTheirs={lockedTheirs} banMine={banishLockMine} " +
                    $"banTheirs={banishLockTheirs} theirsStays={theirsStays} mineBanished={mineBanished}");

                // Face-down or negated Chief: the lock is back.
                chief.FaceUp = false;
                var mine2 = InGy(e, p, Celtic);
                var lockedFaceDown = ContinuousProtections.CannotTargetCardInGy(e, null, mine2);
                chief.FaceUp = true;
                chief.IsNegated = true;
                var lockedNegated = ContinuousProtections.CannotBanishFromGraveyard(e, p);
                chief.IsNegated = false;
                Check("Necrovalley vs Gravekeeper's Chief: no immunity while it is face-down or negated",
                    lockedFaceDown && lockedNegated, $"faceDown={lockedFaceDown} negated={lockedNegated}");
            }

            {
                // AI Tribute Summons Chief under Necrovalley: its trigger revives a "Gravekeeper's" monster from its GY.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                FieldSpell(e, e.Player, Necrovalley);
                Up(e, o, Celtic, 0); // the Tribute: not a "Gravekeeper's" monster, so never the revival
                var spy = InGy(e, o, GkSpy);
                var chief = PutInHand(e, o, Chief);
                var ok = e.TryNormalSummon(o, chief, asSet: false);
                PassAll(e);
                Check("Gravekeeper's Chief: Tribute Summoned under Necrovalley → revives a Gravekeeper's from its GY",
                    reached && ok && OnField(o, chief) && OnField(o, spy) && !o.Graveyard.Contains(spy) &&
                    o.Graveyard.Any(g => g.CardId == Celtic),
                    $"reached={reached} ok={ok} chief={OnField(o, chief)} spy={OnField(o, spy)} celticInGy={o.Graveyard.Any(g => g.CardId == Celtic)}");
            }
        }

        // ───────────────────────── Jowls of Dark Demise ─────────────────────────

        static void JowlsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jowls = Down(e, p, Jowls, 0);
                var skull = Up(e, o, SummonedSkull, 0);
                var oppSet = Down(e, o, Celtic, 1);
                var flip = e.TryFlipSummon(p, jowls);
                var offered = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(skull) && e.IsLegalEffectTarget(oppSet);
                var picked = PickIfAwaiting(e, skull);
                PassAll(e);
                var taken = OnField(p, skull) && !OnField(o, skull);
                var lp = o.LifePoints;
                var bp = e.TryEnterBattlePhase(p);
                PassAll(e);
                skull.ClearAttackFlags();
                var atk = e.TryAttack(p, skull, null);
                DrainCombat(e);
                var direct = o.LifePoints == lp - 2500;
                for (var i = 0; i < 4 && e.TurnPlayer == p; i++)
                {
                    PassAll(e);
                    e.TryEndTurnSafe(p);
                }

                PassAll(e);
                Check("Jowls of Dark Demise: FLIP takes a monster (face-up or Set targets), it attacks directly, " +
                      "control returns at the End Phase",
                    reached && flip && offered && picked && taken && bp && atk && direct && OnField(o, skull),
                    $"reached={reached} flip={flip} offered={offered} picked={picked} taken={taken} bp={bp} atk={atk} " +
                    $"direct={direct} lp={lp}->{o.LifePoints} back={OnField(o, skull)}");
            }

            {
                // A face-down monster can be taken; the grant holds once it is flipped face-up.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jowls = Down(e, p, Jowls, 0);
                var oppSet = Down(e, o, Celtic, 0);
                var flip = e.TryFlipSummon(p, jowls);
                PickIfAwaiting(e, oppSet);
                PassAll(e);
                var taken = OnField(p, oppSet) && !oppSet.FaceUp;
                var flip2 = e.TryFlipSummon(p, oppSet);
                PassAll(e);
                Check("Jowls of Dark Demise: a Set monster is taken and keeps the direct-attack grant once flipped",
                    reached && flip && taken && flip2 && oppSet.FaceUp && e.GrantsDirectAttack(oppSet),
                    $"reached={reached} flip={flip} taken={taken} flip2={flip2} grant={e.GrantsDirectAttack(oppSet)}");
            }

            {
                // AI: its Jowls takes the player's monster.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var jowls = Down(e, o, Jowls, 0);
                var skull = Up(e, p, SummonedSkull, 0);
                var flip = e.TryFlipSummon(o, jowls);
                PassAll(e);
                Check("Jowls of Dark Demise: the AI's FLIP takes the player's monster with no prompt",
                    reached && flip && !e.IsAwaitingEffectTarget && OnField(o, skull) && e.GrantsDirectAttack(skull),
                    $"reached={reached} flip={flip} awaiting={e.IsAwaitingEffectTarget} skull={OnField(o, skull)}");
            }
        }

        // ───────────────────────── Metamorphosis ─────────────────────────

        static void MetamorphosisDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.AddRange(new[] { GaiaChampion, ThousandDragon, DarkFlareKnight });
                var celtic = Up(e, p, Celtic, 0);          // Lv4: no Level 4 Fusion
                var dm = Up(e, p, DarkMagician, 1);        // Lv7: Gaia / Thousand Dragon
                var skull = Up(e, p, SummonedSkull, 2);    // Lv6: Dark Flare Knight
                var card = PutInHand(e, p, Metamorphosis);
                var can = e.CanActivateSpellTrap(p, card, fromHand: true);
                var started = can && e.TryActivateSpellTrap(p, card, fromHand: true);
                var costOffer = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingIgnitionCost;
                var celticIllegal = costOffer && !e.IsLegalEffectTarget(celtic) && e.IsLegalEffectTarget(skull);
                var paid = PickIfAwaiting(e, dm) && p.Graveyard.Contains(dm);
                PassAll(e);
                var offer = e.IsAwaitingEffectTarget
                    ? e.PendingActivation.LegalTargets.Select(t => t.CardId).OrderBy(x => x).ToList()
                    : new System.Collections.Generic.List<int>();
                var twoSevens = offer.Count == 2 && offer.Contains(GaiaChampion) && offer.Contains(ThousandDragon);
                var td = e.IsAwaitingEffectTarget
                    ? e.PendingActivation.LegalTargets.FirstOrDefault(t => t.CardId == ThousandDragon)
                    : null;
                var done = td != null && e.TrySelectEffectTarget(td);
                PassAll(e);
                Check("Metamorphosis: Tribute a Level 7 (a Level 4 is not offered) → choose a Level 7 Fusion to Summon",
                    reached && can && started && costOffer && celticIllegal && paid && twoSevens && done &&
                    p.MonstersOnField().Any(m => m.CardId == ThousandDragon) && !p.ExtraDeck.Contains(ThousandDragon) &&
                    p.ExtraDeck.Contains(GaiaChampion) && p.Graveyard.Contains(card),
                    $"reached={reached} can={can} started={started} costOffer={costOffer} celticIllegal={celticIllegal} " +
                    $"paid={paid} offer=[{string.Join(",", offer)}] done={done}");
            }

            {
                // A Fusion that must be Fusion Summoned is not a match: nothing to Tribute for it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameWingman);
                Up(e, p, SummonedSkull, 0);
                var card = PutInHand(e, p, Metamorphosis);
                var can = e.CanActivateSpellTrap(p, card, fromHand: true);
                Check("Metamorphosis: a \"Must be Fusion Summoned\" Fusion of that Level does not make the Tribute legal",
                    reached && !can, $"reached={reached} can={can}");
            }

            {
                // AI: Tributes its Level 6 and Summons the Level 6 Fusion.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                o.ExtraDeck.Clear();
                o.ExtraDeck.AddRange(new[] { DarkFlareKnight, GaiaChampion });
                var skull = Up(e, o, SummonedSkull, 0);
                var card = PutInHand(e, o, Metamorphosis);
                var ok = e.TryActivateSpellTrap(o, card, fromHand: true);
                PassAll(e);
                Check("Metamorphosis: the AI Tributes a Level 6 and Summons the Level 6 Fusion",
                    reached && ok && !e.IsAwaitingEffectTarget && o.Graveyard.Contains(skull) &&
                    o.MonstersOnField().Any(m => m.CardId == DarkFlareKnight) && o.ExtraDeck.Contains(GaiaChampion),
                    $"reached={reached} ok={ok} awaiting={e.IsAwaitingEffectTarget}");
            }
        }
    }
}
