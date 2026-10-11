using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// GuardiansAndEquipEvents batch: Guardian Elma, Chopman the Desperate Outlaw, Guardian Grarl, Guardian Baou, Fuhma
    /// Shuriken and Morale Boost (<see cref="GuardiansAndEquipEventsAtoms"/>, <see cref="GuardiansAndEquipEventsRuntime"/>).
    /// Compile shapes, near misses, and mini-duels through TryNormalSummon / TryFlipSummon, TrySpecialSummonProcedure,
    /// TryAttack, TryActivateSpellTrap (Equip Spells, Mystical Space Typhoon, Dark Hole, Premature Burial) and the AI.
    /// </summary>
    public static class GuardiansAndEquipEventsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Elma = 74367458;
        const int Chopman = 40884383;
        const int Grarl = 47150851;
        const int Baou = 73544866;
        const int Fuhma = 9373534;
        const int MoraleBoost = 93671934;

        const int Dagger = 69243953;          // Butterfly Dagger - Elma (Elma's gate)
        const int GravityAxe = 32022366;      // Gravity Axe - Grarl (Grarl's gate)
        const int Celtic = 91152256;          // Warrior 1400/1200
        const int AxeRaider = 48305365;       // Warrior 1700/1150
        const int ManEaterBug = 54652250;     // Flip Effect 450/600
        const int PetitDragon = 75356564;     // Normal 600/700
        const int ArmedNinja = 9076207;       // "Ninja" Flip Effect 300/300
        const int Pendant = 65169794;         // Black Pendant: +500 ATK; sent from the field to the GY: 500 to the opponent
        const int AxeOfDespair = 40619825;    // +1000 ATK
        const int LegendarySword = 61854111;  // Equip only to a Warrior monster, +300 ATK/DEF
        const int SnatchSteal = 45986603;     // Equip only to a monster your opponent controls
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int PrematureBurial = 70828912;
        const int Mirror = 44095762;          // Mirror Force (a Set card to fill a zone)
        const int BlueEyes = 89631139;        // Level 8: 2 Tributes
        const int CollectedPower = 7565547;   // equip all Equip Cards to the target; the ones it cannot take are destroyed

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GuardiansAndEquipEvents: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            ElmaDuels(db, pDeck, aDeck, Check);
            ChopmanDuels(db, pDeck, aDeck, Check);
            GrarlDuels(db, pDeck, aDeck, Check);
            BaouDuels(db, pDeck, aDeck, Check);
            FuhmaDuels(db, pDeck, aDeck, Check);
            MoraleBoostDuels(db, pDeck, aDeck, Check);
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

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
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

        /// <summary>An Equip Spell face-up in <paramref name="zone"/>, already equipped to <paramref name="host"/>.</summary>
        static CardInstance EquipOnField(DuelEngine engine, DuelistState who, int id, int zone, CardInstance host)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            c.EquippedTo = host;
            host.Equips.Add(c);
            return c;
        }

        static CardInstance FaceUpSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(engine, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void Settle(DuelEngine engine)
        {
            DrainCombat(engine);
            FieldSpellEffects.RefreshBoard(engine);
        }

        /// <summary>Activate <paramref name="card"/> from the player's hand and pick <paramref name="target"/> if asked.</summary>
        static bool ActivateOn(DuelEngine engine, CardInstance card, CardInstance target, out List<CardInstance> offered)
        {
            offered = null;
            var p = engine.Player;
            if (!engine.TryActivateSpellTrap(p, card, fromHand: true)) return false;
            if (engine.IsAwaitingEffectTarget)
            {
                offered = engine.PendingActivation.LegalTargets.ToList();
                if (!engine.TrySelectEffectTarget(target)) return false;
            }

            Settle(engine);
            return true;
        }

        static string Names(IEnumerable<CardInstance> list) =>
            list == null ? "(no prompt)" : string.Join(",", list.Select(c => c?.Name));

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
                var p = P(Elma, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Guardian Elma: Normal or Special Summon trigger, target 1 Equip Spell in your GY, equip it to this card",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.ThisCardSummoned &&
                    c.Action == EffectActionKind.EquipTargetedGyEquipSpellToThis &&
                    c.Zone == EffectZoneFilter.ControllerGyEquipSpells && c.RequiresTargetChoice && c.IsPsctTarget &&
                    c.IsOptional && c.RequiresThisNormalOrSpecialSummoned && !c.RequiresThisFlipSummoned &&
                    c.CheckedAt == ConditionCheckedAt.Activation, Describe(p));
            }
            {
                var p = P(Chopman, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Chopman the Desperate Outlaw: Flip Summon trigger, equip 1 Equip Spell from your GY (targets, as in Lua)",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 && c.Timing == EffectTiming.ThisCardSummoned &&
                    c.Action == EffectActionKind.EquipTargetedGyEquipSpellToThis &&
                    c.Zone == EffectZoneFilter.ControllerGyEquipSpells && c.RequiresThisFlipSummoned &&
                    !c.RequiresThisNormalOrSpecialSummoned && c.IsPsctTarget && c.IsOptional, Describe(p));
            }
            {
                var p = P(Grarl, out var impl);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Guardian Grarl: its own hand procedure, only as the only card in the hand",
                    impl && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Action == EffectActionKind.SpecialSummonThisFromHandByOwnProcedure && c.RequiresOnlyCardInHand &&
                    !c.MakesChainLink, Describe(p));
            }
            {
                var p = P(Baou, out var impl);
                var gain = p?.ClauseList.FirstOrDefault(c => c.Timing == EffectTiming.ThisCardDestroysByBattle);
                var neg = p?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle);
                Check("Guardian Baou: +1000 ATK after destroying by battle (sent to the GY); negate Effect Monsters it destroys",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 && gain != null &&
                    gain.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && gain.Amount == 1000 &&
                    (gain.SourceSnippet ?? "").Contains("sends it to the Graveyard") && neg != null &&
                    neg.Timing == EffectTiming.ContinuousWhileFaceUp && neg.AffectsThisCardOnly && neg.TargetCardKind == "Effect" &&
                    string.IsNullOrEmpty(neg.RaceFilter) && string.IsNullOrEmpty(neg.CountRaceFilter), Describe(p));
            }
            {
                var p = P(Fuhma, out var impl);
                var eq = p?.ClausesFor(EffectTiming.Activate).FirstOrDefault();
                var burn = p?.ClausesFor(EffectTiming.SentFromFieldToGy).FirstOrDefault();
                Check("Fuhma Shuriken: equip only to a face-up \"Ninja\" monster, +700 ATK; sent from the field: 700 to the opponent",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 && eq != null &&
                    eq.Action == EffectActionKind.EquipThisToTarget && eq.TargetSeriesName == "Ninja" && eq.EquipAtkBonus == 700 &&
                    eq.Zone == EffectZoneFilter.FieldAnyMonster && eq.RequiresTargetFaceUp && eq.AiEquipsOwnMonster &&
                    burn != null && burn.Action == EffectActionKind.InflictDamageToOpponent && burn.Amount == 700 &&
                    !burn.IsOptional, Describe(p));
            }
            {
                var p = P(MoraleBoost, out var impl);
                var gain = p?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.GainLpWhenEquipSpellEquipped);
                var dmg = p?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DamageWhenEquipSpellLeavesField);
                Check("Morale Boost: equip (gain 1000) and leave-field (1000 damage) markers while face-up",
                    impl && p.FullyCompiled && p.ClauseList.Count == 2 && gain != null && dmg != null &&
                    gain.Timing == EffectTiming.ContinuousWhileFaceUp && dmg.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    gain.Amount == 1000 && dmg.Amount == 1000 && !gain.MakesChainLink && !dmg.MakesChainLink, Describe(p));
            }

            Check("ResolutionOf maps the new actions",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.EquipTargetedGyEquipSpellToThis }) == EffectResolutionKind.Equip &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.GainLpWhenEquipSpellEquipped }) == EffectResolutionKind.GainLp &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DamageWhenEquipSpellLeavesField }) == EffectResolutionKind.Damage);
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static CompiledCardProgram CompileText(string text, bool monster) =>
            CardTextEffectCompiler.Compile(monster
                ? new CardDef
                {
                    id = 990176, name = "Near-miss Guardian", type = "Effect Monster", frameType = "effect", race = "Warrior",
                    attribute = "EARTH", level = 4, atk = 1000, def = 1000, desc = text
                }
                : new CardDef
                {
                    id = 990177, name = "Near-miss Equip", type = "Spell Card", frameType = "spell", race = "Equip", desc = text
                });

        static bool FromThisBatch(EffectClause c) =>
            c != null && (c.Action == EffectActionKind.EquipTargetedGyEquipSpellToThis ||
                          c.Action == EffectActionKind.GainLpWhenEquipSpellEquipped ||
                          c.Action == EffectActionKind.DamageWhenEquipSpellLeavesField ||
                          c.RequiresOnlyCardInHand || c.RequiresThisNormalOrSpecialSummoned ||
                          (c.Action == EffectActionKind.EquipThisToTarget && !string.IsNullOrEmpty(c.TargetSeriesName)) ||
                          (c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle && c.AffectsThisCardOnly &&
                           string.IsNullOrEmpty(c.CountRaceFilter)) ||
                          (c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp && c.Amount > 0));

        static void NearMisses(Chk Check)
        {
            var misses = new (string text, bool monster)[]
            {
                // Only a Normal Summon: not Elma's Normal-or-Special trigger.
                ("When this card is Normal Summoned: You can target 1 appropriate Equip Spell Card in your Graveyard; " +
                 "equip that target to this card.", true),
                // The opponent's GY.
                ("When this card is Flip Summoned, you can equip 1 Equip Spell Card in your opponent's Graveyard to this card.", true),
                // A Normal Summon, not a Special Summon.
                ("If this is the only card in your hand, you can Normal Summon it without Tributing.", true),
                // ATK and DEF: not the ATK-only series equip.
                ("Equip only to a \"Ninja\" monster. It gains 700 ATK/DEF.", false),
                // Your own LP, not the Equip Spell's controller.
                ("Each time a player equips an Equip Spell Card, increase your Life Points by 1000 points.", false),
                // Damage to the opponent, not the Equip Spell's controller.
                ("Each time an Equip Spell Card is removed from the field, inflict 1000 damage to your opponent.", false),
                // Loses ATK (Zombyra's sentence family, a different card).
                ("When this card destroys an opponent's monster by battle and sends it to the Graveyard: It loses 1000 ATK.", true),
                // Another monster's battle, not this card's.
                ("Negate the effects of monsters destroyed by battle with monsters you control.", true),
            };
            var leaked = misses.Where(n =>
            {
                var p = CompileText(n.text, n.monster);
                return p != null && p.ClauseList.Exists(FromThisBatch);
            }).Select(n => n.text).ToList();
            Check("Near misses: look-alike sentences never compile to this batch's clauses", leaked.Count == 0,
                string.Join(" || ", leaked));
        }

        // ───────────────────────────── Guardian Elma ─────────────────────────────

        static void ElmaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player Normal Summons Elma: only an appropriate Equip Spell is offered (Snatch Steal is opponent-only,
            // Legendary Sword Warrior-only); the pick is equipped from the GY. A face-up Morale Boost sees that equip.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                EquipOnField(e, p, Dagger, 0, celtic);
                var morale = FaceUpSpell(e, p, MoraleBoost, 1);
                var pendant = ToGy(e, p, Pendant);
                var snatch = ToGy(e, p, SnatchSteal);
                var sword = ToGy(e, p, LegendarySword);
                var elma = PutInHand(e, p, Elma);
                var lp = p.LifePoints;
                var summoned = e.TryNormalSummon(p, elma, asSet: false);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    e.TrySelectEffectTarget(pendant);
                }

                Settle(e);
                var offeredOk = offered == null || (offered.Contains(pendant) && !offered.Contains(snatch) &&
                                                    !offered.Contains(sword));
                Check("Guardian Elma (player): Normal Summon, an appropriate Equip Spell from the GY is equipped to it",
                    reached && summoned && offeredOk && pendant.EquippedTo == elma && p.TryFindSpellTrap(pendant, out _) &&
                    !p.Graveyard.Contains(pendant) && elma.CurrentAtk == 1800 && p.Graveyard.Contains(snatch) &&
                    p.Graveyard.Contains(sword),
                    $"reached={reached} summoned={summoned} offered={Names(offered)} eqTo={pendant.EquippedTo?.Name} " +
                    $"atk={elma.CurrentAtk}");
                Check("Morale Boost: an Equip Spell equipped from the GY by Guardian Elma is an equip (+1000 LP)",
                    morale.FaceUp && p.LifePoints == lp + 1000, $"lp {lp} -> {p.LifePoints}");
            }

            // The AI Normal Summons Elma: it picks the Equip Spell with the higher ATK change.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var raider = PlaceMonster(e, o, AxeRaider, 0, BattlePosition.Attack, true);
                EquipOnField(e, o, Dagger, 0, raider);
                var pendant = ToGy(e, o, Pendant);
                var axe = ToGy(e, o, AxeOfDespair);
                var elma = PutInHand(e, o, Elma);
                var summoned = e.TryNormalSummon(o, elma, asSet: false);
                Settle(e);
                Check("Guardian Elma (AI): equips the best appropriate Equip Spell from its GY",
                    reached && summoned && axe.EquippedTo == elma && o.Graveyard.Contains(pendant) && elma.CurrentAtk == 2300,
                    $"reached={reached} summoned={summoned} axeTo={axe.EquippedTo?.Name} atk={elma.CurrentAtk}");
            }

            // No free Spell & Trap Zone: nothing to target, the Equip Spell stays in the GY. Without the Dagger, no Summon.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                EquipOnField(e, p, Dagger, 0, celtic);
                for (var z = 1; z < p.SpellTrapZones.Length; z++)
                    PlaceSetTrap(e, p, Mirror, z);
                var pendant = ToGy(e, p, Pendant);
                var elma = PutInHand(e, p, Elma);
                var summoned = e.TryNormalSummon(p, elma, asSet: false);
                var prompted = e.IsAwaitingEffectTarget;
                Settle(e);
                Check("Guardian Elma: no free Spell & Trap Zone — no target, the Equip Spell stays in the GY",
                    reached && summoned && !prompted && p.Graveyard.Contains(pendant) && pendant.EquippedTo == null,
                    $"summoned={summoned} prompted={prompted} gy={p.Graveyard.Contains(pendant)}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                ToGy(e, p, Pendant);
                var elma = PutInHand(e, p, Elma);
                var summoned = e.TryNormalSummon(p, elma, asSet: false);
                Settle(e);
                Check("Guardian Elma: cannot be Normal Summoned without a face-up \"Butterfly Dagger - Elma\"",
                    reached && !summoned && p.Hand.Contains(elma), $"summoned={summoned}");
            }

            // A Flip Summon is not a Normal or Special Summon: no trigger.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                EquipOnField(e, p, Dagger, 0, celtic);
                var pendant = ToGy(e, p, Pendant);
                var elma = PlaceMonster(e, p, Elma, 1, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, elma);
                var prompted = e.IsAwaitingEffectTarget;
                Settle(e);
                Check("Guardian Elma: a Flip Summon does not trigger its equip",
                    reached && flipped && elma.FaceUp && !prompted && p.Graveyard.Contains(pendant),
                    $"flipped={flipped} prompted={prompted} gy={p.Graveyard.Contains(pendant)}");
            }
        }

        // ───────────────────────────── Chopman ─────────────────────────────

        static void ChopmanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var pendant = ToGy(e, p, Pendant);
                var sword = ToGy(e, p, LegendarySword); // Warrior only: Chopman is a Zombie
                var chop = PlaceMonster(e, p, Chopman, 0, BattlePosition.Defense, false);
                var flipped = e.TryFlipSummon(p, chop);
                List<CardInstance> offered = null;
                if (e.IsAwaitingEffectTarget)
                {
                    offered = e.PendingActivation.LegalTargets.ToList();
                    e.TrySelectEffectTarget(pendant);
                }

                Settle(e);
                Check("Chopman the Desperate Outlaw: Flip Summon, equip 1 appropriate Equip Spell from the GY",
                    reached && flipped && (offered == null || (offered.Contains(pendant) && !offered.Contains(sword))) &&
                    pendant.EquippedTo == chop && p.TryFindSpellTrap(pendant, out _) && chop.CurrentAtk == 1600 &&
                    p.Graveyard.Contains(sword),
                    $"flipped={flipped} offered={Names(offered)} eqTo={pendant.EquippedTo?.Name} atk={chop.CurrentAtk}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var pendant = ToGy(e, p, Pendant);
                var chop = PutInHand(e, p, Chopman);
                var summoned = e.TryNormalSummon(p, chop, asSet: false);
                var prompted = e.IsAwaitingEffectTarget;
                Settle(e);
                Check("Chopman the Desperate Outlaw: a Normal Summon does not trigger it",
                    reached && summoned && !prompted && p.Graveyard.Contains(pendant),
                    $"summoned={summoned} prompted={prompted}");
            }
        }

        // ───────────────────────────── Guardian Grarl ─────────────────────────────

        static void GrarlDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                EquipOnField(e, p, GravityAxe, 0, celtic);
                var grarl = PutInHand(e, p, Grarl);
                var can = e.CanSpecialSummonProcedure(p, grarl);
                var other = PutInHand(e, p, Pendant);
                var canWithTwo = e.CanSpecialSummonProcedure(p, grarl);
                p.Hand.Remove(other);
                var done = e.TrySpecialSummonProcedure(p, grarl);
                Settle(e);
                Check("Guardian Grarl: Special Summoned from the hand while it is the only card there (Gravity Axe face-up)",
                    reached && can && !canWithTwo && done && p.TryFindMonster(grarl, out _) && grarl.FaceUp &&
                    grarl.Position == BattlePosition.Attack && !p.NormalSummonUsed,
                    $"can={can} canWithTwo={canWithTwo} done={done} onField={p.TryFindMonster(grarl, out _)}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var grarl = PutInHand(e, p, Grarl);
                var can = e.CanSpecialSummonProcedure(p, grarl);
                var done = e.TrySpecialSummonProcedure(p, grarl);
                Check("Guardian Grarl: no procedure without a face-up \"Gravity Axe - Grarl\"",
                    reached && !can && !done && p.Hand.Contains(grarl), $"can={can} done={done}");
            }
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                var raider = PlaceMonster(e, o, AxeRaider, 0, BattlePosition.Attack, true);
                EquipOnField(e, o, GravityAxe, 0, raider);
                var grarl = PutInHand(e, o, Grarl);
                Check("Guardian Grarl (AI): the AI wants its procedure when legal",
                    reached && NegationsAndGySummonsRuntime.AiWantsOwnHandProcedure(e, o, grarl), $"reached={reached}");
            }
        }

        // ───────────────────────────── Guardian Baou ─────────────────────────────

        static void BaouDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var (victimId, effect) in new[] { (ManEaterBug, true), (PetitDragon, false) })
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var baou = PlaceMonster(e, p, Baou, 0, BattlePosition.Attack, true);
                var victim = PlaceMonster(e, o, victimId, 0, BattlePosition.Attack, true);
                e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var attacked = e.TryAttack(p, baou, victim);
                Settle(e);
                var negated = victim.EffectsNegatedAfterBattle;
                Check($"Guardian Baou destroys {victim.Name} by battle: +1000 ATK; " +
                      (effect ? "the Effect Monster's effects are negated" : "a Normal Monster is not marked"),
                    reached && attacked && o.Graveyard.Contains(victim) && baou.CurrentAtk == 1800 && negated == effect,
                    $"attacked={attacked} gy={o.Graveyard.Contains(victim)} atk={baou.CurrentAtk} negated={negated}");
            }
        }

        // ───────────────────────────── Fuhma Shuriken ─────────────────────────────

        static void FuhmaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ninja = PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var fuhma = PutInHand(e, p, Fuhma);
                var act = ActivateOn(e, fuhma, ninja, out var offered);
                Check("Fuhma Shuriken: only a \"Ninja\" monster is offered; it gains 700 ATK",
                    reached && act && (offered == null || (offered.Contains(ninja) && !offered.Contains(celtic))) &&
                    fuhma.EquippedTo == ninja && ninja.CurrentAtk == 1000,
                    $"act={act} offered={Names(offered)} eqTo={fuhma.EquippedTo?.Name} atk={ninja.CurrentAtk}");
                Check("Fuhma Shuriken: its series equip limit (CheckEquipTarget) — a Ninja yes, a Warrior no",
                    EquipFamilyRuntime.IsCorrectEquipTarget(e, fuhma, ninja) &&
                    !EquipFamilyRuntime.IsCorrectEquipTarget(e, fuhma, celtic));

                var oppLp = o.LifePoints;
                var mst = PutInHand(e, p, Mst);
                var destroyed = ActivateOn(e, mst, fuhma, out _);
                Check("Fuhma Shuriken: sent from the field to the GY — 700 damage to the opponent",
                    destroyed && p.Graveyard.Contains(fuhma) && o.LifePoints == oppLp - 700 && ninja.CurrentAtk == 300,
                    $"destroyed={destroyed} gy={p.Graveyard.Contains(fuhma)} opp {oppLp} -> {o.LifePoints}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var fuhma = PutInHand(e, p, Fuhma);
                Check("Fuhma Shuriken: no \"Ninja\" monster on the field — it cannot be activated",
                    reached && !e.CanActivateSpellTrap(p, fuhma, fromHand: true));
            }
        }

        // ───────────────────────────── Morale Boost ─────────────────────────────

        static void MoraleBoostDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Activated from the hand; an Equip Spell activation gains its controller 1000; its destruction costs 1000.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var morale = PutInHand(e, p, MoraleBoost);
                var act = e.TryActivateSpellTrap(p, morale, fromHand: true);
                Settle(e);
                var lp0 = p.LifePoints;
                var pendant = PutInHand(e, p, Pendant);
                var equipped = ActivateOn(e, pendant, celtic, out _);
                var lp1 = p.LifePoints;
                Check("Morale Boost: an Equip Spell is equipped — its controller gains 1000",
                    reached && act && morale.FaceUp && p.TryFindSpellTrap(morale, out _) && equipped &&
                    pendant.EquippedTo == celtic && lp1 == lp0 + 1000,
                    $"act={act} equipped={equipped} lp {lp0} -> {lp1}");

                var oppLp = o.LifePoints;
                var mst = PutInHand(e, p, Mst);
                var destroyed = ActivateOn(e, mst, pendant, out _);
                Check("Morale Boost: the equipped Equip Spell leaves the field — its controller takes 1000",
                    destroyed && p.Graveyard.Contains(pendant) && p.LifePoints == lp1 - 1000 && o.LifePoints == oppLp - 500,
                    $"destroyed={destroyed} lp {lp1} -> {p.LifePoints} opp {oppLp} -> {o.LifePoints}");
            }

            // Dark Hole: three Equip Spells lose their monsters in one event — each controller takes 1000 once.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, p, MoraleBoost, 4);
                var c1 = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var s1 = EquipOnField(e, p, LegendarySword, 0, c1);
                var s2 = EquipOnField(e, p, LegendarySword, 1, c2);
                var oc = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var s3 = EquipOnField(e, o, LegendarySword, 0, oc);
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Settle(e);
                Check("Morale Boost: Dark Hole — the Equip Spells of both players leave together, each player takes 1000 once",
                    reached && act && p.Graveyard.Contains(s1) && p.Graveyard.Contains(s2) && o.Graveyard.Contains(s3) &&
                    p.LifePoints == 7000 && o.LifePoints == 7000,
                    $"act={act} lp={p.LifePoints} opp={o.LifePoints}");
            }

            // Tribute Summon with 2 Tributes: Lua releases them as one group, and their Equip Spells are destroyed together
            // (REASON_LOST_TARGET) — one EVENT_LEAVE_FIELD, 1000 once (review finding).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FaceUpSpell(e, p, MoraleBoost, 4);
                var c1 = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var s1 = EquipOnField(e, p, LegendarySword, 0, c1);
                var s2 = EquipOnField(e, p, LegendarySword, 1, c2);
                var bewd = PutInHand(e, p, BlueEyes);
                e.PendingTributes.Clear();
                e.ToggleTribute(p, c1);
                e.ToggleTribute(p, c2);
                var ok = e.TryNormalSummon(p, bewd, asSet: false);
                Settle(e);
                Check("Morale Boost: a Tribute Summon Tributes 2 equipped monsters — their Equip Spells leave together, 1000 once",
                    reached && ok && p.Graveyard.Contains(s1) && p.Graveyard.Contains(s2) && p.TryFindMonster(bewd, out _) &&
                    p.LifePoints == 7000,
                    $"ok={ok} s1gy={p.Graveyard.Contains(s1)} s2gy={p.Graveyard.Contains(s2)} lp={p.LifePoints}");
            }

            // Cross-group audit (v176): Morale Boost's TRIGGER_F is a Spell/Trap effect, so Sasuke Samurai #2's lock
            // (re:IsSpellTrapEffect()) stops it — the same Tribute Summon deals no damage.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FaceUpSpell(e, p, MoraleBoost, 4);
                var c1 = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var s1 = EquipOnField(e, p, LegendarySword, 0, c1);
                var s2 = EquipOnField(e, p, LegendarySword, 1, c2);
                var bewd = PutInHand(e, p, BlueEyes);
                SpellTrapAnswersAndActivationLocksRuntime.RegisterTurnSpellTrapLock(e, e.Opponent,
                    e.CreateCardInstance(11760174));
                e.PendingTributes.Clear();
                e.ToggleTribute(p, c1);
                e.ToggleTribute(p, c2);
                var ok = e.TryNormalSummon(p, bewd, asSet: false);
                Settle(e);
                Check("Morale Boost: under Sasuke Samurai #2's Spell/Trap lock its trigger is not activated (no damage)",
                    reached && ok && p.Graveyard.Contains(s1) && p.Graveyard.Contains(s2) && p.LifePoints == 8000,
                    $"ok={ok} lp={p.LifePoints}");
            }

            // Collected Power: the Equip Spells the target cannot take are destroyed as one group (Lua Duel.Destroy(dg)).
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FaceUpSpell(e, p, MoraleBoost, 4);
                var c1 = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var petit = PlaceMonster(e, p, PetitDragon, 2, BattlePosition.Attack, true);
                var s1 = EquipOnField(e, p, LegendarySword, 0, c1);
                var s2 = EquipOnField(e, p, LegendarySword, 1, c2);
                var cp = PlaceSetTrap(e, p, CollectedPower, 3);
                var act = e.TryActivateSpellTrap(p, cp, fromHand: false);
                if (act && e.IsAwaitingEffectTarget) act = e.TrySelectEffectTarget(petit);
                Settle(e);
                Check("Morale Boost: Collected Power destroys 2 Equip Spells the target cannot take — together, 1000 once",
                    reached && act && p.Graveyard.Contains(s1) && p.Graveyard.Contains(s2) && p.LifePoints == 7000,
                    $"act={act} s1gy={p.Graveyard.Contains(s1)} s2gy={p.Graveyard.Contains(s2)} lp={p.LifePoints}");
            }

            // Premature Burial is equipped by Duel.Equip as well; an opponent's Morale Boost gains the Equip's controller,
            // a face-down Morale Boost does nothing.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpell(e, o, MoraleBoost, 0);
                PlaceSetTrap(e, p, MoraleBoost, 4);
                var celtic = ToGy(e, p, Celtic);
                var burial = PutInHand(e, p, PrematureBurial);
                var act = ActivateOn(e, burial, celtic, out _);
                Check("Morale Boost (opponent's, face-up): Premature Burial's equip gains its controller 1000; a Set copy " +
                      "does nothing",
                    reached && act && burial.EquippedTo == celtic && p.LifePoints == 8000 - 800 + 1000 &&
                    o.LifePoints == 8000,
                    $"act={act} eqTo={burial.EquippedTo?.Name} lp={p.LifePoints} opp={o.LifePoints}");
            }
        }
    }
}
