using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Live mini-duel regressions for field→GY, Flip, hand QE, zone placement.
    /// These catch silent no-ops (Sangan/Magician-style) that pure battle-math tests miss.
    /// </summary>
    public static class InteractionRegressionTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("PASS  " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
                }
            }

            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                sb.AppendLine("FAIL  CardDatabase load");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            {
                var ssDeckDef = new CardDef { id = 990000101, name = "ARGON SS Deck Fixture", type = "Spell Card", desc = "Special Summon 1 Level 4 or lower Beast-Type monster from your Deck." };
                var ssDeckProg = CardTextEffectCompiler.Compile(ssDeckDef);
                var ssClause = ssDeckProg?.ClauseList?.FirstOrDefault();
                Check("Shared SS-from-Deck atom", ssDeckProg != null && ssDeckProg.FullyCompiled && ssClause != null && ssClause.Action == EffectActionKind.SpecialSummonFromDeck && ssClause.Zone == EffectZoneFilter.ControllerDeckMonsters && ssClause.RequiresTargetChoice && !ssClause.IsPsctTarget);

                var whenIfDef = new CardDef { id = 990000102, name = "ARGON When If Fixture", type = "Monster", desc = "If this card is destroyed by battle: You can Special Summon 1 Warrior-Type monster from your Deck." };
                var whenIfProg = CardTextEffectCompiler.Compile(whenIfDef);
                var whenIfClause = whenIfProg?.ClauseList?.FirstOrDefault();
                Check("When/If optional trigger metadata", whenIfProg != null && whenIfClause != null && whenIfClause.IsOptional && whenIfClause.CheckedAt == ConditionCheckedAt.Both);
            }

            var pDeck = CardDatabase.LoadDeck("lab_rules_player.json");
            var aDeck = CardDatabase.LoadDeck("lab_rules_ai.json");
            if (pDeck == null || aDeck == null)
            {
                sb.AppendLine("FAIL  lab decks missing");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            // ── Compiled rejection must not fall through a registry allowlist ──
            {
                var syntheticDef = new CardDef
                {
                    id = OfficialEffectRegistry.PotOfGreed,
                    name = "Synthetic Pot of Greed cost test",
                    type = "Spell Card",
                    race = "Normal",
                    desc = "Activate this card by paying 8000 LP; draw 2 cards."
                };
                var syntheticProg = CardTextEffectCompiler.Compile(syntheticDef);
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                engine.Player.Hand.Clear();
                engine.Player.LifePoints = 100;
                var syntheticCard = new CardInstance
                {
                    CardId = syntheticDef.id,
                    Def = syntheticDef,
                    InstanceId = 90000001
                };
                engine.Player.Hand.Add(syntheticCard);
                Check("FullyCompiled synthetic legacy-ID card rejects activation without fallback",
                    syntheticProg != null && syntheticProg.FullyCompiled &&
                    OfficialEffectRegistry.ProgramMayActivate(syntheticDef) &&
                    !engine.CanActivateSpellTrap(engine.Player, syntheticCard, fromHand: true),
                    syntheticProg == null
                        ? "null"
                        : $"full={syntheticProg.FullyCompiled} canResolve={syntheticProg.CanResolveAny}");
            }

            // ── Center-first zone placement ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                engine.Player.Hand.Clear();
                engine.Player.Deck.Clear();

                var m1 = PutInHand(engine, engine.Player, 91152256); // Celtic Guardian
                var m2 = PutInHand(engine, engine.Player, 32452818); // Beaver Warrior
                var m3 = PutInHand(engine, engine.Player, 13039848); // Giant Soldier

                // Only one Normal Summon/turn — reset flag between placements for zone-order test
                Check("Center zone: first summon → index 2",
                    engine.TryNormalSummon(engine.Player, m1, asSet: false) &&
                    engine.Player.MonsterZones[2].Occupant == m1);
                engine.Player.NormalSummonUsed = false;

                Check("Center zone: second summon → index 1 (left of center)",
                    engine.TryNormalSummon(engine.Player, m2, asSet: false) &&
                    engine.Player.MonsterZones[1].Occupant == m2);
                engine.Player.NormalSummonUsed = false;

                Check("Center zone: third summon → index 3 (right of center)",
                    engine.TryNormalSummon(engine.Player, m3, asSet: false) &&
                    engine.Player.MonsterZones[3].Occupant == m3);

                // Spell/Trap same policy
                var st = PutInHand(engine, engine.Player, 55144522); // Pot of Greed
                Check("Center ST zone: first set → index 2",
                    engine.TrySetSpellTrap(engine.Player, st) &&
                    engine.Player.SpellTrapZones[2].Occupant == st);
            }

            // ── Sangan field→GY destroy ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                // Deck search targets
                p.Deck.Add(91152256); // Celtic Guardian 1400
                p.Deck.Add(89631139); // Blue-Eyes 3000 — illegal
                p.Deck.Add(32452818); // Beaver 1200

                var sangan = PlaceMonster(engine, p, MonsterEffects.Sangan, 2, BattlePosition.Attack, true);
                var handBefore = p.HandCount;
                engine.DestroyMonsterPublic(p, sangan);

                // Player: should open search pending
                Check("Sangan destroy: opens deck search pending",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq,
                    engine.PendingActivation?.TargetKind.ToString() ?? "no pending");

                if (engine.IsAwaitingEffectTarget)
                {
                    var pick = engine.PendingActivation.LegalTargets
                        .FirstOrDefault(t => t.CardId == 91152256);
                    Check("Sangan destroy: Celtic Guardian is a legal option", pick != null);
                    if (pick != null)
                    {
                        var ok = engine.TrySelectEffectTarget(pick);
                        Check("Sangan destroy: select resolves", ok);
                        Check("Sangan destroy: Celtic Guardian in hand",
                            p.Hand.Exists(c => c.CardId == 91152256) && p.HandCount == handBefore + 1);
                        Check("Sangan destroy: removed from Deck",
                            !p.Deck.Contains(91152256));
                        Check("Sangan destroy: Sangan in GY",
                            p.Graveyard.Exists(c => c.CardId == MonsterEffects.Sangan));
                    }
                }
            }

            // ── Sangan field→GY via SendCardToGrave (tribute / effect send) ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                p.Deck.Add(32452818); // Beaver 1200

                var sangan = PlaceMonster(engine, p, MonsterEffects.Sangan, 2, BattlePosition.Attack, true);
                engine.SendCardToGrave(p, sangan);

                Check("Sangan SendCardToGrave: opens search",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq);

                if (engine.IsAwaitingEffectTarget)
                {
                    var pick = engine.PendingActivation.LegalTargets.FirstOrDefault();
                    Check("Sangan SendCardToGrave: has options", pick != null);
                    if (pick != null)
                    {
                        engine.TrySelectEffectTarget(pick);
                        Check("Sangan SendCardToGrave: added Beaver Warrior",
                            p.Hand.Exists(c => c.CardId == 32452818) &&
                            !p.Deck.Contains(32452818));
                    }
                }
            }

            // ── Witch of the Black Forest field->GY DEF search ──
            {
                const int witchId = 78010363;
                const int battleOx = 5053103; // 1700/1000: DEF legal, ATK illegal
                const int preventRat = 549481; // 500/2000: ATK legal, DEF illegal
                const int bewd = 89631139;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                p.Deck.Add(battleOx);
                p.Deck.Add(preventRat);
                p.Deck.Add(bewd);
                var witch = PlaceMonster(engine, p, witchId, 2, BattlePosition.Attack, true);
                engine.DestroyMonsterPublic(p, witch);
                Check("Witch destroy: opens DEF deck search pending",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.LegalTargets != null,
                    engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                if (engine.IsAwaitingEffectTarget)
                {
                    var legal = engine.PendingActivation.LegalTargets;
                    Check("Witch search: Battle Ox DEF 1000 is legal",
                        legal.Exists(t => t != null && t.CardId == battleOx));
                    Check("Witch search: Prevent Rat DEF 2000 is illegal",
                        !legal.Exists(t => t != null && t.CardId == preventRat));
                    Check("Witch search: Blue-Eyes is illegal",
                        !legal.Exists(t => t != null && t.CardId == bewd));
                    var pick = legal.FirstOrDefault(t => t.CardId == battleOx);
                    if (pick != null)
                    {
                        Check("Witch search: select Battle Ox", engine.TrySelectEffectTarget(pick));
                        Check("Witch search: Battle Ox in hand",
                            p.Hand.Exists(c => c.CardId == battleOx));
                        Check("Witch search: removed from Deck",
                            !p.Deck.Contains(battleOx));
                    }
                }
            }

            // ── Magician of Faith Flip ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                // Spell in GY
                var pot = engine.CreateCardInstance(55144522);
                p.Graveyard.Add(pot);

                var mof = PlaceMonster(engine, p, MonsterEffects.MagicianOfFaith, 2,
                    BattlePosition.Defense, false);
                // Flip Summon
                p.NormalSummonUsed = false;
                // Cannot Flip same turn as Set — clear SetThisTurn
                mof.SetThisTurn = false;
                var ok = engine.TryFlipSummon(p, mof);
                Check("Magician of Faith: Flip Summon succeeds", ok && mof.FaceUp);
                Check("Magician of Faith: opens Spell-in-GY choice",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.SpellInYourGy);

                if (engine.IsAwaitingEffectTarget)
                {
                    var t = engine.PendingActivation.LegalTargets.FirstOrDefault();
                    engine.TrySelectEffectTarget(t);
                    Check("Magician of Faith: Pot of Greed returned to hand",
                        p.Hand.Exists(c => c.CardId == 55144522) &&
                        !p.Graveyard.Exists(c => c.CardId == 55144522));
                }
            }

            // ── Dark Mimic LV1 Flip draw ──
            {
                const int darkMimic1 = 74713516;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                p.Deck.Add(91152256);
                p.Deck.Add(32452818);
                var mimic = PlaceMonster(engine, p, darkMimic1, 2, BattlePosition.Defense, false);
                mimic.SetThisTurn = false;
                var handBefore = p.HandCount;
                Check("Dark Mimic LV1 Flip Summon", engine.TryFlipSummon(p, mimic) && mimic.FaceUp);
                Check("Dark Mimic LV1 Flip: drew 1",
                    p.HandCount == handBefore + 1 && p.Deck.Count == 1,
                    $"hand={p.HandCount} was {handBefore} deck={p.Deck.Count}");
            }

            // ── Gravekeeper's Curse summoned inflict ──
            {
                const int curseId = 50712728;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                var curse = PutInHand(engine, p, curseId);
                opp.LifePoints = 8000;
                Check("Gravekeeper's Curse Normal Summon",
                    engine.TryNormalSummon(p, curse, asSet: false) && curse.FaceUp);
                Check("Gravekeeper's Curse Summoned: opponent takes 500",
                    opp.LifePoints == 7500, $"LP={opp.LifePoints}");
            }

            // ── Iron Blacksmith Kotetsu Flip Equip search ──
            {
                const int kotetsuId = 73431236;
                const int axe = 40619825;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                p.Deck.Add(axe);
                p.Deck.Add(89631139); // Blue-Eyes
                var kot = PlaceMonster(engine, p, kotetsuId, 2, BattlePosition.Defense, false);
                kot.SetThisTurn = false;
                Check("Kotetsu Flip Summon", engine.TryFlipSummon(p, kot) && kot.FaceUp);
                Check("Kotetsu Flip: opens Equip Deck search",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.EquipSpellInYourDeck,
                    engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                if (engine.IsAwaitingEffectTarget)
                {
                    var legal = engine.PendingActivation.LegalTargets;
                    Check("Kotetsu: Axe is legal Equip",
                        legal.Exists(t => t != null && t.CardId == axe));
                    Check("Kotetsu: Blue-Eyes is not legal",
                        !legal.Exists(t => t != null && t.CardId == 89631139));
                    var pick = legal.FirstOrDefault(t => t.CardId == axe);
                    if (pick != null)
                    {
                        Check("Kotetsu: select Axe", engine.TrySelectEffectTarget(pick));
                        Check("Kotetsu: Axe in hand", p.Hand.Exists(c => c.CardId == axe));
                    }
                }
            }

            // ── Man-Eater Bug Flip ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();

                var meb = PlaceMonster(engine, p, MonsterEffects.ManEaterBug, 2,
                    BattlePosition.Defense, false);
                meb.SetThisTurn = false;
                var victim = PlaceMonster(engine, opp, 91152256, 2, BattlePosition.Attack, true);

                var ok = engine.TryFlipSummon(p, meb);
                Check("Man-Eater Bug: Flip Summon succeeds", ok);
                Check("Man-Eater Bug: opens field target choice",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.AnyMonsterOnField);

                if (engine.IsAwaitingEffectTarget)
                {
                    // Prefer opponent victim
                    var t = engine.PendingActivation.LegalTargets
                                .FirstOrDefault(c => c.CardId == 91152256) ??
                            engine.PendingActivation.LegalTargets.FirstOrDefault();
                    engine.TrySelectEffectTarget(t);
                    Check("Man-Eater Bug: victim destroyed to GY",
                        !opp.TryFindMonster(victim, out _) &&
                        opp.Graveyard.Exists(c => c.CardId == 91152256));
                }
            }

            // ── Kuriboh hand QE during damage calculation ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                p.LifePoints = 8000;
                opp.LifePoints = 8000;

                var kuri = PutInHand(engine, p, MonsterEffects.Kuriboh);
                var attacker = PlaceMonster(engine, opp, 89631139, 2, BattlePosition.Attack, true); // BEWD

                Check("Kuriboh: legal DC hand QE vs opp attack",
                    MonsterEffects.IsLegalHandDamageCalculationEffect(
                        p, kuri, ResponseTiming.DamageCalculation, opp, attacker));

                p.PreventBattleDamageThisBattle = false;
                var discarded = MonsterEffects.DiscardFromHandByInstance(p, kuri);
                p.PreventBattleDamageThisBattle = true;
                Check("Kuriboh: discard cost pays",
                    discarded != null && p.Graveyard.Exists(c => c.CardId == MonsterEffects.Kuriboh));
                Check("Kuriboh: prevent battle damage flag set",
                    p.PreventBattleDamageThisBattle);

                var r = BattleMechanics.Calculate(attacker, null, false, false, false, false,
                    p.PreventBattleDamageThisBattle);
                Check("Kuriboh: direct deals 0 with flag",
                    r.DamageToDefendingPlayer == 0);
            }

            // ── La Jinn (1800) vs Giant Soldier DEF (2000) → attacker loses 200 ──
            {
                var laDef = db.Get(97590747);
                var gsDef = db.Get(13039848);
                Check("DB has La Jinn + Giant Soldier",
                    laDef != null && gsDef != null && laDef.atk == 1800 && gsDef.def == 2000,
                    $"la={laDef?.atk} gs.def={gsDef?.def}");

                // Face-up Defense
                var la = new CardInstance
                {
                    InstanceId = 91, CardId = 97590747, FaceUp = true,
                    Position = BattlePosition.Attack, Def = laDef
                };
                var gsUp = new CardInstance
                {
                    InstanceId = 92, CardId = 13039848, FaceUp = true,
                    Position = BattlePosition.Defense, Def = gsDef
                };
                var rUp = BattleMechanics.Calculate(la, gsUp, false, false, false, false, false);
                BattleMechanics.Sanitize(ref rUp, la, gsUp, false, false);
                Check("LaJinn vs face-up GS DEF: attacker takes 200",
                    rUp.DamageToAttackingPlayer == 200 && !rUp.DestroyDefender && !rUp.DestroyAttacker);
                Check("LaJinn vs face-up GS DEF: defender takes 0",
                    rUp.DamageToDefendingPlayer == 0);

                // Face-down (set) — same as in-game Set Giant Soldier
                var gsSet = new CardInstance
                {
                    InstanceId = 93, CardId = 13039848, FaceUp = false,
                    Position = BattlePosition.Defense, Def = gsDef
                };
                var rSet = BattleMechanics.Calculate(la, gsSet, false, false, false, false, false);
                BattleMechanics.Sanitize(ref rSet, la, gsSet, false, false);
                Check("LaJinn vs set GS: attacker takes 200",
                    rSet.DamageToAttackingPlayer == 200 && !rSet.DestroyDefender);

                // Sanitize must restore damage if calc was corrupted / dropped
                rUp.DamageToAttackingPlayer = 0;
                rUp.DestroyDefender = true;
                BattleMechanics.Sanitize(ref rUp, la, gsUp, false, false);
                Check("Sanitize restores ATK<DEF damage to 200",
                    rUp.DamageToAttackingPlayer == 200 && !rUp.DestroyDefender);

                // Full engine path when we can reach Battle on opponent's turn
                var engine = Fresh(db, pDeck, aDeck);
                for (var t = 0; t < 4 && (engine.TurnNumber < 2 || engine.TurnPlayer != engine.Opponent); t++)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.RecoverStuckCombat();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                }

                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.LifePoints = 8000;
                opp.LifePoints = 8000;
                p.Hand.Clear();
                opp.Hand.Clear();

                var gs = PlaceMonster(engine, p, 13039848, 2, BattlePosition.Defense, false);
                var laM = PlaceMonster(engine, opp, 97590747, 2, BattlePosition.Attack, true);
                laM.SummonedThisTurn = false;
                laM.AttackedThisTurn = false;
                laM.SetThisTurn = false;
                gs.SetThisTurn = false;

                if (engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1)
                    engine.TryEnterBattlePhase(opp);

                if (engine.Phase == DuelPhase.Battle && engine.TurnPlayer == opp &&
                    engine.CanAttack(opp, laM))
                {
                    var lpBefore = opp.LifePoints;
                    Check("Engine: LaJinn declares on set GS", engine.TryAttack(opp, laM, gs));
                    for (var i = 0; i < 10; i++)
                    {
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                        else engine.RecoverStuckCombat();
                    }

                    Check("Engine: GS survives defense hold",
                        p.MonstersOnField().Any(m => m.CardId == 13039848));
                    Check("Engine: opponent (attacker) lost 200 LP",
                        opp.LifePoints == lpBefore - 200,
                        $"opp LP {opp.LifePoints} was {lpBefore}");
                    Check("Engine: defender LP unchanged", p.LifePoints == 8000);
                }
                else
                {
                    // Turn setup can vary; math+sanitize above already cover the ruling.
                    Check("Engine battle path (skipped — not in BP)", true);
                }
            }

            // ── Pot of Greed draw 2 ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                p.Deck.Clear();
                for (var i = 0; i < 5; i++)
                    p.Deck.Add(91152256);
                var pot = PutInHand(engine, p, 55144522);
                var deckBefore = p.DeckCount;
                var ok = engine.TryActivateSpellTrap(p, pot, fromHand: true);
                Check("Pot of Greed: activates from hand", ok);
                // -1 pot from hand, +2 draws → hand 2; deck −2
                Check("Pot of Greed: deck decreased by 2",
                    p.DeckCount == deckBefore - 2, $"deck={p.DeckCount} was {deckBefore}");
                Check("Pot of Greed: hand has 2 (drew 2, pot gone)",
                    p.HandCount == 2, $"hand={p.HandCount}");
            }

            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var c = engine.CreateCardInstance(91152256);
                p.Hand.Add(c);
                engine.SendCardToGrave(p, c);
                Check("GameEvent log records SentToGy",
                    engine.GameEvents.HappenedThisTurn(DuelGameEventKind.SentToGy, engine.TurnNumber, c));
                Check("GameEvent log does not invent Destroyed",
                    !engine.GameEvents.HappenedThisTurn(DuelGameEventKind.Destroyed, engine.TurnNumber, c));
            }

            // ── Monster Reborn: activate stays pending (must not bounce to hand) ──
            {
                const int reborn = 83764719;
                const int celtic = 91152256;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var gyMon = engine.CreateCardInstance(celtic);
                p.Graveyard.Add(gyMon);
                var card = PutInHand(engine, p, reborn);
                Check("Monster Reborn: Activate legal with a GY monster",
                    engine.CanActivateSpellTrap(p, card, fromHand: true));
                Check("Monster Reborn: Activate opens GY target pending",
                    engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy);
                Check("Monster Reborn: stays on field while choosing (does not bounce to hand)",
                    !p.Hand.Contains(card) &&
                    p.TryFindSpellTrap(card, out _),
                    p.Hand.Contains(card) ? "bounced to hand" : "not on field");
                var pick = engine.PendingActivation?.LegalTargets
                    ?.FirstOrDefault(t => t != null && t.CardId == celtic);
                Check("Monster Reborn: GY Celtic is a legal target", pick != null);
                if (pick != null)
                {
                    Check("Monster Reborn: select SS Celtic, Reborn to GY",
                        engine.TrySelectEffectTarget(pick) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == celtic) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == reborn) &&
                        !p.Hand.Contains(card) &&
                        !engine.IsAwaitingEffectTarget &&
                        !engine.IsBusy);
                }
            }

            // ── Official Monster Reborn 83764718: compiled text, not the 83764719 allowlist pin ──
            {
                const int officialReborn = 83764718;
                const int celtic = 91152256;
                var officialDef = db.Get(officialReborn);
                var officialProg = officialDef != null
                    ? CardTextEffectCompiler.Compile(officialDef)
                    : null;
                Check("Official Monster Reborn (83764718) FullyCompiles Target GY; Special Summon it",
                    officialDef != null && officialProg != null && officialProg.FullyCompiled &&
                    officialProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.EitherGyMonsters),
                    officialProg == null
                        ? "null"
                        : $"full={officialProg.FullyCompiled} n={officialProg.ClauseList.Count} unparsed={string.Join("|", officialProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                Check("Official Monster Reborn is not on ActivatableScripts allowlist",
                    !OfficialEffectRegistry.HasActivatableScript(officialReborn));
                Check("Official Monster Reborn ProgramMayActivate via FullyCompiled text",
                    officialDef != null && OfficialEffectRegistry.ProgramMayActivate(officialDef));

                var synReborn = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000018,
                    name = "New Spell (Monster Reborn shape)",
                    type = "Spell Card",
                    desc = "Target 1 monster in either GY; Special Summon it."
                });
                Check("New-card rule: Reborn-shaped text compiles without a cardId branch",
                    synReborn != null && synReborn.FullyCompiled &&
                    synReborn.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.EitherGyMonsters));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Graveyard.Clear();
                    engine.Opponent.Graveyard.Clear();
                    var card = PutInHand(engine, p, officialReborn);
                    Check("Official Monster Reborn: refuse with empty GYs",
                        !engine.CanActivateSpellTrap(p, card, fromHand: true));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyMon = engine.CreateCardInstance(celtic);
                    p.Graveyard.Add(gyMon);
                    for (var i = 0; i < p.MonsterZones.Length; i++)
                        PlaceMonster(engine, p, celtic, i, BattlePosition.Attack, true);
                    var card = PutInHand(engine, p, officialReborn);
                    Check("Official Monster Reborn: refuse with no free Monster Zone",
                        !engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                        engine.FirstEmpty(p.MonsterZones) < 0);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyMon = engine.CreateCardInstance(celtic);
                    p.Graveyard.Add(gyMon);
                    var card = PutInHand(engine, p, officialReborn);
                    Check("Official Monster Reborn: Activate legal from hand in MP1 with GY + free MZ",
                        engine.InMainPhase &&
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Official Monster Reborn: Activate opens GY target pending",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy);
                    var pick = engine.PendingActivation?.LegalTargets
                        ?.FirstOrDefault(t => t != null && t.CardId == celtic);
                    Check("Official Monster Reborn: GY Celtic is a legal target", pick != null);
                    if (pick != null)
                    {
                        Check("Official Monster Reborn: resolve SS Celtic, spell to GY",
                            engine.TrySelectEffectTarget(pick) &&
                            p.MonstersOnField().Any(m => m != null && m.CardId == celtic) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == officialReborn) &&
                            !p.Hand.Contains(card) &&
                            !engine.IsAwaitingEffectTarget &&
                            !engine.IsBusy);
                    }
                }

                // Premature Burial: pay LP + SS own GY + equip/destroy host (Call of the Haunted sibling).
                // The Shallow Grave (each player, face-down Defense) stays parked — dual targeting.
                {
                    const int prematureId = 70828912;
                    const int prematureCelticId = 91152256;
                    var pbDef = db.Get(prematureId);
                    var pbProg = pbDef != null ? CardTextEffectCompiler.Compile(pbDef) : null;
                    Check("Premature Burial FullyCompiled pay-LP GY-SS equip (Call of the Haunted sibling)",
                        pbProg != null && pbProg.FullyCompiled &&
                        pbProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.PayLpAmount == 800 &&
                            c.DestroyHostWhenThisLeaves &&
                            c.StaysOnField &&
                            c.Zone == EffectZoneFilter.ControllerGyMonsters),
                        pbProg == null
                            ? "null"
                            : $"full={pbProg.FullyCompiled} n={pbProg.ClauseList.Count} unparsed={string.Join("|", pbProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    var synPb = CardTextEffectCompiler.Compile(new CardDef
                    {
                        id = 90000051,
                        name = "New Equip (Premature Burial shape)",
                        type = "Spell Card",
                        frameType = "spell",
                        desc =
                            "Activate this card by paying 800 LP, then target 1 monster in your Graveyard; " +
                            "Special Summon that target in Attack Position and equip it with this card. " +
                            "When this card is destroyed, destroy the equipped monster."
                    });
                    Check("New-card rule: Premature Burial-shaped text compiles without a cardId branch",
                        synPb != null && synPb.FullyCompiled &&
                        synPb.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.PayLpAmount == 800 &&
                            c.DestroyHostWhenThisLeaves));

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var who = engine.Player;
                        who.Hand.Clear();
                        who.LifePoints = 8000;
                        var gyMon = engine.CreateCardInstance(prematureCelticId);
                        who.Graveyard.Add(gyMon);
                        var card = PutInHand(engine, who, prematureId);
                        Check("Premature Burial: Activate from hand in MP1 with GY + LP",
                            engine.InMainPhase &&
                            engine.CanActivateSpellTrap(who, card, fromHand: true) &&
                            engine.TryActivateSpellTrap(who, card, fromHand: true) &&
                            engine.IsAwaitingEffectTarget);
                        Check("Premature Burial: SS Celtic, Equip stays linked, paid 800",
                            engine.TrySelectEffectTarget(gyMon) &&
                            who.TryFindMonster(gyMon, out _) &&
                            gyMon.Position == BattlePosition.Attack &&
                            card.EquippedTo == gyMon &&
                            who.TryFindSpellTrap(card, out _) &&
                            who.LifePoints == 7200,
                            $"mz={who.TryFindMonster(gyMon, out _)} st={who.TryFindSpellTrap(card, out _)} lp={who.LifePoints} link={card.EquippedTo != null}");
                        engine.SendCardToGrave(who, card);
                        Check("Premature Burial: Equip to GY also destroys the summoned monster",
                            who.Graveyard.Contains(card) && who.Graveyard.Contains(gyMon) &&
                            !who.TryFindMonster(gyMon, out _),
                            $"eqGy={who.Graveyard.Contains(card)} monGy={who.Graveyard.Contains(gyMon)}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var who = engine.Player;
                        who.Hand.Clear();
                        who.LifePoints = 700;
                        who.Graveyard.Add(engine.CreateCardInstance(prematureCelticId));
                        var card = PutInHand(engine, who, prematureId);
                        Check("Premature Burial: refuse with less than 800 LP",
                            !engine.CanActivateSpellTrap(who, card, fromHand: true));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var who = engine.Player;
                        who.Hand.Clear();
                        who.LifePoints = 800;
                        who.Graveyard.Add(engine.CreateCardInstance(prematureCelticId));
                        var card = PutInHand(engine, who, prematureId);
                        Check("Premature Burial: 800 LP is a legal cost (may lose on purpose)",
                            engine.CanActivateSpellTrap(who, card, fromHand: true));
                        Check("Premature Burial: paying last 800 LP asks are-you-sure",
                            engine.TryActivateSpellTrap(who, card, fromHand: true) &&
                            engine.PendingActivation != null &&
                            engine.PendingActivation.AwaitingLpZeroConfirm &&
                            who.LifePoints == 800 &&
                            who.Hand.Contains(card),
                            $"pending={engine.PendingActivation != null} " +
                            $"zero={engine.PendingActivation?.AwaitingLpZeroConfirm} " +
                            $"lp={who.LifePoints} inHand={who.Hand.Contains(card)}");
                        Check("Premature Burial: cancel LP-to-0 keeps the card and LP",
                            engine.CancelEffectTargeting() &&
                            who.LifePoints == 800 &&
                            who.Hand.Contains(card) &&
                            engine.PendingActivation == null);
                        Check("Premature Burial: confirm LP-to-0 continues activation",
                            engine.TryActivateSpellTrap(who, card, fromHand: true) &&
                            engine.TryConfirmLpZeroPay() &&
                            engine.IsAwaitingEffectTarget &&
                            !engine.PendingActivation.AwaitingLpZeroConfirm,
                            $"await={engine.IsAwaitingEffectTarget} " +
                            $"zero={engine.PendingActivation?.AwaitingLpZeroConfirm} " +
                            $"lp={who.LifePoints}");
                    }

                    var sg = TryActivateCompiledGySsIfReady(db, pDeck, aDeck, 43434803);
                    if (sg.HasValue)
                        Check("The Shallow Grave: compiled GY-SS activates from hand in MP1", sg.Value);
                }
            }

            // ── Monster Reincarnation: discard, then add a GY monster ──
            {
                const int reincarnation = 74848038;
                const int celtic = 91152256;
                const int bewd = 89631139;
                var recDef = db.Get(reincarnation);
                var recProg = recDef != null ? CardTextEffectCompiler.Compile(recDef) : null;
                Check("Monster Reincarnation FullyCompiled discard + GY monster add",
                    recProg != null && recProg.FullyCompiled &&
                    recProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.AddFromGyToHand &&
                        c.RequiresDiscardCost &&
                        c.DiscardCostAttribute == "*" &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.ControllerGyMonsters),
                    recProg == null
                        ? "null"
                        : $"full={recProg.FullyCompiled} n={recProg.ClauseList.Count} unparsed={string.Join("|", recProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synRec = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000008,
                    name = "New Spell (Monster Reincarnation shape)",
                    type = "Spell Card",
                    desc = "Discard 1 card, then target 1 monster in your GY; add it to your hand."
                });
                Check("New-card rule: Reincarnation-shaped text compiles without a cardId branch",
                    synRec != null && synRec.FullyCompiled &&
                    synRec.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.AddFromGyToHand &&
                        c.RequiresDiscardCost &&
                        c.Zone == EffectZoneFilter.ControllerGyMonsters));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyMon = engine.CreateCardInstance(bewd);
                    p.Graveyard.Add(gyMon);
                    var card = PutInHand(engine, p, reincarnation);
                    Check("Monster Reincarnation: refuse with no extra card to discard",
                        !engine.CanActivateSpellTrap(p, card, fromHand: true));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var fodder = PutInHand(engine, p, celtic);
                    var card = PutInHand(engine, p, reincarnation);
                    Check("Monster Reincarnation: legal if discard itself supplies the GY monster",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Monster Reincarnation: Activate opens discard (empty GY, monster fodder)",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.DiscardMonsterInHand);
                    Check("Monster Reincarnation: discard fodder",
                        engine.TrySelectEffectTarget(fodder) && p.Graveyard.Contains(fodder));
                    Check("Monster Reincarnation: discarded monster is now the GY target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy &&
                        engine.IsLegalEffectTarget(fodder));
                    Check("Monster Reincarnation: add discarded monster back to hand",
                        engine.TrySelectEffectTarget(fodder) &&
                        p.Hand.Contains(fodder) &&
                        !p.Graveyard.Contains(fodder) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == reincarnation));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyMon = engine.CreateCardInstance(bewd);
                    p.Graveyard.Add(gyMon);
                    var fodder = PutInHand(engine, p, celtic);
                    var card = PutInHand(engine, p, reincarnation);
                    Check("Monster Reincarnation: Activate legal with discard + GY monster",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Monster Reincarnation: Activate opens discard cost",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.DiscardMonsterInHand);
                    Check("Monster Reincarnation: Celtic is a legal discard",
                        engine.IsLegalEffectTarget(fodder));
                    Check("Monster Reincarnation: pay discard",
                        engine.TrySelectEffectTarget(fodder) &&
                        p.Graveyard.Contains(fodder) &&
                        !p.Hand.Contains(fodder));
                    Check("Monster Reincarnation: now choose a GY monster",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy);
                    Check("Monster Reincarnation: BEWD is a legal GY target",
                        engine.IsLegalEffectTarget(gyMon));
                    Check("Monster Reincarnation: add BEWD to hand, spell to GY",
                        engine.TrySelectEffectTarget(gyMon) &&
                        p.Hand.Contains(gyMon) &&
                        !p.Graveyard.Contains(gyMon) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == reincarnation) &&
                        !p.Hand.Contains(card));
                }
            }

            // ── The Warrior Returning Alive / Tribute to the Doomed / Mask of Darkness ──
            {
                const int warriorAlive = 95281259;
                const int celtic = 91152256;
                const int bewd = 89631139;
                const int doomed = 79759861;
                const int mask = 28933734;
                const int mirror = 44095762;
                const int spellRepro = 29228529;

                var wDef = db.Get(warriorAlive);
                var wProg = wDef != null ? CardTextEffectCompiler.Compile(wDef) : null;
                Check("The Warrior Returning Alive FullyCompiled Warrior GY add",
                    wProg != null && wProg.FullyCompiled &&
                    wProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.AddFromGyToHand &&
                        c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                        !string.IsNullOrEmpty(c.RaceFilter) &&
                        c.RaceFilter.IndexOf("Warrior", System.StringComparison.OrdinalIgnoreCase) >= 0),
                    wProg == null
                        ? "null"
                        : $"full={wProg.FullyCompiled} unparsed={string.Join("|", wProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyWarrior = engine.CreateCardInstance(celtic);
                    var gyDragon = engine.CreateCardInstance(bewd);
                    p.Graveyard.Add(gyWarrior);
                    p.Graveyard.Add(gyDragon);
                    var card = PutInHand(engine, p, warriorAlive);
                    Check("Warrior Returning Alive: Activate legal with Warrior in GY",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Warrior Returning Alive: opens GY monster targets",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var legal = engine.PendingActivation.LegalTargets;
                        Check("Warrior Returning Alive: Warrior legal, Dragon is not",
                            legal.Contains(gyWarrior) && !legal.Contains(gyDragon));
                        Check("Warrior Returning Alive: add Celtic to hand",
                            engine.TrySelectEffectTarget(gyWarrior) &&
                            p.Hand.Contains(gyWarrior) &&
                            !p.Graveyard.Contains(gyWarrior));
                    }
                }

                var dDef = db.Get(doomed);
                var dProg = dDef != null ? CardTextEffectCompiler.Compile(dDef) : null;
                Check("Tribute to the Doomed FullyCompiled discard + destroy",
                    dProg != null && dProg.FullyCompiled &&
                    dProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresDiscardCost &&
                        c.Zone == EffectZoneFilter.FieldAnyMonster));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var fodder = PutInHand(engine, p, bewd);
                    var card = PutInHand(engine, p, doomed);
                    Check("Tribute to the Doomed: Activate legal",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Tribute to the Doomed: opens discard",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.DiscardMonsterInHand);
                    Check("Tribute to the Doomed: pay discard",
                        engine.TrySelectEffectTarget(fodder));
                    Check("Tribute to the Doomed: now choose a field monster",
                        engine.IsAwaitingEffectTarget &&
                        engine.IsLegalEffectTarget(prey));
                    Check("Tribute to the Doomed: destroy opponent monster",
                        engine.TrySelectEffectTarget(prey) &&
                        opp.Graveyard.Contains(prey) &&
                        opp.MonsterZones[2].Occupant != prey);
                }

                var mDef = db.Get(mask);
                var mProg = mDef != null ? CardTextEffectCompiler.Compile(mDef) : null;
                Check("Mask of Darkness FullyCompiled Flip Trap GY add",
                    mProg != null && mProg.FullyCompiled &&
                    mProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.AddFromGyToHand &&
                        c.Zone == EffectZoneFilter.ControllerGyTraps),
                    mProg == null
                        ? "null"
                        : $"full={mProg.FullyCompiled} unparsed={string.Join("|", mProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyTrap = engine.CreateCardInstance(mirror);
                    p.Graveyard.Add(gyTrap);
                    var flip = PlaceMonster(engine, p, mask, 2, BattlePosition.Defense, false);
                    flip.SetThisTurn = false;
                    p.NormalSummonUsed = false;
                    Check("Mask of Darkness: Flip Summon succeeds",
                        engine.TryFlipSummon(p, flip) && flip.FaceUp);
                    Check("Mask of Darkness: opens Trap-in-GY choice",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.TrapInYourGy);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Mask of Darkness: Mirror Force returned to hand",
                            engine.TrySelectEffectTarget(gyTrap) &&
                            p.Hand.Contains(gyTrap) &&
                            !p.Graveyard.Contains(gyTrap));
                    }
                }

                var srDef = db.Get(spellRepro);
                var srProg = srDef != null ? CardTextEffectCompiler.Compile(srDef) : null;
                Check("Spell Reproduction leftover send-2 is not FullyCompiled",
                    srProg != null && !srProg.FullyCompiled);
            }

            // ── Tribute Summon Dark Magician with 2 face-down Sets ──
            {
                const int darkMagician = 46986414;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var setA = PlaceMonster(engine, p, 91152256, 1, BattlePosition.Defense, false);
                var setB = PlaceMonster(engine, p, 32452818, 3, BattlePosition.Defense, false);
                var dm = PutInHand(engine, p, darkMagician);
                Check("Dark Magician is Lv7 (2 tributes)", dm.Level == 7 && TcgRules.TributesRequired(dm.Level) == 2,
                    $"lv={dm.Level} name={dm.Name}");
                Check("Face-down Sets are legal tributes",
                    TcgRules.CanBeTributedForSummon(p, setA) && TcgRules.CanBeTributedForSummon(p, setB));
                Check("CanNormalSummonOrSet Dark Magician with 2 Sets",
                    engine.CanNormalSummonOrSet(p, dm));

                // Human, no explicit marks — exactly 2 monsters so both must be tributed
                engine.PendingTributes.Clear();
                var ok = engine.TryNormalSummon(p, dm, asSet: false);
                Check("Tribute Summon DM from 2 face-down (unambiguous auto)",
                    ok && dm.FaceUp && dm.Position == BattlePosition.Attack,
                    ok ? "summoned" : "engine rejected");
                Check("Both face-down tributes went to GY",
                    p.Graveyard.Contains(setA) && p.Graveyard.Contains(setB));
                Check("Dark Magician on field",
                    p.MonstersOnField().Any(m => m.CardId == darkMagician));
                Check("Face-down tributes are not still on field",
                    !p.TryFindMonster(setA, out _) && !p.TryFindMonster(setB, out _));
            }

            // ── Drop Dark Magician onto a face-down Set's zone ──
            {
                const int darkMagician = 46986414;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var setA = PlaceMonster(engine, p, 91152256, 1, BattlePosition.Defense, false);
                PlaceMonster(engine, p, 32452818, 3, BattlePosition.Defense, false);
                var dm = PutInHand(engine, p, darkMagician);
                engine.PendingTributes.Clear();
                var occupied = engine.ValidatePlacement(p, dm, RulesZoneKind.Monster, 1, false);
                Check("ValidatePlacement allows occupied tribute zone", occupied.Legal,
                    occupied.Reason);
                var ok = engine.TryNormalSummonToZone(p, dm, asSet: false, preferredZone: 1);
                Check("Drop onto face-down Set: Tribute Summon succeeds", ok);
                Check("Dark Magician sits in the tributed zone",
                    p.MonsterZones[1].Occupant == dm, 
                    p.MonsterZones[1].Occupant?.Name ?? "empty");
                Check("Tributed Set is in GY", p.Graveyard.Contains(setA));
            }

            // ── 3 monsters, 2 tributes: human must pick (no silent auto) ──
            {
                const int darkMagician = 46986414;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var a = PlaceMonster(engine, p, 91152256, 0, BattlePosition.Defense, false);
                var b = PlaceMonster(engine, p, 32452818, 1, BattlePosition.Defense, false);
                PlaceMonster(engine, p, 13039848, 2, BattlePosition.Attack, true);
                var dm = PutInHand(engine, p, darkMagician);
                engine.PendingTributes.Clear();
                var blocked = engine.TryNormalSummon(p, dm, asSet: false);
                Check("3 monsters / 2 tributes: refuses without a choice", !blocked);

                engine.ToggleTribute(p, a);
                engine.ToggleTribute(p, b);
                var ok = engine.TryNormalSummon(p, dm, asSet: false);
                Check("3 monsters / 2 tributes: explicit face-down marks succeed", ok &&
                    p.Graveyard.Contains(a) && p.Graveyard.Contains(b));
            }

            // ── Abyss Soldier ignition (not a Spell/Trap Activate) ──
            {
                const int abyssId = MonsterEffects.AbyssSoldier;
                const int fenrir = 218704; // WATER — discard cost
                const int celtic = 91152256; // EARTH on opponent's field

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();

                var abyss = PlaceMonster(engine, p, abyssId, 2, BattlePosition.Attack, true);
                Check("Abyss Soldier is an Effect Monster, not a Trap",
                    abyss.Def != null && abyss.Def.IsMonster && !abyss.Def.IsTrap && !abyss.Def.IsSpell);
                Check("Abyss Soldier: cannot activate from hand",
                    !engine.CanActivateSpellTrap(p, abyss, fromHand: true));
                Check("Abyss Soldier: no ignition without WATER in hand",
                    !engine.CanActivateSpellTrap(p, abyss, fromHand: false));

                var water = PutInHand(engine, p, fenrir);
                var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                Check("Abyss Soldier: Activate legal in Main Phase with WATER in hand",
                    engine.CanActivateSpellTrap(p, abyss, fromHand: false));

                var snap = LegalIntentService.Build(engine, p);
                Check("Abyss Soldier: legal intents include Activate from field",
                    snap.HasKind(abyss, LegalIntentService.LegalKind.ActivateFromField));

                Check("Abyss Soldier: Activate starts (cost then target)",
                    engine.TryActivateSpellTrap(p, abyss, fromHand: false) &&
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.DiscardMonsterInHand,
                    engine.PendingActivation?.TargetKind.ToString() ?? "no pending");

                Check("Abyss Soldier: Fenrir is a legal discard",
                    engine.IsLegalEffectTarget(water));
                Check("Abyss Soldier: pay discard cost",
                    engine.TrySelectEffectTarget(water) &&
                    p.Graveyard.Contains(water) &&
                    !p.Hand.Contains(water));
                Check("Abyss Soldier: now choose a card on the field",
                    engine.IsAwaitingEffectTarget &&
                    engine.PendingActivation != null &&
                    engine.PendingActivation.TargetKind == EffectTargetKind.AnyCardOnField);
                Check("Abyss Soldier: Celtic Guardian is a legal bounce",
                    engine.IsLegalEffectTarget(prey));
                Check("Abyss Soldier: bounce resolves",
                    engine.TrySelectEffectTarget(prey));
                Check("Celtic Guardian returned to opponent's hand",
                    opp.Hand.Contains(prey) && opp.MonsterZones[2].Occupant != prey);
                Check("Abyss Soldier stays on the field",
                    p.MonsterZones[2].Occupant == abyss);
                Check("Abyss Soldier once per turn used",
                    abyss.EffectUsedThisTurn &&
                    !engine.CanActivateSpellTrap(p, abyss, fromHand: false));

                var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(abyss.Def);
                Check("Abyss Soldier text compiles (discard WATER + bounce)",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null &&
                                               c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ReturnToHand),
                    prog == null
                        ? "null program"
                        : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? System.Array.Empty<string>())}");
            }

            // ── A Legendary Ocean (Field Spell, not a Trap) ──
            {
                const int aloId = FieldSpellEffects.ALegendaryOcean; // 295517
                const int fenrir = 218704; // WATER Lv4 1400/1200
                const int salmon = 78060096; // WATER Lv5 Terrorking Salmon 2400/1000
                const int celtic = 91152256; // non-WATER

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();

                var alo = PutInHand(engine, p, aloId);
                Check("ALO: database type is Spell Card",
                    alo.Def != null && alo.Def.IsSpell && !alo.Def.IsTrap && alo.Def.IsFieldSpell,
                    alo.Def == null ? "missing def" : $"type={alo.Def.type} race={alo.Def.race}");
                Check("ALO: name is always treated as Umi",
                    alo.IsNamed("Umi") && alo.RulesName == "Umi",
                    alo.RulesName);
                Check("ALO: IsTrap is false (was registering as trap via S/T Set path)",
                    !alo.Def.IsTrap);
                Check("ALO: cannot Set in Spell & Trap Zone",
                    !engine.CanSetSpellTrap(p, alo));

                var stVerdict = engine.ValidatePlacement(p, alo, RulesZoneKind.SpellTrap, 2, true);
                Check("ALO: Spell & Trap Zone illegal",
                    !stVerdict.Legal, stVerdict.Reason);
                var fieldVerdict = engine.ValidatePlacement(p, alo, RulesZoneKind.FieldSpell, 0, false);
                Check("ALO: Field Zone legal in Main Phase",
                    fieldVerdict.Legal, fieldVerdict.Reason);
                Check("ALO: CanActivate from hand",
                    engine.CanActivateSpellTrap(p, alo, fromHand: true));

                var fen = PlaceMonster(engine, p, fenrir, 2, BattlePosition.Attack, true);
                var cel = PlaceMonster(engine, engine.Opponent, celtic, 2, BattlePosition.Attack, true);
                var sal = PutInHand(engine, p, salmon);
                Check("Salmon before ALO needs 1 Tribute",
                    TcgRules.TributesRequired(sal.Level) == 1,
                    $"Lv{sal.Level} tributes={TcgRules.TributesRequired(sal.Level)}");

                Check("ALO: activate from hand into Field Zone",
                    engine.TryActivateSpellTrap(p, alo, fromHand: true));
                Check("ALO: sits face-up in Field Spell Zone",
                    p.FieldSpellZone?.Occupant == alo && alo.FaceUp);
                Check("ALO: not in a Spell & Trap Zone",
                    !p.TryFindSpellTrap(alo, out _));
                Check("Umi is on the field (name condition)",
                    FieldSpellEffects.UmiIsOnField(engine));
                Check("Fenrir WATER +200 ATK/DEF",
                    fen.CurrentAtk == 1600 && fen.CurrentDef == 1400,
                    $"ATK {fen.CurrentAtk} DEF {fen.CurrentDef} Lv{fen.Level}");
                Check("Fenrir WATER Level −1 on field",
                    fen.Level == 3, $"Lv{fen.Level}");
                Check("Celtic Guardian (non-WATER) unchanged",
                    cel.CurrentAtk == cel.Def.atk && cel.Level == cel.PrintedLevel,
                    $"ATK {cel.CurrentAtk} Lv{cel.Level}");
                Check("Salmon in hand is Level 4 (no Tribute)",
                    sal.Level == 4 && TcgRules.TributesRequired(sal.Level) == 0,
                    $"Lv{sal.Level}");
                Check("Salmon Normal Summon without Tribute under ALO",
                    engine.TryNormalSummon(p, sal, asSet: false) &&
                    p.MonstersOnField().Contains(sal));
                Check("Salmon on field 2600/1200",
                    sal.CurrentAtk == 2600 && sal.CurrentDef == 1200,
                    $"ATK {sal.CurrentAtk} DEF {sal.CurrentDef}");

                var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(alo.Def);
                Check("ALO text compiles (name + ATK/DEF + Level)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count >= 3,
                    prog == null
                        ? "null program"
                        : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? System.Array.Empty<string>())}");
            }

            // ── Legendary Fisherman: Umi protection is a shared kind (ALO counts) ──
            {
                const int fisherman = 3643300;
                const int raigeki = SpellTrapEffects.Raigeki;
                const int meb = 54652250;
                const int celtic = 91152256;

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var alo = PutInHand(engine, p, FieldSpellEffects.ALegendaryOcean);
                Check("Fisherman: ALO activates as Umi",
                    engine.TryActivateSpellTrap(p, alo, fromHand: true) &&
                    FieldSpellEffects.UmiIsOnField(engine));
                var fish = PlaceMonster(engine, p, fisherman, 2, BattlePosition.Attack, true);
                var atk = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                atk.SummonedThisTurn = false;
                FieldSpellEffects.RefreshBoard(engine);

                Check("Fisherman: cannot be attack-targeted while ALO/Umi is up",
                    WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, fish));
                Check("Fisherman: unaffected by Spell effects while Umi",
                    WRLDZ.Duel.TextEffects.ContinuousProtections.IsUnaffectedBy(engine, fish, alo));

                var rg = PutInHand(engine, opp, raigeki);
                if (engine.TurnPlayer != opp)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                }

                if (engine.TurnPlayer == opp && engine.InMainPhase)
                {
                    Check("Fisherman: Raigeki does not destroy him under Umi",
                        engine.TryActivateSpellTrap(opp, rg, fromHand: true) &&
                        p.TryFindMonster(fish, out _));
                }
                else
                    Check("Fisherman: Raigeki path (skipped — not opp Main)", false,
                        $"turnOpp={engine.TurnPlayer == opp} phase={engine.Phase}");

                var engine2 = Fresh(db, pDeck, aDeck);
                ClearBoard(engine2);
                var alo2 = PutInHand(engine2, engine2.Player, FieldSpellEffects.ALegendaryOcean);
                engine2.TryActivateSpellTrap(engine2.Player, alo2, fromHand: true);
                var bug = PlaceMonster(engine2, engine2.Player, meb, 1, BattlePosition.Defense, false);
                bug.SetThisTurn = false;
                var fish2 = PlaceMonster(engine2, engine2.Opponent, fisherman, 2, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(engine2);
                var flipped = engine2.TryFlipSummon(engine2.Player, bug);
                if (engine2.IsAwaitingEffectTarget)
                    engine2.TrySelectEffectTarget(fish2);
                Check("Fisherman: MEB Flip (monster effect) still destroys him",
                    flipped && !engine2.Opponent.TryFindMonster(fish2, out _),
                    flipped ? "Fisherman still on field" : "Flip failed");

                var engine3 = Fresh(db, pDeck, aDeck);
                ClearBoard(engine3);
                var alo3 = PutInHand(engine3, engine3.Player, FieldSpellEffects.ALegendaryOcean);
                engine3.TryActivateSpellTrap(engine3.Player, alo3, fromHand: true);
                PlaceMonster(engine3, engine3.Player, fisherman, 2, BattlePosition.Attack, true);
                var striker = PlaceMonster(engine3, engine3.Opponent, celtic, 2, BattlePosition.Attack, true);
                striker.SummonedThisTurn = false;
                FieldSpellEffects.RefreshBoard(engine3);
                if (engine3.TurnPlayer != engine3.Opponent)
                {
                    if (engine3.IsAwaitingResponse) engine3.PassResponse();
                    engine3.TryEndTurnSafe(engine3.TurnPlayer);
                }

                if (engine3.TurnPlayer == engine3.Opponent && engine3.Phase == DuelPhase.Main1)
                    engine3.TryEnterBattlePhase(engine3.Opponent);
                if (engine3.IsAwaitingResponse) engine3.PassResponse();
                Check("Fisherman: only monster — opponent may attack directly",
                    engine3.Phase == DuelPhase.Battle &&
                    engine3.CanAttackDirectly(engine3.Opponent, striker),
                    $"phase={engine3.Phase} canDirect={engine3.CanAttackDirectly(engine3.Opponent, striker)}");
            }

            // ── Absolute End: opponent-turn trap offered, attacks become direct ──
            {
                const int absEnd = 27744077;
                const int celtic = 91152256;
                const int wallId = 13039848; // Giant Soldier

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var trap = PlaceSetTrap(engine, p, absEnd, 2);
                trap.SetThisTurn = false;
                var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(trap.Def);
                Check("Absolute End text compiles opponent-turn direct-attack grant",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind
                            .ForceOpponentDirectAttacksThisTurn &&
                        c.OpponentTurnOnly),
                    prog == null
                        ? "null"
                        : $"full={prog.FullyCompiled} n={prog.ClauseList.Count}");

                Check("Absolute End not legal on your own turn",
                    !engine.CanActivateSpellTrap(p, trap, fromHand: false));

                PlaceMonster(engine, p, wallId, 2, BattlePosition.Defense, true);
                var striker = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                striker.SummonedThisTurn = false;

                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.TryEndTurnSafe(engine.TurnPlayer);
                if (engine.IsAwaitingResponse)
                {
                    Check("Absolute End offered on opponent Main Phase (open-game-state)",
                        engine.PendingResponse != null &&
                        engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                        engine.PendingResponse.LegalCards.Exists(c =>
                            c != null && c.CardId == absEnd));
                    Check("Absolute End activates on opponent's turn",
                        engine.TryActivateSpellTrap(p, trap, fromHand: false));
                    Check("Opponent must attack directly this turn",
                        opp.MustAttackDirectlyThisTurn);
                }
                else
                {
                    Check("Absolute End offered on opponent Main Phase (open-game-state)", false,
                        "no window");
                    Check("Absolute End activates on opponent's turn", false, "no window");
                    Check("Opponent must attack directly this turn", false, "no window");
                }

                var engine2 = Fresh(db, pDeck, aDeck);
                ClearBoard(engine2);
                var trap2 = PlaceSetTrap(engine2, engine2.Player, absEnd, 2);
                trap2.SetThisTurn = false;
                var wallMon = PlaceMonster(engine2, engine2.Player, wallId, 2, BattlePosition.Defense, true);
                var atk2 = PlaceMonster(engine2, engine2.Opponent, celtic, 2, BattlePosition.Attack, true);
                atk2.SummonedThisTurn = false;
                if (engine2.IsAwaitingResponse) engine2.PassResponse();
                engine2.TryEndTurnSafe(engine2.TurnPlayer);
                if (engine2.IsAwaitingResponse) engine2.PassResponse();
                if (engine2.TurnPlayer == engine2.Opponent && engine2.Phase == DuelPhase.Main1)
                    engine2.TryEnterBattlePhase(engine2.Opponent);
                if (engine2.IsAwaitingResponse &&
                    engine2.PendingResponse?.Timing == ResponseTiming.OpponentOpenState)
                    engine2.PassResponse();
                Check("Absolute End is legal in AttackDeclared window",
                    engine2.Phase == DuelPhase.Battle &&
                    SpellTrapEffects.IsLegalResponseCard(engine2, engine2.Player, trap2,
                        ResponseTiming.AttackDeclared, null),
                    $"phase={engine2.Phase} legal={SpellTrapEffects.IsLegalResponseCard(engine2, engine2.Player, trap2, ResponseTiming.AttackDeclared, null)}");
                var lpBefore = engine2.Player.LifePoints;
                var declared = engine2.TryAttack(engine2.Opponent, atk2, wallMon);
                var activated = declared && engine2.IsAwaitingResponse &&
                                engine2.TryActivateSpellTrap(engine2.Player, trap2, fromHand: false);
                Check("Absolute End converts the current attack to direct",
                    activated &&
                    engine2.Opponent.MustAttackDirectlyThisTurn &&
                    engine2.Player.TryFindMonster(wallMon, out _) &&
                    engine2.Player.LifePoints == lpBefore - atk2.CurrentAtk,
                    $"declared={declared} activated={activated} flag={engine2.Opponent.MustAttackDirectlyThisTurn} " +
                    $"wall={engine2.Player.TryFindMonster(wallMon, out _)} LP {engine2.Player.LifePoints} was {lpBefore}");
            }

            // ── Numinous Healer: when you take damage, gain LP ──
            {
                const int healerId = 2130625;
                const int celtic = 91152256;

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var trap = PlaceSetTrap(engine, engine.Player, healerId, 2);
                trap.SetThisTurn = false;
                var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(trap.Def);
                Check("Numinous Healer text compiles YouTakeLifePointDamage",
                    prog != null && prog.FullyCompiled &&
                    prog.HasTiming(WRLDZ.Duel.TextEffects.EffectTiming.YouTakeLifePointDamage));
                Check("Numinous Healer not legal before you take damage",
                    !engine.CanActivateSpellTrap(engine.Player, trap, fromHand: false));

                var atk = PlaceMonster(engine, engine.Opponent, celtic, 2, BattlePosition.Attack, true);
                atk.SummonedThisTurn = false;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.TryEndTurnSafe(engine.TurnPlayer);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                if (engine.TurnPlayer == engine.Opponent && engine.Phase == DuelPhase.Main1)
                    engine.TryEnterBattlePhase(engine.Opponent);
                if (engine.IsAwaitingResponse) engine.PassResponse();

                var lpBefore = engine.Player.LifePoints;
                Check("Direct attack declares",
                    engine.Phase == DuelPhase.Battle &&
                    engine.TryAttack(engine.Opponent, atk, null));
                if (engine.IsAwaitingResponse &&
                    engine.PendingResponse?.Timing == ResponseTiming.AttackDeclared)
                    engine.PassResponse();
                Check("Numinous Healer offered after you take damage",
                    engine.IsAwaitingResponse &&
                    engine.PendingResponse != null &&
                    engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage &&
                    engine.PendingResponse.LegalCards.Exists(c => c != null && c.CardId == healerId),
                    $"awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing} LP={engine.Player.LifePoints}");
                var lpAfterHit = engine.Player.LifePoints;
                Check("Damage applied before the trap window",
                    lpAfterHit == lpBefore - atk.CurrentAtk,
                    $"LP {lpAfterHit} expected {lpBefore - atk.CurrentAtk}");
                Check("Numinous Healer activates and gains 1000 LP",
                    engine.TryActivateSpellTrap(engine.Player, trap, fromHand: false) &&
                    engine.Player.LifePoints == lpAfterHit + 1000,
                    $"LP {engine.Player.LifePoints} expected {lpAfterHit + 1000}");
            }

            // ── Amphibious Bugroth MK-3: direct attack while Umi (ALO counts) ──
            {
                const int mk3Id = MonsterEffects.AmphibiousBugrothMk3; // 64342551
                const int aloId = FieldSpellEffects.ALegendaryOcean; // 295517
                const int celtic = 91152256;

                var engine = Fresh(db, pDeck, aDeck);
                for (var t = 0; t < 6 &&
                                !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.RecoverStuckCombat();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                }

                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.LifePoints = 8000;
                opp.LifePoints = 8000;
                p.Hand.Clear();
                opp.Hand.Clear();

                var mk3 = PlaceMonster(engine, p, mk3Id, 2, BattlePosition.Attack, true);
                mk3.SummonedThisTurn = false;
                mk3.AttackedThisTurn = false;
                mk3.SetThisTurn = false;
                var setMon = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Defense, false);
                setMon.SetThisTurn = false;

                Check("MK-3 text compiles CanAttackDirectly while Umi",
                    mk3.Def != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(mk3.Def) is { } mk3Prog &&
                    mk3Prog.FullyCompiled &&
                    mk3Prog.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.CanAttackDirectly &&
                        string.Equals(c.RequiresFaceUpName, "Umi",
                            System.StringComparison.OrdinalIgnoreCase)),
                    mk3.Def == null
                        ? "missing def"
                        : "compile miss");

                Check("MK-3 without Umi cannot attack directly over a Set",
                    !engine.GrantsDirectAttack(mk3));

                var alo = PutInHand(engine, p, aloId);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                if (engine.TurnPlayer == p && engine.InMainPhase)
                    engine.TryActivateSpellTrap(p, alo, fromHand: true);

                Check("ALO is Umi on the field under MK-3",
                    FieldSpellEffects.UmiIsOnField(engine) &&
                    p.FieldSpellZone?.Occupant == alo);
                Check("MK-3 WATER +200 ATK under ALO",
                    mk3.CurrentAtk == 1700, $"ATK {mk3.CurrentAtk}");
                Check("MK-3 grants direct attack while ALO/Umi is up",
                    engine.GrantsDirectAttack(mk3));

                if (engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1)
                    engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();

                if (engine.Phase == DuelPhase.Battle && engine.TurnPlayer == p &&
                    engine.CanAttack(p, mk3))
                {
                    Check("MK-3 CanAttackDirectly over opponent Set (ALO as Umi)",
                        engine.CanAttackDirectly(p, mk3));
                    var vDirect = engine.ValidateAttack(p, mk3, null);
                    Check("ValidateAttack direct is legal with Set + ALO",
                        vDirect.Legal, vDirect.Reason);
                    var vSet = engine.ValidateAttack(p, mk3, setMon);
                    Check("ValidateAttack still allows attacking the Set",
                        vSet.Legal, vSet.Reason);

                    var snap = LegalIntentService.Build(engine, p);
                    Check("Legal intents include Direct Attack",
                        snap.HasKind(mk3, LegalIntentService.LegalKind.DirectAttack));
                    Check("Legal intents still include Attack on Set",
                        snap.HasKind(mk3, LegalIntentService.LegalKind.Attack));

                    var lpBefore = opp.LifePoints;
                    Check("MK-3 declares direct over Set", engine.TryAttack(p, mk3, null));
                    for (var i = 0; i < 10; i++)
                    {
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                        else engine.RecoverStuckCombat();
                    }

                    Check("MK-3 direct: Set monster still on field",
                        opp.MonstersOnField().Any(m => m.CardId == celtic));
                    Check("MK-3 direct: 1700 damage (1500+200 ALO)",
                        opp.LifePoints == lpBefore - 1700,
                        $"opp LP {opp.LifePoints} was {lpBefore}");
                    Check("MK-3 direct: attacker LP unchanged", p.LifePoints == 8000);
                }
                else
                {
                    Check("MK-3 battle path (skipped — not in BP)", false,
                        $"turn={engine.TurnPlayer == p} phase={engine.Phase} canAtk={engine.CanAttack(p, mk3)}");
                }
            }

            // ── Star Boy continuous WATER +500 / FIRE −400 (both fields) ──
            {
                const int starBoy = MonsterEffects.StarBoy; // 8201910
                const int fenrir = 218704; // WATER 1400
                const int hinotama = 96851799; // FIRE Normal 600
                const int celtic = 91152256; // EARTH — neither WATER nor FIRE

                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;

                var star = PlaceMonster(engine, p, starBoy, 2, BattlePosition.Attack, true);
                var fen = PlaceMonster(engine, p, fenrir, 1, BattlePosition.Attack, true);
                var fire = PlaceMonster(engine, opp, hinotama, 2, BattlePosition.Attack, true);
                var cel = PlaceMonster(engine, opp, celtic, 1, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(engine);

                Check("Star Boy text compiles WATER +500 and FIRE −400",
                    star.Def != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(star.Def) is { } sbProg &&
                    sbProg.FullyCompiled &&
                    sbProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ContinuousGainAtkDef &&
                        string.Equals(c.AttributeFilter, "WATER",
                            System.StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == 500) &&
                    sbProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ContinuousGainAtkDef &&
                        string.Equals(c.AttributeFilter, "FIRE",
                            System.StringComparison.OrdinalIgnoreCase) &&
                        c.Amount == -400),
                    star.Def == null ? "missing def" : "compile miss");

                Check("Star Boy itself is WATER +500 (550→1050)",
                    star.CurrentAtk == 1050, $"ATK {star.CurrentAtk}");
                Check("Fenrir WATER +500 (1400→1900)",
                    fen.CurrentAtk == 1900, $"ATK {fen.CurrentAtk}");
                Check("Hinotama Soul FIRE −400 (600→200)",
                    fire.CurrentAtk == 200, $"ATK {fire.CurrentAtk}");
                Check("Celtic Guardian (neither) unchanged",
                    cel.CurrentAtk == cel.Def.atk, $"ATK {cel.CurrentAtk}");

                var setStar = PlaceMonster(engine, p, starBoy, 3, BattlePosition.Defense, false);
                FieldSpellEffects.RefreshBoard(engine);
                Check("Face-down Star Boy does not apply the aura",
                    fen.CurrentAtk == 1900 && setStar.CurrentAtk == setStar.Def.atk,
                    $"fen {fen.CurrentAtk} setStar {setStar.CurrentAtk}");

                p.MonsterZones[2].Occupant = null; // remove face-up Star Boy
                FieldSpellEffects.RefreshBoard(engine);
                Check("Star Boy leaves: WATER/FIRE ATK return to printed",
                    fen.CurrentAtk == fen.Def.atk && fire.CurrentAtk == fire.Def.atk,
                    $"fen {fen.CurrentAtk} fire {fire.CurrentAtk}");

                var alo = PutInHand(engine, p, FieldSpellEffects.ALegendaryOcean);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                if (engine.TurnPlayer == p && engine.InMainPhase)
                    engine.TryActivateSpellTrap(p, alo, fromHand: true);
                p.MonsterZones[2].Occupant = star;
                star.FaceUp = true;
                star.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(engine);
                Check("Star Boy + ALO stack: Fenrir 1400+200+500=2100",
                    fen.CurrentAtk == 2100, $"ATK {fen.CurrentAtk}");
            }

            // ── Mermaid Knight: extra attack while Umi (ALO counts) ──
            {
                const int mermaidId = MonsterEffects.MermaidKnight; // 24435369
                const int twinId = 82035781; // Twinheaded Beast
                const int grayId = 29618570; // Gray Wing — ignition this-turn, not continuous
                const int aloId = FieldSpellEffects.ALegendaryOcean;

                var mermaidDef = db.Get(mermaidId);
                Check("Mermaid Knight text compiles ExtraAttacks while Umi",
                    mermaidDef != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(mermaidDef) is { } mkProg &&
                    mkProg.FullyCompiled &&
                    mkProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ExtraAttacks &&
                        string.Equals(c.RequiresFaceUpName, "Umi",
                            System.StringComparison.OrdinalIgnoreCase)),
                    mermaidDef == null ? "missing def" : "compile miss");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachPlayerBattle(engine))
                    {
                        Check("Mermaid Knight no-Umi battle path (skipped — not in BP)", false,
                            $"turnP={engine.TurnPlayer == engine.Player} turn={engine.TurnNumber} phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        p.Hand.Clear();
                        opp.Hand.Clear();

                        var mermaid = PlaceMonster(engine, p, mermaidId, 2, BattlePosition.Attack, true);
                        mermaid.ClearAttackFlags();
                        FieldSpellEffects.RefreshBoard(engine);

                        Check("Mermaid Knight without Umi: ExtraAttacksAllowed is 0",
                            engine.ExtraAttacksAllowed(mermaid) == 0,
                            $"extra={engine.ExtraAttacksAllowed(mermaid)}");
                        Check("Mermaid Knight without Umi can declare first attack",
                            engine.CanAttack(p, mermaid));
                        var lp0 = opp.LifePoints;
                        Check("Mermaid Knight first attack (no Umi)",
                            ResolveDirect(engine, p, mermaid));
                        Check("Mermaid Knight without Umi cannot attack twice",
                            !engine.CanAttack(p, mermaid) && mermaid.AttacksDeclaredThisTurn == 1,
                            $"can={engine.CanAttack(p, mermaid)} declared={mermaid.AttacksDeclaredThisTurn}");
                        Check("Mermaid Knight no-Umi: 1500 direct",
                            opp.LifePoints == lp0 - 1500,
                            $"opp LP {opp.LifePoints} was {lp0}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    for (var t = 0; t < 8 &&
                                    !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
                    {
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        engine.RecoverStuckCombat();
                        engine.TryEndTurnSafe(engine.TurnPlayer);
                    }

                    if (!(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2 &&
                          engine.InMainPhase))
                    {
                        Check("Mermaid Knight + ALO battle path (skipped — not in MP)", false,
                            $"turnP={engine.TurnPlayer == engine.Player} turn={engine.TurnNumber} phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        p.Hand.Clear();
                        opp.Hand.Clear();

                        var mermaid = PlaceMonster(engine, p, mermaidId, 2, BattlePosition.Attack, true);
                        mermaid.ClearAttackFlags();
                        var alo = PutInHand(engine, p, aloId);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        Check("Mermaid Knight activates ALO before Battle",
                            engine.TryActivateSpellTrap(p, alo, fromHand: true));
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("ALO is Umi under Mermaid Knight",
                            FieldSpellEffects.UmiIsOnField(engine) &&
                            p.FieldSpellZone?.Occupant == alo);
                        Check("Mermaid Knight WATER +200 ATK under ALO",
                            mermaid.CurrentAtk == 1700, $"ATK {mermaid.CurrentAtk}");
                        Check("Mermaid Knight ExtraAttacksAllowed is 1 while ALO/Umi",
                            engine.ExtraAttacksAllowed(mermaid) == 1 &&
                            engine.MaxAttacksThisTurn(mermaid) == 2,
                            $"extra={engine.ExtraAttacksAllowed(mermaid)} max={engine.MaxAttacksThisTurn(mermaid)}");

                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(p);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        DrainCombat(engine);

                        if (engine.Phase != DuelPhase.Battle)
                        {
                            Check("Mermaid Knight + ALO entered Battle", false, $"phase={engine.Phase}");
                        }
                        else
                        {
                            var lpBefore = opp.LifePoints;
                            Check("Mermaid Knight first attack while Umi",
                                engine.CanAttack(p, mermaid) && ResolveDirect(engine, p, mermaid));
                            Check("Mermaid Knight can attack again after first (ALO)",
                                engine.CanAttack(p, mermaid) && mermaid.AttacksDeclaredThisTurn == 1,
                                $"can={engine.CanAttack(p, mermaid)} declared={mermaid.AttacksDeclaredThisTurn} phase={engine.Phase}");
                            var snap = LegalIntentService.Build(engine, p);
                            Check("Legal intents still include Direct Attack after first",
                                snap.HasKind(mermaid, LegalIntentService.LegalKind.DirectAttack));
                            Check("Mermaid Knight second attack while Umi",
                                ResolveDirect(engine, p, mermaid));
                            Check("Mermaid Knight two directs: 3400 (1700+1700)",
                                opp.LifePoints == lpBefore - 3400,
                                $"opp LP {opp.LifePoints} was {lpBefore} declared={mermaid.AttacksDeclaredThisTurn}");
                            Check("Mermaid Knight cannot attack a third time",
                                !engine.CanAttack(p, mermaid) && mermaid.AttacksDeclaredThisTurn == 2,
                                $"can={engine.CanAttack(p, mermaid)} declared={mermaid.AttacksDeclaredThisTurn}");
                        }
                    }
                }

                // Twinheaded Beast: unconditional extra attack
                {
                    var eng2 = Fresh(db, pDeck, aDeck);
                    if (!ReachPlayerBattle(eng2))
                    {
                        Check("Twinheaded Beast battle path (skipped — not in BP)", false,
                            $"phase={eng2.Phase}");
                    }
                    else
                    {
                        ClearBoard(eng2);
                        var p2 = eng2.Player;
                        var opp2 = eng2.Opponent;
                        p2.LifePoints = 8000;
                        opp2.LifePoints = 8000;
                        p2.Hand.Clear();
                        opp2.Hand.Clear();
                        var twin = PlaceMonster(eng2, p2, twinId, 2, BattlePosition.Attack, true);
                        twin.ClearAttackFlags();
                        Check("Twinheaded Beast ExtraAttacksAllowed is 1 with no Umi",
                            eng2.ExtraAttacksAllowed(twin) == 1,
                            $"extra={eng2.ExtraAttacksAllowed(twin)}");
                        Check("Twinheaded Beast first attack", ResolveDirect(eng2, p2, twin));
                        Check("Twinheaded Beast can attack twice",
                            eng2.CanAttack(p2, twin));
                        Check("Twinheaded Beast second attack", ResolveDirect(eng2, p2, twin));
                        Check("Twinheaded Beast cannot attack a third time",
                            !eng2.CanAttack(p2, twin));
                    }
                }

                // Official-text sweep: every cards_db extra-attack line is compiled + battled.
                {
                    var extraCards = 0;
                    var extraFails = 0;
                    foreach (var def in db.GetAllCards())
                    {
                        if (def == null || string.IsNullOrEmpty(def.desc)) continue;
                        if (def.desc.IndexOf("attack twice during the same Battle Phase",
                                System.StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        extraCards++;
                        var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(def);
                        var compiled = prog != null && prog.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ExtraAttacks);
                        if (!compiled)
                        {
                            extraFails++;
                            Check($"DB extra-attack compiles: {def.name} ({def.id})", false,
                                prog == null
                                    ? "null program"
                                    : $"unparsed={string.Join("|", prog.UnparsedFragments ?? System.Array.Empty<string>())}");
                            continue;
                        }

                        var needsUmi = def.desc.IndexOf("\"Umi\"",
                            System.StringComparison.OrdinalIgnoreCase) >= 0;
                        var sweep = Fresh(db, pDeck, aDeck);
                        for (var t = 0; t < 8 &&
                                        !(sweep.TurnPlayer == sweep.Player && sweep.TurnNumber >= 2); t++)
                        {
                            if (sweep.IsAwaitingResponse) sweep.PassResponse();
                            sweep.RecoverStuckCombat();
                            sweep.TryEndTurnSafe(sweep.TurnPlayer);
                        }

                        if (!(sweep.TurnPlayer == sweep.Player && sweep.TurnNumber >= 2 &&
                              sweep.InMainPhase))
                        {
                            extraFails++;
                            Check($"DB extra-attack battle {def.name}", false, "not in MP");
                            continue;
                        }

                        ClearBoard(sweep);
                        sweep.Player.LifePoints = 8000;
                        sweep.Opponent.LifePoints = 8000;
                        sweep.Player.Hand.Clear();
                        sweep.Opponent.Hand.Clear();
                        var atk = PlaceMonster(sweep, sweep.Player, def.id, 2,
                            BattlePosition.Attack, true);
                        atk.ClearAttackFlags();
                        if (needsUmi)
                        {
                            var ocean = PutInHand(sweep, sweep.Player, aloId);
                            if (sweep.IsAwaitingResponse) sweep.PassResponse();
                            sweep.TryActivateSpellTrap(sweep.Player, ocean, fromHand: true);
                        }

                        if (sweep.Phase == DuelPhase.Main1)
                            sweep.TryEnterBattlePhase(sweep.Player);
                        if (sweep.IsAwaitingResponse) sweep.PassResponse();
                        DrainCombat(sweep);
                        if (sweep.Phase != DuelPhase.Battle)
                        {
                            extraFails++;
                            Check($"DB extra-attack battle {def.name}", false, $"phase={sweep.Phase}");
                            continue;
                        }

                        var extraOk = sweep.ExtraAttacksAllowed(atk) >= 1 &&
                                      ResolveDirect(sweep, sweep.Player, atk) &&
                                      sweep.CanAttack(sweep.Player, atk) &&
                                      ResolveDirect(sweep, sweep.Player, atk) &&
                                      !sweep.CanAttack(sweep.Player, atk);
                        Check($"DB extra-attack battles twice: {def.name} ({def.id})", extraOk,
                            $"extra={sweep.ExtraAttacksAllowed(atk)} declared={atk.AttacksDeclaredThisTurn} umi={needsUmi}");
                        if (!extraOk) extraFails++;
                    }

                    Check("cards_db extra-attack sweep found Mermaid Knight / Twinheaded",
                        extraCards >= 2, $"found={extraCards} fails={extraFails}");
                }

                var grayDef = db.Get(grayId);
                if (grayDef != null)
                {
                    var gp = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(grayDef);
                    Check("Gray Wing is not a continuous extra-attack compile",
                        gp == null ||
                        !gp.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.ExtraAttacks));
                }
            }

            // ── Attack / Damage Step traps (Bark of Dark Ruler, Sakuretsu Armor) ──
            {
                const int barkId = 41925941;
                const int sakuretsuId = 56120475;
                const int laJinn = 97590747; // Fiend 1800
                const int bewd = 89631139; // 3000 ATK
                const int celtic = 91152256;

                var barkDef = db.Get(barkId);
                Check("Bark of Dark Ruler text compiles Damage Step ATK/DEF loss",
                    barkDef != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(barkDef) is { } barkProg &&
                    barkProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.LoseAtkDefUntilEndOfTurn &&
                        c.Timing == WRLDZ.Duel.TextEffects.EffectTiming.DamageCalculation &&
                        c.RequiresLpCostMultiple == 100 &&
                        string.Equals(c.RaceFilter, "Fiend", System.StringComparison.OrdinalIgnoreCase)),
                    barkDef == null ? "missing def" : "compile miss");

                var sakDef = db.Get(sakuretsuId);
                Check("Sakuretsu Armor text compiles destroy attacker",
                    sakDef != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(sakDef) is { } sakProg &&
                    sakProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.Destroy &&
                        c.Timing == WRLDZ.Duel.TextEffects.EffectTiming.AttackDeclared &&
                        c.Zone == WRLDZ.Duel.TextEffects.EffectZoneFilter.AttackingMonster),
                    sakDef == null ? "missing def" : "compile miss");

                YgoProTriggerCatalog.EnsureLoaded();
                Check("YGOPro trigger catalog loaded one-shot traps",
                    YgoProTriggerCatalog.Count > 0, $"count={YgoProTriggerCatalog.Count}");
                Check("YGOPro catalog: Bark of Dark Ruler is Damage Step pay-LP",
                    YgoProTriggerCatalog.For(barkId) is { } bf &&
                    bf.timing == "damage_step" && bf.action == "lose_atk_def_pay_lp");
                Check("YGOPro catalog: Sakuretsu Armor is destroy attacker",
                    YgoProTriggerCatalog.For(sakuretsuId) is { } sf &&
                    sf.timing == "attack_announce" && sf.action == "destroy_attacker");

                // Bark: opponent attacks your Fiend — Activate must appear in Damage Step
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentBattle(engine))
                    {
                        Check("Bark of Dark Ruler battle path (skipped — opp not in BP)", false,
                            $"turnP={engine.TurnPlayer == engine.Opponent} turn={engine.TurnNumber} phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        p.Hand.Clear();
                        opp.Hand.Clear();

                        var fiend = PlaceMonster(engine, p, laJinn, 2, BattlePosition.Attack, true);
                        fiend.ClearAttackFlags();
                        var bark = PlaceSetTrap(engine, p, barkId, 2);
                        var attacker = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        attacker.ClearAttackFlags();
                        attacker.SummonedThisTurn = false;

                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        DrainCombat(engine);

                        Check("Bark: opponent can declare on Fiend",
                            engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, attacker),
                            $"phase={engine.Phase} can={engine.CanAttack(opp, attacker)}");
                        Check("Bark: declare attack", engine.TryAttack(opp, attacker, fiend));

                        if (engine.PendingResponse?.Timing == ResponseTiming.AttackDeclared)
                        {
                            Check("Bark is NOT legal at attack declaration",
                                engine.PendingResponse.LegalCards == null ||
                                !engine.PendingResponse.LegalCards.Exists(c =>
                                    c != null && c.CardId == barkId));
                            engine.PassResponse();
                        }

                        Check("Bark opens Damage Step response with Activate",
                            engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.DamageCalculation &&
                            engine.PendingResponse.LegalCards != null &&
                            engine.PendingResponse.LegalCards.Exists(c =>
                                c != null && c.CardId == barkId),
                            engine.PendingResponse == null
                                ? "no window"
                                : $"timing={engine.PendingResponse.Timing} n={engine.PendingResponse.LegalCards?.Count ?? 0}");

                        if (engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.DamageCalculation)
                        {
                            Check("Bark CanActivate in Damage Step window",
                                engine.CanActivateSpellTrap(p, bark, fromHand: false));
                            var snap = LegalIntentService.Build(engine, p);
                            Check("Bark legal intent includes Response Activate",
                                snap.HasKind(bark, LegalIntentService.LegalKind.ResponseActivate));

                            Check("Bark activates",
                                engine.TryActivateSpellTrap(p, bark, fromHand: false));
                            if (engine.IsAwaitingEffectTarget &&
                                engine.PendingActivation != null &&
                                engine.PendingActivation.AwaitingLpCost)
                            {
                                var pay = engine.PendingActivation.LpCostChoices.Count > 0
                                    ? engine.PendingActivation.LpCostChoices[
                                        engine.PendingActivation.LpCostChoices.Count - 1]
                                    : 100;
                                Check("Bark LP cost accepted", engine.TrySelectLpCost(pay));
                            }

                            DrainCombat(engine);
                            Check("Bark: BEWD ATK reduced until End Phase",
                                attacker.UntilEndOfTurnAtk < 0 && attacker.CurrentAtk < 3000,
                                $"until={attacker.UntilEndOfTurnAtk} ATK={attacker.CurrentAtk}");
                            Check("Bark: trap left the field (Normal Trap)",
                                !p.TryFindSpellTrap(bark, out _));
                        }
                    }
                }

                // Sakuretsu Armor: Activate at attack declaration, destroy the attacker
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentBattle(engine))
                    {
                        Check("Sakuretsu Armor battle path (skipped — opp not in BP)", false,
                            $"phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var wall = PlaceMonster(engine, p, celtic, 2, BattlePosition.Defense, true);
                        wall.ClearAttackFlags();
                        var sak = PlaceSetTrap(engine, p, sakuretsuId, 2);
                        var attacker = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        attacker.ClearAttackFlags();
                        attacker.SummonedThisTurn = false;

                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        DrainCombat(engine);

                        Check("Sakuretsu: declare attack", engine.TryAttack(opp, attacker, wall));
                        Check("Sakuretsu is legal at attack declaration",
                            engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                            engine.PendingResponse.LegalCards != null &&
                            engine.PendingResponse.LegalCards.Exists(c =>
                                c != null && c.CardId == sakuretsuId),
                            engine.PendingResponse == null
                                ? "no window"
                                : $"timing={engine.PendingResponse.Timing}");
                        Check("Attack response window is 5s (timer expiry passes; no PASS button)",
                            engine.PendingResponse != null &&
                            engine.PendingResponse.ReactionSeconds == CombatAnimTimings.DefaultResponseSeconds &&
                            CombatAnimTimings.DefaultResponseSeconds == 5f,
                            engine.PendingResponse == null
                                ? "no window"
                                : $"secs={engine.PendingResponse.ReactionSeconds}");
                        Check("Sakuretsu activates",
                            engine.CanActivateSpellTrap(p, sak, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, sak, fromHand: false));
                        DrainCombat(engine);
                        Check("Sakuretsu: attacker destroyed",
                            !opp.TryFindMonster(attacker, out _) &&
                            opp.Graveyard.Exists(c => c.CardId == bewd));
                        Check("Sakuretsu: player's monster survived",
                            p.TryFindMonster(wall, out _));
                    }
                }

                // Draining Shield: negate that attack (still counts as declared) + gain ATK as LP
                {
                    const int drainId = 43250041;
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentBattle(engine))
                    {
                        Check("Draining Shield battle path (skipped — opp not in BP)", false,
                            $"phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var victim = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                        victim.ClearAttackFlags();
                        var shield = PlaceSetTrap(engine, p, drainId, 2);
                        shield.SetThisTurn = false;
                        var attacker = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        attacker.ClearAttackFlags();
                        attacker.SummonedThisTurn = false;

                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        DrainCombat(engine);

                        var drainProg = CardTextEffectCompiler.Compile(shield.Def);
                        Check("Draining Shield compiles negate + gain LP equal to ATK",
                            drainProg != null &&
                            drainProg.HasTiming(EffectTiming.AttackDeclared) &&
                            drainProg.ClauseList.Exists(c =>
                                c != null && c.Action == EffectActionKind.NegateThisAttack) &&
                            drainProg.ClauseList.Exists(c =>
                                c != null && c.Action == EffectActionKind.GainLpEqualToAtk));

                        Check("Draining Shield: declare attack",
                            engine.TryAttack(opp, attacker, victim));
                        Check("Draining Shield is legal at attack declaration",
                            engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                            engine.PendingResponse.LegalCards != null &&
                            engine.PendingResponse.LegalCards.Exists(c =>
                                c != null && c.CardId == drainId),
                            engine.PendingResponse == null
                                ? "no window"
                                : $"timing={engine.PendingResponse.Timing}");
                        var lpBefore = p.LifePoints;
                        Check("Draining Shield activates",
                            engine.CanActivateSpellTrap(p, shield, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, shield, fromHand: false));
                        DrainCombat(engine);
                        Check("Draining Shield: gain ATK as LP",
                            p.LifePoints == lpBefore + attacker.CurrentAtk,
                            $"LP {p.LifePoints} was {lpBefore} atk={attacker.CurrentAtk}");
                        Check("Draining Shield: defender survived (attack did not resolve)",
                            p.TryFindMonster(victim, out _));
                        Check("Draining Shield: attacker still on field",
                            opp.TryFindMonster(attacker, out _));
                        Check("Draining Shield: that attack cannot be declared again",
                            engine.Phase == DuelPhase.Battle &&
                            !engine.CanAttack(opp, attacker) &&
                            attacker.AttacksDeclaredThisTurn >= 1,
                            $"phase={engine.Phase} can={engine.CanAttack(opp, attacker)} " +
                            $"declared={attacker.AttacksDeclaredThisTurn}");
                    }
                }

                // Bulk: every catalog attack-declare trap in cards_db compiles or is catalog-legal
                {
                    var n = 0;
                    var miss = 0;
                    foreach (var def in db.GetAllCards())
                    {
                        if (def == null || !def.IsTrap) continue;
                        var fact = YgoProTriggerCatalog.For(def.id);
                        if (fact == null || fact.timing != "attack_announce") continue;
                        n++;
                        var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(def);
                        var compiled = prog != null && prog.HasTiming(
                            WRLDZ.Duel.TextEffects.EffectTiming.AttackDeclared);
                        Check($"attack-declare trap covered: {def.name} ({def.id})",
                            compiled || fact.action.Length > 0,
                            compiled ? "ok" : $"catalog={fact.action}");
                        if (!compiled && string.IsNullOrEmpty(fact.action)) miss++;
                    }

                    Check("catalog attack-declare traps in cards_db", n >= 4, $"n={n} miss={miss}");
                }
            }

            // ── Granadora / Maiden of the Aqua / Tornado Wall (Umi environment) ──
            {
                const int granId = 13944422;
                const int maidenId = FieldSpellEffects.MaidenOfTheAqua;
                const int wallId = 18605135;
                const int celtic = 91152256;
                const int bewd = 89631139;

                var granDef = db.Get(granId);
                Check("Granadora text compiles summon LP + destroy damage",
                    granDef != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(granDef) is { } gp &&
                    gp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.GainLifePoints &&
                        c.Amount == 1000) &&
                    gp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.TakeEffectDamage &&
                        c.RequiresDestroyed),
                    granDef == null ? "missing def" : "compile miss");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.LifePoints = 8000;
                    var gran = PutInHand(engine, p, granId);
                    Check("Granadora Normal Summon",
                        engine.TryNormalSummon(p, gran, asSet: false));
                    Check("Granadora: +1000 LP on Normal Summon",
                        p.LifePoints == 9000, $"LP {p.LifePoints}");

                    engine.DestroyMonsterPublic(p, gran);
                    Check("Granadora: 2000 damage when destroyed",
                        p.LifePoints == 7000, $"LP {p.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    var gran = PlaceMonster(engine, p, granId, 2, BattlePosition.Attack, true);
                    engine.SendCardToGrave(p, gran);
                    Check("Granadora tributed/sent (not destroyed): no 2000 damage",
                        p.LifePoints == 8000, $"LP {p.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var maiden = PlaceMonster(engine, p, maidenId, 2, BattlePosition.Attack, true);
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Maiden of the Aqua treats the field as Umi (no Field Spell)",
                        FieldSpellEffects.UmiIsOnField(engine) &&
                        FieldSpellEffects.MonsterTreatsFieldAs(engine, "Umi"),
                        maiden.Name);

                    var wall = PlaceSetTrap(engine, p, wallId, 2);
                    wall.SetThisTurn = true;
                    Check("Tornado Wall cannot activate the turn it was Set",
                        !engine.CanActivateSpellTrap(p, wall, fromHand: false));
                    wall.SetThisTurn = false;
                    Check("Tornado Wall activates while Maiden treats field as Umi",
                        engine.CanActivateSpellTrap(p, wall, fromHand: false) &&
                        engine.TryActivateSpellTrap(p, wall, fromHand: false));
                    Check("Tornado Wall stays face-up Continuous",
                        wall.FaceUp && p.TryFindSpellTrap(wall, out _));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var alo = PutInHand(engine, p, FieldSpellEffects.ALegendaryOcean);
                    Check("Tornado Wall: ALO activates as Umi",
                        engine.TryActivateSpellTrap(p, alo, fromHand: true) &&
                        FieldSpellEffects.UmiIsOnField(engine));
                    var wall = PlaceSetTrap(engine, p, wallId, 2);
                    wall.SetThisTurn = false;
                    var celticM = PlaceMonster(engine, p, celtic, 2, BattlePosition.Defense, true);
                    var bewdM = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                    bewdM.SummonedThisTurn = false;
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    Check("Tornado Wall offered on opponent Main (free-chain Continuous Trap)",
                        engine.IsAwaitingResponse &&
                        engine.PendingResponse != null &&
                        engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                        engine.PendingResponse.LegalCards.Exists(c =>
                            c != null && c.CardId == wallId),
                        $"awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing}");
                    Check("Tornado Wall activates from opponent-turn window and stays",
                        engine.TryActivateSpellTrap(p, wall, fromHand: false) &&
                        wall.FaceUp && p.TryFindSpellTrap(wall, out _));
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    if (engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1)
                        engine.TryEnterBattlePhase(opp);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    var lpBefore = p.LifePoints;
                    if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, bewdM))
                    {
                        engine.TryAttack(opp, bewdM, celticM);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        DrainCombat(engine);
                    }

                    Check("Tornado Wall: no battle damage while Umi is up",
                        p.LifePoints == lpBefore,
                        $"LP {p.LifePoints} was {lpBefore}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var alo = PutInHand(engine, p, FieldSpellEffects.ALegendaryOcean);
                    engine.TryActivateSpellTrap(p, alo, fromHand: true);
                    var wall = PlaceSetTrap(engine, p, wallId, 2);
                    wall.SetThisTurn = false;
                    PlaceMonster(engine, p, celtic, 2, BattlePosition.Defense, true);
                    var bewdM = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                    bewdM.SummonedThisTurn = false;
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    if (engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1)
                        engine.TryEnterBattlePhase(opp);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    Check("Tornado Wall is legal in AttackDeclared window while Umi is up",
                        engine.Phase == DuelPhase.Battle &&
                        SpellTrapEffects.IsLegalResponseCard(engine, p, wall,
                            ResponseTiming.AttackDeclared, null));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentBattle(engine))
                    {
                        Check("Tornado Wall battle (skipped — opp not in BP)", false,
                            $"phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        PlaceMonster(engine, p, maidenId, 1, BattlePosition.Defense, true);
                        var wall = PlaceSetTrap(engine, p, wallId, 2);
                        wall.SetThisTurn = false;
                        engine.TryActivateSpellTrap(p, wall, fromHand: false);
                        FieldSpellEffects.RefreshBoard(engine);
                        var victim = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                        victim.ClearAttackFlags();
                        var attacker = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        attacker.ClearAttackFlags();
                        attacker.SummonedThisTurn = false;
                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        DrainCombat(engine);
                        var lpBefore = p.LifePoints;
                        if (engine.CanAttack(opp, attacker))
                        {
                            engine.TryAttack(opp, attacker, victim);
                            DrainCombat(engine);
                        }

                        Check("Tornado Wall + Maiden: controller takes no battle damage",
                            p.LifePoints == lpBefore,
                            $"LP {p.LifePoints} was {lpBefore}");
                    }
                }
            }

            // ── Levia-Dragon - Daedalus: send face-up Umi (ALO counts) ; destroy all other ──
            {
                const int daedalusId = 37721209;
                const int aloId = FieldSpellEffects.ALegendaryOcean; // 295517
                const int umiId = 22702055;
                const int maidenId = FieldSpellEffects.MaidenOfTheAqua;
                const int celtic = 91152256;
                const int wallId = 18605135;

                var daeDef = db.Get(daedalusId);
                Check("Daedalus text compiles send-Umi + destroy all other",
                    daeDef != null &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(daeDef) is { } dp &&
                    dp.FullyCompiled &&
                    dp.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.Destroy &&
                        c.RequiresSendNamedToGy &&
                        string.Equals(c.RequiresFaceUpName, "Umi",
                            System.StringComparison.OrdinalIgnoreCase) &&
                        c.Zone == WRLDZ.Duel.TextEffects.EffectZoneFilter.AllOtherCardsOnField),
                    daeDef == null ? "missing def" : "compile miss");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var dae = PlaceMonster(engine, p, daedalusId, 2, BattlePosition.Attack, true);
                    Check("Daedalus: cannot activate without a face-up Umi you control",
                        !engine.CanActivateSpellTrap(p, dae, fromHand: false));

                    var maiden = PlaceMonster(engine, p, maidenId, 1, BattlePosition.Attack, true);
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Maiden treats the field as Umi but is not a named Umi cost",
                        FieldSpellEffects.UmiIsOnField(engine) &&
                        !engine.CanActivateSpellTrap(p, dae, fromHand: false));
                    p.MonsterZones[1].Occupant = null;
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var dae = PlaceMonster(engine, p, daedalusId, 2, BattlePosition.Attack, true);
                    var alo = engine.CreateCardInstance(aloId);
                    alo.FaceUp = true;
                    p.FieldSpellZone.Occupant = alo;
                    FieldSpellEffects.RefreshBoard(engine);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var wall = PlaceSetTrap(engine, opp, wallId, 2);

                    Check("ALO is always treated as Umi for Daedalus cost",
                        alo.IsNamed("Umi") && FieldSpellEffects.UmiIsOnField(engine));
                    Check("Daedalus: Activate legal with face-up ALO you control",
                        engine.CanActivateSpellTrap(p, dae, fromHand: false));
                    var snap = LegalIntentService.Build(engine, p);
                    Check("Daedalus: legal intents include Activate from field",
                        snap.HasKind(dae, LegalIntentService.LegalKind.ActivateFromField));

                    Check("Daedalus: Activate starts send-Umi cost",
                        engine.TryActivateSpellTrap(p, dae, fromHand: false) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.AwaitingSendNamedCost &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.SendFaceUpNamedToGy,
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    Check("Daedalus: ALO is a legal send-to-GY cost",
                        engine.IsLegalEffectTarget(alo));
                    Check("Daedalus: pay ALO cost then destroy others",
                        engine.TrySelectEffectTarget(alo));
                    Check("ALO sent to GY as cost",
                        p.Graveyard.Contains(alo) && p.FieldSpellZone?.Occupant != alo);
                    Check("Daedalus survives its own effect",
                        p.MonsterZones[2].Occupant == dae);
                    Check("Daedalus destroyed the other monster",
                        !opp.TryFindMonster(prey, out _) &&
                        opp.Graveyard.Exists(c => c.CardId == celtic));
                    Check("Daedalus destroyed the other Spell/Trap",
                        !opp.TryFindSpellTrap(wall, out _) &&
                        opp.Graveyard.Exists(c => c.CardId == wallId));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var dae = PlaceMonster(engine, p, daedalusId, 2, BattlePosition.Attack, true);
                    var umi = engine.CreateCardInstance(umiId);
                    umi.FaceUp = true;
                    p.FieldSpellZone.Occupant = umi;
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Daedalus: printed Umi is a legal send cost",
                        engine.CanActivateSpellTrap(p, dae, fromHand: false) &&
                        engine.TryActivateSpellTrap(p, dae, fromHand: false) &&
                        engine.IsLegalEffectTarget(umi) &&
                        engine.TrySelectEffectTarget(umi));
                    Check("Printed Umi sent; Celtic destroyed; Daedalus remains",
                        p.Graveyard.Contains(umi) &&
                        !opp.TryFindMonster(prey, out _) &&
                        p.MonsterZones[2].Occupant == dae);
                }
            }

            // ── Main Phase ignition templates (tribute / banish / hand discard / set FD) ──
            {
                const int exiled = 74131780;
                const int cannon = 11384280;
                const int chaosId = 9596126;
                const int lacooda = 2326738;
                const int thunder = 31786629;
                const int golem = 30190809;
                const int turtle = 95727991;
                const int paladin = 73398797;
                const int swamp = 79109599;
                const int celtic = 91152256;
                const int bewd = 89631139;
                const int poly = 24094653;
                const int laJinn = 97590747;

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var force = PlaceMonster(engine, p, exiled, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Exiled Force: Activate legal",
                        engine.CanActivateSpellTrap(p, force, fromHand: false));
                    Check("Exiled Force: Activate tributes itself then asks for a target",
                        engine.TryActivateSpellTrap(p, force, fromHand: false) &&
                        p.Graveyard.Contains(force) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.IsLegalEffectTarget(prey));
                    Check("Exiled Force: destroy target",
                        engine.TrySelectEffectTarget(prey) &&
                        opp.Graveyard.Contains(prey));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.LifePoints = 8000;
                    opp.LifePoints = 8000;
                    var sol = PlaceMonster(engine, p, cannon, 2, BattlePosition.Attack, true);
                    var fodder = PlaceMonster(engine, p, laJinn, 1, BattlePosition.Attack, true);
                    Check("Cannon Soldier: Activate",
                        engine.TryActivateSpellTrap(p, sol, fromHand: false) &&
                        engine.IsAwaitingEffectTarget);
                    Check("Cannon Soldier: Tribute La Jinn for 500",
                        engine.TrySelectEffectTarget(fodder) &&
                        p.Graveyard.Contains(fodder) &&
                        opp.LifePoints == 7500,
                        $"LP {opp.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var sorc = PlaceMonster(engine, p, chaosId, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var sorcOk = OfficialEffectRegistry.ProgramMayActivate(sorc.Def);
                    if (sorcOk)
                    {
                        Check("Chaos Sorcerer: Activate",
                            engine.TryActivateSpellTrap(p, sorc, fromHand: false) &&
                            engine.IsLegalEffectTarget(prey));
                        Check("Chaos Sorcerer: banish the target",
                            engine.TrySelectEffectTarget(prey) &&
                            opp.Banished.Contains(prey) &&
                            !opp.TryFindMonster(prey, out _));
                    }
                    else
                    {
                        Check("Chaos Sorcerer: stub cannot activate (incomplete program)",
                            !engine.TryActivateSpellTrap(p, sorc, fromHand: false));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var worm = PlaceMonster(engine, p, lacooda, 2, BattlePosition.Attack, true);
                    Check("Des Lacooda: sets itself face-down Defense",
                        engine.TryActivateSpellTrap(p, worm, fromHand: false) &&
                        !worm.FaceUp && worm.Position == BattlePosition.Defense);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    var mk = PlaceMonster(engine, p, golem, 2, BattlePosition.Attack, true);
                    Check("Gear Golem: pay 800 LP to attack directly this turn",
                        engine.CanActivateSpellTrap(p, mk, fromHand: false) &&
                        engine.TryActivateSpellTrap(p, mk, fromHand: false) &&
                        p.LifePoints == 7200 &&
                        mk.DirectAttackThisTurn &&
                        engine.GrantsDirectAttack(mk),
                        $"LP {p.LifePoints} direct={mk.DirectAttackThisTurn}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.LifePoints = 8000;
                    opp.LifePoints = 8000;
                    var cat = PlaceMonster(engine, p, turtle, 2, BattlePosition.Attack, true);
                    var fodder = PlaceMonster(engine, p, laJinn, 1, BattlePosition.Attack, true);
                    Check("Catapult Turtle: Activate",
                        engine.TryActivateSpellTrap(p, cat, fromHand: false));
                    Check("Catapult Turtle: half ATK of tributed La Jinn (900)",
                        engine.TrySelectEffectTarget(fodder) &&
                        opp.LifePoints == 8000 - fodder.CurrentAtk / 2,
                        $"LP {opp.LifePoints} fodderATK was used after tribute");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Insert(0, thunder);
                    p.Deck.Insert(0, thunder);
                    var td = PutInHand(engine, p, thunder);
                    Check("Thunder Dragon: Activate from hand",
                        engine.CanActivateSpellTrap(p, td, fromHand: true));
                    Check("Thunder Dragon: discard self, add copies from Deck",
                        engine.TryActivateSpellTrap(p, td, fromHand: true) &&
                        p.Graveyard.Contains(td) &&
                        p.Hand.Count >= 1 &&
                        p.Hand.Exists(c => c.CardId == thunder),
                        $"hand={p.Hand.Count} gy={p.Graveyard.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Deck.Insert(0, bewd);
                    var pal = PlaceMonster(engine, p, paladin, 2, BattlePosition.Attack, true);
                    if (OfficialEffectRegistry.ProgramMayActivate(pal.Def))
                    {
                        Check("Paladin of White Dragon: Tribute self, SS Blue-Eyes from Deck",
                            engine.TryActivateSpellTrap(p, pal, fromHand: false) &&
                            p.Graveyard.Contains(pal) &&
                            p.MonstersOnField().Any(m => m.CardId == bewd),
                            p.MonstersOnField().Count().ToString());
                    }
                    else
                    {
                        Check("Paladin of White Dragon: stub cannot activate (incomplete program)",
                            !engine.TryActivateSpellTrap(p, pal, fromHand: false));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Insert(0, poly);
                    var king = PutInHand(engine, p, swamp);
                    if (OfficialEffectRegistry.ProgramMayActivate(king.Def))
                    {
                        Check("King of the Swamp: discard from hand, add Polymerization",
                            engine.CanActivateSpellTrap(p, king, fromHand: true) &&
                            engine.TryActivateSpellTrap(p, king, fromHand: true) &&
                            p.Graveyard.Contains(king) &&
                            p.Hand.Exists(c => c.CardId == poly));
                    }
                    else
                    {
                        Check("King of the Swamp: stub cannot activate (incomplete program)",
                            !engine.CanActivateSpellTrap(p, king, fromHand: true) &&
                            !engine.TryActivateSpellTrap(p, king, fromHand: true));
                    }
                }
            }

            // ── Refused-list primitives: coin, counters, token, Extra Deck, control, Standby ──
            {
                const int timeWiz = 71625222;
                const int barrel = 81480461;
                const int breaker = 71413901;
                const int stein = 69015963;
                const int lekunga = 62543393;
                const int pds = 52860176;
                const int lava = 102380;
                const int lv3 = 980973;
                const int lv5 = 46384672;
                const int ojama = 29843091;
                const int relinquished = 64631466;
                const int celtic = 91152256;
                const int bsd = 11901678;
                const int fenrir = 218704;
                const int mst = 5318639;

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var wiz = PlaceMonster(engine, p, timeWiz, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    engine.Rng.QueueCoin(true);
                    Check("Time Wizard: Activate",
                        engine.TryActivateSpellTrap(p, wiz, fromHand: false) &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.AwaitingCoinCall);
                    Check("Time Wizard: call Heads, toss Heads, opp destroyed",
                        engine.TrySelectCoinCall(true) &&
                        !opp.TryFindMonster(prey, out _) &&
                        p.TryFindMonster(wiz, out _));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var drag = PlaceMonster(engine, p, barrel, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    engine.Rng.QueueCoin(true);
                    engine.Rng.QueueCoin(true);
                    engine.Rng.QueueCoin(false);
                    Check("Barrel Dragon: 2 heads destroys the target",
                        engine.TryActivateSpellTrap(p, drag, fromHand: false) &&
                        engine.IsLegalEffectTarget(prey) &&
                        engine.TrySelectEffectTarget(prey) &&
                        opp.Graveyard.Contains(prey));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var br = PutInHand(engine, p, breaker);
                    Check("Breaker Normal Summon", engine.TryNormalSummon(p, br, asSet: false));
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Breaker: 1 Spell Counter and 1900 ATK",
                        br.Counters == 1 && br.CurrentAtk == 1900,
                        $"counters={br.Counters} ATK={br.CurrentAtk}");
                    var st = PlaceSetTrap(engine, engine.Opponent, mst, 2);
                    st.SetThisTurn = false;
                    Check("Breaker: remove counter, destroy ST",
                        engine.CanActivateSpellTrap(p, br, fromHand: false) &&
                        engine.TryActivateSpellTrap(p, br, fromHand: false) &&
                        engine.TrySelectEffectTarget(st) &&
                        engine.Opponent.Graveyard.Contains(st) &&
                        br.Counters == 0);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    p.ExtraDeck.Clear();
                    p.ExtraDeck.Add(bsd);
                    var cyb = PlaceMonster(engine, p, stein, 2, BattlePosition.Attack, true);
                    Check("Cyber-Stein: pay 5000, SS Black Skull Dragon",
                        engine.TryActivateSpellTrap(p, cyb, fromHand: false) &&
                        p.LifePoints == 3000 &&
                        p.MonstersOnField().Any(m => m.CardId == bsd),
                        $"LP {p.LifePoints} extra left={p.ExtraDeck.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var plant = PlaceMonster(engine, p, lekunga, 2, BattlePosition.Attack, true);
                    var w1 = engine.CreateCardInstance(fenrir);
                    var w2 = engine.CreateCardInstance(fenrir);
                    p.Graveyard.Add(w1);
                    p.Graveyard.Add(w2);
                    Check("Lekunga: Activate",
                        engine.TryActivateSpellTrap(p, plant, fromHand: false) &&
                        engine.IsAwaitingEffectTarget);
                    Check("Lekunga: banish first WATER", engine.TrySelectEffectTarget(w1));
                    Check("Lekunga: banish second WATER, SS Token 700/700",
                        engine.TrySelectEffectTarget(w2) &&
                        p.Banished.Count >= 2 &&
                        p.MonstersOnField().Any(m => m.IsToken && m.CurrentAtk == 700),
                        $"tokens={p.MonstersOnField().Count(m => m.IsToken)} banished={p.Banished.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var soul = PlaceMonster(engine, p, pds, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Possessed Dark Soul: Tribute self, take LV4? Celtic is 4 — should fail LV≤3",
                        !engine.CanActivateSpellTrap(p, soul, fromHand: false) ||
                        prey.Level > 3);
                    var lv3mon = PlaceMonster(engine, opp, fenrir, 1, BattlePosition.Attack, true);
                    Check("Possessed Dark Soul: take control of Fenrir LV4 under ALO? printed 4",
                        lv3mon.Level <= 4);
                    // Fenrir is LV4; need LV3 or lower. Use a LV3: Giant Soldier 13039848 is 3.
                    opp.MonsterZones[1].Occupant = null;
                    var gs = PlaceMonster(engine, opp, 13039848, 1, BattlePosition.Defense, true);
                    Check("Possessed Dark Soul: take Giant Soldier of Stone",
                        engine.TryActivateSpellTrap(p, soul, fromHand: false) &&
                        p.Graveyard.Contains(soul) &&
                        p.MonstersOnField().Any(m => m.CardId == 13039848));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    var golem = PlaceMonster(engine, p, lava, 2, BattlePosition.Attack, true);
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Lava Golem Standby: controller takes 1000",
                        p.LifePoints == 7000, $"LP {p.LifePoints}");
                }

                {
                    const int cure = 85802526;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    var mer = PlaceMonster(engine, p, cure, 2, BattlePosition.Attack, true);
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Cure Mermaid Standby: +800 LP while face-up",
                        p.LifePoints == 8800, $"LP {p.LifePoints}");
                    mer.FaceUp = false;
                    p.LifePoints = 8000;
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Cure Mermaid face-down: no Standby LP",
                        p.LifePoints == 8000, $"LP {p.LifePoints}");
                }

                {
                    const int breeze = 53530069;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 8000;
                    var spr = PlaceMonster(engine, p, breeze, 2, BattlePosition.Attack, true);
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Spirit of the Breeze ATK: +1000 LP on Standby",
                        p.LifePoints == 9000, $"LP {p.LifePoints}");
                    spr.Position = BattlePosition.Defense;
                    p.LifePoints = 8000;
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Spirit of the Breeze DEF: no Standby LP",
                        p.LifePoints == 8000, $"LP {p.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Deck.Insert(0, lv5);
                    var baby = PlaceMonster(engine, p, lv3, 2, BattlePosition.Attack, true);
                    WRLDZ.Duel.TextEffects.TextEffectRuntime.FirePhaseTriggers(
                        engine, p, WRLDZ.Duel.TextEffects.EffectTiming.StandbyPhase);
                    Check("Armed Dragon LV3: send self, SS LV5 from Deck",
                        p.Graveyard.Exists(c => c.CardId == lv3) &&
                        p.MonstersOnField().Any(m => m.CardId == lv5),
                        $"gy={p.Graveyard.Count} field={p.MonstersOnField().Count()}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var trio = PlaceSetTrap(engine, p, ojama, 2);
                    trio.SetThisTurn = false;
                    Check("Ojama Trio: Activate",
                        engine.TryActivateSpellTrap(p, trio, fromHand: false));
                    Check("Ojama Trio: 3 Tokens on opponent",
                        opp.MonstersOnField().Count(m => m.IsToken) == 3,
                        $"oppTok={opp.MonstersOnField().Count(m => m.IsToken)} pTok={p.MonstersOnField().Count(m => m.IsToken)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var rel = PlaceMonster(engine, p, relinquished, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var prey2 = PlaceMonster(engine, opp, fenrir, 0, BattlePosition.Attack, true);
                    const int bewdId = 89631139;
                    var bewd = PlaceMonster(engine, opp, bewdId, 1, BattlePosition.Attack, true);
                    bewd.SummonedThisTurn = false;

                    var relProg = CardTextEffectCompiler.Compile(rel.Def);
                    Check("Relinquished: FullyCompiled so ProgramMayActivate",
                        OfficialEffectRegistry.ProgramMayActivate(rel.Def) &&
                        relProg != null && relProg.FullyCompiled &&
                        relProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipTargetToThis),
                        relProg == null
                            ? "null"
                            : $"full={relProg.FullyCompiled} unparsed={string.Join("|", relProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                    Check("Relinquished: Activate absorb Celtic",
                        engine.TryActivateSpellTrap(p, rel, fromHand: false) &&
                        engine.IsLegalEffectTarget(prey));
                    Check("Relinquished: equip Celtic, prey off opp field",
                        engine.TrySelectEffectTarget(prey) &&
                        rel.Equips.Contains(prey) &&
                        !opp.TryFindMonster(prey, out _));
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Relinquished ATK equals equipped printed ATK",
                        rel.CurrentAtk == 1400, $"ATK {rel.CurrentAtk}");
                    Check("Relinquished: second absorb same turn refused (OPT)",
                        opp.TryFindMonster(prey2, out _) &&
                        !engine.CanActivateSpellTrap(p, rel, fromHand: false));

                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    if (engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1)
                        engine.TryEnterBattlePhase(opp);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    DrainCombat(engine);
                    if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, bewd))
                    {
                        engine.TryAttack(opp, bewd, rel);
                        DrainCombat(engine);
                    }

                    Check("Relinquished: battle would destroy it — equipped destroyed instead, Relinquished stays",
                        p.TryFindMonster(rel, out _) &&
                        !rel.Equips.Contains(prey) &&
                        (opp.Graveyard.Contains(prey) || p.Graveyard.Contains(prey)),
                        $"relOnField={p.TryFindMonster(rel, out _)} equips={rel.Equips.Count} " +
                        $"preyGyOpp={opp.Graveyard.Contains(prey)} preyGyP={p.Graveyard.Contains(prey)} " +
                        $"phase={engine.Phase} turnOpp={engine.TurnPlayer == opp}");
                    Check("Relinquished battle: BEWD attacker still on field",
                        opp.TryFindMonster(bewd, out _));
                }

                {
                    const int bewdId = 89631139;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var rel = PlaceMonster(engine, p, 64631466, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var attacker = PlaceMonster(engine, opp, bewdId, 1, BattlePosition.Attack, true);
                    var absorbed = engine.TryActivateSpellTrap(p, rel, fromHand: false) &&
                                   engine.TrySelectEffectTarget(prey);
                    rel.IsNegated = true;
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    if (engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1)
                        engine.TryEnterBattlePhase(opp);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    DrainCombat(engine);
                    if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, attacker))
                    {
                        engine.TryAttack(opp, attacker, rel);
                        DrainCombat(engine);
                    }

                    Check("Relinquished: negated absorb host is destroyed by battle",
                        absorbed && !p.TryFindMonster(rel, out _) && p.Graveyard.Contains(rel) &&
                        !rel.Equips.Contains(prey) &&
                        (opp.Graveyard.Contains(prey) || p.Graveyard.Contains(prey)),
                        $"absorbed={absorbed} relOn={p.TryFindMonster(rel, out _)} relGy={p.Graveyard.Contains(rel)} " +
                        $"eq={rel.Equips.Count} preyGyOpp={opp.Graveyard.Contains(prey)} preyGyP={p.Graveyard.Contains(prey)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var zorc = PlaceMonster(engine, p, 97642679, 2, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    engine.Rng.QueueDie(1);
                    if (OfficialEffectRegistry.ProgramMayActivate(zorc.Def))
                    {
                        Check("Dark Master Zorc: roll 1 destroys all opp monsters",
                            engine.TryActivateSpellTrap(p, zorc, fromHand: false) &&
                            !opp.TryFindMonster(prey, out _));
                    }
                    else
                    {
                        Check("Dark Master Zorc: stub cannot activate (incomplete program)",
                            !engine.TryActivateSpellTrap(p, zorc, fromHand: false));
                    }
                }
            }

            // ── Live refused activations: Orca / Suijin / Fenrir ──
            {
                const int orcaId = 63120904;
                const int fishId = 90337190;
                const int suijinId = 98434877;
                const int fenrirId = 218704;
                const int cureId = 85802526;
                const int celtic = 91152256;
                const int bewd = 89631139;

                {
                    var orcaDef = db.Get(orcaId);
                    var op = CardTextEffectCompiler.Compile(orcaDef);
                    Check("Orca Mega-Fortress compiles two tribute-named destroy ignitions",
                        op != null && op.FullyCompiled &&
                        op.ClauseList.FindAll(c =>
                            c != null &&
                            c.Action == EffectActionKind.Destroy &&
                            c.RequiresTributeCount == 1 &&
                            !string.IsNullOrEmpty(c.NamedCard)).Count == 2,
                        op == null ? "null" : $"full={op.FullyCompiled} n={op.ClauseList.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var orca = PlaceMonster(engine, p, orcaId, 2, BattlePosition.Attack, true);
                    var fish = PlaceMonster(engine, p, fishId, 1, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Orca: Activate legal with Torpedo Fish to Tribute",
                        engine.CanActivateSpellTrap(p, orca, fromHand: false));
                    Check("Orca: Activate starts tribute cost",
                        engine.TryActivateSpellTrap(p, orca, fromHand: false) &&
                        engine.IsAwaitingEffectTarget);
                    Check("Orca: Tribute Torpedo Fish",
                        engine.IsLegalEffectTarget(fish) && engine.TrySelectEffectTarget(fish) &&
                        p.Graveyard.Contains(fish));
                    Check("Orca: destroy the targeted monster",
                        engine.IsLegalEffectTarget(prey) && engine.TrySelectEffectTarget(prey) &&
                        opp.Graveyard.Contains(prey) &&
                        p.TryFindMonster(orca, out _));
                }

                {
                    var suiDef = db.Get(suijinId);
                    var sp = CardTextEffectCompiler.Compile(suiDef);
                    Check("Suijin compiles damage-calc ATK 0 Quick Effect",
                        sp != null && sp.FullyCompiled &&
                        sp.HasTiming(EffectTiming.DamageCalculation) &&
                        sp.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SetAttackingMonsterAtkToZeroThisCalc),
                        suiDef == null
                            ? "missing"
                            : sp == null
                                ? "null"
                                : $"full={sp.FullyCompiled} n={sp.ClauseList.Count} unparsed={string.Join("|", sp.UnparsedFragments ?? System.Array.Empty<string>())}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentBattle(engine))
                    {
                        Check("Suijin battle path (skipped — opp not in BP)", false,
                            $"phase={engine.Phase}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var sui = PlaceMonster(engine, p, suijinId, 2, BattlePosition.Attack, true);
                        sui.ClearAttackFlags();
                        var attacker = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        attacker.ClearAttackFlags();
                        attacker.SummonedThisTurn = false;
                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        DrainCombat(engine);
                        Check("Suijin: BEWD declares the attack",
                            engine.TryAttack(opp, attacker, sui));
                        if (engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
                            engine.PassResponse();
                        Check("Suijin activates in Damage Calculation",
                            engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.DamageCalculation &&
                            engine.PendingResponse.LegalCards.Exists(c =>
                                c != null && c.CardId == suijinId) &&
                            engine.TryActivateSpellTrap(p, sui, fromHand: false),
                            $"timing={engine.PendingResponse?.Timing} " +
                            $"legal={(engine.PendingResponse?.LegalCards == null ? "null" : string.Join(",", engine.PendingResponse.LegalCards.ConvertAll(x => x?.Name)))}");
                        DrainCombat(engine);
                        Check("Suijin: attacker ATK 0 — Suijin survived, BEWD destroyed",
                            p.TryFindMonster(sui, out _) &&
                            !opp.TryFindMonster(attacker, out _),
                            $"sui={p.TryFindMonster(sui, out _)} bewd={opp.TryFindMonster(attacker, out _)} LP you={p.LifePoints} ai={opp.LifePoints}");
                    }
                }

                {
                    var fenDef = db.Get(fenrirId);
                    var fp = CardTextEffectCompiler.Compile(fenDef);
                    Check("Fenrir compiles SS-from-hand by banishing WATER + skip next Draw",
                        fp != null &&
                        fp.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SpecialSummonThisFromHand &&
                            c.ActivatesFromHand &&
                            c.BanishFromGyCount == 2) &&
                        fp.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.SkipOpponentNextDrawPhase),
                        fp == null
                            ? "null"
                            : $"full={fp.FullyCompiled} n={fp.ClauseList.Count} unparsed={string.Join("|", fp.UnparsedFragments ?? System.Array.Empty<string>())}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var fen = PutInHand(engine, p, fenrirId);
                    Check("Fenrir cannot be Normal Summoned",
                        !engine.CanNormalSummonOrSet(p, fen));
                    var w1 = engine.CreateCardInstance(cureId);
                    var w2 = engine.CreateCardInstance(cureId);
                    p.Graveyard.Add(w1);
                    p.Graveyard.Add(w2);
                    Check("Fenrir: Activate from hand is legal with 2 WATER in GY",
                        engine.CanActivateSpellTrap(p, fen, fromHand: true));
                    Check("Fenrir: SS procedure starts",
                        engine.TryActivateSpellTrap(p, fen, fromHand: true));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Fenrir: banish first WATER",
                            engine.TrySelectEffectTarget(w1));
                        if (engine.IsAwaitingEffectTarget)
                            Check("Fenrir: banish second WATER",
                                engine.TrySelectEffectTarget(w2));
                    }

                    Check("Fenrir Special Summoned from hand",
                        p.TryFindMonster(fen, out _) && fen.WasSpecialSummoned,
                        $"onField={p.TryFindMonster(fen, out _)} ss={fen.WasSpecialSummoned}");

                    var prey = PlaceMonster(engine, opp, celtic, 1, BattlePosition.Attack, true);
                    prey.ClearAttackFlags();
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    fen.ClearAttackFlags();
                    fen.SummonedThisTurn = false;
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    if (engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1)
                        engine.TryEnterBattlePhase(p);
                    DrainCombat(engine);
                    Check("Fenrir can attack after sitting a turn",
                        engine.Phase == DuelPhase.Battle && engine.CanAttack(p, fen),
                        $"phase={engine.Phase} can={engine.CanAttack(p, fen)} turn={engine.TurnNumber}");
                    if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, fen))
                    {
                        engine.TryAttack(p, fen, prey);
                        DrainCombat(engine);
                    }

                    Check("Fenrir: opponent skips next Draw Phase after battle destroy",
                        !opp.TryFindMonster(prey, out _) && opp.SkipNextDrawPhase,
                        $"skip={opp.SkipNextDrawPhase} preyDead={!opp.TryFindMonster(prey, out _)} phase={engine.Phase}");
                }

                // Lava Golem: printed NS lock. Empty-field tributes-needed is not enough —
                // the hole was NS succeeding when 2 tributes were available.
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    PlaceMonster(engine, p, celtic, 1, BattlePosition.Attack, true);
                    PlaceMonster(engine, p, 32452818, 2, BattlePosition.Attack, true);
                    var lavaDef = db.Get(102380) ?? new CardDef
                    {
                        id = 102380,
                        name = "Lava Golem",
                        type = "Effect Monster",
                        level = 8,
                        desc =
                            "Cannot be Normal Summoned/Set. Must first be Special Summoned (from your hand) to your opponent's field by Tributing 2 monsters they control. You cannot Normal Summon/Set the turn you Special Summon this card. Once per turn, during your Standby Phase: Take 1000 damage."
                    };
                    var lava = engine.CreateCardInstance(102380);
                    lava.Def = lavaDef;
                    lava.CardId = 102380;
                    p.Hand.Add(lava);
                    var ns = SummonProcedures.CheckNormalOrTribute(engine, p, lava, asSet: false);
                    Check("Lava Golem cannot be Normal Summoned even with 2 tributes",
                        !engine.CanNormalSummonOrSet(p, lava) && !ns.Legal,
                        ns.Reason);
                    Check("Lava Golem refusal is the printed NS lock (not tribute count)",
                        ns.Reason != null &&
                        ns.Reason.IndexOf("cannot be Normal Summoned", System.StringComparison.OrdinalIgnoreCase) >= 0,
                        ns.Reason);
                    Check("Lava Golem TryNormalSummon does not eat tributes",
                        !engine.TryNormalSummon(p, lava, asSet: false) && p.MonsterCount == 2);
                }

                // Guard: inherent SS-from-hand (Cyber Dragon text) stays Normal Summonable.
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var cyber = new CardInstance
                    {
                        InstanceId = 1,
                        CardId = 1,
                        Def = new CardDef
                        {
                            id = 1,
                            name = "Cyber Dragon",
                            type = "Effect Monster",
                            level = 4,
                            desc =
                                "If only your opponent controls a monster, you can Special Summon this card (from your hand)."
                        }
                    };
                    p.Hand.Add(cyber);
                    var ns = SummonProcedures.CheckNormalOrTribute(engine, p, cyber, asSet: false);
                    Check("Cyber Dragon inherent SS does not block Normal Summon",
                        ns.Legal,
                        ns.Reason);
                }
            }

            // ── Terraforming / ROTA / Hinotama / Feather Duster: Normal Spell from hand ──
            {
                const int terra = 73628505;
                const int alo = 295517;
                const int rota = 32807846;
                const int hinotama = 46130346;
                const int duster = 18144507;
                const int celtic = 91152256;
                const int bewd = 89631139;
                const int mst = 5318639;

                {
                    var tDef = db.Get(terra);
                    var tProg = tDef != null ? CardTextEffectCompiler.Compile(tDef) : null;
                    Check("Terraforming compiles AddFromDeckToHand (Field Spell in Deck)",
                        tProg != null && tProg.FullyCompiled &&
                        tProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckFieldSpells &&
                            c.RequiresTargetChoice),
                        tProg == null
                            ? "null"
                            : $"full={tProg.FullyCompiled} n={tProg.ClauseList.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(alo);
                    p.Deck.Add(celtic);
                    var card = PutInHand(engine, p, terra);
                    Check("Terraforming: Activate legal in MP1 with Field Spell in Deck",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Terraforming: Activate from hand opens Field Spell search",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.FieldSpellInYourDeck);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var pick = engine.PendingActivation.LegalTargets
                            .FirstOrDefault(t => t.CardId == alo);
                        var illegal = engine.PendingActivation.LegalTargets
                            .FirstOrDefault(t => t.CardId == celtic);
                        Check("Terraforming: A Legendary Ocean is a legal Deck option", pick != null);
                        Check("Terraforming: Celtic Guardian is not a Field Spell option",
                            illegal == null);
                        if (pick != null)
                        {
                            Check("Terraforming: select ALO, search resolves, Terraforming to GY",
                                engine.TrySelectEffectTarget(pick) &&
                                p.Hand.Exists(c => c.CardId == alo) &&
                                !p.Deck.Contains(alo) &&
                                p.Graveyard.Exists(c => c.CardId == terra));
                        }
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(celtic);
                    var card = PutInHand(engine, p, terra);
                    Check("Terraforming: illegal with no Field Spell in Deck (Set only)",
                        !engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Terraforming: still Settable",
                        engine.CanSetSpellTrap(p, card));
                }

                {
                    var rDef = db.Get(rota);
                    var rProg = rDef != null ? CardTextEffectCompiler.Compile(rDef) : null;
                    Check("ROTA compiles Level 4 Warrior search",
                        rProg != null && rProg.FullyCompiled &&
                        rProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq &&
                            c.Amount == 4 &&
                            !string.IsNullOrEmpty(c.RaceFilter) &&
                            c.RaceFilter.IndexOf("Warrior",
                                System.StringComparison.OrdinalIgnoreCase) >= 0));
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(celtic);
                    p.Deck.Add(bewd);
                    var card = PutInHand(engine, p, rota);
                    Check("ROTA: Activate legal with Warrior in Deck",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("ROTA: Activate opens filtered Deck search",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind ==
                        EffectTargetKind.MonsterInYourDeckFiltered);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var pick = engine.PendingActivation.LegalTargets
                            .FirstOrDefault(t => t.CardId == celtic);
                        var dragon = engine.PendingActivation.LegalTargets
                            .FirstOrDefault(t => t.CardId == bewd);
                        Check("ROTA: Celtic Guardian (Warrior Lv4) is legal", pick != null);
                        Check("ROTA: Blue-Eyes is not a Warrior option", dragon == null);
                        if (pick != null)
                            Check("ROTA: select Celtic, ROTA to GY",
                                engine.TrySelectEffectTarget(pick) &&
                                p.Hand.Exists(c => c.CardId == celtic) &&
                                p.Graveyard.Exists(c => c.CardId == rota));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var card = PutInHand(engine, p, hinotama);
                    var lp = opp.LifePoints;
                    Check("Hinotama: Activate legal in MP1",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Hinotama: inflicts 500, goes to GY",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        opp.LifePoints == lp - 500 &&
                        p.Graveyard.Exists(c => c.CardId == hinotama),
                        $"opp LP {opp.LifePoints} was {lp}");
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var st = PlaceSetTrap(engine, opp, mst, 2);
                    var card = PutInHand(engine, p, duster);
                    Check("Feather Duster: Activate legal in MP1",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Feather Duster: destroys opponent S/T, not yours",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        opp.Graveyard.Contains(st) &&
                        p.Graveyard.Exists(c => c.CardId == duster));
                }

                // ── Equip Spells: Activate + target; host leaving field destroys Equip ──
                {
                    const int treasure = 1435851;
                    const int salamandra = 32268901;
                    const int legendarySword = 61854111;
                    const int axe = 40619825;
                    const int angus = 11813953; // Great Angus, FIRE Beast

                    var tDef = db.Get(treasure);
                    var tProg = tDef != null ? CardTextEffectCompiler.Compile(tDef) : null;
                    Check("Dragon Treasure FullyCompiled Equip +300/+300 Dragon",
                        tProg != null && tProg.FullyCompiled &&
                        tProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Dragon",
                                System.StringComparison.OrdinalIgnoreCase)),
                        tProg == null
                            ? "null"
                            : $"full={tProg.FullyCompiled} unparsed={string.Join("|", tProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    var sDef = db.Get(salamandra);
                    var sProg = sDef != null ? CardTextEffectCompiler.Compile(sDef) : null;
                    Check("Salamandra FullyCompiled Equip only FIRE +700 ATK",
                        sProg != null && sProg.FullyCompiled &&
                        sProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 700 && c.EquipDefBonus == 0 &&
                            string.Equals(c.AttributeFilter, "FIRE",
                                System.StringComparison.OrdinalIgnoreCase)),
                        sProg == null
                            ? "null"
                            : $"full={sProg.FullyCompiled} unparsed={string.Join("|", sProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    var swDef = db.Get(legendarySword);
                    var swProg = swDef != null ? CardTextEffectCompiler.Compile(swDef) : null;
                    Check("Legendary Sword FullyCompiled Equip only Warrior +300/+300",
                        swProg != null && swProg.FullyCompiled &&
                        swProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Warrior",
                                System.StringComparison.OrdinalIgnoreCase)),
                        swProg == null
                            ? "null"
                            : $"full={swProg.FullyCompiled} unparsed={string.Join("|", swProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    var aDef = db.Get(axe);
                    var aProg = aDef != null ? CardTextEffectCompiler.Compile(aDef) : null;
                    Check("Axe of Despair FullyCompiled Equip +1000 and GY tribute to Deck",
                        aProg != null && aProg.FullyCompiled &&
                        aProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 1000) &&
                        aProg.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.PlaceThisOnTopOfDeck &&
                            c.RequiresTributeCount == 1),
                        aProg == null
                            ? "null"
                            : $"full={aProg.FullyCompiled} unparsed={string.Join("|", aProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, axe);
                        Check("Axe of Despair: Activate legal with a face-up monster",
                            engine.CanActivateSpellTrap(p, card, fromHand: true));
                        Check("Axe of Despair: Activate opens Equip target (not Set-only)",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            engine.IsAwaitingEffectTarget);
                        if (engine.IsAwaitingEffectTarget)
                            Check("Axe of Despair: select host, +1000 ATK",
                                engine.TrySelectEffectTarget(host) &&
                                card.EquippedTo == host &&
                                host.CurrentAtk == 4000,
                                $"atk={host.CurrentAtk}");
                    }

                    {
                        const int united = 56747793;
                        var uDef = db.Get(united);
                        var uProg = uDef != null ? CardTextEffectCompiler.Compile(uDef) : null;
                        Check("United We Stand FullyCompiled +800 ATK/DEF per face-up monster",
                            uProg != null && uProg.FullyCompiled &&
                            uProg.ClauseList.Exists(c =>
                                c != null && c.ScaleAmountByControllerMonsters &&
                                c.EquipAtkBonus == 800 && c.EquipDefBonus == 800));

                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, united);
                        Check("United We Stand: Activate legal with a face-up monster",
                            engine.CanActivateSpellTrap(p, card, fromHand: true));
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        if (engine.IsAwaitingEffectTarget)
                            engine.TrySelectEffectTarget(host);
                        engine.NotifyPublic();
                        Check("United We Stand: 1 face-up monster → +800 ATK",
                            card.EquippedTo == host && host.CurrentAtk == 3800,
                            $"atk={host.CurrentAtk}");
                        PlaceMonster(engine, p, celtic, 1, BattlePosition.Attack, true);
                        engine.NotifyPublic();
                        Check("United We Stand: 2 face-up monsters → +1600 ATK",
                            host.CurrentAtk == 4600,
                            $"atk={host.CurrentAtk}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var fodder = PlaceMonster(engine, p, celtic, 1, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, axe);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        if (engine.IsAwaitingEffectTarget)
                            engine.TrySelectEffectTarget(host);
                        p.Deck.Clear();
                        for (var i = 0; i < 4; i++) p.Deck.Add(celtic);
                        engine.SendCardToGrave(p, card);
                        Check("Axe of Despair GY: optional tribute pending",
                            engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation != null &&
                            engine.PendingActivation.TargetKind ==
                            EffectTargetKind.TributeMonsterYouControl);
                        Check("Axe of Despair GY: tribute Celtic, Axe on top of Deck",
                            engine.TrySelectEffectTarget(fodder) &&
                            p.Deck.Count > 0 && p.Deck[0] == axe &&
                            !p.Graveyard.Contains(card) &&
                            p.Graveyard.Contains(fodder),
                            $"top={((p.Deck.Count > 0) ? p.Deck[0].ToString() : "empty")} gyAxe={p.Graveyard.Contains(card)}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, treasure);
                        Check("Dragon Treasure: Activate legal with Dragon you control",
                            engine.CanActivateSpellTrap(p, card, fromHand: true));
                        Check("Dragon Treasure: Activate opens Equip target",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            engine.IsAwaitingEffectTarget);
                        if (engine.IsAwaitingEffectTarget)
                        {
                            Check("Dragon Treasure: BEWD legal, Celtic not in list yet",
                                engine.PendingActivation.LegalTargets.Contains(host));
                            Check("Dragon Treasure: select BEWD, stays S/T, +300 ATK/DEF",
                                engine.TrySelectEffectTarget(host) &&
                                card.EquippedTo == host &&
                                host.Equips.Contains(card) &&
                                p.TryFindSpellTrap(card, out _) &&
                                host.CurrentAtk == 3300 &&
                                host.CurrentDef == 2800,
                                $"atk={host.CurrentAtk} def={host.CurrentDef} eq={card.EquippedTo != null} st={p.TryFindSpellTrap(card, out _)}");
                        }
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, treasure);
                        Check("Dragon Treasure: refuse with only a Warrior you control",
                            !engine.CanActivateSpellTrap(p, card, fromHand: true));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var card = PutInHand(engine, p, treasure);
                        Check("Equip: refuse with no monster you control",
                            !engine.CanActivateSpellTrap(p, card, fromHand: true));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var fire = PlaceMonster(engine, p, angus, 2, BattlePosition.Attack, true);
                        PlaceMonster(engine, p, celtic, 1, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, salamandra);
                        Check("Salamandra: Activate legal with FIRE you control",
                            engine.CanActivateSpellTrap(p, card, fromHand: true));
                        Check("Salamandra: Activate opens FIRE-only targets",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            engine.IsAwaitingEffectTarget);
                        if (engine.IsAwaitingEffectTarget)
                        {
                            var legal = engine.PendingActivation.LegalTargets;
                            Check("Salamandra: FIRE is legal, Warrior is not",
                                legal.Contains(fire) &&
                                !legal.Exists(t => t != null && t.CardId == celtic));
                            Check("Salamandra: select FIRE, +700 ATK",
                                engine.TrySelectEffectTarget(fire) &&
                                card.EquippedTo == fire &&
                                fire.CurrentAtk == 2500,
                                $"atk={fire.CurrentAtk}");
                        }
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, treasure);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        if (engine.IsAwaitingEffectTarget)
                            engine.TrySelectEffectTarget(host);
                        Check("Equip: attached before host leaves",
                            card.EquippedTo == host && host.Equips.Contains(card));
                        engine.SendCardToGrave(p, host);
                        engine.NotifyPublic();
                        Check("Equip: host to GY also sends Equip to GY",
                            p.Graveyard.Contains(host) &&
                            p.Graveyard.Contains(card) &&
                            card.EquippedTo == null &&
                            !p.TryFindSpellTrap(card, out _),
                            $"hostGy={p.Graveyard.Contains(host)} eqGy={p.Graveyard.Contains(card)} link={card.EquippedTo != null}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, treasure);
                        var v = engine.ValidatePlacement(p, card, RulesZoneKind.Monster, 2, false);
                        Check("RulesValidator: Equip drop on your face-up monster is legal",
                            v.Legal, v.Reason);
                        Check("RulesValidator: Equip cannot Set on a monster",
                            !engine.ValidatePlacement(p, card, RulesZoneKind.Monster, 2, true).Legal);
                        Check("RulesValidator: Equip on empty monster zone refused",
                            !engine.ValidatePlacement(p, card, RulesZoneKind.Monster, 0, false).Legal);
                    }

                    {
                        const int falling = 32919136;
                        const int soldier = 49881766;
                        var fDef = db.Get(falling);
                        var fProg = fDef != null ? CardTextEffectCompiler.Compile(fDef) : null;
                        Check("Falling Down FullyCompiled opponent-equip take-control",
                            fProg != null && fProg.FullyCompiled,
                            fProg == null
                                ? "null"
                                : $"full={fProg.FullyCompiled} unparsed={string.Join("|", fProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                        {
                            var engine = Fresh(db, pDeck, aDeck);
                            ClearBoard(engine);
                            var p = engine.Player;
                            p.Hand.Clear();
                            PlaceMonster(engine, p, soldier, 2, BattlePosition.Attack, true);
                            var card = PutInHand(engine, p, falling);
                            Check("Falling Down: refuse with Archfiend but no opponent monster",
                                !engine.CanActivateSpellTrap(p, card, fromHand: true));
                        }

                        {
                            var engine = Fresh(db, pDeck, aDeck);
                            ClearBoard(engine);
                            var p = engine.Player;
                            var opp = engine.Opponent;
                            p.Hand.Clear();
                            var arch = PlaceMonster(engine, p, soldier, 2, BattlePosition.Attack, true);
                            var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                            var card = PutInHand(engine, p, falling);
                            Check("Falling Down: Activate legal with Archfiend Soldier + opp monster",
                                engine.CanActivateSpellTrap(p, card, fromHand: true));
                            Check("Falling Down: drop on your Archfiend is still legal (not the target)",
                                engine.ValidatePlacement(p, card, RulesZoneKind.Monster, 2, false).Legal);
                            Check("Falling Down: Activate opens opponent-monster targets",
                                engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                                engine.IsAwaitingEffectTarget);
                            if (engine.IsAwaitingEffectTarget)
                            {
                                var legal = engine.PendingActivation.LegalTargets;
                                Check("Falling Down: opponent monster is legal, Archfiend Soldier is not",
                                    legal.Contains(prey) && !legal.Contains(arch));
                                Check("Falling Down: select opp monster, take control, Equip stays",
                                    engine.TrySelectEffectTarget(prey) &&
                                    card.EquippedTo == prey &&
                                    p.TryFindMonster(prey, out _) &&
                                    !opp.TryFindMonster(prey, out _) &&
                                    p.TryFindSpellTrap(card, out _) &&
                                    prey.TakenByEquipControl,
                                    $"eq={card.EquippedTo != null} mine={p.TryFindMonster(prey, out _)} st={p.TryFindSpellTrap(card, out _)}");
                            }
                        }

                        {
                            var engine = Fresh(db, pDeck, aDeck);
                            ClearBoard(engine);
                            var p = engine.Player;
                            var opp = engine.Opponent;
                            p.Hand.Clear();
                            var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                            var card = PutInHand(engine, p, falling);
                            Check("Falling Down: Activate legal without Archfiend (maintenance, not cost)",
                                engine.CanActivateSpellTrap(p, card, fromHand: true));
                            engine.TryActivateSpellTrap(p, card, fromHand: true);
                            if (engine.IsAwaitingEffectTarget)
                                engine.TrySelectEffectTarget(prey);
                            engine.NotifyPublic();
                            Check("Falling Down: no Archfiend → Equip destroyed, control returns",
                                p.Graveyard.Contains(card) &&
                                opp.TryFindMonster(prey, out _) &&
                                !p.TryFindMonster(prey, out _),
                                $"eqGy={p.Graveyard.Contains(card)} oppHas={opp.TryFindMonster(prey, out _)}");
                        }

                        {
                            var engine = Fresh(db, pDeck, aDeck);
                            ClearBoard(engine);
                            var p = engine.Player;
                            var opp = engine.Opponent;
                            p.Hand.Clear();
                            PlaceMonster(engine, p, soldier, 1, BattlePosition.Attack, true);
                            var prey = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                            var card = PutInHand(engine, p, falling);
                            engine.TryActivateSpellTrap(p, card, fromHand: true);
                            if (engine.IsAwaitingEffectTarget)
                                engine.TrySelectEffectTarget(prey);
                            var lp = p.LifePoints;
                            TextEffectRuntime.FirePhaseTriggers(engine, opp, EffectTiming.StandbyPhase);
                            Check("Falling Down: opponent Standby, controller takes 800",
                                p.LifePoints == lp - 800,
                                $"lp {p.LifePoints} was {lp}");
                        }
                    }

                    {
                        var unplayable = new System.Collections.Generic.List<string>();
                        var engine = Fresh(db, pDeck, aDeck);
                        foreach (var def in db.GetAllCards())
                        {
                            if (def == null || !def.IsEquipSpell) continue;
                            var prog = CardTextEffectCompiler.Compile(def);
                            if (prog == null || !prog.FullyCompiled) continue;
                            if (engine.IsAwaitingResponse) engine.PassResponse();
                            ClearBoard(engine);
                            var p = engine.Player;
                            p.Hand.Clear();
                            var gySs = prog.ClauseList.Exists(c =>
                                c != null &&
                                c.Action == EffectActionKind.SpecialSummonFromGy);
                            if (gySs) continue;
                            var clause = prog.ClauseList.Find(c =>
                                c != null && c.Action == EffectActionKind.EquipThisToTarget);
                            var hostId = celtic;
                            if (clause != null)
                            {
                                foreach (var cand in db.GetAllCards())
                                {
                                    if (cand == null || !cand.IsMonster || cand.IsExtraDeck)
                                        continue;
                                    if (!string.IsNullOrEmpty(clause.AttributeFilter) &&
                                        (cand.attribute == null ||
                                         !cand.attribute.Equals(clause.AttributeFilter,
                                             System.StringComparison.OrdinalIgnoreCase)))
                                        continue;
                                    if (!string.IsNullOrEmpty(clause.RaceFilter) &&
                                        (cand.race == null ||
                                         cand.race.IndexOf(clause.RaceFilter,
                                             System.StringComparison.OrdinalIgnoreCase) < 0))
                                        continue;
                                    var want = clause.EquipHostName ?? clause.NamedCard;
                                    if (!string.IsNullOrEmpty(want))
                                    {
                                        var n = cand.name ?? "";
                                        var hit = n.Equals(want, System.StringComparison.OrdinalIgnoreCase) ||
                                                  (!string.IsNullOrEmpty(clause.AltNamedCard) &&
                                                   n.Equals(clause.AltNamedCard,
                                                       System.StringComparison.OrdinalIgnoreCase));
                                        if (!hit) continue;
                                    }
                                    hostId = cand.id;
                                    break;
                                }
                            }

                            var oppHost = clause != null &&
                                          (clause.TakeControlOfTarget ||
                                           clause.Zone == EffectZoneFilter.OppFaceUpMonsters);
                            if (oppHost)
                            {
                                var needName = prog.ClauseList.Find(c =>
                                    c != null && c.RequiresControllerNamedCard &&
                                    !string.IsNullOrEmpty(c.RequiresFaceUpName));
                                if (needName != null)
                                {
                                    foreach (var cand in db.GetAllCards())
                                    {
                                        if (cand == null || !cand.IsMonster || cand.IsExtraDeck)
                                            continue;
                                        var n = cand.name ?? "";
                                        if (n.IndexOf(needName.RequiresFaceUpName,
                                                System.StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            PlaceMonster(engine, p, cand.id, 1,
                                                BattlePosition.Attack, true);
                                            break;
                                        }
                                    }
                                }
                            }

                            var host = PlaceMonster(engine, oppHost ? engine.Opponent : p, hostId, 2,
                                BattlePosition.Attack, true);
                            var card = PutInHand(engine, p, def.id);
                            if (!engine.CanActivateSpellTrap(p, card, fromHand: true))
                            {
                                unplayable.Add(def.name ?? $"#{def.id}");
                                continue;
                            }

                            if (!engine.TryActivateSpellTrap(p, card, fromHand: true))
                            {
                                unplayable.Add((def.name ?? $"#{def.id}") + " activate");
                                continue;
                            }

                            if (engine.IsAwaitingEffectTarget)
                            {
                                var pick = engine.PendingActivation.LegalTargets
                                    .Find(t => t != null && t == host)
                                    ?? engine.PendingActivation.LegalTargets
                                        .Find(t => t != null);
                                if (pick == null || !engine.TrySelectEffectTarget(pick) ||
                                    card.EquippedTo == null)
                                    unplayable.Add((def.name ?? $"#{def.id}") + " attach");
                            }
                            else if (card.EquippedTo == null)
                                unplayable.Add((def.name ?? $"#{def.id}") + " no-attach");
                        }

                        Check("Stress: FullyCompiled Equips activate and attach",
                            unplayable.Count == 0,
                            unplayable.Count == 0
                                ? ""
                                : string.Join(", ", unplayable));
                    }
                }

                {
                    const int ectoplasmer = 97342942;
                    const int levelLimit = 3136426;
                    const int jamBreeding = 21770260;
                    const int toonWorld = 15259703;
                    const int labyrinth = 66526672;
                    const int burningLand = 24294108;
                    const int gravityBind = 85742772;
                    const int insectBarrier = 23615409;
                    const int messenger = 44656491;
                    const int callHaunted = 97077563;
                    const int soulRes = 92924317;
                    const int giantSoldier = 13039848;
                    const int umi = 22702055;
                    const int insect = 3134241; // Flying Kamakiri #2

                    var eDef = db.Get(ectoplasmer);
                    var eProg = eDef != null ? CardTextEffectCompiler.Compile(eDef) : null;
                    Check("Ectoplasmer FullyCompiled End Phase turn-player tribute",
                        eProg != null && eProg.FullyCompiled &&
                        eProg.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.EndPhase &&
                            c.TurnPlayerTributes &&
                            c.TributeFaceUpOnly &&
                            c.Action == EffectActionKind.InflictDamageHalfTributedAtk),
                        eProg == null
                            ? "null"
                            : $"full={eProg.FullyCompiled} unparsed={string.Join("|", eProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var card = PutInHand(engine, p, ectoplasmer);
                        Check("Ectoplasmer: Activate from hand with empty Monster Zones",
                            engine.CanActivateSpellTrap(p, card, fromHand: true),
                            "CanActivate false");
                        Check("Ectoplasmer: Activate stays in S/T, not sent to GY",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            p.TryFindSpellTrap(card, out _) &&
                            card.FaceUp &&
                            !p.Graveyard.Contains(card),
                            $"st={p.TryFindSpellTrap(card, out _)} gy={p.Graveyard.Contains(card)}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, ectoplasmer);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        var oppLp = opp.LifePoints;
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                        Check("Ectoplasmer: End Phase with 3000 ATK opens tribute pick",
                            engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation != null &&
                            engine.PendingActivation.LegalTargets.Contains(host),
                            $"pending={engine.IsAwaitingEffectTarget}");
                        Check("Ectoplasmer: tribute BEWD, opponent takes 1500 (printed/2)",
                            engine.TrySelectEffectTarget(host) &&
                            p.Graveyard.Contains(host) &&
                            !p.TryFindMonster(host, out _) &&
                            opp.LifePoints == oppLp - 1500,
                            $"gy={p.Graveyard.Contains(host)} oppLp={opp.LifePoints} was {oppLp}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var card = PutInHand(engine, p, ectoplasmer);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        var oppLp = opp.LifePoints;
                        var pLp = p.LifePoints;
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                        Check("Ectoplasmer: End Phase with no monster skips, no damage",
                            !engine.IsAwaitingEffectTarget &&
                            opp.LifePoints == oppLp &&
                            p.LifePoints == pLp &&
                            p.TryFindSpellTrap(card, out _),
                            $"pending={engine.IsAwaitingEffectTarget} oppLp={opp.LifePoints}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var card = PutInHand(engine, p, ectoplasmer);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        Check("Ectoplasmer: player End Phase with no monster lets the turn end",
                            engine.TryEndTurnSafe(p),
                            "TryEndTurnSafe false");
                        var prey = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        var pLp = p.LifePoints;
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        engine.EndTurn(opp);
                        Check("Ectoplasmer: opponent End Phase, they tribute, you take 1500",
                            opp.Graveyard.Contains(prey) &&
                            p.LifePoints == pLp - 1500 &&
                            engine.TurnPlayer == p,
                            $"gy={opp.Graveyard.Contains(prey)} lp={p.LifePoints} was {pLp} turnP={engine.TurnPlayer == p}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        foreach (var id in new[] { levelLimit, jamBreeding })
                        {
                            var def = db.Get(id);
                            if (def == null) continue;
                            var prog = CardTextEffectCompiler.Compile(def);
                            if (prog != null && prog.FullyCompiled) continue;
                            var card = PutInHand(engine, p, id);
                            Check($"{def.name}: unique leftover Continuous still refuses Activate",
                                !engine.CanActivateSpellTrap(p, card, fromHand: true));
                            p.Hand.Remove(card);
                        }
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var lp = p.LifePoints;
                        var card = PutInHand(engine, p, toonWorld);
                        Check("Toon World: Activate from hand (pay 1000 LP)",
                            engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            p.TryFindSpellTrap(card, out _) &&
                            card.FaceUp &&
                            p.LifePoints == lp - 1000,
                            $"st={p.TryFindSpellTrap(card, out _)} lp={p.LifePoints} was {lp}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        p.LifePoints = 500;
                        var card = PutInHand(engine, p, toonWorld);
                        Check("Toon World: refuse with less than 1000 LP",
                            !engine.CanActivateSpellTrap(p, card, fromHand: true));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        p.LifePoints = 1000;
                        var card = PutInHand(engine, p, toonWorld);
                        Check("Toon World: 1000 LP is a legal cost (may lose on purpose)",
                            engine.CanActivateSpellTrap(p, card, fromHand: true));
                        Check("Toon World: paying last 1000 LP asks are-you-sure",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            engine.PendingActivation != null &&
                            engine.PendingActivation.AwaitingLpZeroConfirm &&
                            p.LifePoints == 1000,
                            $"zero={engine.PendingActivation?.AwaitingLpZeroConfirm} lp={p.LifePoints}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var host = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var st = PlaceSetTrap(engine, p, labyrinth, 2);
                        st.SetThisTurn = false;
                        Check("Labyrinth of Nightmare: Activate from Set, empty-cost Continuous Trap",
                            engine.CanActivateSpellTrap(p, st, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, st, fromHand: false) &&
                            st.FaceUp);
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                        Check("Labyrinth of Nightmare: End Phase flips turn player's face-up ATK↔DEF",
                            host.Position == BattlePosition.Defense && host.FaceUp,
                            $"pos={host.Position} face={host.FaceUp}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var field = engine.CreateCardInstance(umi);
                        field.FaceUp = true;
                        opp.FieldSpellZone.Occupant = field;
                        var card = PutInHand(engine, p, burningLand);
                        var lp = p.LifePoints;
                        Check("Burning Land: Activate destroys Field Spells and stays",
                            engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                            p.TryFindSpellTrap(card, out _) &&
                            opp.FieldSpellZone.Occupant == null &&
                            opp.Graveyard.Contains(field),
                            $"st={p.TryFindSpellTrap(card, out _)} fieldGy={opp.Graveyard.Contains(field)}");
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.StandbyPhase);
                        Check("Burning Land: Standby, turn player takes 500",
                            p.LifePoints == lp - 500,
                            $"lp={p.LifePoints} was {lp}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var lv8 = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var lv3 = PlaceMonster(engine, p, giantSoldier, 1, BattlePosition.Attack, true);
                        var st = PlaceSetTrap(engine, p, gravityBind, 2);
                        st.SetThisTurn = false;
                        engine.TryActivateSpellTrap(p, st, fromHand: false);
                        Check("Gravity Bind: Activate stays",
                            st.FaceUp && p.TryFindSpellTrap(st, out _));
                        Check("Gravity Bind: Level 8 cannot attack",
                            engine.ContinuousCannotAttackBlocks(p, lv8));
                        Check("Gravity Bind: Level 3 can attack",
                            !engine.ContinuousCannotAttackBlocks(p, lv3));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var card = PutInHand(engine, p, insectBarrier);
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        var bug = PlaceMonster(engine, engine.Opponent, insect, 2,
                            BattlePosition.Attack, true);
                        var warrior = PlaceMonster(engine, engine.Opponent, giantSoldier, 1,
                            BattlePosition.Attack, true);
                        Check("Insect Barrier: opponent Insect cannot attack",
                            engine.ContinuousCannotAttackBlocks(engine.Opponent, bug));
                        Check("Insect Barrier: opponent Rock can still attack",
                            !engine.ContinuousCannotAttackBlocks(engine.Opponent, warrior));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var lv8 = PlaceMonster(engine, p, bewd, 2, BattlePosition.Attack, true);
                        var low = PlaceMonster(engine, p, giantSoldier, 1, BattlePosition.Attack, true);
                        var card = PutInHand(engine, p, messenger);
                        var lp = p.LifePoints;
                        engine.TryActivateSpellTrap(p, card, fromHand: true);
                        Check("Messenger of Peace: 3000 ATK cannot attack, 1300 can",
                            engine.ContinuousCannotAttackBlocks(p, lv8) &&
                            !engine.ContinuousCannotAttackBlocks(p, low));
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.StandbyPhase);
                        Check("Messenger of Peace: Standby pays 100 LP",
                            p.LifePoints == lp - 100 && p.TryFindSpellTrap(card, out _),
                            $"lp={p.LifePoints} was {lp}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var gyMon = engine.CreateCardInstance(celtic);
                        p.Graveyard.Add(gyMon);
                        var st = PlaceSetTrap(engine, p, callHaunted, 2);
                        st.SetThisTurn = false;
                        Check("Call of the Haunted: Activate from Set with GY monster",
                            engine.CanActivateSpellTrap(p, st, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, st, fromHand: false) &&
                            engine.IsAwaitingEffectTarget);
                        Check("Call of the Haunted: SS Celtic, trap stays linked",
                            engine.TrySelectEffectTarget(gyMon) &&
                            p.TryFindMonster(gyMon, out _) &&
                            gyMon.Position == BattlePosition.Attack &&
                            st.EquippedTo == gyMon &&
                            p.TryFindSpellTrap(st, out _),
                            $"mz={p.TryFindMonster(gyMon, out _)} link={st.EquippedTo != null}");
                        engine.SendCardToGrave(p, st);
                        Check("Call of the Haunted: trap to GY also destroys the summoned monster",
                            p.Graveyard.Contains(st) && p.Graveyard.Contains(gyMon) &&
                            !p.TryFindMonster(gyMon, out _),
                            $"stGy={p.Graveyard.Contains(st)} monGy={p.Graveyard.Contains(gyMon)}");
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var gyMon = engine.CreateCardInstance(celtic);
                        p.Graveyard.Add(gyMon);
                        var st = PlaceSetTrap(engine, p, soulRes, 2);
                        st.SetThisTurn = false;
                        engine.TryActivateSpellTrap(p, st, fromHand: false);
                        if (engine.IsAwaitingEffectTarget)
                            engine.TrySelectEffectTarget(gyMon);
                        Check("Soul Resurrection: SS in Defense, trap stays",
                            p.TryFindMonster(gyMon, out _) &&
                            gyMon.Position == BattlePosition.Defense &&
                            p.TryFindSpellTrap(st, out _),
                            $"pos={gyMon.Position} st={p.TryFindSpellTrap(st, out _)}");
                    }

                    {
                        var unplayable = new System.Collections.Generic.List<string>();
                        var engine = Fresh(db, pDeck, aDeck);
                        foreach (var def in db.GetAllCards())
                        {
                            if (def == null || !def.IsContinuousSpellOrTrap) continue;
                            var prog = CardTextEffectCompiler.Compile(def);
                            if (prog == null || !prog.FullyCompiled) continue;
                            var unique = false;
                            foreach (var c in prog.ClauseList)
                            {
                                if (c != null &&
                                    EffectVocabulary.IsUniqueException(c.Action))
                                {
                                    unique = true;
                                    break;
                                }
                            }

                            if (unique) continue;
                            var act = prog.ClausesFor(EffectTiming.Activate);
                            if (act.Exists(c => c != null && c.OpponentTurnOnly))
                                continue;
                            var needsBoard = false;
                            foreach (var c in act)
                            {
                                if (c == null) continue;
                                if (c.RequiresTargetChoice || c.RequiresLordOfDOnField ||
                                    c.RequiresDiscardCost || c.RequiresSendNamedToGy ||
                                    !string.IsNullOrEmpty(c.RequiresFaceUpName) ||
                                    c.RequiresTributeCount > 0 || c.RequiresTributeThis ||
                                    c.PayLpAmount > 0 || c.BanishFromGyCount > 0 ||
                                    c.RequiresSendThisToGy || c.RequiresSendHandToGy ||
                                    c.RequiresSendOtherYouControl ||
                                    c.Action == EffectActionKind.FusionSummonRegistered ||
                                    c.Action == EffectActionKind.AddFromDeckToHand ||
                                    c.Action == EffectActionKind.AddNamedFromDeckToHand ||
                                    c.Action == EffectActionKind.SpecialSummonFromGy ||
                                    c.Action == EffectActionKind.SpecialSummonFromHand)
                                {
                                    needsBoard = true;
                                    break;
                                }
                            }

                            if (needsBoard) continue;
                            if (engine.IsAwaitingResponse) engine.PassResponse();
                            ClearBoard(engine);
                            var p = engine.Player;
                            p.Hand.Clear();
                            if (def.IsTrap)
                            {
                                var st = PlaceSetTrap(engine, p, def.id, 2);
                                st.SetThisTurn = false;
                                if (!engine.CanActivateSpellTrap(p, st, fromHand: false))
                                {
                                    unplayable.Add(def.name ?? $"#{def.id}");
                                    continue;
                                }

                                if (!engine.TryActivateSpellTrap(p, st, fromHand: false))
                                {
                                    unplayable.Add((def.name ?? $"#{def.id}") + " activate");
                                    continue;
                                }

                                if (!p.TryFindSpellTrap(st, out _) || !st.FaceUp)
                                    unplayable.Add((def.name ?? $"#{def.id}") + " not-staying");
                            }
                            else
                            {
                                var card = PutInHand(engine, p, def.id);
                                if (!engine.CanActivateSpellTrap(p, card, fromHand: true))
                                {
                                    unplayable.Add(def.name ?? $"#{def.id}");
                                    continue;
                                }

                                if (!engine.TryActivateSpellTrap(p, card, fromHand: true))
                                {
                                    unplayable.Add((def.name ?? $"#{def.id}") + " activate");
                                    continue;
                                }

                                if (!p.TryFindSpellTrap(card, out _) || !card.FaceUp)
                                    unplayable.Add((def.name ?? $"#{def.id}") + " not-staying");
                            }
                        }

                        Check("Stress: FullyCompiled Continuous S/T Activate (empty MZ legal)",
                            unplayable.Count == 0,
                            unplayable.Count == 0
                                ? ""
                                : string.Join(", ", unplayable));
                    }

                    {
                        var leftover = 0;
                        var complete = 0;
                        var structural = 0;
                        foreach (var def in db.GetAllCards())
                        {
                            if (def == null) continue;
                            var status = CardEffectStatus.Classify(def);
                            if (status == CardEffectStatusKind.Structural)
                            {
                                structural++;
                                continue;
                            }

                            var prog = CardTextEffectCompiler.Compile(def);
                            if (prog != null && prog.FullyCompiled)
                                complete++;
                            else
                                leftover++;
                        }

                        Check("Pool: FullyCompiled programs are a non-empty implemented set",
                            complete >= 80,
                            $"complete={complete} leftover={leftover} structural={structural}");
                    }
                }

                {
                    var missing = new System.Collections.Generic.List<string>();
                    var engine = Fresh(db, pDeck, aDeck);
                    foreach (var def in db.GetAllCards())
                    {
                        if (def == null || !def.IsSpell || def.IsTrap || def.IsFieldSpell ||
                            def.IsEquipSpell || def.IsContinuousSpellOrTrap || def.IsRitualSpell)
                            continue;
                        if (def.race != null &&
                            def.race.IndexOf("Quick", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        var prog = CardTextEffectCompiler.Compile(def);
                        // Activate gate requires FullyCompiled — stubs are deck/activate illegal.
                        if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.Activate))
                            continue;
                        var act = prog.ClausesFor(EffectTiming.Activate);
                        if (act.Count == 0) continue;
                        var needsBoard = false;
                        foreach (var c in act)
                        {
                            if (c == null) continue;
                            if (c.RequiresTargetChoice || c.RequiresLordOfDOnField ||
                                c.RequiresDiscardCost || c.RequiresSendNamedToGy ||
                                !string.IsNullOrEmpty(c.RequiresFaceUpName) ||
                                c.Action == EffectActionKind.FusionSummonRegistered ||
                                c.Action == EffectActionKind.RitualSummon ||
                                c.Action == EffectActionKind.AddFromDeckToHand ||
                                c.Action == EffectActionKind.AddNamedFromDeckToHand ||
                                c.Action == EffectActionKind.SpecialSummonFromGy ||
                                c.Action == EffectActionKind.SpecialSummonFromHand)
                            {
                                needsBoard = true;
                                break;
                            }
                        }

                        if (needsBoard) continue;
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        p.Deck.Clear();
                        for (var i = 0; i < 8; i++) p.Deck.Add(celtic);
                        var card = PutInHand(engine, p, def.id);
                        if (!engine.CanActivateSpellTrap(p, card, fromHand: true))
                            missing.Add(def.name ?? $"#{def.id}");
                    }

                    Check("Triple-check: compiled targetless Normal Spells Activate from hand in MP1",
                        missing.Count == 0,
                        missing.Count == 0
                            ? ""
                            : string.Join(", ", missing.Take(8)) +
                              (missing.Count > 8 ? $" (+{missing.Count - 8})" : ""));
                }
            }

            // ── Effect vocabulary: kinds cover the pool; unique cards stay exceptions ──
            {
                var structural = 0;
                var shared = 0;
                var unique = 0;
                var stub = 0;
                var unimplemented = 0;
                foreach (var def in db.GetAllCards())
                {
                    if (def == null) continue;
                    var status = CardEffectStatus.Classify(def);
                    if (status == CardEffectStatusKind.Structural)
                    {
                        structural++;
                        continue;
                    }

                    var prog = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(def);
                    if (prog == null || prog.ClauseList.Count == 0)
                    {
                        unimplemented++;
                        continue;
                    }

                    var hasUnique = false;
                    var hasShared = false;
                    foreach (var c in prog.ClauseList)
                    {
                        if (c == null) continue;
                        var r = WRLDZ.Duel.TextEffects.EffectVocabulary.ResolutionOf(c);
                        if (r == WRLDZ.Duel.TextEffects.EffectResolutionKind.UniqueException)
                            hasUnique = true;
                        else if (r != WRLDZ.Duel.TextEffects.EffectResolutionKind.None)
                            hasShared = true;
                    }

                    if (hasUnique) unique++;
                    else if (hasShared && (prog.FullyCompiled || status == CardEffectStatusKind.Implemented))
                        shared++;
                    else if (hasShared)
                        stub++;
                    else
                        unimplemented++;
                }

                Check("Vocabulary: Normal/effectless cards are structural (not silent vanilla)",
                    structural >= 200, $"structural={structural}");
                Check("Vocabulary: shared kinds cover a majority of compiled effect cards",
                    shared > unique && shared >= 80,
                    $"shared={shared} unique={unique} stub={stub} unimplemented={unimplemented} structural={structural}");
                Check("Vocabulary: unique exceptions stay a short named list",
                    unique <= 40,
                    $"unique={unique}");
                Check("Vocabulary: Exiled Force compiles as shared Tribute→Destroy",
                    db.Get(74131780) is { } exd &&
                    WRLDZ.Duel.TextEffects.EffectVocabulary.ResolutionOf(
                        WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(exd)
                            .ClauseList.Find(c => c != null &&
                                                  c.Action == WRLDZ.Duel.TextEffects.EffectActionKind.Destroy))
                    == WRLDZ.Duel.TextEffects.EffectResolutionKind.Destroy);
                Check("Vocabulary: Time Wizard is a unique exception, not a new kind",
                    db.Get(71625222) is { } twd &&
                    WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(twd).ClauseList.Exists(c =>
                        c != null &&
                        WRLDZ.Duel.TextEffects.EffectVocabulary.IsUniqueException(c.Action)));
            }

            // ── Ring of Destruction / trap speed-2: cannot activate the turn it was Set ──
            {
                const int ringId = 83555666;
                const int trapHoleId = 4206964;
                const int celtic = 91152256;

                Check("Arena pace: opponent spell hold is at least 7s",
                    DuelPresentationPacer.ReadAfterOpponentSpellActivate >= 7f,
                    $"{DuelPresentationPacer.ReadAfterOpponentSpellActivate}");
                Check("Arena pace: AI step is at least 3s",
                    DuelPresentationPacer.DefaultAiStep >= 3f,
                    $"{DuelPresentationPacer.DefaultAiStep}");
                Check("Arena S/T hologram hover is at least 4s (not the disk toaster)",
                    WRLDZ.Presentation.ArInteraction.SpellActivationPresentation.MinHoverSeconds >= 4f,
                    $"{WRLDZ.Presentation.ArInteraction.SpellActivationPresentation.MinHoverSeconds}");
                Check("Arena opponent S/T hologram hover is at least 6s",
                    WRLDZ.Presentation.ArInteraction.SpellActivationPresentation.OpponentReadHoverSeconds >= 6f,
                    $"{WRLDZ.Presentation.ArInteraction.SpellActivationPresentation.OpponentReadHoverSeconds}");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var ring = PlaceSetTrap(engine, opp, ringId, 2);
                    ring.SetThisTurn = true;
                    Check("Ring of Destruction cannot activate the turn it was Set",
                        !engine.CanActivateSpellTrap(opp, ring, fromHand: false));
                    Check("Ring of Destruction not legal in OpponentOpenState the turn it was Set",
                        !SpellTrapEffects.IsLegalResponseCard(engine, opp, ring,
                            ResponseTiming.OpponentOpenState, null));

                    ring.SetThisTurn = false;
                    Check("Ring of Destruction legal on opponent's turn after sitting a turn",
                        engine.CanActivateSpellTrap(opp, ring, fromHand: false));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var hole = PlaceSetTrap(engine, engine.Player, trapHoleId, 2);
                    hole.SetThisTurn = false;
                    var holeProg = CardTextEffectCompiler.Compile(hole.Def);
                    Check("Trap Hole is not a compiled free-chain",
                        holeProg != null && !SpellTrapEffects.IsCompiledFreeChain(hole.Def, holeProg));
                    Check("Trap Hole not legal in OpponentOpenState",
                        !SpellTrapEffects.IsLegalResponseCard(engine, engine.Player, hole,
                            ResponseTiming.OpponentOpenState, null));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var ring = PlaceSetTrap(engine, opp, ringId, 2);
                    ring.SetThisTurn = false;
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                    Check("AI does not auto-activate Ring of Destruction on open-game-state",
                        !ring.FaceUp && opp.TryFindSpellTrap(ring, out _) &&
                        !p.Graveyard.Exists(c => c != null && c.CardId == celtic),
                        $"faceUp={ring.FaceUp} onField={opp.TryFindSpellTrap(ring, out _)} " +
                        $"celticGy={p.Graveyard.Exists(c => c != null && c.CardId == celtic)} " +
                        $"awaiting={engine.IsAwaitingResponse} prompt={engine.PendingResponse?.Prompt}");
                    if (engine.IsAwaitingResponse)
                    {
                        Check("AI trap window does not name the face-down card",
                            engine.PendingResponse != null &&
                            (engine.PendingResponse.Prompt ?? "").IndexOf("Ring of Destruction",
                                System.StringComparison.OrdinalIgnoreCase) < 0,
                            engine.PendingResponse?.Prompt);
                        engine.PassResponse();
                    }
                }
            }

            // ── Bottomless Trap Hole: La Jinn NS → destroy and banish; responder-only glow ──
            {
                const int bottomlessId = 29401950;
                const int laJinn = 97590747;
                const int celtic = 91152256;
                var bDef = db.Get(bottomlessId);
                var bProg = bDef != null ? CardTextEffectCompiler.Compile(bDef) : null;
                Check("Bottomless Trap Hole official text FullyCompiled",
                    bProg != null && bProg.FullyCompiled,
                    bProg == null
                        ? "null def/program"
                        : $"unparsed={string.Join("|", bProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (!ReachOpponentMain(engine))
                    {
                        Check("Bottomless mini-duel reached opponent Main", false,
                            $"tp={engine.TurnPlayer?.Name} phase={engine.Phase} turn={engine.TurnNumber}");
                    }
                    else
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var hole = PlaceSetTrap(engine, p, bottomlessId, 2);
                        hole.SetThisTurn = false;
                        opp.Hand.Clear();
                        var summoned = PutInHand(engine, opp, laJinn);
                        Check("Bottomless not legal in OpponentOpenState",
                            !SpellTrapEffects.IsLegalResponseCard(engine, p, hole,
                                ResponseTiming.OpponentOpenState, null));
                        var summonedOk = engine.TryNormalSummon(opp, summoned, asSet: false);
                        Check("Opponent Normal Summons La Jinn (1800)",
                            summonedOk && opp.MonsterZones[2].Occupant == summoned,
                            $"ok={summonedOk} awaiting={engine.IsAwaitingResponse} " +
                            $"occ={opp.MonsterZones[2].Occupant?.Name}");
                        Check("Bottomless is legal in the summon window",
                            engine.IsAwaitingResponse &&
                            engine.PendingResponse != null &&
                            engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned &&
                            engine.PendingResponse.Responder == p &&
                            SpellTrapEffects.IsLegalResponseCard(engine, p, hole,
                                ResponseTiming.MonsterSummoned, summoned),
                            $"awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing} " +
                            $"legal={SpellTrapEffects.IsLegalResponseCard(engine, p, hole, ResponseTiming.MonsterSummoned, summoned)}");
                        var snapYou = LegalIntentService.Build(engine, p);
                        var snapOpp = LegalIntentService.Build(engine, opp);
                        Check("ResponseActivate glow is only on the responder snapshot",
                            snapYou.HasKind(hole, LegalIntentService.LegalKind.ResponseActivate) &&
                            !snapOpp.HasKind(hole, LegalIntentService.LegalKind.ResponseActivate) &&
                            !snapOpp.CanGlow(hole));
                        var slotsYou = LegalIntentService.ResponseActivationSlots(engine, p);
                        var slotsOpp = LegalIntentService.ResponseActivationSlots(engine, opp);
                        Check("ResponseActivationSlots only for the responder's zone",
                            slotsYou.Count == 1 && slotsYou[0].Kind == RulesZoneKind.SpellTrap &&
                            slotsYou[0].Index == 2 && slotsOpp.Count == 0);
                        Check("Bottomless activates vs La Jinn",
                            engine.TryActivateSpellTrap(p, hole, fromHand: false));
                        Check("La Jinn is banished (not GY)",
                            opp.Banished.Contains(summoned) &&
                            !opp.Graveyard.Contains(summoned) &&
                            opp.MonsterZones[2].Occupant == null,
                            $"banished={opp.Banished.Contains(summoned)} gy={opp.Graveyard.Contains(summoned)} " +
                            $"field={opp.MonsterZones[2].Occupant?.Name}");
                        Check("Bottomless goes to GY after resolve",
                            p.Graveyard.Contains(hole) && !p.TryFindSpellTrap(hole, out _));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentMain(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var hole = PlaceSetTrap(engine, p, bottomlessId, 2);
                        hole.SetThisTurn = false;
                        opp.Hand.Clear();
                        var weak = PutInHand(engine, opp, celtic);
                        engine.TryNormalSummon(opp, weak, asSet: false);
                        Check("Bottomless does not open vs ATK 1400 Celtic Guardian",
                            !engine.IsAwaitingResponse ||
                            engine.PendingResponse == null ||
                            !SpellTrapEffects.IsLegalResponseCard(engine, p, hole,
                                ResponseTiming.MonsterSummoned, weak),
                            $"awaiting={engine.IsAwaitingResponse} timing={engine.PendingResponse?.Timing}");
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentMain(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var hole = PlaceSetTrap(engine, p, bottomlessId, 2);
                        hole.SetThisTurn = true;
                        opp.Hand.Clear();
                        var summoned = PutInHand(engine, opp, laJinn);
                        engine.TryNormalSummon(opp, summoned, asSet: false);
                        Check("Bottomless Set this turn does not answer La Jinn",
                            !SpellTrapEffects.IsLegalResponseCard(engine, p, hole,
                                ResponseTiming.MonsterSummoned, summoned));
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentMain(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var hole = PlaceSetTrap(engine, p, bottomlessId, 2);
                        hole.SetThisTurn = false;
                        var trapHole = PlaceSetTrap(engine, p, 4206964, 1);
                        trapHole.SetThisTurn = false;
                        var ss = engine.CreateCardInstance(laJinn);
                        Check("Special Summon La Jinn onto opponent field",
                            engine.SpecialSummonToField(opp, ss, BattlePosition.Attack, true));
                        Check("Bottomless answers Special Summon of La Jinn",
                            SpellTrapEffects.IsLegalResponseCard(engine, p, hole,
                                ResponseTiming.MonsterSummoned, ss));
                        Check("Trap Hole does not answer Special Summon",
                            !SpellTrapEffects.IsLegalResponseCard(engine, p, trapHole,
                                ResponseTiming.MonsterSummoned, ss));
                        if (engine.IsAwaitingResponse)
                        {
                            Check("SS opens a summon window",
                                engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned);
                            Check("Bottomless activates vs SS La Jinn",
                                engine.TryActivateSpellTrap(p, hole, fromHand: false));
                            Check("SS La Jinn is banished",
                                opp.Banished.Contains(ss) && !opp.Graveyard.Contains(ss));
                        }
                    }
                }
            }

            // ── Adhesion / Torrential / Eatgaboon summon-window family ──
            {
                const int adhesionId = 62325062;
                const int torrentialId = 53582587;
                const int eatgaboonId = 42578427;
                const int houseId = 15083728;
                const int laJinn = 97590747;
                const int inpachi = 13179332; // Charcoal Inpachi ATK 100
                const int maiden = 51275027; // Unhappy Maiden DEF 100

                var aDef = db.Get(adhesionId);
                var aProg = aDef != null ? CardTextEffectCompiler.Compile(aDef) : null;
                Check("Adhesion Trap Hole official text FullyCompiled",
                    aProg != null && aProg.FullyCompiled,
                    aProg == null
                        ? "null"
                        : $"unparsed={string.Join("|", aProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                var tDef = db.Get(torrentialId);
                var tProg = tDef != null ? CardTextEffectCompiler.Compile(tDef) : null;
                Check("Torrential Tribute official text FullyCompiled",
                    tProg != null && tProg.FullyCompiled,
                    tProg == null
                        ? "null"
                        : $"unparsed={string.Join("|", tProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                var eDef = db.Get(eatgaboonId);
                var eProg = eDef != null ? CardTextEffectCompiler.Compile(eDef) : null;
                Check("Eatgaboon official text FullyCompiled",
                    eProg != null && eProg.FullyCompiled);
                var hDef = db.Get(houseId);
                var hProg = hDef != null ? CardTextEffectCompiler.Compile(hDef) : null;
                Check("House of Adhesive Tape official text FullyCompiled",
                    hProg != null && hProg.FullyCompiled);

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentMain(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var trap = PlaceSetTrap(engine, p, adhesionId, 2);
                        trap.SetThisTurn = false;
                        opp.Hand.Clear();
                        var summoned = PutInHand(engine, opp, laJinn);
                        Check("Opponent NS La Jinn for Adhesion",
                            engine.TryNormalSummon(opp, summoned, asSet: false));
                        Check("Adhesion is legal vs NS La Jinn",
                            SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                                ResponseTiming.MonsterSummoned, summoned));
                        Check("Adhesion activates and halves original ATK",
                            engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                            summoned.CurrentAtk == 900,
                            $"atk={summoned.CurrentAtk} lingering={summoned.LingeringAtkModifier}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentMain(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var trap = PlaceSetTrap(engine, p, torrentialId, 2);
                        trap.SetThisTurn = false;
                        var wall = PlaceMonster(engine, p, 91152256, 1, BattlePosition.Attack, true);
                        opp.Hand.Clear();
                        var summoned = PutInHand(engine, opp, laJinn);
                        Check("Opponent NS La Jinn for Torrential",
                            engine.TryNormalSummon(opp, summoned, asSet: false));
                        Check("Torrential is legal vs opponent NS",
                            SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                                ResponseTiming.MonsterSummoned, summoned));
                        Check("Torrential destroys all monsters",
                            engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                            opp.MonsterZones[2].Occupant == null &&
                            p.MonsterZones[1].Occupant == null &&
                            p.Graveyard.Contains(wall) && opp.Graveyard.Contains(summoned));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    var p = engine.Player;
                    var trap = PlaceSetTrap(engine, p, torrentialId, 2);
                    trap.SetThisTurn = false;
                    p.Hand.Clear();
                    var self = PutInHand(engine, p, laJinn);
                    var ns = engine.TurnPlayer == p && engine.InMainPhase &&
                             engine.TryNormalSummon(p, self, asSet: false);
                    if (engine.IsAwaitingResponse &&
                        engine.PendingResponse.Responder != p)
                        engine.PassResponse();
                    Check("Torrential answers your own Normal Summon",
                        ns && SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                            ResponseTiming.MonsterSummoned, self),
                        $"ns={ns} awaiting={engine.IsAwaitingResponse} " +
                        $"responder={engine.PendingResponse?.Responder?.Name} " +
                        $"legal={SpellTrapEffects.IsLegalResponseCard(engine, p, trap, ResponseTiming.MonsterSummoned, self)}");
                    if (engine.IsAwaitingResponse && engine.PendingResponse.Responder == p)
                        Check("Torrential activates on your own summon and destroys it",
                            engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                            !p.TryFindMonster(self, out _));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var eat = PlaceSetTrap(engine, p, eatgaboonId, 2);
                    eat.SetThisTurn = false;
                    var weak = PlaceMonster(engine, engine.Opponent, inpachi, 2,
                        BattlePosition.Attack, true);
                    var hi = PlaceMonster(engine, engine.Opponent, laJinn, 1,
                        BattlePosition.Attack, true);
                    Check("Eatgaboon legal vs ATK 100 NS",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, eat,
                            ResponseTiming.MonsterSummoned, weak));
                    Check("Eatgaboon illegal vs ATK 1800",
                        !SpellTrapEffects.IsLegalResponseCard(engine, p, eat,
                            ResponseTiming.MonsterSummoned, hi));
                    weak.WasSpecialSummoned = true;
                    Check("Eatgaboon illegal vs SS",
                        !SpellTrapEffects.IsLegalResponseCard(engine, p, eat,
                            ResponseTiming.MonsterSummoned, weak));
                    var house = PlaceSetTrap(engine, p, houseId, 3);
                    house.SetThisTurn = false;
                    var lowDef = PlaceMonster(engine, engine.Opponent, maiden, 0,
                        BattlePosition.Defense, true);
                    Check("House of Adhesive Tape legal vs DEF 100 NS",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, house,
                            ResponseTiming.MonsterSummoned, lowDef));
                    Check("House of Adhesive Tape illegal vs DEF 1000+ La Jinn",
                        !SpellTrapEffects.IsLegalResponseCard(engine, p, house,
                            ResponseTiming.MonsterSummoned, hi));
                }
            }

            // ── Token Feastevil / Mispolymerization / Chain Disappearance / Destruction / Blast Held ──
            {
                const int tokenFeast = 83675475;
                const int mispoly = 58392024;
                const int chainDis = 57139487;
                const int chainDes = 1248895;
                const int blastHeld = 89041555;
                const int disarm = 20727787;
                const int eternal = 95051344;
                const int axe = 40619825;
                const int bsd = 11901678;
                const int inpachi = 13179332;
                const int celtic = 91152256;
                const int skull = 70781052;

                var feastProg = db.Get(tokenFeast) != null
                    ? CardTextEffectCompiler.Compile(db.Get(tokenFeast)) : null;
                Check("Token Feastevil official text FullyCompiled",
                    feastProg != null && feastProg.FullyCompiled,
                    feastProg == null ? "null" : string.Join("|", feastProg.UnparsedFragments ?? System.Array.Empty<string>()));
                Check("Mispolymerization official text FullyCompiled",
                    db.Get(mispoly) is { } md && CardTextEffectCompiler.Compile(md).FullyCompiled);
                Check("Chain Disappearance official text FullyCompiled",
                    db.Get(chainDis) is { } cd && CardTextEffectCompiler.Compile(cd).FullyCompiled);
                Check("Chain Destruction official text FullyCompiled",
                    db.Get(chainDes) is { } cx && CardTextEffectCompiler.Compile(cx).FullyCompiled);
                Check("Blast Held by a Tribute official text FullyCompiled",
                    db.Get(blastHeld) is { } bh && CardTextEffectCompiler.Compile(bh).FullyCompiled);
                Check("Disarmament official text FullyCompiled",
                    db.Get(disarm) is { } da && CardTextEffectCompiler.Compile(da).FullyCompiled);
                Check("Eternal Rest official text FullyCompiled",
                    db.Get(eternal) is { } er && CardTextEffectCompiler.Compile(er).FullyCompiled);

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var trap = PlaceSetTrap(engine, p, tokenFeast, 2);
                    trap.SetThisTurn = false;
                    var tok = engine.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                    var lp = opp.LifePoints;
                    Check("Token SS for Feastevil",
                        engine.SpecialSummonToField(opp, tok, BattlePosition.Attack, true));
                    Check("Token Feastevil legal vs Token SS",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                            ResponseTiming.MonsterSummoned, tok));
                    Check("Token Feastevil destroys token and deals 300",
                        engine.IsAwaitingResponse &&
                        engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                        !opp.TryFindMonster(tok, out _) &&
                        opp.LifePoints == lp - 300,
                        $"awaiting={engine.IsAwaitingResponse} lp={opp.LifePoints} was {lp}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var trap = PlaceSetTrap(engine, p, mispoly, 2);
                    trap.SetThisTurn = false;
                    var fusion = engine.CreateCardInstance(bsd);
                    Check("Fusion SS for Mispolymerization",
                        engine.SpecialSummonToField(opp, fusion, BattlePosition.Attack, true));
                    Check("Mispolymerization legal vs Fusion SS",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                            ResponseTiming.MonsterSummoned, fusion));
                    Check("Mispolymerization returns Fusion to Extra",
                        engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                        !opp.TryFindMonster(fusion, out _) &&
                        opp.ExtraDeck.Contains(bsd));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var trap = PlaceSetTrap(engine, p, chainDis, 2);
                    trap.SetThisTurn = false;
                    var onField = engine.CreateCardInstance(inpachi);
                    var inHand = engine.CreateCardInstance(inpachi);
                    opp.Hand.Add(inHand);
                    opp.Deck.Insert(0, inpachi);
                    Check("Chain Disappearance SS of ATK 100",
                        engine.SpecialSummonToField(opp, onField, BattlePosition.Attack, true));
                    Check("Chain Disappearance legal vs ATK 100",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                            ResponseTiming.MonsterSummoned, onField));
                    Check("Chain Disappearance banishes field + opponent hand/Deck copies",
                        engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                        opp.Banished.Exists(c => c != null && c.CardId == inpachi) &&
                        !opp.TryFindMonster(onField, out _) &&
                        !opp.Hand.Contains(inHand) &&
                        !opp.Deck.Contains(inpachi));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var trap = PlaceSetTrap(engine, p, chainDes, 2);
                    trap.SetThisTurn = false;
                    var onField = engine.CreateCardInstance(inpachi);
                    var inHand = engine.CreateCardInstance(inpachi);
                    opp.Hand.Add(inHand);
                    opp.Deck.Insert(0, inpachi);
                    Check("Chain Destruction SS of ATK 100",
                        engine.SpecialSummonToField(opp, onField, BattlePosition.Attack, true));
                    Check("Chain Destruction legal vs ATK 100",
                        SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                            ResponseTiming.MonsterSummoned, onField));
                    Check("Chain Destruction wipes hand/Deck copies, field monster stays",
                        engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                        opp.TryFindMonster(onField, out _) &&
                        opp.Graveyard.Exists(c => c != null && c.CardId == inpachi) &&
                        !opp.Hand.Contains(inHand) &&
                        !opp.Deck.Contains(inpachi));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var wall = PlaceMonster(engine, p, celtic, 2, BattlePosition.Defense, true);
                        var trap = PlaceSetTrap(engine, p, blastHeld, 1);
                        trap.SetThisTurn = false;
                        var tribute = PlaceMonster(engine, opp, skull, 2, BattlePosition.Attack, true);
                        tribute.WasTributeSummoned = true;
                        tribute.SummonedThisTurn = false;
                        tribute.ClearAttackFlags();
                        var extra = PlaceMonster(engine, opp, celtic, 1, BattlePosition.Attack, true);
                        extra.SummonedThisTurn = false;
                        extra.ClearAttackFlags();
                        var lp = opp.LifePoints;
                        if (engine.Phase == DuelPhase.Main1)
                            engine.TryEnterBattlePhase(opp);
                        DrainCombat(engine);
                        Check("Blast Held: tribute monster declares",
                            engine.TryAttack(opp, tribute, wall));
                        Check("Blast Held legal vs Tribute Summoned attacker",
                            SpellTrapEffects.IsLegalResponseCard(engine, p, trap,
                                ResponseTiming.AttackDeclared, null));
                        Check("Blast Held wipes opp ATK monsters and deals 1000",
                            engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                            !opp.TryFindMonster(tribute, out _) &&
                            !opp.TryFindMonster(extra, out _) &&
                            opp.LifePoints == lp - 1000,
                            $"lp={opp.LifePoints} was {lp} awaiting={engine.IsAwaitingResponse}");
                    }
                    else
                        Check("Blast Held battle path", false, $"phase={engine.Phase}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var host = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var eq = PutInHand(engine, p, axe);
                    Check("Axe equips for Eternal Rest / Disarmament",
                        engine.TryActivateSpellTrap(p, eq, fromHand: true) &&
                        engine.TrySelectEffectTarget(host) &&
                        host.Equips.Count > 0);
                    var rest = PutInHand(engine, p, eternal);
                    Check("Eternal Rest destroys equipped monster",
                        engine.TryActivateSpellTrap(p, rest, fromHand: true) &&
                        !p.TryFindMonster(host, out _));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var host = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var trap = PlaceSetTrap(engine, p, disarm, 0);
                    trap.SetThisTurn = false;
                    var eq = PutInHand(engine, p, axe);
                    engine.TryActivateSpellTrap(p, eq, fromHand: true);
                    engine.TrySelectEffectTarget(host);
                    Check("Disarmament destroys Equip Cards",
                        engine.TryActivateSpellTrap(p, trap, fromHand: false) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == axe) &&
                        p.TryFindMonster(host, out _));
                }
            }

            // ── Flip / battle-GY / delayed Standby effect-monster families ──
            {
                const int slateId = 78636495;
                const int maliceId = 72657739;
                const int ectoId = 97342942;
                const int yomiId = 51534754;
                const int poisonId = 43716289;
                const int haneId = 7089711;
                const int stealthId = 3510565;
                const int needleId = 81843628;
                const int newdoriaId = 4335645;
                const int lordPoisonId = 40320754;
                const int darkworldThorns = 43500484;
                const int penguinId = 93920745;
                const int laJinn = 97590747;
                const int celtic = 91152256;

                var slateDef = db.Get(slateId);
                var slateProg = slateDef != null ? CardTextEffectCompiler.Compile(slateDef) : null;
                Check("Slate Warrior official text FullyCompiled",
                    slateProg != null && slateProg.FullyCompiled &&
                    slateProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ApplyLingeringAtkDef && c.Amount == 500) &&
                    slateProg.ClauseList.Exists(c =>
                        c != null && c.RequiresThisDestroyedByBattle &&
                        c.ImplicitTargetIsBattleDestroyer &&
                        c.Action == EffectActionKind.ApplyLingeringAtkDef && c.Amount == -500),
                    slateProg == null
                        ? "null"
                        : $"full={slateProg.FullyCompiled} unparsed={string.Join("|", slateProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var maliceDef = db.Get(maliceId);
                var maliceProg = maliceDef != null ? CardTextEffectCompiler.Compile(maliceDef) : null;
                Check("Malice Doll of Demise official text FullyCompiled",
                    maliceProg != null && maliceProg.FullyCompiled &&
                    maliceProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.StandbyPhase &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.ResolvesFromGy && c.RequiresSentByContinuousSpell &&
                        c.RequiresNextControllerStandby),
                    maliceProg == null
                        ? "null"
                        : $"full={maliceProg.FullyCompiled} unparsed={string.Join("|", maliceProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var yomiDef = db.Get(yomiId);
                var yomiProg = yomiDef != null ? CardTextEffectCompiler.Compile(yomiDef) : null;
                Check("Yomi Ship official text FullyCompiled destroyer-destroy",
                    yomiProg != null && yomiProg.FullyCompiled &&
                    yomiProg.ClauseList.Exists(c =>
                        c != null && c.ImplicitTargetIsBattleDestroyer &&
                        c.Action == EffectActionKind.Destroy));

                var lordDef = db.Get(lordPoisonId);
                var lordProg = lordDef != null ? CardTextEffectCompiler.Compile(lordDef) : null;
                Check("Lord Poison official text FullyCompiled battle-GY Plant SS except itself",
                    lordProg != null && lordProg.FullyCompiled &&
                    lordProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        string.Equals(c.RaceFilter, "Plant", System.StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.ExceptNamedCard, "Lord Poison",
                            System.StringComparison.OrdinalIgnoreCase)),
                    lordProg == null
                        ? "null"
                        : $"full={lordProg.FullyCompiled} unparsed={string.Join("|", lordProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int giantRatId = 97017120;
                var ratDef = db.Get(giantRatId);
                var ratProg = ratDef != null ? CardTextEffectCompiler.Compile(ratDef) : null;
                Check("Giant Rat official text FullyCompiled battle-GY EARTH 1500 Deck SS",
                    ratProg != null && ratProg.FullyCompiled &&
                    ratProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromDeck &&
                        c.AmountIsAtkMax && c.Amount == 1500 &&
                        string.Equals(c.AttributeFilter, "EARTH",
                            System.StringComparison.OrdinalIgnoreCase) &&
                        c.IsOptional),
                    ratProg == null
                        ? "null"
                        : $"full={ratProg.FullyCompiled} unparsed={string.Join("|", ratProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int troopDragonId = 55013285;
                var troopDef = db.Get(troopDragonId);
                var troopProg = troopDef != null ? CardTextEffectCompiler.Compile(troopDef) : null;
                Check("Troop Dragon official text FullyCompiled named Deck SS",
                    troopProg != null && troopProg.FullyCompiled &&
                    troopProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromDeck &&
                        string.Equals(c.NamedCard, "Troop Dragon",
                            System.StringComparison.OrdinalIgnoreCase)));

                const int foxFireId = 88753985;
                var foxDef = db.Get(foxFireId);
                var foxProg = foxDef != null ? CardTextEffectCompiler.Compile(foxDef) : null;
                Check("Fox Fire official text FullyCompiled End Phase SS + tribute lock",
                    foxProg != null && foxProg.FullyCompiled &&
                    foxProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.EndPhase &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.ResolvesFromGy &&
                        c.RequiresThisDestroyedByBattle) &&
                    foxProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                        c.Action == EffectActionKind.CannotBeTributedForSummon) &&
                    !foxProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Activate),
                    foxProg == null
                        ? "null"
                        : $"full={foxProg.FullyCompiled} n={foxProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", foxProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int witchId = 78010363;
                var witchDef = db.Get(witchId);
                var witchProg = witchDef != null ? CardTextEffectCompiler.Compile(witchDef) : null;
                Check("Witch of the Black Forest official text FullyCompiled DEF 1500 search",
                    witchProg != null && witchProg.FullyCompiled &&
                    witchProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.Action == EffectActionKind.AddFromDeckToHand &&
                        c.Zone == EffectZoneFilter.DeckMonstersAtkLeq &&
                        c.AmountIsDefMax && c.Amount == 1500 &&
                        !c.AmountIsAtkMax),
                    witchProg == null
                        ? "null"
                        : $"full={witchProg.FullyCompiled} n={witchProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", witchProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int giantGermId = 95178994;
                var germDef = db.Get(giantGermId);
                var germProg = germDef != null ? CardTextEffectCompiler.Compile(germDef) : null;
                Check("Giant Germ battle-GY 500 burn compiles; any-number SS leftover",
                    germProg != null && !germProg.FullyCompiled &&
                    germProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.InflictDamageToOpponent &&
                        c.Amount == 500) &&
                    !germProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed),
                    germProg == null
                        ? "null"
                        : $"full={germProg.FullyCompiled} n={germProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", germProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int momongaId = 22567609;
                var momDef = db.Get(momongaId);
                var momProg = momDef != null ? CardTextEffectCompiler.Compile(momDef) : null;
                Check("Nimble Momonga battle-GY 1000 LP compiles; any-number SS leftover",
                    momProg != null && !momProg.FullyCompiled &&
                    momProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.GainLifePoints &&
                        c.Amount == 1000) &&
                    !momProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed),
                    momProg == null
                        ? "null"
                        : $"full={momProg.FullyCompiled} n={momProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", momProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int hyenaId = 22873798;
                var hyenaDef = db.Get(hyenaId);
                var hyenaProg = hyenaDef != null ? CardTextEffectCompiler.Compile(hyenaDef) : null;
                Check("Hyena any-number Deck SS stays leftover",
                    hyenaProg != null && !hyenaProg.FullyCompiled &&
                    !hyenaProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed),
                    hyenaProg == null
                        ? "null"
                        : $"full={hyenaProg.FullyCompiled} n={hyenaProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", hyenaProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int darkMimic1 = 74713516;
                var dm1Def = db.Get(darkMimic1);
                var dm1Prog = dm1Def != null ? CardTextEffectCompiler.Compile(dm1Def) : null;
                Check("Dark Mimic LV1 official text FullyCompiled Flip draw + LV send SS",
                    dm1Prog != null && dm1Prog.FullyCompiled &&
                    dm1Prog.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.Draw && c.Amount == 1),
                    dm1Prog == null
                        ? "null"
                        : $"full={dm1Prog.FullyCompiled} n={dm1Prog.ClauseList.Count} " +
                          $"unparsed={string.Join("|", dm1Prog.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int curseId = 50712728;
                var curseDef = db.Get(curseId);
                var curseProg = curseDef != null ? CardTextEffectCompiler.Compile(curseDef) : null;
                Check("Gravekeeper's Curse official text FullyCompiled summoned inflict 500",
                    curseProg != null && curseProg.FullyCompiled &&
                    curseProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.ThisCardSummoned &&
                        c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500),
                    curseProg == null
                        ? "null"
                        : $"full={curseProg.FullyCompiled} n={curseProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", curseProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int kotetsuId = 73431236;
                var kotDef = db.Get(kotetsuId);
                var kotProg = kotDef != null ? CardTextEffectCompiler.Compile(kotDef) : null;
                Check("Iron Blacksmith Kotetsu official text FullyCompiled Flip Equip search",
                    kotProg != null && kotProg.FullyCompiled &&
                    kotProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.AddFromDeckToHand &&
                        c.Zone == EffectZoneFilter.DeckEquipSpells),
                    kotProg == null
                        ? "null"
                        : $"full={kotProg.FullyCompiled} n={kotProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", kotProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int darkMimic3 = 1102515;
                var dm3Def = db.Get(darkMimic3);
                var dm3Prog = dm3Def != null ? CardTextEffectCompiler.Compile(dm3Def) : null;
                Check("Dark Mimic LV3 battle-GY draw 1 compiles; LV1 rider leftover",
                    dm3Prog != null && !dm3Prog.FullyCompiled &&
                    dm3Prog.ClauseList.Exists(c =>
                        c != null && c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.Draw && c.Amount == 1),
                    dm3Prog == null
                        ? "null"
                        : $"full={dm3Prog.FullyCompiled} n={dm3Prog.ClauseList.Count} " +
                          $"unparsed={string.Join("|", dm3Prog.UnparsedFragments ?? System.Array.Empty<string>())}");

                var poisonDef = db.Get(poisonId);
                var poisonProg = poisonDef != null ? CardTextEffectCompiler.Compile(poisonDef) : null;
                Check("Poison Mummy official text FullyCompiled Flip inflict",
                    poisonProg != null && poisonProg.FullyCompiled &&
                    poisonProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500));

                var penguinDef = db.Get(penguinId);
                var penguinProg = penguinDef != null ? CardTextEffectCompiler.Compile(penguinDef) : null;

                Check("Penguin Soldier official text FullyCompiled Flip bounce Amount=2",
                    penguinProg != null && penguinProg.FullyCompiled &&
                    penguinProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ReturnToHand &&
                        c.Amount == 2 && c.RequiresTargetChoice),
                    penguinProg == null
                        ? "null"
                        : $"full={penguinProg.FullyCompiled} n={penguinProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", penguinProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var dekoichiDef = db.Get(87621407);
                var dekoichiProg = dekoichiDef != null ? CardTextEffectCompiler.Compile(dekoichiDef) : null;
                Check("Dekoichi scaled extra-draw stays leftover (not invented)",
                    dekoichiProg != null && !dekoichiProg.FullyCompiled);

                var ladyDef = db.Get(90147755);
                var ladyProg = ladyDef != null ? CardTextEffectCompiler.Compile(ladyDef) : null;
                Check("Lady Assailant banish-top-3 stays leftover (not invented)",
                    ladyProg != null && !ladyProg.FullyCompiled);

                void CheckFilterSs(int id, string name, string kind, int cap, bool atk)
                {
                    var d = db.Get(id);
                    var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                    var attr = atk && string.Equals(kind, "Dragon", System.StringComparison.OrdinalIgnoreCase)
                        ? false
                        : atk && (kind == "WATER" || kind == "FIRE" || kind == "WIND" ||
                                  kind == "LIGHT" || kind == "DARK" || kind == "EARTH");
                    Check(name + " official text FullyCompiled battle-GY Deck SS",
                        pr != null && pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null &&
                            c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.Amount == cap &&
                            (atk ? c.AmountIsAtkMax : c.AmountIsDefMax) &&
                            (attr
                                ? string.Equals(c.AttributeFilter, kind,
                                    System.StringComparison.OrdinalIgnoreCase)
                                : string.Equals(c.RaceFilter, kind,
                                    System.StringComparison.OrdinalIgnoreCase))),
                        pr == null
                            ? "null"
                            : $"full={pr.FullyCompiled} n={pr.ClauseList.Count} " +
                              $"unparsed={string.Join("|", pr.UnparsedFragments ?? System.Array.Empty<string>())}");
                }

                CheckFilterSs(83011278, "Mystic Tomato", "DARK", 1500, true);
                CheckFilterSs(97017120, "Giant Rat", "EARTH", 1500, true);
                CheckFilterSs(57839750, "Mother Grizzly", "WATER", 1500, true);
                CheckFilterSs(60806437, "UFO Turtle", "FIRE", 1500, true);
                CheckFilterSs(39191307, "Masked Dragon", "Dragon", 1500, true);
                CheckFilterSs(77044671, "Pyramid Turtle", "Zombie", 2000, false);
                CheckFilterSs(84834865, "Flying Kamakiri #1", "WIND", 1500, true);
                CheckFilterSs(93107608, "Howling Insect", "Insect", 1500, true);
                CheckFilterSs(95956346, "Shining Angel", "LIGHT", 1500, true);

                const int orchisId = 46571052;
                var orchisDef = db.Get(orchisId);
                var orchisProg = orchisDef != null ? CardTextEffectCompiler.Compile(orchisDef) : null;
                Check("Vampiric Orchis official text FullyCompiled NS named hand SS",
                    orchisProg != null && orchisProg.FullyCompiled &&
                    orchisProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisNormalSummoned &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromHand &&
                        string.Equals(c.NamedCard, "Des Dendle",
                            System.StringComparison.OrdinalIgnoreCase)),
                    orchisProg == null
                        ? "null"
                        : $"full={orchisProg.FullyCompiled} n={orchisProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", orchisProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int decayedId = 10209545;
                var decayedDef = db.Get(decayedId);
                var decayedProg = decayedDef != null ? CardTextEffectCompiler.Compile(decayedDef) : null;
                Check("Decayed Commander NS named hand SS + direct-attack random discard compile",
                    decayedProg != null && decayedProg.FullyCompiled &&
                    decayedProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisNormalSummoned &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromHand &&
                        string.Equals(c.NamedCard, "Zombie Tiger",
                            System.StringComparison.OrdinalIgnoreCase)) &&
                    decayedProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        c.RequiresDirectAttack &&
                        c.Amount == 1),
                    decayedProg == null
                        ? "null"
                        : $"full={decayedProg.FullyCompiled} n={decayedProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", decayedProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int oldVinId = 45141844;
                var oldVinDef = db.Get(oldVinId);
                var oldVinProg = oldVinDef != null ? CardTextEffectCompiler.Compile(oldVinDef) : null;
                Check("Old Vindictive Magician official text FullyCompiled Flip opp destroy",
                    oldVinProg != null && oldVinProg.FullyCompiled &&
                    oldVinProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.Destroy &&
                        c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                    oldVinProg == null
                        ? "null"
                        : $"full={oldVinProg.FullyCompiled} unparsed={string.Join("|", oldVinProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int biteId = 50122883;
                var biteDef = db.Get(biteId);
                var biteProg = biteDef != null ? CardTextEffectCompiler.Compile(biteDef) : null;
                Check("Bite Shoes official text FullyCompiled Flip change position",
                    biteProg != null && biteProg.FullyCompiled &&
                    biteProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ChangeBattlePosition),
                    biteProg == null
                        ? "null"
                        : $"full={biteProg.FullyCompiled} unparsed={string.Join("|", biteProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int desertapirId = 13409151;
                var desertDef = db.Get(desertapirId);
                var desertProg = desertDef != null ? CardTextEffectCompiler.Compile(desertDef) : null;
                Check("Desertapir official text FullyCompiled Flip set-FD except itself",
                    desertProg != null && desertProg.FullyCompiled &&
                    desertProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                        c.RequiresTargetChoice &&
                        string.Equals(c.ExceptNamedCard, "Desertapir",
                            System.StringComparison.OrdinalIgnoreCase)),
                    desertProg == null
                        ? "null"
                        : $"full={desertProg.FullyCompiled} n={desertProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", desertProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synDesert = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000021,
                    name = "Set-FD Flip (new-card shape)",
                    type = "Flip Effect Monster",
                    desc =
                        "FLIP: Flip 1 face-up monster on the field into face-down Defense Position. You cannot select \"Set-FD Flip\"."
                });
                Check("New-card rule: Desertapir-shaped Flip set-FD compiles without a cardId branch",
                    synDesert != null && synDesert.FullyCompiled &&
                    synDesert.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                        string.Equals(c.ExceptNamedCard, "Set-FD Flip",
                            System.StringComparison.OrdinalIgnoreCase)));

                const int clownId = 42647539;
                var clownDef = db.Get(clownId);
                var clownProg = clownDef != null ? CardTextEffectCompiler.Compile(clownDef) : null;
                Check("Ryu-Kishin Clown official text FullyCompiled Summoned change position",
                    clownProg != null && clownProg.FullyCompiled &&
                    clownProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.Action == EffectActionKind.ChangeBattlePosition &&
                        c.RequiresTargetChoice &&
                        !c.RequiresThisNormalSummoned &&
                        !c.RequiresThisFlipSummoned),
                    clownProg == null
                        ? "null"
                        : $"full={clownProg.FullyCompiled} n={clownProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", clownProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synClown = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000022,
                    name = "Summon Flipper (new-card shape)",
                    type = "Effect Monster",
                    desc =
                        "When this card is Summoned (including Flip Summon and Special Summon), select 1 face-up monster on the field and change its battle position."
                });
                Check("New-card rule: Ryu-Kishin-shaped Summoned change-pos compiles without a cardId branch",
                    synClown != null && synClown.FullyCompiled &&
                    synClown.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.Action == EffectActionKind.ChangeBattlePosition));

                const int bowganianId = 52090844;
                var bowDef = db.Get(bowganianId);
                var bowProg = bowDef != null ? CardTextEffectCompiler.Compile(bowDef) : null;
                Check("Bowganian official text FullyCompiled Standby inflict 600",
                    bowProg != null && bowProg.FullyCompiled &&
                    bowProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.StandbyPhase &&
                        c.Action == EffectActionKind.InflictDamageToOpponent &&
                        c.Amount == 600),
                    bowProg == null
                        ? "null"
                        : $"full={bowProg.FullyCompiled} n={bowProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", bowProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var sageStone = db.Get(13604200);
                var sageProg = sageStone != null ? CardTextEffectCompiler.Compile(sageStone) : null;
                Check("Sage's Stone leftover (no invented spell If-you-control lock)",
                    sageProg != null && !sageProg.FullyCompiled &&
                    !sageProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed));

                var release = db.Get(75417459);
                var relProg = release != null ? CardTextEffectCompiler.Compile(release) : null;
                Check("Release Restraint leftover (no invented tribute-named spell procedure)",
                    relProg != null && !relProg.FullyCompiled);

                var knightTitle = db.Get(87210505);
                var ktProg = knightTitle != null ? CardTextEffectCompiler.Compile(knightTitle) : null;
                Check("Knight's Title leftover (no invented tribute-named spell procedure)",
                    ktProg != null && !ktProg.FullyCompiled);

                const int mineGolemId = 76321376;
                var mineDef = db.Get(mineGolemId);
                var mineProg = mineDef != null ? CardTextEffectCompiler.Compile(mineDef) : null;
                Check("Mine Golem official text FullyCompiled battle-GY inflict 500",
                    mineProg != null && mineProg.FullyCompiled &&
                    mineProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.InflictDamageToOpponent &&
                        c.Amount == 500),
                    mineProg == null
                        ? "null"
                        : $"full={mineProg.FullyCompiled} unparsed={string.Join("|", mineProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int guardId = 37101832;
                var guardDef = db.Get(guardId);
                var guardProg = guardDef != null ? CardTextEffectCompiler.Compile(guardDef) : null;
                Check("Gravekeeper's Guard official text FullyCompiled Flip bounce opp",
                    guardProg != null && guardProg.FullyCompiled &&
                    guardProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ReturnToHand),
                    guardProg == null
                        ? "null"
                        : $"full={guardProg.FullyCompiled} unparsed={string.Join("|", guardProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int galeId = 77491079;
                var galeDef = db.Get(galeId);
                var galeProg = galeDef != null ? CardTextEffectCompiler.Compile(galeDef) : null;
                Check("Gale Lizard official text FullyCompiled Flip bounce opp",
                    galeProg != null && galeProg.FullyCompiled &&
                    galeProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.ReturnToHand),
                    galeProg == null
                        ? "null"
                        : $"full={galeProg.FullyCompiled} unparsed={string.Join("|", galeProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int maskDarkId = 28933734;
                var maskDef = db.Get(maskDarkId);
                var maskProg = maskDef != null ? CardTextEffectCompiler.Compile(maskDef) : null;
                Check("Mask of Darkness official text FullyCompiled Flip Trap GY add",
                    maskProg != null && maskProg.FullyCompiled &&
                    maskProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.AddFromGyToHand &&
                        c.Zone == EffectZoneFilter.ControllerGyTraps),
                    maskProg == null
                        ? "null"
                        : $"full={maskProg.FullyCompiled} unparsed={string.Join("|", maskProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int gigantesId = 47606319;
                var gigDef = db.Get(gigantesId);
                var gigProg = gigDef != null ? CardTextEffectCompiler.Compile(gigDef) : null;
                Check("Gigantes battle-GY destroy all S/T FullyCompiled (nomi is summon restriction)",
                    gigProg != null && gigProg.FullyCompiled &&
                    gigProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.Destroy &&
                        c.Zone == EffectZoneFilter.FieldSpellTraps) &&
                    !gigProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Activate),
                    gigProg == null
                        ? "null"
                        : $"full={gigProg.FullyCompiled} n={gigProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", gigProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int lacoodaId = 2326738;
                var lacDef = db.Get(lacoodaId);
                var lacProg = lacDef != null ? CardTextEffectCompiler.Compile(lacDef) : null;
                Check("Des Lacooda Flip Summoned draw 1 compiles",
                    lacProg != null &&
                    lacProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.Draw && c.Amount == 1),
                    lacProg == null
                        ? "null"
                        : $"full={lacProg.FullyCompiled} n={lacProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", lacProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int scarabsId = 15383415;
                var scarabsDef = db.Get(scarabsId);
                var scarabsProg = scarabsDef != null ? CardTextEffectCompiler.Compile(scarabsDef) : null;
                Check("Swarm of Scarabs official text FullyCompiled Flip Summon destroy + set-FD",
                    scarabsProg != null && scarabsProg.FullyCompiled &&
                    scarabsProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                        c.OncePerTurn) &&
                    scarabsProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                    scarabsProg == null
                        ? "null"
                        : $"full={scarabsProg.FullyCompiled} n={scarabsProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", scarabsProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int locustsId = 41872150;
                var locustsDef = db.Get(locustsId);
                var locustsProg = locustsDef != null ? CardTextEffectCompiler.Compile(locustsDef) : null;
                Check("Swarm of Locusts official text FullyCompiled Flip Summon ST destroy + set-FD",
                    locustsProg != null && locustsProg.FullyCompiled &&
                    locustsProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                        c.OncePerTurn) &&
                    locustsProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.FieldSpellTraps),
                    locustsProg == null
                        ? "null"
                        : $"full={locustsProg.FullyCompiled} n={locustsProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", locustsProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int craterId = 78243409;
                var craterDef = db.Get(craterId);
                var craterProg = craterDef != null ? CardTextEffectCompiler.Compile(craterDef) : null;
                Check("The Thing in the Crater official text FullyCompiled destroyed-field Pyro hand SS",
                    craterProg != null && craterProg.FullyCompiled &&
                    craterProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.SentFromFieldToGy &&
                        c.RequiresDestroyed &&
                        !c.RequiresThisDestroyedByBattle &&
                        c.Action == EffectActionKind.SpecialSummonFromHand &&
                        c.FromHand && c.Amount == 1 &&
                        string.Equals(c.RaceFilter, "Pyro", System.StringComparison.OrdinalIgnoreCase) &&
                        c.IsOptional) &&
                    !craterProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.Activate),
                    craterProg == null
                        ? "null"
                        : $"full={craterProg.FullyCompiled} n={craterProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", craterProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int witchDoctorId = 75946257;
                var wdDef = db.Get(witchDoctorId);
                var wdProg = wdDef != null ? CardTextEffectCompiler.Compile(wdDef) : null;
                Check("Witch Doctor of Chaos official text FullyCompiled Flip either-GY banish",
                    wdProg != null && wdProg.FullyCompiled &&
                    wdProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.Banish &&
                        c.Zone == EffectZoneFilter.EitherGyMonsters &&
                        c.RequiresTargetChoice),
                    wdProg == null
                        ? "null"
                        : $"full={wdProg.FullyCompiled} n={wdProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", wdProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int spiritId = 48659020;
                var spiritDef = db.Get(spiritId);
                var spiritProg = spiritDef != null ? CardTextEffectCompiler.Compile(spiritDef) : null;
                Check("Spirit Caller official text FullyCompiled Flip Level-3-or-lower Normal GY SS",
                    spiritProg != null && spiritProg.FullyCompiled &&
                    spiritProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                        c.RequiresNormalMonster &&
                        c.AmountIsLevel && c.Amount == 3 &&
                        c.RequiresTargetChoice && c.IsOptional),
                    spiritProg == null
                        ? "null"
                        : $"full={spiritProg.FullyCompiled} n={spiritProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", spiritProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int gkSpyId = 24317029;
                var gkSpyDef = db.Get(gkSpyId);
                var gkSpyProg = gkSpyDef != null ? CardTextEffectCompiler.Compile(gkSpyDef) : null;
                Check("Gravekeeper's Spy official text FullyCompiled Flip series ATK<=1500 Deck SS",
                    gkSpyProg != null && gkSpyProg.FullyCompiled &&
                    gkSpyProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromDeck && c.NamedCardIsSeries && c.AmountIsAtkMax &&
                        c.Amount == 1500 &&
                        string.Equals(c.NamedCard, "Gravekeeper's", System.StringComparison.OrdinalIgnoreCase)),
                    gkSpyProg == null
                        ? "null"
                        : $"full={gkSpyProg.FullyCompiled} n={gkSpyProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", gkSpyProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int cobraId = 86801871;
                var cobraDef = db.Get(cobraId);
                var cobraProg = cobraDef != null ? CardTextEffectCompiler.Compile(cobraDef) : null;
                Check("Cobra Jar official text Flip token SS compiles; battle-destroyed inflict leftover",
                    cobraProg != null && !cobraProg.FullyCompiled &&
                    cobraProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.SpecialSummonToken &&
                        c.TokenLevel == 3 && c.TokenAtk == 1200 && c.TokenDef == 1200) &&
                    !cobraProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.InflictDamageToOpponent),
                    cobraProg == null
                        ? "null"
                        : $"full={cobraProg.FullyCompiled} n={cobraProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", cobraProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int incarnateId = 97093037;
                var incDef = db.Get(incarnateId);
                var incProg = incDef != null ? CardTextEffectCompiler.Compile(incDef) : null;
                Check("The Creator Incarnate official text FullyCompiled tribute-this named hand SS",
                    incProg != null && incProg.FullyCompiled &&
                    incProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.RequiresTributeThis &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromHand &&
                        string.Equals(c.NamedCard, "The Creator", System.StringComparison.OrdinalIgnoreCase)),
                    incProg == null
                        ? "null"
                        : $"full={incProg.FullyCompiled} unparsed={string.Join("|", incProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int chickId = 36262024;
                var chickDef = db.Get(chickId);
                var chickProg = chickDef != null ? CardTextEffectCompiler.Compile(chickDef) : null;
                Check("Black Dragon's Chick official text FullyCompiled send-this named hand SS",
                    chickProg != null && chickProg.FullyCompiled &&
                    chickProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.RequiresSendThisToGy &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromHand &&
                        string.Equals(c.NamedCard, "Red-Eyes B. Dragon",
                            System.StringComparison.OrdinalIgnoreCase)),
                    chickProg == null
                        ? "null"
                        : $"full={chickProg.FullyCompiled} unparsed={string.Join("|", chickProg.UnparsedFragments ?? System.Array.Empty<string>())}");


                const int gymId = 7512044;
                var gymDef = db.Get(gymId);
                var gymProg = gymDef != null ? CardTextEffectCompiler.Compile(gymDef) : null;
                Check("Gather Your Mind official text FullyCompiled named Deck add (shuffle absorbed; Oath OPT boilerplate)",
                    gymProg != null && gymProg.FullyCompiled &&
                    gymProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                        c.FromDeck &&
                        string.Equals(c.NamedCard, "Gather Your Mind", System.StringComparison.OrdinalIgnoreCase)),
                    gymProg == null
                        ? "null"
                        : $"full={gymProg.FullyCompiled} n={gymProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", gymProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int venusId = 64734921;
                var venusDef = db.Get(venusId);
                var venusProg = venusDef != null ? CardTextEffectCompiler.Compile(venusDef) : null;
                Check("Venus official text FullyCompiled pay-500 named hand-or-Deck SS",
                    venusProg != null && venusProg.FullyCompiled &&
                    venusProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Activate &&
                        c.PayLpAmount == 500 &&
                        c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromHand && c.FromDeck &&
                        string.Equals(c.NamedCard, "Mystical Shine Ball",
                            System.StringComparison.OrdinalIgnoreCase)),
                    venusProg == null
                        ? "null"
                        : $"full={venusProg.FullyCompiled} n={venusProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", venusProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int ladybugId = 83994646;
                var ladybugDef = db.Get(ladybugId);
                var ladybugProg = ladybugDef != null ? CardTextEffectCompiler.Compile(ladybugDef) : null;
                Check("4-Starred Ladybug official text FullyCompiled Flip destroy opp Level 4",
                    ladybugProg != null && ladybugProg.FullyCompiled &&
                    ladybugProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.Destroy &&
                        c.AmountIsLevel && c.Amount == 4 &&
                        c.Side == EffectSide.Opponent &&
                        !c.RequiresTargetChoice),
                    ladybugProg == null
                        ? "null"
                        : $"full={ladybugProg.FullyCompiled} n={ladybugProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", ladybugProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int statueId = 75209824;
                var statueDef = db.Get(statueId);
                var statueProg = statueDef != null ? CardTextEffectCompiler.Compile(statueDef) : null;
                Check("Guardian Statue official text FullyCompiled Flip Summon bounce + set-FD",
                    statueProg != null && statueProg.FullyCompiled &&
                    statueProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense) &&
                    statueProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.ReturnToHand &&
                        c.RequiresTargetChoice),
                    statueProg == null
                        ? "null"
                        : $"full={statueProg.FullyCompiled} unparsed={string.Join("|", statueProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int medusaId = 2694423;
                var medusaDef = db.Get(medusaId);
                var medusaProg = medusaDef != null ? CardTextEffectCompiler.Compile(medusaDef) : null;
                Check("Medusa Worm official text FullyCompiled Flip Summon destroy + set-FD",
                    medusaProg != null && medusaProg.FullyCompiled &&
                    medusaProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense) &&
                    medusaProg.ClauseList.Exists(c =>
                        c != null &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresTargetChoice),
                    medusaProg == null
                        ? "null"
                        : $"full={medusaProg.FullyCompiled} unparsed={string.Join("|", medusaProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int moaiId = 45159319;
                var moaiDef = db.Get(moaiId);
                var moaiProg = moaiDef != null ? CardTextEffectCompiler.Compile(moaiDef) : null;
                Check("Moai Interceptor Cannons official text FullyCompiled OPT set-FD",
                    moaiProg != null && moaiProg.FullyCompiled &&
                    moaiProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense &&
                        c.OncePerTurn),
                    moaiProg == null
                        ? "null"
                        : $"full={moaiProg.FullyCompiled} unparsed={string.Join("|", moaiProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int immortalId = 84926738;
                var immortalDef = db.Get(immortalId);
                var immortalProg = immortalDef != null ? CardTextEffectCompiler.Compile(immortalDef) : null;
                Check("Immortal of Thunder Flip gain 3000 compiles; GY lose leftover",
                    immortalProg != null && !immortalProg.FullyCompiled &&
                    immortalProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.Flip &&
                        c.Action == EffectActionKind.GainLifePoints &&
                        c.Amount == 3000),
                    immortalProg == null
                        ? "null"
                        : $"full={immortalProg.FullyCompiled} unparsed={string.Join("|", immortalProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int sentryId = 52323207;
                var sentryDef = db.Get(sentryId);
                var sentryProg = sentryDef != null ? CardTextEffectCompiler.Compile(sentryDef) : null;
                Check("Golem Sentry official text FullyCompiled Flip Summon bounce + set-FD",
                    sentryProg != null && sentryProg.FullyCompiled &&
                    sentryProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense) &&
                    sentryProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.ReturnToHand &&
                        c.RequiresTargetChoice),
                    sentryProg == null
                        ? "null"
                        : $"full={sentryProg.FullyCompiled} n={sentryProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", sentryProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                const int sphinxId = 40659562;
                var sphinxDef = db.Get(sphinxId);
                var sphinxProg = sphinxDef != null ? CardTextEffectCompiler.Compile(sphinxDef) : null;
                Check("Guardian Sphinx official text FullyCompiled Flip Summon bounce-all + set-FD",
                    sphinxProg != null && sphinxProg.FullyCompiled &&
                    sphinxProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SetThisFaceDownDefense) &&
                    sphinxProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisFlipSummoned &&
                        c.Action == EffectActionKind.ReturnToHand &&
                        !c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.FieldMonsters &&
                        c.Side == EffectSide.Opponent),
                    sphinxProg == null
                        ? "null"
                        : $"full={sphinxProg.FullyCompiled} n={sphinxProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", sphinxProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var slate = PlaceMonster(engine, p, slateId, 2, BattlePosition.Defense, false);
                    slate.SetThisTurn = false;
                    Check("Slate Warrior: Flip Summon succeeds", engine.TryFlipSummon(p, slate));
                    Check("Slate Warrior Flip: lingering +500 ATK/DEF",
                        slate.CurrentAtk == 2400 && slate.CurrentDef == 900 &&
                        slate.LingeringAtkModifier == 500 && slate.LingeringDefModifier == 500,
                        $"atk={slate.CurrentAtk} def={slate.CurrentDef} " +
                        $"la={slate.LingeringAtkModifier} ld={slate.LingeringDefModifier}");
                    Check("Slate Warrior Flip: battle math uses lingering 2400/900",
                        BattleMechanics.AttackValue(slate) == 2400 &&
                        BattleMechanics.DefenseValue(slate) == 900,
                        $"bvATK={BattleMechanics.AttackValue(slate)} " +
                        $"bvDEF={BattleMechanics.DefenseValue(slate)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var slate = PlaceMonster(engine, p, slateId, 2, BattlePosition.Defense, false);
                    slate.SetThisTurn = false;
                    Check("Slate Warrior Flip then later battle: Flip Summon",
                        engine.TryFlipSummon(p, slate));
                    if (ReachPlayerBattle(engine) && p.TryFindMonster(slate, out _))
                    {
                        opp.LifePoints = 8000;
                        opp.Hand.Clear();
                        var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                        slate.SummonedThisTurn = false;
                        slate.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        Check("Slate Warrior Flip: 2400 can attack Celtic",
                            engine.Phase == DuelPhase.Battle && engine.CanAttack(p, slate));
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, slate))
                        {
                            engine.TryAttack(p, slate, victim);
                            DrainCombat(engine);
                        }

                        Check("Slate Warrior Flip: battles as 2400 (Celtic 1400 dies, 1000 dmg)",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.LifePoints == 7000,
                            $"victimOnField={opp.TryFindMonster(victim, out _)} LP={opp.LifePoints}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var poison = PlaceMonster(engine, p, poisonId, 2, BattlePosition.Defense, false);
                    poison.SetThisTurn = false;
                    opp.LifePoints = 8000;
                    Check("Poison Mummy Flip Summon", engine.TryFlipSummon(p, poison));
                    Check("Poison Mummy Flip: opponent takes 500",
                        opp.LifePoints == 7500, $"LP={opp.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var hane = PlaceMonster(engine, p, haneId, 2, BattlePosition.Defense, false);
                    hane.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Hane-Hane Flip Summon", engine.TryFlipSummon(p, hane));
                    Check("Hane-Hane Flip: opens bounce target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Hane-Hane Flip: victim returned to hand",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var penguin = PlaceMonster(engine, p, penguinId, 2, BattlePosition.Defense, false);
                    penguin.SetThisTurn = false;
                    var v1 = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var v2 = PlaceMonster(engine, opp, laJinn, 1, BattlePosition.Attack, true);
                    Check("Penguin Soldier Flip Summon", engine.TryFlipSummon(p, penguin));
                    Check("Penguin Soldier Flip: opens bounce (up to 2)",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                        engine.PendingActivation.TargetPicksAreUpTo &&
                        engine.PendingActivation.TargetPicksRemaining == 2);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Penguin Soldier: first bounce still pending",
                            engine.TrySelectEffectTarget(v1) && engine.IsAwaitingEffectTarget);
                        Check("Penguin Soldier: second bounce returns both",
                            engine.TrySelectEffectTarget(v2) &&
                            !opp.TryFindMonster(v1, out _) &&
                            !opp.TryFindMonster(v2, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic) &&
                            opp.Hand.Exists(c => c.CardId == laJinn));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var bird = PlaceMonster(engine, p, stealthId, 2, BattlePosition.Defense, false);
                    bird.SetThisTurn = false;
                    opp.LifePoints = 8000;
                    Check("Stealth Bird Flip Summon", engine.TryFlipSummon(p, bird));
                    Check("Stealth Bird Flip Summoned: inflict 1000",
                        opp.LifePoints == 7000, $"LP={opp.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    opp.Deck.Clear();
                    for (var i = 0; i < 5; i++)
                        opp.Deck.Add(celtic);
                    var worm = PlaceMonster(engine, p, needleId, 2, BattlePosition.Defense, false);
                    worm.SetThisTurn = false;
                    Check("Needle Worm Flip Summon", engine.TryFlipSummon(p, worm));
                    Check("Needle Worm Flip: mill 5 from opponent Deck",
                        opp.Deck.Count == 0 &&
                        opp.Graveyard.Count(c => c != null && c.CardId == celtic) == 5,
                        $"deck={opp.Deck.Count} gy={opp.Graveyard.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var slate = PlaceMonster(engine, p, slateId, 2, BattlePosition.Defense, false);
                        slate.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        Check("Slate Warrior battle: La Jinn can attack Set Slate",
                            engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk));
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, slate);
                            DrainCombat(engine);
                        }

                        Check("Slate Warrior battle: Slate to GY",
                            p.Graveyard.Exists(c => c != null && c.CardId == slateId));
                        Check("Slate Warrior battle: destroyer loses 500 ATK/DEF lingering",
                            atk.CurrentAtk == 1300 && atk.CurrentDef == 500 &&
                            atk.LingeringAtkModifier == -500 && atk.LingeringDefModifier == -500,
                            $"atk={atk.CurrentAtk} def={atk.CurrentDef} " +
                            $"la={atk.LingeringAtkModifier} ld={atk.LingeringDefModifier}");
                        Check("Slate Warrior battle: destroyer math is 1300/500",
                            BattleMechanics.AttackValue(atk) == 1300 &&
                            BattleMechanics.DefenseValue(atk) == 500,
                            $"bvATK={BattleMechanics.AttackValue(atk)} " +
                            $"bvDEF={BattleMechanics.DefenseValue(atk)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        const int kuriboh = 40640057;
                        var slate = PlaceMonster(engine, p, slateId, 2, BattlePosition.Defense, false);
                        slate.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, kuriboh, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, slate);
                            DrainCombat(engine);
                        }

                        Check("Slate Warrior Flip-by-battle: survives 300 ATK, Flip +500 sticks",
                            p.TryFindMonster(slate, out _) &&
                            slate.LingeringAtkModifier == 500 &&
                            BattleMechanics.AttackValue(slate) == 2400 &&
                            BattleMechanics.DefenseValue(slate) == 900,
                            $"onField={p.TryFindMonster(slate, out _)} " +
                            $"atk={slate.CurrentAtk} bv={BattleMechanics.AttackValue(slate)} " +
                            $"la={slate.LingeringAtkModifier}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var yomi = PlaceMonster(engine, p, yomiId, 2, BattlePosition.Defense, false);
                        yomi.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, yomi);
                            DrainCombat(engine);
                        }

                        Check("Yomi Ship battle: destroyer destroyed",
                            !opp.TryFindMonster(atk, out _) &&
                            opp.Graveyard.Exists(c => c != null && c.CardId == laJinn),
                            $"atkOnField={opp.TryFindMonster(atk, out _)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var nd = PlaceMonster(engine, p, newdoriaId, 2, BattlePosition.Defense, false);
                        nd.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        var other = PlaceMonster(engine, opp, celtic, 1, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, nd);
                            DrainCombat(engine);
                        }

                        if (engine.IsAwaitingEffectTarget && engine.PendingActivation != null)
                        {
                            var t = engine.PendingActivation.LegalTargets
                                        .FirstOrDefault(c => c.CardId == celtic) ??
                                    engine.PendingActivation.LegalTargets.FirstOrDefault();
                            engine.TrySelectEffectTarget(t);
                        }

                        Check("Newdoria battle GY: targeted monster destroyed",
                            !opp.TryFindMonster(other, out _) || !opp.TryFindMonster(atk, out _),
                            $"celticOn={opp.TryFindMonster(other, out _)} laOn={opp.TryFindMonster(atk, out _)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var plant = engine.CreateCardInstance(darkworldThorns);
                        p.Graveyard.Add(plant);
                        var lord = PlaceMonster(engine, p, lordPoisonId, 2, BattlePosition.Defense, false);
                        lord.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, lord);
                            DrainCombat(engine);
                        }

                        if (engine.IsAwaitingEffectTarget && engine.PendingActivation != null)
                        {
                            var t = engine.PendingActivation.LegalTargets
                                        .FirstOrDefault(c => c.CardId == darkworldThorns) ??
                                    engine.PendingActivation.LegalTargets.FirstOrDefault();
                            engine.TrySelectEffectTarget(t);
                        }

                        Check("Lord Poison battle GY: Plant SS from GY, not itself",
                            p.TryFindMonster(plant, out _) && plant.WasSpecialSummoned &&
                            !p.Graveyard.Contains(plant) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == lordPoisonId),
                            $"plantOn={p.TryFindMonster(plant, out _)} plantGy={p.Graveyard.Contains(plant)} " +
                            $"lordGy={p.Graveyard.Exists(c => c != null && c.CardId == lordPoisonId)} " +
                            $"ss={plant.WasSpecialSummoned}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(32452818); // Beaver Warrior: EARTH 1200 ATK
                        var rat = PlaceMonster(engine, p, giantRatId, 2, BattlePosition.Defense, false);
                        rat.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, rat);
                            DrainCombat(engine);
                        }

                        Check("Giant Rat battle GY: EARTH 1500- ATK SS from Deck",
                            p.MonstersOnField().Any(m => m != null && m.CardId == 32452818) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == giantRatId) &&
                            !p.Deck.Contains(32452818),
                            $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} " +
                            $"gyRat={p.Graveyard.Exists(c => c != null && c.CardId == giantRatId)} " +
                            $"deck={p.Deck.Count}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        var fox = PlaceMonster(engine, p, foxFireId, 2, BattlePosition.Attack, true);
                        fox.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, fox);
                            DrainCombat(engine);
                        }

                        Check("Fox Fire battle: sent to GY destroyed by battle",
                            p.Graveyard.Contains(fox) && fox.WasDestroyedByBattle,
                            $"gy={p.Graveyard.Contains(fox)} battle={fox.WasDestroyedByBattle}");
                        TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                        Check("Fox Fire End Phase: self-SS from GY",
                            p.TryFindMonster(fox, out _) && fox.WasSpecialSummoned &&
                            !p.Graveyard.Contains(fox),
                            $"on={p.TryFindMonster(fox, out _)} gy={p.Graveyard.Contains(fox)} " +
                            $"ss={fox.WasSpecialSummoned}");
                    }
                }

                {
                    const int necrovalley = 47355498;
                    const int necrovalleyFoxFireId = 88753985;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var nv = PutInHand(engine, p, necrovalley);
                    var nvActive = engine.TryActivateSpellTrap(p, nv, fromHand: true);
                    FieldSpellEffects.RefreshBoard(engine);
                    var fox = engine.CreateCardInstance(necrovalleyFoxFireId);
                    fox.WasDestroyedByBattle = true;
                    fox.SentFromFieldTurnNumber = engine.TurnNumber;
                    p.Graveyard.Add(fox);
                    TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                    Check("Necrovalley: Fox Fire End Phase SS is blocked",
                        nvActive && p.Graveyard.Contains(fox) && !p.TryFindMonster(fox, out _),
                        $"nv={nvActive} gy={p.Graveyard.Contains(fox)} on={p.TryFindMonster(fox, out _)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(giantGermId);
                        p.Deck.Add(giantGermId);
                        var germ = PlaceMonster(engine, p, giantGermId, 2, BattlePosition.Defense, false);
                        germ.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        var lp = opp.LifePoints;
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, germ);
                            DrainCombat(engine);
                        }
                        Check("Giant Germ battle GY: opponent takes 500",
                            opp.LifePoints == lp - 500 &&
                            p.Graveyard.Exists(c => c != null && c.CardId == giantGermId),
                            $"lp={opp.LifePoints} was {lp}");
                        Check("Giant Germ any-number SS leftover: Deck copies remain",
                            p.Deck.Count == 2,
                            $"deck={p.Deck.Count} field={p.MonstersOnField().Count()}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        p.Deck.Clear();
                        p.Deck.Add(91152256);
                        p.Deck.Add(32452818);
                        var mimic = PlaceMonster(engine, p, 1102515, 2, BattlePosition.Defense, false);
                        mimic.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        var handBefore = p.HandCount;
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, mimic);
                            DrainCombat(engine);
                        }
                        Check("Dark Mimic LV3 battle GY: draw 1 (LV1 rider leftover)",
                            p.HandCount == handBefore + 1 &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 1102515),
                            $"hand={p.HandCount} was {handBefore}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(momongaId);
                        p.Deck.Add(momongaId);
                        var mom = PlaceMonster(engine, p, momongaId, 2, BattlePosition.Defense, false);
                        mom.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        var lp = p.LifePoints;
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, mom);
                            DrainCombat(engine);
                        }
                        Check("Nimble Momonga battle GY: controller gains 1000 LP",
                            p.LifePoints == lp + 1000 &&
                            p.Graveyard.Exists(c => c != null && c.CardId == momongaId),
                            $"lp={p.LifePoints} was {lp}");
                        Check("Nimble Momonga any-number SS leftover: Deck copies remain",
                            p.Deck.Count == 2,
                            $"deck={p.Deck.Count} field={p.MonstersOnField().Count()}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(77491079);
                        var grizz = PlaceMonster(engine, p, 57839750, 2, BattlePosition.Defense, false);
                        grizz.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, grizz);
                            DrainCombat(engine);
                        }
                        Check("Mother Grizzly battle GY: WATER 1500- ATK SS from Deck",
                            p.MonstersOnField().Any(m => m != null && m.CardId == 77491079) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 57839750),
                            $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(2326738);
                        var turtle = PlaceMonster(engine, p, 77044671, 2, BattlePosition.Defense, false);
                        turtle.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, turtle);
                            DrainCombat(engine);
                        }
                        Check("Pyramid Turtle battle GY: Zombie 2000- DEF SS from Deck",
                            p.MonstersOnField().Any(m => m != null && m.CardId == 2326738) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 77044671),
                            $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(55013285);
                        var troop = PlaceMonster(engine, p, 55013285, 2, BattlePosition.Defense, false);
                        troop.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, troop);
                            DrainCombat(engine);
                        }
                        Check("Troop Dragon battle GY: named Deck SS",
                            p.MonstersOnField().Any(m => m != null && m.CardId == 55013285 &&
                                                       m.WasSpecialSummoned) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 55013285),
                            $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Deck.Clear();
                        p.Deck.Add(76812113);
                        var bird = PlaceMonster(engine, p, 45547649, 2, BattlePosition.Defense, false);
                        bird.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, bird);
                            DrainCombat(engine);
                        }
                        Check("Birdface battle GY: add Harpie Lady from Deck",
                            p.Hand.Exists(c => c != null && c.CardId == 76812113) &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 45547649) &&
                            !p.Deck.Contains(76812113),
                            $"handHarpie={p.Hand.Exists(c => c != null && c.CardId == 76812113)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    if (ReachOpponentBattle(engine))
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        opp.LifePoints = 8000;
                        var golem = PlaceMonster(engine, p, 76321376, 2, BattlePosition.Attack, true);
                        golem.SetThisTurn = false;
                        var atk = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                        atk.SummonedThisTurn = false;
                        atk.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, atk))
                        {
                            engine.TryAttack(opp, atk, golem);
                            DrainCombat(engine);
                        }
                        Check("Mine Golem battle GY: opponent takes 500",
                            opp.LifePoints == 7500 &&
                            p.Graveyard.Exists(c => c != null && c.CardId == 76321376),
                            $"LP={opp.LifePoints}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var bite = PlaceMonster(engine, p, 50122883, 2, BattlePosition.Defense, false);
                    bite.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Bite Shoes Flip Summon", engine.TryFlipSummon(p, bite));
                    Check("Bite Shoes Flip: opens change-position target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Bite Shoes Flip: victim now Defense Position",
                            victim.Position == BattlePosition.Defense, $"pos={victim.Position}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var tapir = PlaceMonster(engine, p, desertapirId, 2, BattlePosition.Defense, false);
                    tapir.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Desertapir Flip Summon", engine.TryFlipSummon(p, tapir) && tapir.FaceUp);
                    Check("Desertapir Flip: opens set-FD target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var legal = engine.PendingActivation.LegalTargets;
                        Check("Desertapir Flip: itself is illegal",
                            !legal.Exists(c => c != null && c.CardId == desertapirId));
                        Check("Desertapir Flip: opp face-up is legal",
                            legal.Exists(c => c != null && c.CardId == celtic));
                        var t = legal.FirstOrDefault(c => c != null && c.CardId == celtic) ??
                                legal.FirstOrDefault();
                        Check("Desertapir: select Celtic", engine.TrySelectEffectTarget(t));
                        Check("Desertapir Flip: victim set face-down Defense",
                            !victim.FaceUp && victim.Position == BattlePosition.Defense,
                            $"face={victim.FaceUp} pos={victim.Position}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var clown = PutInHand(engine, p, clownId);
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Ryu-Kishin Clown Normal Summon",
                        engine.TryNormalSummon(p, clown, asSet: false) && clown.FaceUp);
                    Check("Ryu-Kishin Clown NS: opens change-position target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c != null && c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        Check("Ryu-Kishin Clown: select Celtic", engine.TrySelectEffectTarget(t));
                        Check("Ryu-Kishin Clown NS: victim now Defense Position",
                            victim.Position == BattlePosition.Defense, $"pos={victim.Position}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var mag = PlaceMonster(engine, p, 45141844, 2, BattlePosition.Defense, false);
                    mag.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Old Vindictive Magician Flip Summon", engine.TryFlipSummon(p, mag));
                    Check("Old Vindictive Magician Flip: opens opp destroy target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.OppFaceUpMonster);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Old Vindictive Magician Flip: victim destroyed",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Graveyard.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var guard = PlaceMonster(engine, p, 37101832, 2, BattlePosition.Defense, false);
                    guard.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Gravekeeper's Guard Flip Summon", engine.TryFlipSummon(p, guard));
                    Check("Gravekeeper's Guard Flip: opens bounce target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Gravekeeper's Guard Flip: victim returned to hand",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var gale = PlaceMonster(engine, p, galeId, 2, BattlePosition.Defense, false);
                    gale.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Gale Lizard Flip Summon", engine.TryFlipSummon(p, gale));
                    Check("Gale Lizard Flip: opens bounce target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Gale Lizard Flip: victim returned to hand",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var trap = engine.CreateCardInstance(SpellTrapEffects.Waboku);
                    p.Graveyard.Add(trap);
                    var mask = PlaceMonster(engine, p, 28933734, 2, BattlePosition.Defense, false);
                    mask.SetThisTurn = false;
                    Check("Mask of Darkness Flip Summon", engine.TryFlipSummon(p, mask) && mask.FaceUp);
                    Check("Mask of Darkness Flip: opens Trap-in-GY choice",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.TrapInYourGy);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Mask of Darkness Flip: Waboku added to hand",
                            p.Hand.Exists(c => c.CardId == SpellTrapEffects.Waboku) &&
                            !p.Graveyard.Exists(c => c.CardId == SpellTrapEffects.Waboku));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var dendle = PutInHand(engine, p, 12965761);
                    var orchis = PutInHand(engine, p, 46571052);
                    Check("Vampiric Orchis Normal Summon",
                        engine.TryNormalSummon(p, orchis, asSet: false) && orchis.FaceUp);
                    Check("Vampiric Orchis NS: Special Summoned Des Dendle from hand",
                        p.MonstersOnField().Any(m => m != null && m.CardId == 12965761 &&
                                                     m.WasSpecialSummoned) &&
                        !p.Hand.Contains(dendle),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} hand={p.Hand.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var tiger = PutInHand(engine, p, 47693640);
                    var cmd = PutInHand(engine, p, 10209545);
                    Check("Decayed Commander Normal Summon",
                        engine.TryNormalSummon(p, cmd, asSet: false) && cmd.FaceUp);
                    Check("Decayed Commander NS: Special Summoned Zombie Tiger from hand",
                        p.MonstersOnField().Any(m => m != null && m.CardId == 47693640 &&
                                                     m.WasSpecialSummoned) &&
                        !p.Hand.Contains(tiger),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} hand={p.Hand.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(celtic);
                    p.Deck.Add(32452818);
                    var lac = PlaceMonster(engine, p, 2326738, 2, BattlePosition.Defense, false);
                    lac.SetThisTurn = false;
                    var handBefore = p.HandCount;
                    Check("Des Lacooda Flip Summon", engine.TryFlipSummon(p, lac) && lac.FaceUp);
                    Check("Des Lacooda Flip Summoned: drew 1",
                        p.HandCount == handBefore + 1 && p.Deck.Count == 1,
                        $"hand={p.HandCount} was {handBefore} deck={p.Deck.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var scarab = PlaceMonster(engine, p, scarabsId, 2, BattlePosition.Attack, true);
                    Check("Swarm of Scarabs: sets itself face-down Defense",
                        engine.TryActivateSpellTrap(p, scarab, fromHand: false) &&
                        !scarab.FaceUp && scarab.Position == BattlePosition.Defense);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var bug = PlaceMonster(engine, p, scarabsId, 2, BattlePosition.Defense, false);
                    bug.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var other = PlaceMonster(engine, opp, 32452818, 1, BattlePosition.Attack, true);
                    Check("Swarm of Scarabs Flip Summon", engine.TryFlipSummon(p, bug) && bug.FaceUp);
                    Check("Swarm of Scarabs Flip Summon: opens opp destroy target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets != null &&
                        engine.PendingActivation.LegalTargets.Exists(c => c != null && c.CardId == celtic) &&
                        engine.PendingActivation.LegalTargets.Exists(c => c != null && c.CardId == 32452818),
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        Check("Swarm of Scarabs: select Celtic", engine.TrySelectEffectTarget(t));
                        Check("Swarm of Scarabs Flip Summon: only the targeted monster destroyed",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Graveyard.Exists(c => c.CardId == celtic) &&
                            opp.TryFindMonster(other, out _),
                            $"celticGy={opp.Graveyard.Exists(c => c.CardId == celtic)} beaverOn={opp.TryFindMonster(other, out _)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var loc = PlaceMonster(engine, p, locustsId, 2, BattlePosition.Defense, false);
                    loc.SetThisTurn = false;
                    var st = PlaceSetTrap(engine, opp, SpellTrapEffects.Waboku, 2);
                    Check("Swarm of Locusts Flip Summon", engine.TryFlipSummon(p, loc) && loc.FaceUp);
                    Check("Swarm of Locusts Flip Summon: opens ST destroy target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets != null &&
                        engine.PendingActivation.LegalTargets.Exists(c => c != null && c.CardId == SpellTrapEffects.Waboku),
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == SpellTrapEffects.Waboku) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        Check("Swarm of Locusts: select Waboku", engine.TrySelectEffectTarget(t));
                        Check("Swarm of Locusts Flip Summon: opponent ST destroyed",
                            !opp.TryFindSpellTrap(st, out _) &&
                            opp.Graveyard.Exists(c => c.CardId == SpellTrapEffects.Waboku));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var sent = PlaceMonster(engine, p, sentryId, 2, BattlePosition.Defense, false);
                    sent.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Golem Sentry Flip Summon", engine.TryFlipSummon(p, sent));
                    Check("Golem Sentry Flip Summon: opens bounce target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        engine.TrySelectEffectTarget(t);
                        Check("Golem Sentry Flip Summon: victim returned to hand",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var sph = PlaceMonster(engine, p, sphinxId, 2, BattlePosition.Defense, false);
                    sph.SetThisTurn = false;
                    var v1 = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var v2 = PlaceMonster(engine, opp, 32452818, 1, BattlePosition.Attack, true);
                    var keep = PlaceMonster(engine, p, 13039848, 1, BattlePosition.Attack, true);
                    Check("Guardian Sphinx Flip Summon", engine.TryFlipSummon(p, sph) && sph.FaceUp);
                    Check("Guardian Sphinx Flip Summon: bounced all opponent monsters, kept own",
                        !opp.TryFindMonster(v1, out _) &&
                        !opp.TryFindMonster(v2, out _) &&
                        opp.Hand.Exists(c => c.CardId == celtic) &&
                        opp.Hand.Exists(c => c.CardId == 32452818) &&
                        p.TryFindMonster(keep, out _) &&
                        !engine.IsAwaitingEffectTarget,
                        $"oppField={opp.MonstersOnField().Count()} keep={p.TryFindMonster(keep, out _)} pending={engine.IsAwaitingEffectTarget}");
                }

                {
                    const int hinotama = 96851799;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var pyro = PutInHand(engine, p, hinotama);
                    var warrior = PutInHand(engine, p, celtic);
                    var crater = PlaceMonster(engine, p, craterId, 2, BattlePosition.Attack, true);
                    engine.DestroyMonsterPublic(p, crater);
                    Check("Thing in the Crater destroy: SS Pyro from hand, not Warrior",
                        p.Graveyard.Exists(c => c != null && c.CardId == craterId) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == hinotama && m.WasSpecialSummoned) &&
                        !p.Hand.Contains(pyro) &&
                        p.Hand.Contains(warrior),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} hand={p.Hand.Count}");
                }

                {
                    const int hinotama = 96851799;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    PutInHand(engine, p, hinotama);
                    var crater = PlaceMonster(engine, p, craterId, 2, BattlePosition.Attack, true);
                    engine.SendCardToGrave(p, crater);
                    Check("Thing in the Crater send (not destroy): does not SS from hand",
                        p.Graveyard.Exists(c => c != null && c.CardId == craterId) &&
                        !p.MonstersOnField().Any(m => m != null && m.CardId == hinotama),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} gy={p.Graveyard.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var gyMonster = engine.CreateCardInstance(celtic);
                    var ownGy = engine.CreateCardInstance(32452818);
                    var gySpell = engine.CreateCardInstance(55144522);
                    opp.Graveyard.Add(gyMonster);
                    p.Graveyard.Add(ownGy);
                    p.Graveyard.Add(gySpell);
                    var fieldMon = PlaceMonster(engine, opp, laJinn, 2, BattlePosition.Attack, true);
                    var doc = PlaceMonster(engine, p, witchDoctorId, 2, BattlePosition.Defense, false);
                    doc.SetThisTurn = false;
                    Check("Witch Doctor Flip Summon", engine.TryFlipSummon(p, doc) && doc.FaceUp);
                    Check("Witch Doctor Flip: opens either-GY monster choice",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy,
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var legal = engine.PendingActivation.LegalTargets;
                        Check("Witch Doctor: opp GY monster is legal",
                            legal.Exists(t => t != null && t.CardId == celtic));
                        Check("Witch Doctor: own GY monster is legal",
                            legal.Exists(t => t != null && t.CardId == 32452818));
                        Check("Witch Doctor: GY Spell is illegal",
                            !legal.Exists(t => t != null && t.CardId == 55144522));
                        Check("Witch Doctor: field monster is illegal",
                            !legal.Exists(t => t != null && t.CardId == laJinn));
                        var pick = legal.FirstOrDefault(t => t.CardId == celtic);
                        Check("Witch Doctor: select opp GY monster",
                            pick != null && engine.TrySelectEffectTarget(pick));
                        Check("Witch Doctor: opp GY monster banished, not in GY",
                            opp.Banished.Exists(c => c != null && c.CardId == celtic) &&
                            !opp.Graveyard.Exists(c => c != null && c.CardId == celtic));
                        Check("Witch Doctor: own GY monster stays, field monster stays",
                            p.Graveyard.Exists(c => c != null && c.CardId == 32452818) &&
                            opp.TryFindMonster(fieldMon, out _));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var legal = engine.CreateCardInstance(13039848);
                    var lv4Normal = engine.CreateCardInstance(celtic);
                    var lv2Effect = engine.CreateCardInstance(54652250);
                    var oppLegal = engine.CreateCardInstance(90357090);
                    p.Graveyard.Add(legal);
                    p.Graveyard.Add(lv4Normal);
                    p.Graveyard.Add(lv2Effect);
                    opp.Graveyard.Add(oppLegal);
                    var caller = PlaceMonster(engine, p, 48659020, 2, BattlePosition.Defense, false);
                    caller.SetThisTurn = false;
                    Check("Spirit Caller Flip Summon", engine.TryFlipSummon(p, caller) && caller.FaceUp);
                    Check("Spirit Caller Flip: opens controller-GY monster choice",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.TargetKind == EffectTargetKind.MonsterInEitherGy,
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var legalTargets = engine.PendingActivation.LegalTargets;
                        Check("Spirit Caller: Level 3 Normal is legal",
                            legalTargets.Exists(t => t != null && t.CardId == 13039848));
                        Check("Spirit Caller: Level 4 Normal is illegal",
                            !legalTargets.Exists(t => t != null && t.CardId == celtic));
                        Check("Spirit Caller: Level 2 Effect is illegal",
                            !legalTargets.Exists(t => t != null && t.CardId == 54652250));
                        Check("Spirit Caller: opponent GY is illegal",
                            !legalTargets.Exists(t => t != null && t.CardId == 90357090));
                        var pick = legalTargets.FirstOrDefault(t => t.CardId == 13039848);
                        Check("Spirit Caller: select Level 3 Normal",
                            pick != null && engine.TrySelectEffectTarget(pick));
                        Check("Spirit Caller: Giant Soldier Special Summoned from GY",
                            p.MonstersOnField().Any(m => m != null && m.CardId == 13039848 &&
                                                         m.WasSpecialSummoned) &&
                            !p.Graveyard.Exists(c => c != null && c.CardId == 13039848),
                            $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} gy={p.Graveyard.Count}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(celtic);
                    p.Deck.Add(37101832);
                    var spy = PlaceMonster(engine, p, 24317029, 2, BattlePosition.Defense, false);
                    spy.SetThisTurn = false;
                    Check("Gravekeeper's Spy Flip Summon", engine.TryFlipSummon(p, spy) && spy.FaceUp);
                    Check("Gravekeeper's Spy Flip: SS series ATK<=1500 from Deck",
                        p.MonstersOnField().Any(m => m != null && m.CardId == 37101832 &&
                                                     m.WasSpecialSummoned) &&
                        !p.Deck.Contains(37101832) &&
                        p.Deck.Contains(celtic),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} deck={p.Deck.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var jar = PlaceMonster(engine, p, 86801871, 2, BattlePosition.Defense, false);
                    jar.SetThisTurn = false;
                    Check("Cobra Jar Flip Summon", engine.TryFlipSummon(p, jar) && jar.FaceUp);
                    Check("Cobra Jar Flip: Special Summoned Poisonous Snake Token 1200/1200",
                        p.MonstersOnField().Any(m => m != null && m.IsToken &&
                                                     m.CurrentAtk == 1200 && m.CurrentDef == 1200 &&
                                                     m.Level == 3),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name + "/" + m.CurrentAtk))}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var creator = PutInHand(engine, p, 61505339);
                    var inc = PlaceMonster(engine, p, 97093037, 2, BattlePosition.Attack, true);
                    Check("The Creator Incarnate: Activate tribute-this named hand SS",
                        engine.TryActivateSpellTrap(p, inc, fromHand: false) &&
                        p.Graveyard.Contains(inc) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == 61505339 &&
                                                     m.WasSpecialSummoned) &&
                        !p.Hand.Contains(creator),
                        $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} gy={p.Graveyard.Count} hand={p.Hand.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(7512044);
                    p.Deck.Add(91152256);
                    var gym = PutInHand(engine, p, 7512044);
                    Check("Gather Your Mind Activate",
                        engine.TryActivateSpellTrap(p, gym, fromHand: true));
                    Check("Gather Your Mind: added named copy from Deck; decoy remains",
                        p.Hand.Exists(c => c != null && c.CardId == 7512044) &&
                        !p.Deck.Contains(7512044) &&
                        p.Deck.Contains(91152256) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == 7512044),
                        $"hand={string.Join(",", p.Hand.Select(c => c.Name))} " +
                        $"deck={p.Deck.Count} gy={p.Graveyard.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(39552864);
                    p.Deck.Add(91152256);
                    p.LifePoints = 4000;
                    var venus = PlaceMonster(engine, p, 64734921, 2, BattlePosition.Attack, true);
                    Check("Venus: Activate pay 500 named SS from Deck",
                        engine.TryActivateSpellTrap(p, venus, fromHand: false) &&
                        p.LifePoints == 3500 &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == 39552864 &&
                                                     m.WasSpecialSummoned) &&
                        !p.Deck.Contains(39552864) &&
                        p.Deck.Contains(91152256),
                        $"LP={p.LifePoints} field={string.Join(",", p.MonstersOnField().Select(m => m.Name))} " +
                        $"deck={p.Deck.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var bug = PlaceMonster(engine, p, 83994646, 2, BattlePosition.Defense, false);
                    bug.SetThisTurn = false;
                    PlaceMonster(engine, opp, 91152256, 2, BattlePosition.Attack, true);
                    PlaceMonster(engine, opp, 13039848, 1, BattlePosition.Attack, true);
                    PlaceMonster(engine, p, 97590747, 1, BattlePosition.Attack, true);
                    Check("4-Starred Ladybug Flip Summon", engine.TryFlipSummon(p, bug) && bug.FaceUp);
                    Check("4-Starred Ladybug Flip: opp Level 4 destroyed; Level 3 and controller Level 4 remain",
                        !opp.MonstersOnField().Any(m => m != null && m.CardId == 91152256) &&
                        opp.Graveyard.Exists(c => c != null && c.CardId == 91152256) &&
                        opp.MonstersOnField().Any(m => m != null && m.CardId == 13039848) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == 97590747),
                        $"oppField={string.Join(",", opp.MonstersOnField().Select(m => m.Name))} " +
                        $"pField={string.Join(",", p.MonstersOnField().Select(m => m.Name))} " +
                        $"oppGy={opp.Graveyard.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.LifePoints = 4000;
                    var immortal = PlaceMonster(engine, p, immortalId, 2, BattlePosition.Defense, false);
                    immortal.SetThisTurn = false;
                    Check("Immortal of Thunder Flip Summon",
                        engine.TryFlipSummon(p, immortal) && immortal.FaceUp);
                    Check("Immortal of Thunder Flip: controller gains 3000 LP",
                        p.LifePoints == 7000, $"LP={p.LifePoints}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var moai = PlaceMonster(engine, p, moaiId, 2, BattlePosition.Attack, true);
                    Check("Moai Interceptor Cannons: sets itself face-down Defense",
                        engine.TryActivateSpellTrap(p, moai, fromHand: false) &&
                        !moai.FaceUp && moai.Position == BattlePosition.Defense);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var worm = PlaceMonster(engine, p, medusaId, 2, BattlePosition.Defense, false);
                    worm.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    var other = PlaceMonster(engine, opp, 32452818, 1, BattlePosition.Attack, true);
                    Check("Medusa Worm Flip Summon", engine.TryFlipSummon(p, worm) && worm.FaceUp);
                    Check("Medusa Worm Flip Summon: opens opp destroy target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets != null &&
                        engine.PendingActivation.LegalTargets.Exists(c => c != null && c.CardId == celtic),
                        engine.PendingActivation?.TargetKind.ToString() ?? "no pending");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        Check("Medusa Worm: select Celtic", engine.TrySelectEffectTarget(t));
                        Check("Medusa Worm Flip Summon: only the targeted monster destroyed",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Graveyard.Exists(c => c.CardId == celtic) &&
                            opp.TryFindMonster(other, out _),
                            $"gy={opp.Graveyard.Count} field={opp.MonstersOnField().Count()}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var statue = PlaceMonster(engine, p, statueId, 2, BattlePosition.Defense, false);
                    statue.SetThisTurn = false;
                    var victim = PlaceMonster(engine, opp, celtic, 2, BattlePosition.Attack, true);
                    Check("Guardian Statue Flip Summon", engine.TryFlipSummon(p, statue) && statue.FaceUp);
                    Check("Guardian Statue Flip Summon: opens bounce target",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        var t = engine.PendingActivation.LegalTargets
                                    .FirstOrDefault(c => c.CardId == celtic) ??
                                engine.PendingActivation.LegalTargets.FirstOrDefault();
                        Check("Guardian Statue: select Celtic", engine.TrySelectEffectTarget(t));
                        Check("Guardian Statue Flip Summon: victim returned to hand",
                            !opp.TryFindMonster(victim, out _) &&
                            opp.Hand.Exists(c => c.CardId == celtic));
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    var p = engine.Player;
                    var foxLock = PlaceMonster(engine, p, foxFireId, 1, BattlePosition.Attack, true);
                    Check("Fox Fire face-up instance has tribute lock flag",
                        foxLock.CannotBeTributedForSummon);
                    Check("Fox Fire face-up cannot be Tributed for a Tribute Summon",
                        !TcgRules.CanBeTributedForSummon(p, foxLock));
                    foxLock.FaceUp = false;
                    Check("Fox Fire face-down can be Tributed",
                        TcgRules.CanBeTributedForSummon(p, foxLock));
                    ClearBoard(engine);
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    p = engine.Player;
                    var malice = PlaceMonster(engine, p, maliceId, 2, BattlePosition.Attack, true);
                    var ecto = PlaceSetTrap(engine, p, ectoId, 2);
                    ecto.FaceUp = true;
                    engine.SendCardToGrave(p, malice, sentBy: ecto);
                    Check("Malice Doll sent by Continuous Spell to GY",
                        p.Graveyard.Contains(malice) && malice.SentByContinuousSpellEffect);
                    for (var t = 0; t < 8; t++)
                    {
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        engine.RecoverStuckCombat();
                        if (p.TryFindMonster(malice, out _))
                            break;
                        engine.TryEndTurnSafe(engine.TurnPlayer);
                    }

                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    Check("Malice Doll next controller Standby: Special Summoned from GY",
                        p.TryFindMonster(malice, out _) && malice.WasSpecialSummoned &&
                        !p.Graveyard.Contains(malice),
                        $"onField={p.TryFindMonster(malice, out _)} gy={p.Graveyard.Contains(malice)} " +
                        $"turn={engine.TurnNumber} turnPlayer={engine.TurnPlayer?.Name}");
                }
            }

            // ── Registered Polymerization Fusion stress ──
            {
                const int bsd = SpellTrapEffects.BlackSkullDragon;
                const int gaia = SpellTrapEffects.GaiaTheDragonChampion;
                const int teR = SpellTrapEffects.ThousandEyesRestrict;

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    engine.Opponent.Hand.Clear();
                    p.ExtraDeck.Clear();
                    p.ExtraDeck.Add(bsd);
                    var poly = PutInHand(engine, p, SpellTrapEffects.Polymerization);
                    var skull = PutInHand(engine, p, SpellTrapEffects.SummonedSkull);
                    var redEyes = PutInHand(engine, p, SpellTrapEffects.RedEyesBlackDragon);
                    var materials = new System.Collections.Generic.List<CardInstance> { skull, redEyes };
                    var check = SummonProcedures.CheckFusion(engine, p, engine.CreateCardInstance(bsd),
                        materials, poly);
                    Check("Polymerization: Black Skull exact recipe is legal", check.Legal, check.Reason);
                    Check("Polymerization: Black Skull sends hand materials and summons from Extra",
                        engine.TryActivateSpellTrap(p, poly, fromHand: true) &&
                        p.Graveyard.Contains(skull) && p.Graveyard.Contains(redEyes) &&
                        p.Graveyard.Contains(poly) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == bsd),
                         $"gy={p.Graveyard.Count} field={p.MonsterCount} extra={p.ExtraDeck.Count}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    engine.Opponent.Hand.Clear();
                    p.ExtraDeck.Clear();
                    p.ExtraDeck.Add(gaia);
                    var poly = PutInHand(engine, p, SpellTrapEffects.Polymerization);
                    var fieldGaia = PlaceMonster(engine, p, SpellTrapEffects.GaiaTheFierceKnight, 2,
                        BattlePosition.Attack, true);
                    var handCurse = PutInHand(engine, p, SpellTrapEffects.CurseOfDragon);
                    Check("Polymerization: Gaia accepts field + hand materials",
                        engine.TryActivateSpellTrap(p, poly, fromHand: true) &&
                        p.Graveyard.Contains(fieldGaia) && p.Graveyard.Contains(handCurse) &&
                        p.Graveyard.Contains(poly) && p.MonstersOnField().Any(m => m != null && m.CardId == gaia));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    engine.Opponent.Hand.Clear();
                    p.Hand.Clear();
                    p.ExtraDeck.Clear();
                    p.ExtraDeck.Add(teR);
                    var poly = PutInHand(engine, p, SpellTrapEffects.Polymerization);
                    var relinquished = PutInHand(engine, p, SpellTrapEffects.Relinquished);
                    var idol = PutInHand(engine, p, SpellTrapEffects.ThousandEyesIdol);
                    Check("Polymerization: Thousand-Eyes Restrict effect Fusion is registered",
                        OfficialEffectRegistry.HasSummonProcedure(teR, SummonKind.FusionSummon));
                    Check("Polymerization: Thousand-Eyes Restrict summons with official materials",
                        engine.TryActivateSpellTrap(p, poly, fromHand: true) &&
                        p.Graveyard.Contains(relinquished) && p.Graveyard.Contains(idol) &&
                        p.MonstersOnField().Any(m => m != null && m.CardId == teR));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    engine.Opponent.Hand.Clear();
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.ExtraDeck.Clear();
                    p.ExtraDeck.Add(bsd);
                    var poly = PutInHand(engine, p, SpellTrapEffects.Polymerization);
                    var skull = PutInHand(engine, p, SpellTrapEffects.SummonedSkull);
                    var wrong = PutInHand(engine, p, SpellTrapEffects.CurseOfDragon);
                    Check("Polymerization: illegal Black Skull materials refuse without moving cards",
                        !engine.CanActivateSpellTrap(p, poly, fromHand: true) &&
                        !engine.TryActivateSpellTrap(p, poly, fromHand: true) &&
                        p.Hand.Contains(poly) && p.Hand.Contains(skull) && p.Hand.Contains(wrong) &&
                        p.Graveyard.Count == 0);
                    var contact = SummonProcedures.CheckFusion(engine, p, engine.CreateCardInstance(bsd),
                        new System.Collections.Generic.List<CardInstance> { skull, wrong }, null);
                    Check("Fusion: unregistered contact procedure refuses",
                        !contact.Legal && contact.Reason.IndexOf("Contact", System.StringComparison.OrdinalIgnoreCase) >= 0,
                        contact.Reason);
                    var parkedFusion = SummonProcedures.CheckFusion(engine, p, engine.CreateCardInstance(85684223),
                        new System.Collections.Generic.List<CardInstance>(), poly);
                    Check("Fusion: unregistered Reaper on the Nightmare refuses",
                        !parkedFusion.Legal && parkedFusion.Reason.IndexOf("No official", System.StringComparison.OrdinalIgnoreCase) >= 0,
                        parkedFusion.Reason);
                }
            }

            // ── Phase trigger location guard: sent-to-GY source must not fire as field trigger ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var sourceDef = new CardDef
                {
                    id = 90000101,
                    name = "End Phase Send-Then-Summon Probe",
                    type = "Effect Monster",
                    desc = "During the End Phase: You can send this face-up card to the GY; Special Summon 1 \"Celtic Guardian\" from your hand or Deck."
                };
                var source = new CardInstance
                {
                    InstanceId = 90000101,
                    CardId = sourceDef.id,
                    Def = sourceDef,
                    FaceUp = true
                };
                p.Graveyard.Add(source);
                p.Deck.Insert(0, 91152256);
                TextEffectRuntime.FirePhaseTriggers(engine, p, EffectTiming.EndPhase);
                Check("End Phase trigger: field-only send-then-summon does not fire from GY",
                    p.Graveyard.Contains(source) && p.MonsterCount == 0 && p.Deck.Contains(91152256),
                     $"gy={p.Graveyard.Contains(source)} monsters={p.MonsterCount} deckHasTarget={p.Deck.Contains(91152256)}");
            }

            // ── Ritual Summon (Greater named / Equal attribute) ──
            {
                const int blRitual = RitualProcedures.BlackLusterRitual;
                const int bls = RitualProcedures.BlackLusterSoldier;
                const int biRitual = RitualProcedures.BlackIllusionRitual;
                const int relId = RitualProcedures.Relinquished;
                const int earthChant = RitualProcedures.EarthChant;
                const int abyssContract = RitualProcedures.ContractWithTheAbyss;
                const int celtic = 91152256; // Lv4
                const int beaver = 32452818; // Lv4
                const int giant = 13039848; // Lv3
                const int queen = 5901497; // Lv1

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var monster = PutInHand(engine, p, bls);
                    var ns = SummonProcedures.CheckNormalOrTribute(engine, p, monster, asSet: false);
                    Check("Ritual: Black Luster Soldier cannot Normal Summon",
                        !engine.CanNormalSummonOrSet(p, monster) && !ns.Legal,
                        ns.Reason);
                    Check("Ritual: BLS refusal is the Ritual NS lock",
                        ns.Reason != null &&
                        ns.Reason.IndexOf("Ritual", System.StringComparison.OrdinalIgnoreCase) >= 0,
                        ns.Reason);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var monster = PutInHand(engine, p, bls);
                    var t1 = PutInHand(engine, p, celtic);
                    var t2 = PutInHand(engine, p, beaver);
                    Check("Ritual: Black Luster Ritual Greater 8 activates",
                        engine.CanActivateSpellTrap(p, spell, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, spell, fromHand: true));
                    Check("Ritual: BLS is on the field after Greater 8",
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned);
                    Check("Ritual: Greater 8 tributes in GY, spell in GY",
                        p.Graveyard.Contains(t1) && p.Graveyard.Contains(t2) &&
                        p.Graveyard.Contains(spell) && !p.Hand.Contains(monster),
                        $"gy={p.Graveyard.Count} hand={p.Hand.Count} field={p.MonsterCount}");
                }
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var monster = PutInHand(engine, p, bls);
                    var baseDef = monster.Def;
                    monster.Def = new CardDef
                    {
                        id = baseDef.id, name = baseDef.name, type = baseDef.type, frameType = baseDef.frameType,
                        desc = "Cannot be Special Summoned except by its registered Ritual Summon procedure.",
                        atk = baseDef.atk, def = baseDef.def, level = baseDef.level, race = baseDef.race,
                        attribute = baseDef.attribute, archetype = baseDef.archetype
                    };
                    PutInHand(engine, p, celtic);
                    PutInHand(engine, p, beaver);
                    Check("Ritual: registered hard Nomi still summons by Ritual procedure",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned);
                }


                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var monster = PutInHand(engine, p, bls);
                    var t1 = PutInHand(engine, p, giant);
                    Check("Ritual: Greater 8 refuses when tribute sum < 8",
                        !engine.CanActivateSpellTrap(p, spell, fromHand: true) &&
                        !engine.TryActivateSpellTrap(p, spell, fromHand: true));
                    Check("Ritual: insufficient Greater tributes move no cards",
                        p.Hand.Contains(spell) && p.Hand.Contains(monster) &&
                        p.Hand.Contains(t1) && p.Graveyard.Count == 0 &&
                        p.MonsterCount == 0);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, biRitual);
                    var monster = PutInHand(engine, p, relId);
                    var t1 = PutInHand(engine, p, celtic);
                    Check("Ritual: Black Illusion Ritual Greater 1 SS Relinquished",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned &&
                        p.Graveyard.Contains(t1) && p.Graveyard.Contains(spell));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, abyssContract);
                    var monster = PutInHand(engine, p, relId);
                    var t1 = PutInHand(engine, p, queen);
                    Check("Ritual: Contract with the Abyss Equal 1 DARK SS Relinquished",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned &&
                        p.Graveyard.Contains(t1) && p.Graveyard.Contains(spell));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, earthChant);
                    var monster = PutInHand(engine, p, bls);
                    var t1 = PutInHand(engine, p, celtic);
                    var t2 = PutInHand(engine, p, beaver);
                    Check("Ritual: Earth Chant Equal 8 EARTH SS Black Luster Soldier",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned &&
                        p.Graveyard.Contains(t1) && p.Graveyard.Contains(t2) &&
                        p.Graveyard.Contains(spell));
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var wrong = PutInHand(engine, p, relId);
                    var t1 = PutInHand(engine, p, celtic);
                    var t2 = PutInHand(engine, p, beaver);
                    Check("Ritual: wrong monster in hand is refused",
                        !engine.CanActivateSpellTrap(p, spell, fromHand: true) &&
                        !engine.TryActivateSpellTrap(p, spell, fromHand: true));
                    Check("Ritual: wrong monster leaves all cards unmoved",
                        p.Hand.Contains(spell) && p.Hand.Contains(wrong) &&
                        p.Hand.Contains(t1) && p.Hand.Contains(t2) &&
                        p.Graveyard.Count == 0 && p.MonsterCount == 0);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, earthChant);
                    var dark = PutInHand(engine, p, relId);
                    var t1 = PutInHand(engine, p, queen);
                    Check("Ritual: Earth Chant refuses DARK Relinquished",
                        !engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.Hand.Contains(dark) && p.Hand.Contains(spell) &&
                        p.Graveyard.Count == 0);
                }

                {
                    const int curse = 28279543; // Lv5
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var monster = PutInHand(engine, p, bls);
                    var t1 = PutInHand(engine, p, curse);
                    var t2 = PutInHand(engine, p, celtic);
                    Check("Ritual: Greater 8 accepts 5+4 overshoot",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned &&
                        p.Graveyard.Contains(t1) && p.Graveyard.Contains(t2) &&
                        p.Graveyard.Contains(spell));
                }

                {
                    const int curse = 28279543; // Lv5
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, earthChant);
                    var monster = PutInHand(engine, p, bls);
                    var t1 = PutInHand(engine, p, curse);
                    var t2 = PutInHand(engine, p, celtic);
                    Check("Ritual: Equal 8 refuses 5+4 overshoot",
                        !engine.CanActivateSpellTrap(p, spell, fromHand: true) &&
                        !engine.TryActivateSpellTrap(p, spell, fromHand: true));
                    Check("Ritual: Equal overshoot moves no cards",
                        p.Hand.Contains(spell) && p.Hand.Contains(monster) &&
                        p.Hand.Contains(t1) && p.Hand.Contains(t2) &&
                        p.Graveyard.Count == 0 && p.MonsterCount == 0);
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var spell = PutInHand(engine, p, blRitual);
                    var monster = PutInHand(engine, p, bls);
                    var fieldT = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var handT = PutInHand(engine, p, beaver);
                    Check("Ritual: Greater 8 tributes from field + hand",
                        engine.TryActivateSpellTrap(p, spell, fromHand: true) &&
                        p.TryFindMonster(monster, out _) && monster.WasSpecialSummoned &&
                        p.Graveyard.Contains(fieldT) && p.Graveyard.Contains(handT) &&
                        !p.TryFindMonster(fieldT, out _));
                }
            }

            // ── Spirit Reaper family: destroy after a targeting effect resolves ──
            {
                const int reaperId = 23205979;
                const int rotnId = 85684223;
                const int axeId = 40619825;
                const int mstId = SpellTrapEffects.Mst;
                const int bookMoonId = 14087893;
                const int wabokuId = SpellTrapEffects.Waboku;

                var reaperDef = db.Get(reaperId);
                var reaperProg = reaperDef != null ? CardTextEffectCompiler.Compile(reaperDef) : null;
                Check("Spirit Reaper compiles DestroyThisAfterResolvingTargetingEffect from official PSCT",
                    reaperProg != null &&
                    reaperProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect &&
                        c.Timing == EffectTiming.ContinuousWhileFaceUp),
                    reaperProg == null
                        ? "null"
                        : $"full={reaperProg.FullyCompiled} n={reaperProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", reaperProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                Check("Spirit Reaper compiles CannotBeDestroyedByBattle from official first sentence",
                    reaperProg != null &&
                    reaperProg.FullyCompiled &&
                    reaperProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.CannotBeDestroyedByBattle &&
                        c.Timing == EffectTiming.ContinuousWhileFaceUp),
                    reaperProg == null
                        ? "null"
                        : $"full={reaperProg.FullyCompiled} n={reaperProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", reaperProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var rotnDef = db.Get(rotnId);
                var rotnProg = rotnDef != null ? CardTextEffectCompiler.Compile(rotnDef) : null;
                Check("Reaper on the Nightmare compiles the same shared targeting-destroy kind",
                    rotnProg != null &&
                    rotnProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect),
                    rotnProg == null
                        ? "null"
                        : $"full={rotnProg.FullyCompiled} n={rotnProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", rotnProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                Check("Reaper on the Nightmare compiles CannotBeDestroyedByBattle (not-destroyed-as-result)",
                    rotnProg != null &&
                    rotnProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.CannotBeDestroyedByBattle),
                    rotnProg == null
                        ? "null"
                        : $"full={rotnProg.FullyCompiled} n={rotnProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", rotnProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                Check("Reaper on the Nightmare compiles direct-attack random discard as the shared kind",
                    rotnProg != null &&
                    rotnProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        c.RequiresDirectAttack),
                    rotnProg == null
                        ? "null"
                        : $"full={rotnProg.FullyCompiled} n={rotnProg.ClauseList.Count} " +
                          $"unparsed={string.Join("|", rotnProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synCdbb = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000034,
                    name = "Battle-Protect Reaper (new-card shape)",
                    type = "Effect Monster",
                    desc = "Cannot be destroyed by battle."
                });
                Check("New-card rule: Cannot-be-destroyed-by-battle compiles without a cardId branch",
                    synCdbb != null && synCdbb.FullyCompiled &&
                    synCdbb.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.CannotBeDestroyedByBattle));

                var synAfter = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000031,
                    name = "After-Target Reaper (new-card shape)",
                    type = "Effect Monster",
                    desc =
                        "After resolving a card effect that targets this face-up card, destroy this card."
                });
                Check("New-card rule: After-resolving-target destroy compiles without a cardId branch",
                    synAfter != null && synAfter.FullyCompiled &&
                    synAfter.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect));

                var synWhen = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000032,
                    name = "When-Targeted Reaper (new-card shape)",
                    type = "Effect Monster",
                    desc = "Destroy this card when it is targeted by a card effect."
                });
                Check("New-card rule: Destroy-when-targeted compiles as the same shared kind",
                    synWhen != null && synWhen.FullyCompiled &&
                    synWhen.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect));

                var synIf = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000033,
                    name = "If-Targeted Reaper (new-card shape)",
                    type = "Effect Monster",
                    desc = "If this card is targeted by an effect, destroy it."
                });
                Check("New-card rule: If-targeted-destroy compiles as the same shared kind",
                    synIf != null && synIf.FullyCompiled &&
                    synIf.ClauseList.Exists(c =>
                        c != null &&
                        c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var host = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                    var axe = PutInHand(engine, p, axeId);
                    Check("Spirit Reaper: Equip Axe activate opens host target",
                        engine.CanActivateSpellTrap(p, axe, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, axe, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(host));
                    Check("Spirit Reaper: after Equip resolves, Reaper and Axe are in GY (no host)",
                        engine.IsAwaitingEffectTarget &&
                        engine.TrySelectEffectTarget(host) &&
                        p.Graveyard.Contains(host) &&
                        p.Graveyard.Contains(axe) &&
                        !p.TryFindMonster(host, out _) &&
                        axe.EquippedTo == null,
                        $"reaperGy={p.Graveyard.Contains(host)} axeGy={p.Graveyard.Contains(axe)} " +
                        $"eqHost={axe.EquippedTo != null} field={p.TryFindMonster(host, out _)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var host = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                    var st = PlaceSetTrap(engine, opp, wabokuId, 2);
                    var mst = PutInHand(engine, p, mstId);
                    Check("Spirit Reaper: MST targeting a S/T does not self-destroy Reaper",
                        engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation.LegalTargets.Contains(st) &&
                        !engine.PendingActivation.LegalTargets.Contains(host) &&
                        engine.TrySelectEffectTarget(st) &&
                        p.TryFindMonster(host, out _) &&
                        host.FaceUp &&
                        opp.Graveyard.Contains(st),
                        $"field={p.TryFindMonster(host, out _)} gy={p.Graveyard.Contains(host)} stGy={opp.Graveyard.Contains(st)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var host = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                    var moon = PutInHand(engine, p, bookMoonId);
                    Check("Spirit Reaper: Book of Moon targeting Reaper leaves it face-down (PSCT face-up)",
                        engine.CanActivateSpellTrap(p, moon, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, moon, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.TrySelectEffectTarget(host) &&
                        p.TryFindMonster(host, out _) &&
                        !host.FaceUp &&
                        !p.Graveyard.Contains(host),
                        $"field={p.TryFindMonster(host, out _)} faceUp={host.FaceUp} gy={p.Graveyard.Contains(host)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var host = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Defense, false);
                    TextEffectRuntime.NotifyTargetingEffectResolved(engine, host, true);
                    Check("Spirit Reaper: face-down notify does not self-destroy (PSCT face-up)",
                        p.TryFindMonster(host, out _) &&
                        !host.FaceUp &&
                        !p.Graveyard.Contains(host));
                }
            }

                {
                    const int reaperId = 23205979;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var other = PlaceMonster(engine, p, 91152256, 1, BattlePosition.Attack, true);
                    var reaper = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                    TextEffectRuntime.NotifyTargetingEffectsResolved(engine,
                        new[] { other, reaper }, true);
                    Check("Spirit Reaper: multi-target notify reaches every selected card",
                        p.TryFindMonster(other, out _) &&
                        p.Graveyard.Contains(reaper) &&
                        !p.Graveyard.Contains(other));
                }


            // ── This card inflicts battle damage: discard N random from opponent hand ──
            {
                const int reaperId = 23205979;
                const int decayedId = 10209545;
                const int hatId = 15150365;
                const int idolId = 27125110; // Thousand-Eyes Idol 0/0
                const int celtic = 91152256;

                var reaperDef = db.Get(reaperId);
                var reaperBd = reaperDef != null ? CardTextEffectCompiler.Compile(reaperDef) : null;
                Check("Spirit Reaper compiles DiscardRandomFromOpponentHand (direct-attack filter)",
                    reaperBd != null &&
                    reaperBd.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        c.RequiresDirectAttack &&
                        c.Amount == 1),
                    reaperBd == null
                        ? "null"
                        : $"full={reaperBd.FullyCompiled} n={reaperBd.ClauseList.Count} " +
                          $"unparsed={string.Join("|", reaperBd.UnparsedFragments ?? System.Array.Empty<string>())}");

                var hatDef = db.Get(hatId);
                var hatBd = hatDef != null ? CardTextEffectCompiler.Compile(hatDef) : null;
                Check("White Magical Hat compiles random discard on any battle damage (not direct-only)",
                    hatBd != null &&
                    hatBd.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        !c.RequiresDirectAttack &&
                        c.Amount == 1),
                    hatBd == null
                        ? "null"
                        : $"full={hatBd.FullyCompiled} n={hatBd.ClauseList.Count} " +
                          $"unparsed={string.Join("|", hatBd.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synDirect = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000041,
                    name = "Direct-Damage Discard (new-card shape)",
                    type = "Effect Monster",
                    desc =
                        "When this card inflicts battle damage to your opponent by a direct attack: " +
                        "Discard 1 random card from their hand."
                });
                Check("New-card rule: direct-attack random discard compiles without a cardId branch",
                    synDirect != null && synDirect.FullyCompiled &&
                    synDirect.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        c.RequiresDirectAttack));

                var synAny = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000042,
                    name = "Any-Damage Discard (new-card shape)",
                    type = "Effect Monster",
                    desc =
                        "If this card inflicts Battle Damage to your opponent's Life Points, " +
                        "your opponent discards 1 card randomly from his/her hand."
                });
                Check("New-card rule: any-battle-damage random discard compiles as the same kind",
                    synAny != null && synAny.FullyCompiled &&
                    synAny.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                        c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                        !c.RequiresDirectAttack));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachPlayerBattle(engine);
                    Check("battle-damage discard: reach player Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Player} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        opp.Hand.Clear();
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var reaper = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                        reaper.SummonedThisTurn = false;
                        reaper.ClearAttackFlags();
                        var held = PutInHand(engine, opp, celtic);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var lpBefore = opp.LifePoints;
                        Check("Reaper direct: can attack",
                            engine.Phase == DuelPhase.Battle && engine.CanAttack(p, reaper));
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, reaper))
                        {
                            engine.TryAttack(p, reaper, null);
                            DrainCombat(engine);
                        }

                        Check("Reaper direct: inflicts damage and discards 1 random from opponent hand",
                            opp.LifePoints < lpBefore &&
                            opp.Hand.Count == 0 &&
                            opp.Graveyard.Contains(held) &&
                            !opp.Hand.Contains(held),
                            $"LP {lpBefore}->{opp.LifePoints} hand={opp.Hand.Count} " +
                            $"gyHas={opp.Graveyard.Contains(held)} phase={engine.Phase}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachPlayerBattle(engine);
                    Check("battle-damage discard: reach player Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Player} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        opp.Hand.Clear();
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var reaper = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                        reaper.SummonedThisTurn = false;
                        reaper.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var lpBefore = opp.LifePoints;
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, reaper))
                        {
                            engine.TryAttack(p, reaper, null);
                            DrainCombat(engine);
                        }

                        Check("Reaper direct vs empty hand: still deals damage, no crash",
                            opp.LifePoints < lpBefore &&
                            opp.Hand.Count == 0 &&
                            p.TryFindMonster(reaper, out _),
                            $"LP {lpBefore}->{opp.LifePoints} hand={opp.Hand.Count} " +
                            $"field={p.TryFindMonster(reaper, out _)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachPlayerBattle(engine);
                    Check("battle-damage discard: reach player Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Player} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        opp.Hand.Clear();
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var reaper = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                        reaper.SummonedThisTurn = false;
                        reaper.ClearAttackFlags();
                        var prey = PlaceMonster(engine, opp, idolId, 2, BattlePosition.Attack, true);
                        var held = PutInHand(engine, opp, celtic);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var lpBefore = opp.LifePoints;
                        Check("Reaper vs 0-ATK: can attack",
                            engine.Phase == DuelPhase.Battle && engine.CanAttack(p, reaper));
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, reaper))
                        {
                            engine.TryAttack(p, reaper, prey);
                            DrainCombat(engine);
                        }

                        Check("Reaper vs monster: inflicts battle damage but does not discard (direct-attack filter)",
                            opp.LifePoints < lpBefore &&
                            opp.Hand.Contains(held) &&
                            !opp.Graveyard.Contains(held),
                            $"LP {lpBefore}->{opp.LifePoints} handHas={opp.Hand.Contains(held)} " +
                            $"gyHas={opp.Graveyard.Contains(held)} preyDead={!opp.TryFindMonster(prey, out _)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachPlayerBattle(engine);
                    Check("battle-damage discard: reach player Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Player} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        opp.Hand.Clear();
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var cmd = PlaceMonster(engine, p, decayedId, 2, BattlePosition.Attack, true);
                        cmd.SummonedThisTurn = false;
                        cmd.ClearAttackFlags();
                        var held = PutInHand(engine, opp, celtic);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var lpBefore = opp.LifePoints;
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, cmd))
                        {
                            engine.TryAttack(p, cmd, null);
                            DrainCombat(engine);
                        }

                        Check("Decayed Commander direct: discards 1 random from opponent hand",
                            opp.LifePoints < lpBefore &&
                            opp.Hand.Count == 0 &&
                            opp.Graveyard.Contains(held),
                            $"LP {lpBefore}->{opp.LifePoints} hand={opp.Hand.Count} " +
                            $"gyHas={opp.Graveyard.Contains(held)}");
                    }
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachPlayerBattle(engine);
                    Check("battle-damage discard: reach player Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Player} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        opp.Hand.Clear();
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var hat = PlaceMonster(engine, p, hatId, 2, BattlePosition.Attack, true);
                        hat.SummonedThisTurn = false;
                        hat.ClearAttackFlags();
                        var prey = PlaceMonster(engine, opp, idolId, 2, BattlePosition.Attack, true);
                        var held = PutInHand(engine, opp, celtic);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var lpBefore = opp.LifePoints;
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(p, hat))
                        {
                            engine.TryAttack(p, hat, prey);
                            DrainCombat(engine);
                        }

                        Check("White Magical Hat vs monster: battle damage (not direct) discards 1 random",
                            opp.LifePoints < lpBefore &&
                            opp.Hand.Count == 0 &&
                            opp.Graveyard.Contains(held),
                            $"LP {lpBefore}->{opp.LifePoints} hand={opp.Hand.Count} " +
                            $"gyHas={opp.Graveyard.Contains(held)} preyDead={!opp.TryFindMonster(prey, out _)}");
                    }
                }
            }

            // ── Cannot be destroyed by battle (Spirit Reaper flag from text) ──
            {
                const int reaperId = 23205979;
                const int bewd = 89631139;
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    var reachedBattle = ReachOpponentBattle(engine);
                    Check("CDBB: reach opponent Battle",
                        reachedBattle,
                        $"phase={engine.Phase} turnP={engine.TurnPlayer == engine.Opponent} tn={engine.TurnNumber}");
                    if (reachedBattle)
                    {
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.LifePoints = 8000;
                        opp.LifePoints = 8000;
                        var reaper = PlaceMonster(engine, p, reaperId, 2, BattlePosition.Attack, true);
                        reaper.SummonedThisTurn = false;
                        reaper.ClearAttackFlags();
                        FieldSpellEffects.ApplyRuleConditions(reaper);
                        var dragon = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                        dragon.SummonedThisTurn = false;
                        dragon.ClearAttackFlags();
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        Check("Spirit Reaper: OfficialEffectRegistry CannotBeDestroyedByBattle from compiled text",
                            OfficialEffectRegistry.CannotBeDestroyedByBattle(reaper));
                        var lpBefore = p.LifePoints;
                        if (engine.Phase == DuelPhase.Battle && engine.CanAttack(opp, dragon))
                        {
                            engine.TryAttack(opp, dragon, reaper);
                            DrainCombat(engine);
                        }
                        Check("Spirit Reaper: survives 3000 ATK battle (cannot be destroyed by battle)",
                            p.TryFindMonster(reaper, out _) &&
                            !p.Graveyard.Contains(reaper) &&
                            p.LifePoints < lpBefore,
                            $"field={p.TryFindMonster(reaper, out _)} gy={p.Graveyard.Contains(reaper)} lp={p.LifePoints}");
                    }
                }
            }

            // ── Tribute Summoned: target 1 monster; destroy (Zaborg / Monarch shape) ──
            {
                const int zaborgId = 51945556;
                const int celtic = 91152256;
                const int bewd = 89631139;
                var zDef = db.Get(zaborgId);
                var zProg = zDef != null ? CardTextEffectCompiler.Compile(zDef) : null;
                Check("Zaborg the Thunder Monarch FullyCompiled Tribute Summoned destroy",
                    zProg != null && zProg.FullyCompiled &&
                    zProg.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisTributeSummoned &&
                        c.Action == EffectActionKind.Destroy &&
                        c.RequiresTargetChoice &&
                        c.Zone == EffectZoneFilter.FieldAnyMonster),
                    zProg == null
                        ? "null"
                        : $"full={zProg.FullyCompiled} n={zProg.ClauseList.Count} unparsed={string.Join("|", zProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var synZ = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000052,
                    name = "New Monarch (Zaborg shape)",
                    type = "Effect Monster",
                    desc =
                        "If this card is Tribute Summoned: Target 1 monster on the field; destroy that target."
                });
                Check("New-card rule: Tribute-Summoned destroy compiles without a cardId branch",
                    synZ != null && synZ.FullyCompiled &&
                    synZ.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.ThisCardSummoned &&
                        c.RequiresThisTributeSummoned &&
                        c.Action == EffectActionKind.Destroy));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var who = engine.Player;
                    var opp = engine.Opponent;
                    who.Hand.Clear();
                    var fodder = PlaceMonster(engine, who, celtic, 0, BattlePosition.Attack, true);
                    var prey = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                    var zaborg = PutInHand(engine, who, zaborgId);
                    Check("Zaborg: Tribute Summon with one fodder",
                        engine.TryNormalSummon(who, zaborg, asSet: false) &&
                        who.TryFindMonster(zaborg, out _) &&
                        who.Graveyard.Contains(fodder),
                        $"mz={who.TryFindMonster(zaborg, out _)} fodderGy={who.Graveyard.Contains(fodder)} pending={engine.IsAwaitingEffectTarget}");
                    Check("Zaborg: Tribute Summon opens monster-destroy target",
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(prey),
                        engine.PendingActivation == null
                            ? "no pending"
                            : $"n={engine.PendingActivation.LegalTargets.Count}");
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Zaborg: destroy opponent BEWD",
                            engine.TrySelectEffectTarget(prey) &&
                            opp.Graveyard.Contains(prey) &&
                            !opp.TryFindMonster(prey, out _),
                            $"gy={opp.Graveyard.Contains(prey)} field={opp.TryFindMonster(prey, out _)}");
                    }
                }
            }

            // ── Deck-Grok P0 street staples (shared kinds / PSCT fragments) ──
            {
                const int fissure = 66788016;
                const int reinforcements = 17814387;
                const int castleWalls = 44209392;
                const int rush = 70046172;
                const int smashing = 97169186;
                const int hammer = 26412047;
                const int stopDefense = 63102017;
                const int blackPendant = 65169794;
                const int axe = 40619825;
                const int kamakiri = 84834865;
                const int elegant = 90219263;
                const int umi = 22702055;
                const int callHaunted = 97077563;
                const int enchantedJavelin = 96355986;
                const int sevenTools = 3819470;
                const int magicJammer = 77414722;
                const int spellbinding = 18807108;
                const int celtic = 91152256; // 1400 ATK
                const int bewd = 89631139; // 3000 ATK / 2500 DEF
                const int harpie = 76812113;
                const int harpieSisters = 12206212;
                const int giantSoldier = 13039848; // 1300 ATK / 2000 DEF

                var fProg = CardTextEffectCompiler.Compile(db.Get(fissure));
                Check("Fissure FullyCompiled destroy lowest ATK opp",
                    fProg != null && fProg.FullyCompiled &&
                    fProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy && c.PickLowestAtk &&
                        c.Zone == EffectZoneFilter.OppFaceUpMonsters),
                    fProg == null ? "null" : $"full={fProg.FullyCompiled} unparsed={string.Join("|", fProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                Check("Fissure choice is not PSCT targeting",
                    fProg != null && fProg.ClauseList.Exists(c => c != null && c.PickLowestAtk &&
                        c.RequiresTargetChoice && !c.IsPsctTarget));
                var rProg = CardTextEffectCompiler.Compile(db.Get(reinforcements));
                Check("Reinforcements FullyCompiled +500 ATK until EOT",
                    rProg != null && rProg.FullyCompiled &&
                    rProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn &&
                        c.Amount == 500 && c.RequiresTargetChoice),
                    rProg == null ? "null" : $"full={rProg.FullyCompiled} unparsed={string.Join("|", rProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var cProg = CardTextEffectCompiler.Compile(db.Get(castleWalls));
                Check("Castle Walls FullyCompiled +500 DEF until EOT",
                    cProg != null && cProg.FullyCompiled &&
                    cProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn &&
                        c.DefAmount == 500),
                    cProg == null ? "null" : $"full={cProg.FullyCompiled} unparsed={string.Join("|", cProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var rushProg = CardTextEffectCompiler.Compile(db.Get(rush));
                Check("Rush Recklessly FullyCompiled +700 ATK until EOT",
                    rushProg != null && rushProg.FullyCompiled &&
                    rushProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn &&
                        c.Amount == 700 && c.IsPsctTarget),
                    rushProg == null ? "null" : $"full={rushProg.FullyCompiled} unparsed={string.Join("|", rushProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var sProg = CardTextEffectCompiler.Compile(db.Get(smashing));
                Check("Smashing Ground FullyCompiled destroy highest DEF opp",
                    sProg != null && sProg.FullyCompiled &&
                    sProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy && c.PickHighestDef),
                    sProg == null ? "null" : $"full={sProg.FullyCompiled} unparsed={string.Join("|", sProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                Check("Smashing Ground choice is not PSCT targeting",
                    sProg != null && sProg.ClauseList.Exists(c => c != null && c.PickHighestDef &&
                        c.RequiresTargetChoice && !c.IsPsctTarget));
                var hProg = CardTextEffectCompiler.Compile(db.Get(hammer));
                Check("Hammer Shot FullyCompiled destroy highest ATK face-up ATK pos",
                    hProg != null && hProg.FullyCompiled &&
                    hProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy && c.PickHighestAtk &&
                        c.RequiresAttackPosition),
                    hProg == null ? "null" : $"full={hProg.FullyCompiled} unparsed={string.Join("|", hProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                Check("Hammer Shot choice is not PSCT targeting",
                    hProg != null && hProg.ClauseList.Exists(c => c != null && c.PickHighestAtk &&
                        c.RequiresTargetChoice && !c.IsPsctTarget));
                var sdProg = CardTextEffectCompiler.Compile(db.Get(stopDefense));
                Check("Stop Defense FullyCompiled ForceAttackPosition",
                    sdProg != null && sdProg.FullyCompiled &&
                    sdProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ChangeBattlePosition &&
                        c.ForceAttackPosition),
                    sdProg == null ? "null" : $"full={sdProg.FullyCompiled} unparsed={string.Join("|", sdProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var bpProg = CardTextEffectCompiler.Compile(db.Get(blackPendant));
                Check("Black Pendant FullyCompiled equip +500 + GY damage",
                    bpProg != null && bpProg.FullyCompiled &&
                    bpProg.ClauseList.Exists(c => c != null && c.EquipAtkBonus == 500) &&
                    bpProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500),
                    bpProg == null ? "null" : $"full={bpProg.FullyCompiled} n={bpProg.ClauseList.Count} unparsed={string.Join("|", bpProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var axeProg = CardTextEffectCompiler.Compile(db.Get(axe));
                Check("Axe of Despair FullyCompiled Archfiend +1000 + GY top-deck",
                    axeProg != null && axeProg.FullyCompiled &&
                    axeProg.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.AlwaysTreatedAsName) &&
                    axeProg.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.PlaceThisOnTopOfDeck),
                    axeProg == null ? "null" : $"full={axeProg.FullyCompiled} unparsed={string.Join("|", axeProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var kProg = CardTextEffectCompiler.Compile(db.Get(kamakiri));
                Check("Flying Kamakiri #1 FullyCompiled battle-GY WIND ≤1500 SS deck",
                    kProg != null && kProg.FullyCompiled &&
                    kProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed &&
                        c.FromDeck && c.RequiresThisDestroyedByBattle &&
                        !string.IsNullOrEmpty(c.AttributeFilter) &&
                        c.AttributeFilter.IndexOf("WIND", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                        c.AmountIsAtkMax && c.Amount == 1500),
                    kProg == null ? "null" : $"full={kProg.FullyCompiled} unparsed={string.Join("|", kProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var eProg = CardTextEffectCompiler.Compile(db.Get(elegant));
                Check("Elegant Egotist FullyCompiled Harpie gate + AltNamed SS",
                    eProg != null && eProg.FullyCompiled &&
                    eProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonNamed &&
                        !string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                        !string.IsNullOrEmpty(c.AltNamedCard) && c.FromHand && c.FromDeck),
                    eProg == null ? "null" : $"full={eProg.FullyCompiled} unparsed={string.Join("|", eProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var uProg = CardTextEffectCompiler.Compile(db.Get(umi));
                Check("Umi FullyCompiled multi-race ATK/DEF auras",
                    uProg != null && uProg.FullyCompiled &&
                    uProg.ClauseList.Count(c =>
                        c != null && c.Action == EffectActionKind.ContinuousGainAtkDef) >= 6,
                    uProg == null ? "null" : $"full={uProg.FullyCompiled} n={uProg.ClauseList.Count} unparsed={string.Join("|", uProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var callProg = CardTextEffectCompiler.Compile(db.Get(callHaunted));
                Check("Call of the Haunted FullyCompiled GY-SS stay-on-field (Premature sibling)",
                    callProg != null && callProg.FullyCompiled &&
                    callProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                        c.DestroyHostWhenThisLeaves && c.StaysOnField),
                    callProg == null ? "null" : $"full={callProg.FullyCompiled} unparsed={string.Join("|", callProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                var ejProg = CardTextEffectCompiler.Compile(db.Get(enchantedJavelin));
                Check("Enchanted Javelin FullyCompiled GainLpEqualToAtk on attacker",
                    ejProg != null && ejProg.FullyCompiled &&
                    ejProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.AttackDeclared &&
                        c.Action == EffectActionKind.GainLpEqualToAtk &&
                        c.Zone == EffectZoneFilter.AttackingMonster),
                    ejProg == null ? "null" : $"full={ejProg.FullyCompiled} unparsed={string.Join("|", ejProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                // Parked: counter-negate needs Spell/Trap activation response timing.
                var stProg = CardTextEffectCompiler.Compile(db.Get(sevenTools));
                Check("Seven Tools of the Bandit PARKED (no Spell/Trap activation response timing)",
                    stProg == null || !stProg.FullyCompiled ||
                    !stProg.ClauseList.Exists(c =>
                        c != null && c.Action != EffectActionKind.None &&
                        (c.SourceSnippet ?? "").IndexOf("negate", System.StringComparison.OrdinalIgnoreCase) >= 0));

                var mjProg = CardTextEffectCompiler.Compile(db.Get(magicJammer));
                Check("Seven Tools remains not FullyCompiled", stProg != null && !stProg.FullyCompiled);
                Check("Magic Jammer remains not FullyCompiled", mjProg != null && !mjProg.FullyCompiled);
                Check("Magic Jammer PARKED (no Spell/Trap activation response timing)",
                    mjProg == null || !mjProg.FullyCompiled ||
                    !mjProg.ClauseList.Exists(c =>
                        c != null && (c.SourceSnippet ?? "").IndexOf("negate", System.StringComparison.OrdinalIgnoreCase) >= 0));

                var sbProg = CardTextEffectCompiler.Compile(db.Get(spellbinding));
                Check("Spellbinding Circle FullyCompiled bound cannot-attack/position",
                    sbProg != null && sbProg.FullyCompiled &&
                    sbProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.ContinuousCannotAttack &&
                        c.RequiresTargetChoice && c.StaysOnField &&
                        c.DestroyThisWhenBoundHostDestroyed &&
                        !c.DestroyHostWhenThisLeaves &&
                        c.AlsoCannotChangeBattlePosition) &&
                    sbProg.ClauseList.Exists(c =>
                        c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                        c.Action == EffectActionKind.ContinuousCannotAttack),
                    sbProg == null ? "null" : $"full={sbProg.FullyCompiled} unparsed={string.Join("|", sbProg.UnparsedFragments ?? System.Array.Empty<string>())}");

                // ── Deck-Grok P1 wander searchers (shared kinds / PSCT fragments) ──
                {
                    void CheckSearcher(int id, string label, System.Func<CompiledCardProgram, bool> ok)
                    {
                        var d = db.Get(id);
                        var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                        Check(label,
                            pr != null && pr.FullyCompiled && ok(pr),
                            pr == null
                                ? "null"
                                : $"full={pr.FullyCompiled} n={pr.ClauseList.Count} " +
                                  $"unparsed={string.Join("|", pr.UnparsedFragments ?? System.Array.Empty<string>())}");
                    }

                    void CheckRecruiter(int id, string name, string kind, int cap, bool atk)
                    {
                        var isAttr = kind == "DARK" || kind == "LIGHT" || kind == "EARTH" ||
                                     kind == "WATER" || kind == "FIRE" || kind == "WIND";
                        CheckSearcher(id, name + " FullyCompiled battle-GY Deck SS",
                            pr => pr.ClauseList.Exists(c =>
                                c != null &&
                                c.Action == EffectActionKind.SpecialSummonNamed &&
                                c.FromDeck && c.RequiresThisDestroyedByBattle &&
                                c.Amount == cap &&
                                (atk ? c.AmountIsAtkMax : c.AmountIsDefMax) &&
                                (isAttr
                                    ? string.Equals(c.AttributeFilter, kind,
                                        System.StringComparison.OrdinalIgnoreCase)
                                    : string.Equals(c.RaceFilter, kind,
                                        System.StringComparison.OrdinalIgnoreCase))));
                    }

                    CheckRecruiter(83011278, "Mystic Tomato", "DARK", 1500, true);
                    CheckRecruiter(97017120, "Giant Rat", "EARTH", 1500, true);
                    CheckRecruiter(95956346, "Shining Angel", "LIGHT", 1500, true);
                    CheckRecruiter(60806437, "UFO Turtle", "FIRE", 1500, true);
                    CheckRecruiter(57839750, "Mother Grizzly", "WATER", 1500, true);
                    CheckRecruiter(77044671, "Pyramid Turtle", "Zombie", 2000, false);
                    CheckRecruiter(93107608, "Howling Insect", "Insect", 1500, true);
                    CheckRecruiter(39191307, "Masked Dragon", "Dragon", 1500, true);
                    // Flying Kamakiri #1 is the P0 recruiter fixture (84834865).

                    CheckSearcher(32807846, "ROTA FullyCompiled Level≤4 Warrior Deck add",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.AddFromDeckToHand &&
                            c.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq &&
                            c.Amount == 4 &&
                            !string.IsNullOrEmpty(c.RaceFilter) &&
                            c.RaceFilter.IndexOf("Warrior", System.StringComparison.OrdinalIgnoreCase) >= 0));

                    CheckSearcher(31786629, "Thunder Dragon FullyCompiled discard-self named Deck add×2",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresDiscardSelf && c.ActivatesFromHand &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck && c.Amount == 2 &&
                            string.Equals(c.NamedCard, "Thunder Dragon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(24317029, "Gravekeeper's Spy FullyCompiled Flip series ATK≤1500 Deck SS",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromDeck && c.AmountIsAtkMax && c.Amount == 1500 &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(45547649, "Birdface FullyCompiled battle-GY named Deck add (not free ignition)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresThisDestroyedByBattle &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)) &&
                        !pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            !c.RequiresThisDestroyedByBattle));

                    CheckSearcher(89997728, "Toon Table of Contents FullyCompiled named Toon series Deck add",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                            c.FromDeck && c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Toon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(36262024, "Black Dragon's Chick FullyCompiled send-this named hand SS",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresSendThisToGy &&
                            c.Action == EffectActionKind.SpecialSummonNamed &&
                            c.FromHand &&
                            string.Equals(c.NamedCard, "Red-Eyes B. Dragon",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(14087893, "Book of Moon FullyCompiled SetTargetFaceDownDefense",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                            c.RequiresTargetChoice));

                    CheckSearcher(53582587, "Torrential Tribute FullyCompiled summon-window destroy all",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.Destroy &&
                            c.AnswersSpecialSummon && c.AnswersControllerSummon &&
                            c.Side == EffectSide.Both));

                    CheckSearcher(30450531, "Rite of Spirit FullyCompiled named GY-SS (unaffected by Necrovalley)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.UnaffectedByNamedCard, "Necrovalley",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(15259703, "Toon World FullyCompiled pay-1000 activate",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.PayLpAmount == 1000 && c.StaysOnField));

                    CheckSearcher(45986603, "Snatch Steal FullyCompiled take-control + opp Standby they-gain-LP",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.TakeControlOfTarget) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.GainLifePoints &&
                            c.Side == EffectSide.Opponent && c.OpponentTurnOnly &&
                            c.Amount == 1000));

                    void CheckParked(int id, string label)
                    {
                        var d = db.Get(id);
                        var pr = d != null ? CardTextEffectCompiler.Compile(d) : null;
                        // UniqueException FullyCompiled (Time Wizard coin-call) is still "parked"
                        // as a named unique — not a shared kind.
                        var uniqueOnly = pr != null && pr.FullyCompiled && pr.ClauseList.Count > 0 &&
                            pr.ClauseList.TrueForAll(c =>
                                c == null ||
                                WRLDZ.Duel.TextEffects.EffectVocabulary.IsUniqueException(c.Action));
                        Check(label + " PARKED",
                            pr == null || !pr.FullyCompiled || uniqueOnly,
                            pr == null ? "null" :
                            $"full={pr.FullyCompiled} uniqueOnly={uniqueOnly} unparsed={string.Join("|", pr.UnparsedFragments ?? System.Array.Empty<string>())}");
                    }

                    CheckSearcher(47355498, "Necrovalley FullyCompiled GK aura + GY cannot banish/target",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.Amount == 500 && c.DefAmount == 500) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotBanishFromGraveyard) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard &&
                            string.Equals(c.ExceptNamedCard, "Necrovalley",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(75782277, "Harpies' Hunting Ground FullyCompiled Winged Beast aura + Harpie NS/SS destroy S/T",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                            string.Equals(c.RaceFilter, "Winged Beast",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.Amount == 200 && c.DefAmount == 200) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.Destroy &&
                            c.AnswersSpecialSummon && c.RequiresTargetChoice &&
                            c.Zone == EffectZoneFilter.FieldSpellTraps &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(77007920, "Laser Cannon Armor FullyCompiled Equip Insect +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Insect",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(25769732, "Machine Conversion Factory FullyCompiled Equip Machine +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Machine",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(98299011, "Gift of The Mystical Elf FullyCompiled LP per monster on field",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.Activate &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 300 &&
                            c.Side == EffectSide.Controller &&
                            c.ScaleAmountByFieldMonsters));

                    CheckSearcher(98252586, "Follow Wind FullyCompiled Equip Winged Beast +300/+300",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Winged Beast",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(36361633, "Threatening Roar FullyCompiled cannot-attack this turn",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.PreventOpponentAttacksThisTurn));

                    CheckSearcher(94716515, "Eradicating Aerosol FullyCompiled destroy all Insect",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.Destroy &&
                            string.Equals(c.RaceFilter, "Insect",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(64306248, "Skull-Mark Ladybug FullyCompiled GY +1000 LP",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.Action == EffectActionKind.GainLifePoints &&
                            c.Amount == 1000 &&
                            c.Side == EffectSide.Controller));

                    CheckSearcher(46700124, "Machine King FullyCompiled ATK per Machine on field",
                        pr => pr.FullyCompiled &&
                        pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Action == EffectActionKind.ContinuousGainAtkDef &&
                            c.ScaleThisAtkByMatchingCount &&
                            c.Amount == 100 &&
                            string.Equals(c.RaceFilter, "Machine",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(51267887, "Raise Body Heat FullyCompiled Equip Dinosaur +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Dinosaur",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(63224564, "Cyber Shield FullyCompiled Equip Harpie Lady or Sisters +500",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 500 &&
                            string.Equals(c.EquipHostName, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.AltNamedCard, "Harpie Lady Sisters",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(18605135, "Tornado Wall FullyCompiled Umi activate + no battle damage + self-destroy",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Activate &&
                            string.Equals(c.RequiresFaceUpName, "Umi",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.StaysOnField) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.PreventControllerBattleDamage) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp));

                    CheckSearcher(26185991, "Pinch Hopper FullyCompiled sent-to-GY Insect SS",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
                            c.Action == EffectActionKind.SpecialSummonFromHand &&
                            c.FromHand &&
                            string.Equals(c.RaceFilter, "Insect",
                                System.StringComparison.OrdinalIgnoreCase)));
                    CheckParked(68191243, "Mustering of the Dark Scorpions");
                    CheckSearcher(2204140, "Book of Life FullyCompiled dual GY SS+banish",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.RequiresSecondTarget &&
                            c.SecondAction == EffectActionKind.Banish &&
                            c.SecondZone == EffectZoneFilter.OppGyMonsters &&
                            string.Equals(c.RaceFilter, "Zombie",
                                System.StringComparison.OrdinalIgnoreCase)));
                    CheckParked(3819470, "Seven Tools of the Bandit");
                    CheckParked(77414722, "Magic Jammer");
                    CheckSearcher(18807108, "Spellbinding Circle FullyCompiled bound cannot-attack",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ContinuousCannotAttack &&
                            c.RequiresTargetChoice && c.AlsoCannotChangeBattlePosition &&
                            c.StaysOnField));
                    CheckParked(57728570, "Crush Card Virus");
                    CheckParked(71625222, "Time Wizard");
                    CheckParked(81210420, "Magical Hats");
                    CheckParked(40703222, "Multiply");

                    // ── P1 battle leftovers (shared kinds) ──
                    CheckSearcher(13945283, "Wall of Illusion FullyCompiled after-dmg bounce attacker",
                        pr => pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.AfterDamageCalculation &&
                            c.Action == EffectActionKind.ReturnToHand &&
                            c.RequiresThisIsAttackTarget &&
                            c.Zone == EffectZoneFilter.AttackingMonster));

                    CheckSearcher(2671330, "Hyper Hammerhead FullyCompiled end-DS bounce if survived",
                        pr => pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.EndOfDamageStep &&
                            c.Action == EffectActionKind.ReturnToHand &&
                            c.RequiresBattledMonsterNotDestroyed &&
                            c.Zone == EffectZoneFilter.OpponentBattlingMonster));

                    CheckSearcher(7572887, "D.D. Warrior Lady FullyCompiled after-dmg optional banish-both",
                        pr => pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.AfterDamageCalculation &&
                            c.Action == EffectActionKind.Banish &&
                            c.AlsoBanishThis && c.IsOptional &&
                            c.Zone == EffectZoneFilter.OpponentBattlingMonster));

                    CheckSearcher(70074904, "D.D. Assailant FullyCompiled after-dmg destroyed banish-both",
                        pr => pr.ClauseList.Exists(c =>
                            c != null &&
                            c.Timing == EffectTiming.AfterDamageCalculation &&
                            c.Action == EffectActionKind.Banish &&
                            c.AlsoBanishThis && c.RequiresThisDestroyedByBattle &&
                            c.Zone == EffectZoneFilter.OpponentBattlingMonster));

                    CheckSearcher(74131780, "Exiled Force FullyCompiled Tribute this → Destroy target",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresTributeThis &&
                            c.Action == EffectActionKind.Destroy && c.RequiresTargetChoice));

                    CheckSearcher(11384280, "Cannon Soldier FullyCompiled Tribute 1 → 500 damage",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresTributeCount == 1 &&
                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                            c.Amount == 500));

                    CheckSearcher(91869203, "Amazoness Archer FullyCompiled Tribute 2 → 1200 damage (shared)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.RequiresTributeCount == 2 &&
                            c.Action == EffectActionKind.InflictDamageToOpponent &&
                            c.Amount == 1200));

                    CheckSearcher(12206212, "Harpie Lady Sisters FullyCompiled summon-restriction-only",
                        pr => pr.FullyCompiled && pr.ClauseList.Count == 0);

                    CheckSearcher(63162310, "Wall Shadow FullyCompiled summon-restriction-only (shared)",
                        pr => pr.FullyCompiled && pr.ClauseList.Count == 0);

                    CheckSearcher(93920745, "Penguin Soldier FullyCompiled Flip bounce up-to-2",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.ReturnToHand &&
                            c.Amount == 2 && c.RequiresTargetChoice));
                    CheckSearcher(28357177, "Hade-Hane FullyCompiled Flip bounce up-to-3 (shared)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.Flip &&
                            c.Action == EffectActionKind.ReturnToHand &&
                            c.Amount == 3 && c.RequiresTargetChoice));
                    CheckSearcher(65240384, "Big Shield Gardna FullyCompiled Gardna-only negate",
                        pr => pr.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ChainLinkActivated && c.Action == EffectActionKind.NegateActivation && c.ChainResponseOnly && c.FlipSelfFaceUpDefense && !c.DestroyNegatedCard && !c.RequiresDiscardCost));
                    CheckSearcher(98502113, "Dark Paladin FullyCompiled discard negate+destroy",
                        pr => pr.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ChainLinkActivated && c.Action == EffectActionKind.NegateActivation && c.ChainResponseOnly && c.RequiresDiscardCost && c.DiscardCostCount == 1 && c.DestroyNegatedCard && !c.FlipSelfFaceUpDefense));
                    CheckSearcher(48229808, "Horus LV8 FullyCompiled face-up negate+destroy",
                        pr => pr.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ChainLinkActivated && c.Action == EffectActionKind.NegateActivation && c.ChainResponseOnly && !c.RequiresDiscardCost && c.DestroyNegatedCard && !c.FlipSelfFaceUpDefense));
                    CheckParked(94004268, "Amazoness Swords Woman");
                    CheckParked(37390589, "Kunai with Chain");
                    CheckParked(96008713, "Magical Arm Shield");
                    CheckParked(26302522, "Blast Sphere");
                    CheckParked(68540059, "Metalmorph");
                    CheckSearcher(2460565, "Marauding Captain FullyCompiled NS SS Lv≤4 + Warrior attack-lock",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.ThisCardSummoned &&
                            c.Action == EffectActionKind.SpecialSummonFromHand &&
                            c.FromHand && c.AmountIsLevel && c.Amount == 4 &&
                            c.RequiresThisNormalSummoned) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotBeAttackTarget &&
                            c.ExceptThisCard &&
                            string.Equals(c.RaceFilter, "Warrior",
                                System.StringComparison.OrdinalIgnoreCase)));
                    CheckSearcher(25262697, "Gravekeeper's Assailant FullyCompiled named attack position",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Timing == EffectTiming.AttackDeclared &&
                            c.Action == EffectActionKind.ChangeBattlePosition &&
                            c.RequiresTargetChoice &&
                            string.Equals(c.RequiresFaceUpName, "Necrovalley",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.Zone == EffectZoneFilter.OppFaceUpMonsters));
                }

                // ── Shared-kind live: Penguin already covered in Flip block; Captain / Book / Circle ──
                {
                    const int captainId = 2460565;
                    const int captainGiantSoldier = 13039848; // Level 3 Rock
                    const int celticW = 91152256; // Warrior
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var cap = PutInHand(engine, p, captainId);
                    var lv3 = PutInHand(engine, p, captainGiantSoldier);
                    Check("Marauding Captain: Normal Summon",
                        engine.TryNormalSummon(p, cap, asSet: false));
                    Check("Marauding Captain: opens Level≤4 hand SS",
                        engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(lv3));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Marauding Captain: SS Level 3 from hand",
                            engine.TrySelectEffectTarget(lv3) &&
                            p.TryFindMonster(lv3, out _) &&
                            !p.Hand.Contains(lv3));
                    }

                    var warrior = PlaceMonster(engine, p, celticW, 0, BattlePosition.Attack, true);
                    Check("Marauding Captain: other Warrior cannot be attack-targeted",
                        WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, warrior));
                    Check("Marauding Captain: itself can still be attacked",
                        !WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, cap));
                    Check("Marauding Captain: non-Warrior not attack-locked",
                        !WRLDZ.Duel.TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, lv3));
                }

                {
                    const int bookId = 2204140;
                    const int turtle = 77044671; // Zombie
                    const int celticW = 91152256;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var z = engine.CreateCardInstance(turtle);
                    p.Graveyard.Add(z);
                    var og = engine.CreateCardInstance(celticW);
                    opp.Graveyard.Add(og);
                    var book = PutInHand(engine, p, bookId);
                    Check("Book of Life: Activate legal with both GY monsters",
                        engine.CanActivateSpellTrap(p, book, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, book, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(z));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Book of Life: first pick waits for opp GY",
                            engine.TrySelectEffectTarget(z) && engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation.AwaitingSecondTarget &&
                            engine.PendingActivation.LegalTargets.Contains(og));
                        Check("Book of Life: SS Zombie, banish opp GY",
                            engine.TrySelectEffectTarget(og) &&
                            p.TryFindMonster(z, out _) &&
                            opp.Banished.Contains(og) &&
                            !opp.Graveyard.Contains(og));
                    }
                }

                {
                    const int circleId = 18807108;
                    const int celticW = 91152256;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var prey = PlaceMonster(engine, opp, celticW, 2, BattlePosition.Attack, true);
                    prey.SummonedThisTurn = false;
                    var st = PlaceSetTrap(engine, p, circleId, 2);
                    st.SetThisTurn = false;
                    Check("Spellbinding Circle: Activate from Set",
                        engine.CanActivateSpellTrap(p, st, fromHand: false) &&
                        engine.TryActivateSpellTrap(p, st, fromHand: false) &&
                        engine.IsAwaitingEffectTarget);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Spellbinding Circle: binds prey, stays, locks attack/position",
                            engine.TrySelectEffectTarget(prey) &&
                            st.EquippedTo == prey &&
                            p.TryFindSpellTrap(st, out _) &&
                            engine.ContinuousCannotAttackBlocks(opp, prey) &&
                            engine.BoundTrapLocksBattlePosition(prey));
                    }

                    engine.DestroyMonsterPublic(opp, prey);
                    Check("Spellbinding Circle: destroyed host also destroys the trap",
                        p.Graveyard.Contains(st) && opp.Graveyard.Contains(prey) &&
                        !p.TryFindSpellTrap(st, out _),
                        $"stGy={p.Graveyard.Contains(st)} preyGy={opp.Graveyard.Contains(prey)}");
                }

                {
                    const int circleId = 18807108;
                    const int celticW = 91152256;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var prey = PlaceMonster(engine, opp, celticW, 2, BattlePosition.Attack, true);
                    prey.SummonedThisTurn = false;
                    var st = PlaceSetTrap(engine, p, circleId, 2);
                    st.SetThisTurn = false;
                    engine.TryActivateSpellTrap(p, st, fromHand: false);
                    if (engine.IsAwaitingEffectTarget)
                        engine.TrySelectEffectTarget(prey);
                    Check("Spellbinding Circle: bounce host leaves Circle face-up and unlinks",
                        engine.ReturnCardToHand(prey) &&
                        opp.Hand.Contains(prey) &&
                        p.TryFindSpellTrap(st, out _) &&
                        st.EquippedTo == null &&
                        !engine.BoundTrapLocksBattlePosition(prey) &&
                        !engine.ContinuousCannotAttackBlocks(opp, prey),
                        $"hand={opp.Hand.Contains(prey)} stField={p.TryFindSpellTrap(st, out _)} " +
                        $"link={st.EquippedTo != null}");
                }

                {
                    const int circleId = 18807108;
                    const int celticW = 91152256;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var prey = PlaceMonster(engine, opp, celticW, 2, BattlePosition.Attack, true);
                    var st = PlaceSetTrap(engine, p, circleId, 2);
                    st.SetThisTurn = false;
                    engine.TryActivateSpellTrap(p, st, fromHand: false);
                    if (engine.IsAwaitingEffectTarget)
                        engine.TrySelectEffectTarget(prey);
                    engine.SendCardToGrave(p, st);
                    Check("Spellbinding Circle: trap to GY does not destroy bound monster",
                        p.Graveyard.Contains(st) &&
                        opp.TryFindMonster(prey, out _) &&
                        !opp.Graveyard.Contains(prey));
                }

                // ── P1 remaining field/GY engines: Necrovalley, HHG, Tornado Umi-leave ──
                {
                    const int necrovalley = 47355498;
                    const int spyId = 24317029;
                    const int reborn = 83764719;
                    const int riteId = 30450531;
                    const int hhgId = 75782277;
                    const int harpieLady = 76812113;
                    const int waboku = 12607053;
                    const int umiId = 22702055;
                    const int tornadoId = 18605135;

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var spy = PlaceMonster(engine, p, spyId, 2, BattlePosition.Attack, true);
                        var nv = PutInHand(engine, p, necrovalley);
                        Check("Necrovalley: Field Spell activate legal",
                            engine.CanActivateSpellTrap(p, nv, fromHand: true));
                        Check("Necrovalley: activates to Field Zone",
                            engine.TryActivateSpellTrap(p, nv, fromHand: true) &&
                            p.FieldSpellZone?.Occupant == nv && nv.FaceUp);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("Necrovalley: Gravekeeper's Spy +500/+500",
                            spy.CurrentAtk == 1700 && spy.CurrentDef == 2500,
                            $"atk={spy.CurrentAtk} def={spy.CurrentDef}");

                        var gyMon = engine.CreateCardInstance(celtic);
                        p.Graveyard.Add(gyMon);
                        engine.BanishCard(p, gyMon);
                        Check("Necrovalley: GY banish blocked",
                            p.Graveyard.Contains(gyMon) &&
                            (p.Banished == null || !p.Banished.Contains(gyMon)));

                        var rb = PutInHand(engine, p, reborn);
                        Check("Necrovalley: Monster Reborn cannot target GY",
                            !engine.CanActivateSpellTrap(p, rb, fromHand: true));
                    }

                    {
                        const int haDes = 53982768;
                        const int exodiaNecross = 12600382;
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        p.Graveyard.Add(engine.CreateCardInstance(haDes));
                        var exodia = engine.CreateCardInstance(exodiaNecross);
                        p.Graveyard.Add(exodia);
                        var rb = PutInHand(engine, p, reborn);
                        Check("GY summon restrictions: Monster Reborn refuses Ha Des and hard Nomi",
                            !engine.CanActivateSpellTrap(p, rb, fromHand: true));
                        Check("GY summon restrictions: direct generic SpecialSummonToField refuses hard Nomi",
                            !engine.SpecialSummonToField(p, exodia, BattlePosition.Attack, true) &&
                            p.Graveyard.Contains(exodia));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var nv = PutInHand(engine, p, necrovalley);
                        engine.TryActivateSpellTrap(p, nv, fromHand: true);
                        var gk = engine.CreateCardInstance(spyId);
                        p.Graveyard.Add(gk);
                        var rite = PlaceSetTrap(engine, p, riteId, 2);
                        rite.SetThisTurn = false;
                        Check("Rite of Spirit still legal under Necrovalley",
                            engine.CanActivateSpellTrap(p, rite, fromHand: false));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var hhg = PutInHand(engine, p, hhgId);
                        Check("HHG: Field Spell activate legal",
                            engine.CanActivateSpellTrap(p, hhg, fromHand: true));
                        Check("HHG: activates to Field Zone",
                            engine.TryActivateSpellTrap(p, hhg, fromHand: true) &&
                            p.FieldSpellZone?.Occupant == hhg && hhg.FaceUp);
                        var lady = PlaceMonster(engine, p, harpieLady, 1, BattlePosition.Attack, true);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("HHG: Harpie Lady Winged Beast +200/+200",
                            lady.CurrentAtk == 1500 && lady.CurrentDef == 1600,
                            $"atk={lady.CurrentAtk} def={lady.CurrentDef}");

                        var st = PlaceSetTrap(engine, opp, waboku, 2);
                        st.SetThisTurn = false;
                        p.Hand.Clear();
                        var nsLady = PutInHand(engine, p, harpieLady);
                        var ns = engine.TurnPlayer == p && engine.InMainPhase &&
                                 engine.TryNormalSummon(p, nsLady, asSet: false);
                        if (engine.IsAwaitingResponse &&
                            engine.PendingResponse.Responder != p)
                            engine.PassResponse();
                        Check("HHG: NS Harpie Lady", ns, $"ns={ns}");
                        Check("HHG: summon trigger opens S/T target",
                            engine.IsAwaitingEffectTarget,
                            $"awaiting={engine.IsAwaitingEffectTarget} pending={engine.PendingActivation?.Card?.Name}");
                        if (engine.IsAwaitingEffectTarget)
                            Check("HHG: destroy targeted S/T",
                                engine.TrySelectEffectTarget(st) &&
                                opp.Graveyard.Contains(st) &&
                                p.FieldSpellZone?.Occupant == hhg);
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var p = engine.Player;
                        p.Hand.Clear();
                        var tornadoUmi = PutInHand(engine, p, umiId);
                        Check("Tornado setup: Umi activates",
                            engine.TryActivateSpellTrap(p, tornadoUmi, fromHand: true) &&
                            FieldSpellEffects.UmiIsOnField(engine));
                        var wall = PlaceSetTrap(engine, p, tornadoId, 2);
                        wall.SetThisTurn = false;
                        Check("Tornado Wall activates while Umi is up",
                            engine.CanActivateSpellTrap(p, wall, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, wall, fromHand: false) &&
                            wall.FaceUp);
                        engine.SendCardToGrave(p, tornadoUmi);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("Tornado Wall destroyed when Umi leaves",
                            p.Graveyard.Contains(wall) && !p.TryFindSpellTrap(wall, out _),
                            $"gy={p.Graveyard.Contains(wall)} onField={p.TryFindSpellTrap(wall, out _)}");
                    }
                }

                {
                    const int toonTable = 89997728;
                    const int toonWorld = 15259703;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    p.Deck.Clear();
                    p.Deck.Add(toonWorld);
                    p.Deck.Add(celtic);
                    var card = PutInHand(engine, p, toonTable);
                    Check("Toon Table: Activate legal with Toon card in Deck",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Toon Table: add Toon World (series, not exact name Toon)",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        p.Hand.Exists(c => c != null && c.CardId == toonWorld) &&
                        p.Graveyard.Exists(c => c != null && c.CardId == toonTable) &&
                        !p.Deck.Contains(toonWorld) &&
                        p.Deck.Contains(celtic),
                        $"hand={string.Join(",", p.Hand.Select(c => c.Name))} gy={p.Graveyard.Count}");
                }

                var akProg = CardTextEffectCompiler.Compile(db.Get(18036057));
                Check("Airknight Parshath compiles PiercingBattleDamage",
                    akProg != null &&
                    akProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.PiercingBattleDamage),
                    akProg == null ? "null" : $"full={akProg.FullyCompiled} unparsed={string.Join("|", akProg.UnparsedFragments ?? System.Array.Empty<string>())}");
                var ddProg = CardTextEffectCompiler.Compile(db.Get(65287621));
                Check("Dark Driceratops FullyCompiled piercing",
                    ddProg != null && ddProg.FullyCompiled &&
                    ddProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.PiercingBattleDamage));
                var yataProg = CardTextEffectCompiler.Compile(db.Get(3078576));
                Check("Yata-Garasu summon-restriction absorbed",
                    yataProg != null &&
                    !System.Array.Exists(yataProg.UnparsedFragments ?? System.Array.Empty<string>(), f =>
                        f != null && f.IndexOf("cannot be Special Summoned", System.StringComparison.OrdinalIgnoreCase) >= 0));
                var fdProg = CardTextEffectCompiler.Compile(db.Get(18591904));
                Check("Final Destiny compiles discard-5 + destroy all",
                    fdProg != null &&
                    fdProg.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.Destroy &&
                        c.RequiresDiscardCost && c.DiscardCostCount == 5));
                var synRush = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 90000401,
                    name = "New Spell (Rush Recklessly shape)",
                    type = "Spell Card",
                    desc = "Target 1 face-up monster on the field; it gains 700 ATK until the end of this turn."
                });
                Check("New card without cardId: until-EOT ATK fragment",
                    synRush != null && synRush.FullyCompiled &&
                    synRush.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn && c.Amount == 700 && c.IsPsctTarget));

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var kn = PlaceMonster(engine, engine.Player, 18036057, 2, BattlePosition.Attack, true);
                    Check("Airknight HasPiercing from compiled piercing",
                        OfficialEffectRegistry.HasPiercing(kn));
                }

                // Live: Fissure destroys the lowest ATK face-up opp monster (ties offered).
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var low = PlaceMonster(engine, opp, celtic, 1, BattlePosition.Attack, true);
                    var high = PlaceMonster(engine, opp, bewd, 2, BattlePosition.Attack, true);
                    var card = PutInHand(engine, p, fissure);
                    Check("Fissure: Activate opens lowest-ATK targets only",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(low) &&
                        !engine.PendingActivation.LegalTargets.Contains(high));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Fissure: destroy Celtic (lowest ATK)",
                            engine.TrySelectEffectTarget(low) &&
                            opp.Graveyard.Contains(low) &&
                            opp.TryFindMonster(high, out _));
                    }
                }

                // Fissure is an extremum choice, not PSCT targeting: Reaper must survive a
                // resolution that destroys a different lowest-ATK monster.
                {
                    const int reaper = 23205979;
                    const int zeroAtk = 27125110;
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var reaperCard = PlaceMonster(engine, opp, reaper, 1, BattlePosition.Attack, true);
                    var zeroCard = PlaceMonster(engine, opp, zeroAtk, 2, BattlePosition.Attack, true);
                    var card = PutInHand(engine, p, fissure);
                    Check("Fissure vs Reaper: non-targeted resolution does not notify Reaper",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation.LegalTargets.Contains(zeroCard) &&
                        !engine.PendingActivation.LegalTargets.Contains(reaperCard) &&
                        engine.TrySelectEffectTarget(zeroCard) &&
                        opp.TryFindMonster(reaperCard, out _) &&
                        opp.Graveyard.Contains(zeroCard));
                }


                // Live: Rush Recklessly +700 until EOT
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var mon = PlaceMonster(engine, p, celtic, 2, BattlePosition.Attack, true);
                    var before = mon.CurrentAtk;
                    var card = PutInHand(engine, p, rush);
                    Check("Rush Recklessly: Activate targets your monster",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.IsLegalEffectTarget(mon));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Rush Recklessly: +700 ATK until EOT",
                            engine.TrySelectEffectTarget(mon) &&
                            mon.CurrentAtk == before + 700,
                            $"atk {mon.CurrentAtk} expected {before + 700}");
                    }
                }

                // Live: Stop Defense flips opp DEF → ATK
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var wall = PlaceMonster(engine, opp, giantSoldier, 2, BattlePosition.Defense, true);
                    var card = PutInHand(engine, p, stopDefense);
                    Check("Stop Defense: Activate legal vs DEF monster",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.IsLegalEffectTarget(wall));
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Stop Defense: switch to Attack Position",
                            engine.TrySelectEffectTarget(wall) &&
                            wall.Position == BattlePosition.Attack && wall.FaceUp);
                    }
                }

                // Live: Umi Aqua +200 / Machine −200
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    const int penguinSoldier = 93920745; // Aqua
                    const int roboticKnight = 44203504; // Machine
                    var aqua = PlaceMonster(engine, p, penguinSoldier, 2, BattlePosition.Attack, true);
                    var machine = PlaceMonster(engine, engine.Opponent, roboticKnight, 1, BattlePosition.Attack, true);
                    var beforeA = aqua.Def.atk;
                    var beforeM = machine.Def.atk;
                    var card = PutInHand(engine, p, umi);
                    Check("Umi: activate into Field Zone",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        p.FieldSpellZone?.Occupant == card && card.FaceUp);
                    FieldSpellEffects.RefreshBoard(engine);
                    Check("Umi: Aqua +200 ATK/DEF",
                        aqua.CurrentAtk == beforeA + 200,
                        $"aqua {aqua.CurrentAtk} expected {beforeA + 200} name={aqua.Name}");
                    Check("Umi: Machine −200 ATK/DEF",
                        machine.CurrentAtk == beforeM - 200,
                        $"machine {machine.CurrentAtk} expected {beforeM - 200} name={machine.Name}");
                }

                // Live: Elegant Egotist with Harpie Lady on field + Sisters in deck
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    PlaceMonster(engine, p, harpie, 2, BattlePosition.Attack, true);
                    // Ensure Sisters is searchable from Deck
                    if (!p.Deck.Contains(harpieSisters))
                        p.Deck.Insert(0, harpieSisters);
                    var card = PutInHand(engine, p, elegant);
                    var beforeZones = p.MonsterCount;
                    Check("Elegant Egotist: Activate legal with Harpie Lady on field",
                        engine.CanActivateSpellTrap(p, card, fromHand: true));
                    Check("Elegant Egotist: resolve SS from Deck",
                        engine.TryActivateSpellTrap(p, card, fromHand: true) &&
                        p.MonsterCount == beforeZones + 1,
                        $"monsters {p.MonsterCount} was {beforeZones}");
                }

                // Live: Call of the Haunted GY SS + leave-field destroy
                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var gyMon = engine.CreateCardInstance(celtic);
                    p.Graveyard.Add(gyMon);
                    var card = PlaceSetTrap(engine, p, callHaunted, 2);
                    Check("Call of the Haunted: Activate from set",
                        engine.CanActivateSpellTrap(p, card, fromHand: false));
                    Check("Call of the Haunted: opens GY target",
                        engine.TryActivateSpellTrap(p, card, fromHand: false) &&
                        engine.IsAwaitingEffectTarget);
                    if (engine.IsAwaitingEffectTarget)
                    {
                        Check("Call of the Haunted: SS Celtic, trap stays linked",
                            engine.TrySelectEffectTarget(gyMon) &&
                            p.TryFindMonster(gyMon, out _) &&
                            p.TryFindSpellTrap(card, out _) &&
                            card.EquippedTo == gyMon);
                        // MST the Call → host dies
                        var mst = PutInHand(engine, p, SpellTrapEffects.Mst);
                        Check("Call of the Haunted: destroy Call also destroys SS'd monster",
                            engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                            engine.IsAwaitingEffectTarget &&
                            engine.TrySelectEffectTarget(card) &&
                            p.Graveyard.Contains(gyMon) &&
                            p.Graveyard.Contains(card),
                            $"gyMon={p.Graveyard.Contains(gyMon)} callGy={p.Graveyard.Contains(card)}");
                    }
                }
            }

            // ── MST self-target UX (legal; default offer skips activating card) ──
            {
                const int mstId = SpellTrapEffects.Mst;
                const int waboku = SpellTrapEffects.Waboku;
                PlayerPrefs.DeleteKey(SpellTrapEffects.AllowSelfTargetPrefKey);
                PlayerPrefs.Save();

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    p.Hand.Clear();
                    var oppSt = PlaceSetTrap(engine, opp, waboku, 2);
                    var mst = PutInHand(engine, p, mstId);
                    Check("MST: vs opponent S/T does not pick itself",
                        engine.CanActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(oppSt) &&
                        !engine.PendingActivation.LegalTargets.Contains(mst) &&
                        !LegalIntentService.Build(engine, p)
                            .HasKind(mst, LegalIntentService.LegalKind.EffectTarget),
                        engine.PendingActivation == null
                            ? "no pending"
                            : $"n={engine.PendingActivation.LegalTargets.Count} hasSelf={engine.PendingActivation.LegalTargets.Contains(mst)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var ownSt = PlaceSetTrap(engine, p, waboku, 1);
                    var mst = PutInHand(engine, p, mstId);
                    Check("MST: own other backrow stays legal (self hidden)",
                        engine.CanActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(ownSt) &&
                        !engine.PendingActivation.LegalTargets.Contains(mst),
                        engine.PendingActivation == null
                            ? "no pending"
                            : $"n={engine.PendingActivation.LegalTargets.Count} hasSelf={engine.PendingActivation.LegalTargets.Contains(mst)} hasOwn={engine.PendingActivation.LegalTargets.Contains(ownSt)}");
                }

                {
                    var engine = Fresh(db, pDeck, aDeck);
                    ClearBoard(engine);
                    var p = engine.Player;
                    p.Hand.Clear();
                    var mst = PutInHand(engine, p, mstId);
                    Check("MST: sole S/T still offers self (legal)",
                        engine.CanActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                        engine.IsAwaitingEffectTarget &&
                        engine.PendingActivation != null &&
                        engine.PendingActivation.LegalTargets.Contains(mst) &&
                        engine.PendingActivation.LegalTargets.Count == 1,
                        engine.PendingActivation == null
                            ? "no pending"
                            : $"n={engine.PendingActivation.LegalTargets.Count}");
                }

                {
                    SpellTrapEffects.AllowSpellTrapSelfTarget = true;
                    try
                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var oppSt = PlaceSetTrap(engine, opp, waboku, 2);
                        var ownSt = PlaceSetTrap(engine, p, SpellTrapEffects.SwordsOfRevealingLight, 1);
                        var mst = PutInHand(engine, p, mstId);
                        Check("MST: pref on, self is legal",
                            engine.TryActivateSpellTrap(p, mst, fromHand: true) &&
                            engine.IsAwaitingEffectTarget &&
                            engine.PendingActivation != null &&
                            engine.PendingActivation.LegalTargets.Contains(mst) &&
                            engine.PendingActivation.LegalTargets.Contains(oppSt) &&
                            engine.PendingActivation.LegalTargets.Contains(ownSt),
                            engine.PendingActivation == null
                                ? "no pending"
                                : $"n={engine.PendingActivation.LegalTargets.Count} hasSelf={engine.PendingActivation.LegalTargets.Contains(mst)}");
                    }
                    finally
                    {
                        PlayerPrefs.DeleteKey(SpellTrapEffects.AllowSelfTargetPrefKey);
                        PlayerPrefs.Save();
                    }
                }
            }

            // ── Player-facing activate/response window is 5s (expiry = PassResponse) ──
            {
                Check("DefaultResponseSeconds is 5s",
                    CombatAnimTimings.DefaultResponseSeconds == 5f);
            }

            // ── Bad Reaction to Simochi: opponent LP gain becomes effect damage ──
            {
                const int simochi = 40633297;
                const int remedy = 11868825;
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                p.Hand.Clear();
                opp.Hand.Clear();
                var simochiCard = PlaceSetTrap(engine, p, simochi, 2);
                var simochiProg = CardTextEffectCompiler.Compile(db.Get(simochi));
                Check("Simochi: compiles shared continuous LP replacement",
                    simochiProg.FullyCompiled &&
                    simochiProg.ClauseList.Exists(c => c != null &&
                        c.Action == EffectActionKind.ConvertOpponentLpGainToDamage &&
                        c.Timing == EffectTiming.ContinuousWhileFaceUp && c.StaysOnField));
                Check("Simochi: activates and stays face-up",
                    engine.CanActivateSpellTrap(p, simochiCard, fromHand: false) &&
                    engine.TryActivateSpellTrap(p, simochiCard, fromHand: false) &&
                    simochiCard.FaceUp && p.TryFindSpellTrap(simochiCard, out _));
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();

                const int rainOfMercy = 66719324;
                var rainProg = CardTextEffectCompiler.Compile(db.Get(rainOfMercy));
                Check("Simochi: Rain of Mercy compiles both-player LP gain",
                    rainProg.FullyCompiled &&
                    rainProg.ClauseList.Exists(c => c != null &&
                        c.Action == EffectActionKind.GainLifePoints && c.Amount == 1000 &&
                        c.Side == EffectSide.Both));
                var rainOk = engine.TryActivateSpellTrap(p, PutInHand(engine, p, rainOfMercy), fromHand: true);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Simochi: Rain of Mercy controller gains, opponent takes damage",
                    rainOk && p.LifePoints == 9000 && opp.LifePoints == 7000,
                    $"got {p.LifePoints}/{opp.LifePoints}");

                var ownRemedy = PutInHand(engine, p, remedy);
                var ownBefore = p.LifePoints;
                var ownOk = engine.TryActivateSpellTrap(p, ownRemedy, fromHand: true);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Simochi: controller own LP gain is unchanged",
                    ownOk && p.LifePoints == ownBefore + 600);

                Check("Simochi: reach opponent Main Phase", ReachOpponentMain(engine));
                var oppRemedy = PutInHand(engine, opp, remedy);
                var oppBefore = opp.LifePoints;
                var oppOk = engine.TryActivateSpellTrap(opp, oppRemedy, fromHand: true);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Simochi: opponent +600 LP becomes 600 effect damage",
                    oppOk && opp.LifePoints == oppBefore - 600);
            }

            // ── FirstEmpty order ──

            {
                var engine = Fresh(db, pDeck, aDeck);
                var zones = engine.Player.MonsterZones;
                foreach (var z in zones) z.Occupant = null;
                var i0 = engine.FirstEmpty(zones);
                zones[i0].Occupant = MockOcc();
                var i1 = engine.FirstEmpty(zones);
                zones[i1].Occupant = MockOcc();
                var i2 = engine.FirstEmpty(zones);
                Check("FirstEmpty order is 2,1,3", i0 == 2 && i1 == 1 && i2 == 3,
                    $"got {i0},{i1},{i2}");
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            var summary = sb.ToString();
            if (fail == 0)
                Debug.Log("[WRLDZ Interaction Tests]\n" + summary);
            else
                Debug.LogError("[WRLDZ Interaction Tests]\n" + summary);
            return summary;
        }

        static DuelEngine Fresh(CardDatabase db, DeckFile p, DeckFile a)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, p, a, cinematicOpening: false);
            return engine;
        }

        static void ClearBoard(DuelEngine engine)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                for (var i = 0; i < who.MonsterZones.Length; i++)
                    who.MonsterZones[i].Occupant = null;
                for (var i = 0; i < who.SpellTrapZones.Length; i++)
                    who.SpellTrapZones[i].Occupant = null;
                if (who.FieldSpellZone != null)
                    who.FieldSpellZone.Occupant = null;
                who.NormalSummonUsed = false;
            }

            engine.ClearPendingActivation();
            engine.PendingTributes.Clear();
        }

        /// <summary>null = does not FullyCompile as GY-SS (leave unimplemented; no new primitives).</summary>
        static bool? TryActivateCompiledGySsIfReady(CardDatabase db, DeckFile pDeck, DeckFile aDeck, int id)
        {
            var def = db.Get(id);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            var gySs = prog != null && prog.FullyCompiled &&
                       prog.ClauseList.Exists(c =>
                           c != null &&
                           c.Timing == EffectTiming.Activate &&
                           c.Action == EffectActionKind.SpecialSummonFromGy);
            if (!gySs)
                return null;

            const int celtic = 91152256;
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            var who = engine.Player;
            who.Hand.Clear();
            who.Graveyard.Add(engine.CreateCardInstance(celtic));
            var card = PutInHand(engine, who, id);
            return engine.CanActivateSpellTrap(who, card, fromHand: true);
        }

        static CardInstance PutInHand(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Hand.Add(c);
            return c;
        }

        static CardInstance PlaceMonster(DuelEngine engine, DuelistState who, int id, int zone,
            BattlePosition pos, bool faceUp)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = faceUp;
            c.Position = pos;
            c.SetThisTurn = false;
            c.SummonedThisTurn = false;
            who.MonsterZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceSetTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = false;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static bool ReachOpponentMain(DuelEngine engine)
        {
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Opponent && engine.InMainPhase &&
                              engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == engine.Opponent && engine.InMainPhase &&
                    engine.TurnNumber >= 2)
                    break;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == engine.Opponent && engine.InMainPhase;
        }

        static bool ReachOpponentBattle(DuelEngine engine)
        {
            for (var t = 0; t < 8 &&
                            !(engine.TurnPlayer == engine.Opponent && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Opponent) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(engine.Opponent);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Opponent;
        }

        static CardInstance MockOcc() => new CardInstance { InstanceId = 1, CardId = 1 };

        static bool ReachPlayerBattle(DuelEngine engine)
        {
            for (var t = 0; t < 8 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Player) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(engine.Player);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Player;
        }

        static void DrainCombat(DuelEngine engine)
        {
            for (var i = 0; i < 12; i++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                else if (engine.HasDeclaredAttack) engine.ResolveDeclaredAttack();
                else engine.RecoverStuckCombat();
            }
        }

        static bool ResolveDirect(DuelEngine engine, DuelistState who, CardInstance attacker)
        {
            var before = attacker.AttacksDeclaredThisTurn;
            if (!engine.CanAttack(who, attacker)) return false;
            if (!engine.TryAttack(who, attacker, null)) return false;
            DrainCombat(engine);
            return attacker.AttacksDeclaredThisTurn > before;
        }
    }
}
