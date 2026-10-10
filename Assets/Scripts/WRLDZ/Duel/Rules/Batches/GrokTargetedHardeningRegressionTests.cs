using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Hardening review (2026-10-04) of six cards compiled without review or tests between 2026-09-30 and 10-02
    /// (TargetedClassicsAtoms, RockSentenceAtoms): Remove Trap, Nobleman of Crossout, Deal of Phantom, Miracle Dig,
    /// Tornado Bird and Tribe-Infecting Virus. Each card is played through the real entry points against its official
    /// Lua: Remove Trap targets a face-up Trap Card on either field; Nobleman of Crossout destroys and banishes a face-down
    /// monster and, only for a Flip monster, banishes every copy from both Decks; Deal of Phantom gives 100 ATK for each
    /// monster in its controller's GY until the end of the turn; Miracle Dig needs 5 face-up banished monsters and returns
    /// exactly 3 to the GY; Tornado Bird's FLIP returns exactly 2 Spell/Trap Cards; Tribe-Infecting Virus lets its
    /// controller choose the discard and the Type, then destroys every face-up monster of that Type, even after the Virus
    /// itself has left the field (the Lua keeps the announced Type in the effect's label).
    /// </summary>
    public static class GrokTargetedHardeningRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int RemoveTrap = 51482758;
        const int NoblemanOfCrossout = 71044499;
        const int DealOfPhantom = 69122763;
        const int MiracleDig = 6343408;
        const int TornadoBird = 71283180;
        const int TribeInfectingVirus = 33184167;

        const int CallOfTheHaunted = 97077563;  // Continuous Trap
        const int MagicCylinder = 62279055;     // Normal Trap (needs an attack: never chained here)
        const int MessengerOfPeace = 44656491;  // Continuous Spell
        const int Fissure = 66788016;           // Normal Spell
        const int DarkHole = 53129443;          // Normal Spell
        const int PotOfGreed = 55144522;        // Normal Spell
        const int ManEaterBug = 54652250;       // Flip Effect Insect
        const int GiantSoldier = 13039848;      // Normal Rock 1300/2000
        const int Celtic = 91152256;            // Normal Warrior 1400/1200
        const int Gaia = 6368038;               // Normal Warrior 2300/2100
        const int MysticalElf = 15025844;       // Normal Spellcaster 800/2000
        const int FeralImp = 41392891;          // Normal Fiend 1300/1400
        const int SkullServant = 32274490;      // Normal Zombie 300/200
        const int Kuriboh = 40640057;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("GrokTargeted: " + name, ok, detail ?? "");

            RemoveTrapDuels(db, pDeck, aDeck, Check);
            NoblemanDuels(db, pDeck, aDeck, Check);
            DealOfPhantomDuels(db, pDeck, aDeck, Check);
            MiracleDigDuels(db, pDeck, aDeck, Check);
            TornadoBirdDuels(db, pDeck, aDeck, Check);
            TribeInfectingVirusDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── Remove Trap ─────────────────────────────

        static void RemoveTrapDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            Shape(db, Check, RemoveTrap, "Remove Trap: Destroy 1 face-up Trap Card on either field", c =>
                c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                c.Zone == EffectZoneFilter.FieldSpellTraps && c.Side == EffectSide.Both && c.RequiresTargetChoice &&
                c.IsPsctTarget && c.RequiresTargetFaceUp && c.TargetCardKind == "Trap" && c.TargetCount == 1);

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var oppTrap = PlaceFaceUpSt(e, o, CallOfTheHaunted, 0);
                var oppSet = PlaceSetTrap(e, o, MagicCylinder, 1);
                var oppSpell = PlaceFaceUpSt(e, o, MessengerOfPeace, 2);
                var myTrap = PlaceFaceUpSt(e, p, CallOfTheHaunted, 0);
                var rt = PutInHand(e, p, RemoveTrap);
                var activated = e.TryActivateSpellTrap(p, rt, fromHand: true);
                var legal = Legal(e);
                Check("Remove Trap: the prompt offers the face-up Traps of both fields only (not a Set card or a Spell)",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(oppTrap) &&
                    legal.Contains(myTrap),
                    $"activated={activated} awaiting={e.IsAwaitingEffectTarget} legal={Names(legal)}");
                var ok = Drive(e, oppTrap);
                Check("Remove Trap: destroys the targeted face-up Trap, and only it",
                    ok && Where(e, oppTrap) == "O.gy" && Where(e, oppSet) == "O.stz" && Where(e, oppSpell) == "O.stz" &&
                    Where(e, myTrap) == "P.stz" && Where(e, rt) == "P.gy",
                    $"ok={ok} trap={Where(e, oppTrap)} set={Where(e, oppSet)} spell={Where(e, oppSpell)} " +
                    $"mine={Where(e, myTrap)} rt={Where(e, rt)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceSetTrap(e, o, MagicCylinder, 0);
                PlaceFaceUpSt(e, o, MessengerOfPeace, 1);
                var rt = PutInHand(e, p, RemoveTrap);
                var can = e.CanActivateSpellTrap(p, rt, fromHand: true);
                Check("Remove Trap: with only a Set Trap and a face-up Spell on the field it cannot be activated",
                    !can, $"can={can}");
            }
        }

        // ───────────────────────────── Nobleman of Crossout ─────────────────────────────

        static void NoblemanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            Shape(db, Check, NoblemanOfCrossout, "Nobleman of Crossout: face-down target, Destroy + banish, Flip purge",
                c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                     c.Zone == EffectZoneFilter.FieldFaceDownMonsters && c.Side == EffectSide.Both &&
                     c.RequiresTargetChoice && c.IsPsctTarget && c.BanishIfDestroyed && c.PurgeDecksIfFlipMonster);

            // A Flip monster: destroyed and banished face-up; every copy leaves both Decks for the banished pile.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var mySet = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, false);
                o.Deck.Add(ManEaterBug);
                o.Deck.Add(ManEaterBug);
                p.Deck.Add(ManEaterBug);
                int Copies(DuelistState s) => s.Deck.Count(id => id == ManEaterBug);
                int BanishedCopies(DuelistState s) => s.Banished.Count(c => c.CardId == ManEaterBug && c.FaceUp);
                var oDeckBefore = o.Deck.Count;
                var pDeckBefore = p.Deck.Count;
                var oCopies = Copies(o);
                var pCopies = Copies(p);
                var nob = PutInHand(e, p, NoblemanOfCrossout);
                var activated = e.TryActivateSpellTrap(p, nob, fromHand: true);
                var legal = Legal(e);
                Check("Nobleman of Crossout: the prompt offers the face-down monsters of both fields only",
                    activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(bug) &&
                    legal.Contains(mySet),
                    $"activated={activated} legal={Names(legal)}");
                var ok = Drive(e, bug);
                Check("Nobleman of Crossout: the Set Flip monster is banished face-up (never the GY), the face-up one stays",
                    ok && Where(e, bug) == "O.ban" && bug.FaceUp && Where(e, celtic) == "O.mz" &&
                    Where(e, mySet) == "P.mz",
                    $"ok={ok} bug={Where(e, bug)} faceUp={bug.FaceUp} celtic={Where(e, celtic)} mine={Where(e, mySet)}");
                Check("Nobleman of Crossout: a Flip monster's copies leave both Decks for the banished piles",
                    oCopies >= 2 && pCopies >= 1 && Copies(o) == 0 && Copies(p) == 0 &&
                    o.Deck.Count == oDeckBefore - oCopies && p.Deck.Count == pDeckBefore - pCopies &&
                    BanishedCopies(o) == oCopies + 1 && BanishedCopies(p) == pCopies,
                    $"o {oCopies}->{Copies(o)} ban={BanishedCopies(o)} p {pCopies}->{Copies(p)} ban={BanishedCopies(p)}");
            }

            // Not a Flip monster: destroyed and banished; the Decks keep their copies.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var set = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, false);
                o.Deck.Add(GiantSoldier);
                p.Deck.Add(GiantSoldier);
                var oCopies = o.Deck.Count(id => id == GiantSoldier);
                var pCopies = p.Deck.Count(id => id == GiantSoldier);
                var nob = PutInHand(e, p, NoblemanOfCrossout);
                var activated = e.TryActivateSpellTrap(p, nob, fromHand: true);
                if (e.IsAwaitingEffectTarget || e.IsAwaitingResponse) Drive(e, set);
                Check("Nobleman of Crossout: a non-Flip monster is banished, both Decks keep its copies",
                    activated && Where(e, set) == "O.ban" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks &&
                    o.Deck.Count(id => id == GiantSoldier) == oCopies && p.Deck.Count(id => id == GiantSoldier) == pCopies,
                    $"activated={activated} set={Where(e, set)} o={oCopies} p={pCopies}");
            }

            // No face-down monster: it cannot be activated.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var nob = PutInHand(e, e.Player, NoblemanOfCrossout);
                var can = e.CanActivateSpellTrap(e.Player, nob, fromHand: true);
                Check("Nobleman of Crossout: with no face-down monster it cannot be activated", !can, $"can={can}");
            }
        }

        // ───────────────────────────── Deal of Phantom ─────────────────────────────

        static void DealOfPhantomDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            Shape(db, Check, DealOfPhantom, "Deal of Phantom: face-up target gains 100 per monster in your GY",
                c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.ModifyTargetUntilEndOfTurn &&
                     c.Zone == EffectZoneFilter.FieldAnyMonster && c.Side == EffectSide.Both && c.RequiresTargetChoice &&
                     c.RequiresTargetFaceUp && c.Amount == 100 && c.CountSource == TargetedClassicsAtoms.MonstersInYourGy);

            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            foreach (var id in new[] { GiantSoldier, FeralImp, SkullServant })
                p.Graveyard.Add(e.CreateCardInstance(id));
            p.Graveyard.Add(e.CreateCardInstance(PotOfGreed));          // a Spell does not count
            o.Graveyard.Add(e.CreateCardInstance(Gaia));                // the opponent's GY does not count
            o.Graveyard.Add(e.CreateCardInstance(MysticalElf));
            var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var theirs = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var set = PlaceMonster(e, o, GiantSoldier, 1, BattlePosition.Defense, false);
            var dop = PlaceSetTrap(e, p, DealOfPhantom, 0);
            var activated = e.TryActivateSpellTrap(p, dop, fromHand: false);
            var legal = Legal(e);
            Check("Deal of Phantom: the prompt offers the face-up monsters of both fields (not a Set one)",
                activated && e.IsAwaitingEffectTarget && legal.Count == 2 && legal.Contains(mine) &&
                legal.Contains(theirs) && !legal.Contains(set),
                $"activated={activated} legal={Names(legal)}");
            var ok = Drive(e, mine);
            Check("Deal of Phantom: 3 monsters in your GY → the target gains 300 ATK; the other monster is unchanged",
                ok && mine.CurrentAtk == 1700 && theirs.CurrentAtk == 2300,
                $"ok={ok} mine={mine.CurrentAtk} theirs={theirs.CurrentAtk}");
            p.Graveyard.Add(e.CreateCardInstance(Kuriboh));
            Check("Deal of Phantom: the boost is fixed at resolution (a later GY monster adds nothing)",
                mine.CurrentAtk == 1700, $"atk={mine.CurrentAtk}");
            e.TryEndTurnSafe(p);
            if (e.IsAwaitingResponse) e.PassResponse();
            Check("Deal of Phantom: the boost ends at the end of the turn",
                mine.CurrentAtk == 1400, $"atk={mine.CurrentAtk} turn={e.TurnNumber}");
        }

        // ───────────────────────────── Miracle Dig ─────────────────────────────

        static void MiracleDigDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            Shape(db, Check, MiracleDig, "Miracle Dig: 5+ face-up banished monsters, exactly 3 return to the GY",
                c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.ReturnBanishedTargetToGy &&
                     c.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters && c.RequiresTargetChoice &&
                     c.TargetCount == 3 && !c.TargetUpTo && c.ConditionAmount == 5 &&
                     c.ActivationCondition == TargetedClassicsRuntime.YourFaceUpBanishedMonstersAtLeast);

            CardInstance Banish(DuelEngine e, DuelistState who, int id, bool faceUp)
            {
                var c = e.CreateCardInstance(id);
                c.FaceUp = faceUp;
                who.Banished.Add(c);
                return c;
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var up = new[] { Celtic, GiantSoldier, Gaia, MysticalElf, FeralImp }
                    .Select(id => Banish(e, p, id, true)).ToList();
                var down = Banish(e, p, SkullServant, false);
                var spell = Banish(e, p, PotOfGreed, true);
                var oppBan = Banish(e, o, Kuriboh, true);
                var dig = PutInHand(e, p, MiracleDig);
                var activated = e.TryActivateSpellTrap(p, dig, fromHand: true);
                var pa = e.PendingActivation;
                var legal = Legal(e);
                Check("Miracle Dig: the prompt takes exactly 3 of your 5 face-up banished monsters",
                    activated && pa != null && pa.MultiTarget && pa.MultiStepRemaining == 3 && !pa.MultiStepUpTo &&
                    legal.Count == 5 && up.All(legal.Contains) && !legal.Contains(down) && !legal.Contains(spell) &&
                    !legal.Contains(oppBan),
                    pa == null
                        ? $"activated={activated} no prompt"
                        : $"multi={pa.MultiTarget} left={pa.MultiStepRemaining} upTo={pa.MultiStepUpTo} legal={Names(legal)}");
                var picks = up.Take(3).ToList();
                var picked = 0;
                for (var i = 0; i < 20; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var next = picks.FirstOrDefault(c => Legal(e).Contains(c));
                        if (next == null || !e.TrySelectEffectTarget(next)) { e.CancelEffectTargeting(); continue; }
                        picks.Remove(next);
                        picked++;
                        continue;
                    }

                    if (e.IsAwaitingResponse) { e.PassResponse(); continue; }
                    break;
                }

                Check("Miracle Dig: the 3 chosen monsters go to your GY; the rest stay banished",
                    picked == 3 && up.Take(3).All(c => Where(e, c) == "P.gy") &&
                    up.Skip(3).All(c => Where(e, c) == "P.ban") && Where(e, down) == "P.ban" &&
                    Where(e, spell) == "P.ban" && Where(e, oppBan) == "O.ban" && !e.Chain.HasLinks,
                    $"picked={picked} up={string.Join(",", up.Select(c => Where(e, c)))} down={Where(e, down)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                foreach (var id in new[] { Celtic, GiantSoldier, Gaia, MysticalElf })
                    Banish(e, p, id, true);
                Banish(e, p, FeralImp, false);
                Banish(e, e.Opponent, Kuriboh, true);
                var dig = PutInHand(e, p, MiracleDig);
                var can = e.CanActivateSpellTrap(p, dig, fromHand: true);
                Check("Miracle Dig: 4 face-up + 1 face-down banished monsters are not enough", !can, $"can={can}");
            }
        }

        // ───────────────────────────── Tornado Bird ─────────────────────────────

        static void TornadoBirdDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            Shape(db, Check, TornadoBird, "Tornado Bird: FLIP, exactly 2 Spell/Trap Cards on the field to the hand",
                c => c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.ReturnToHand &&
                     c.Zone == EffectZoneFilter.FieldSpellTraps && c.Side == EffectSide.Both && c.RequiresTargetChoice &&
                     c.TargetCount == 2 && !c.TargetUpTo && !c.RequiresTargetFaceUp);

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bird = PlaceMonster(e, p, TornadoBird, 0, BattlePosition.Defense, false);
                var oppSet = PlaceSetTrap(e, o, Fissure, 0);
                var oppUp = PlaceFaceUpSt(e, o, MessengerOfPeace, 1);
                var mySet = PlaceSetTrap(e, p, DarkHole, 0);
                var flipped = e.TryFlipSummon(p, bird) && bird.FaceUp;
                var pa = e.PendingActivation;
                var legal = Legal(e);
                Check("Tornado Bird: the FLIP prompt takes exactly 2 of the Spell/Trap Cards (Set or face-up, both fields)",
                    flipped && pa != null && pa.TargetPicksRemaining == 2 && !pa.TargetPicksAreUpTo && legal.Count == 3 &&
                    legal.Contains(oppSet) && legal.Contains(oppUp) && legal.Contains(mySet),
                    pa == null
                        ? $"flipped={flipped} no prompt"
                        : $"left={pa.TargetPicksRemaining} upTo={pa.TargetPicksAreUpTo} legal={Names(legal)}");
                var picks = new List<CardInstance> { oppSet, oppUp };
                for (var i = 0; i < 20; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var next = picks.FirstOrDefault(c => Legal(e).Contains(c));
                        if (next == null || !e.TrySelectEffectTarget(next)) { e.CancelEffectTargeting(); continue; }
                        picks.Remove(next);
                        continue;
                    }

                    if (e.IsAwaitingResponse) { e.PassResponse(); continue; }
                    break;
                }

                Check("Tornado Bird: both chosen cards return to their owner's hand; the third stays",
                    picks.Count == 0 && Where(e, oppSet) == "O.hand" && Where(e, oppUp) == "O.hand" &&
                    Where(e, mySet) == "P.stz" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"left={picks.Count} set={Where(e, oppSet)} up={Where(e, oppUp)} mine={Where(e, mySet)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var bird = PlaceMonster(e, p, TornadoBird, 0, BattlePosition.Defense, false);
                var lone = PlaceSetTrap(e, e.Opponent, Fissure, 0);
                var flipped = e.TryFlipSummon(p, bird) && bird.FaceUp;
                for (var i = 0; i < 10 && (e.IsAwaitingEffectTarget || e.IsAwaitingResponse); i++)
                {
                    if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                    else e.PassResponse();
                }

                Check("Tornado Bird: with only 1 Spell/Trap Card on the field nothing returns",
                    flipped && Where(e, lone) == "O.stz" && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"flipped={flipped} lone={Where(e, lone)}");
            }
        }

        // ───────────────────────────── Tribe-Infecting Virus ─────────────────────────────

        static void TribeInfectingVirusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(TribeInfectingVirus));
                var cl = prog?.ClauseList?.Where(c => c != null).ToList() ?? new List<EffectClause>();
                var st = CardEffectStatus.Classify(db.Get(TribeInfectingVirus));
                Check("Tribe-Infecting Virus: discard cost + declare a Type, then destroy face-up monsters of it",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && cl.Count == 2 &&
                    cl[0].Action == EffectActionKind.DeclareMonsterType && cl[0].RequiresDiscardCost &&
                    cl[0].DiscardCostCount == 1 && cl[0].ChoiceDoesNotTarget && !cl[0].OncePerTurn &&
                    cl[1].Action == EffectActionKind.Destroy && cl[1].RaceFilterIsDeclared &&
                    cl[1].Side == EffectSide.Both && !cl[1].RequiresTargetChoice,
                    $"status={st} {Describe(prog)}");
            }

            // The player chooses the discard and the Type; every face-up Warrior on both fields is destroyed.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var virus = PlaceMonster(e, p, TribeInfectingVirus, 0, BattlePosition.Attack, true);
                var myWarrior = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
                var setWarrior = PlaceMonster(e, o, Celtic, 2, BattlePosition.Defense, false);
                var keep = PutInHand(e, p, PotOfGreed);
                var discard = PutInHand(e, p, Kuriboh);
                var spare = PutInHand(e, p, FeralImp);
                var activated = e.TryActivateSpellTrap(p, virus, fromHand: false);
                var first = Legal(e);
                var discardOffered = activated && e.IsAwaitingEffectTarget && first.Count == 3 &&
                                     first.Contains(keep) && first.Contains(discard) && first.Contains(spare);
                var discarded = discardOffered && e.TrySelectEffectTarget(discard);
                var tokens = Legal(e);
                var typeOffered = discarded && e.IsAwaitingEffectTarget && tokens.Count > 1 &&
                                  tokens.All(TypeDeclarationRuntime.IsTypeToken);
                Check("Tribe-Infecting Virus: the player chooses the card to discard, then declares a Type",
                    discardOffered && discarded && typeOffered && Where(e, discard) == "P.gy" &&
                    Where(e, keep) == "P.hand",
                    $"activated={activated} first={Names(first)} discarded={discarded} tokens={Names(tokens)} " +
                    $"discard={Where(e, discard)}");
                var warrior = tokens.FirstOrDefault(t => t?.Def?.race == "Warrior");
                var declared = warrior != null && e.TrySelectEffectTarget(warrior);
                PassAll(e);
                Check("Tribe-Infecting Virus: declaring Warrior destroys every face-up Warrior on both fields, only them",
                    declared && Where(e, myWarrior) == "P.gy" && Where(e, gaia) == "O.gy" && Where(e, elf) == "O.mz" &&
                    Where(e, setWarrior) == "O.mz" && Where(e, virus) == "P.mz" && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget,
                    $"declared={declared} mine={Where(e, myWarrior)} gaia={Where(e, gaia)} elf={Where(e, elf)} " +
                    $"set={Where(e, setWarrior)} virus={Where(e, virus)}");
                var again = e.CanActivateSpellTrap(p, virus, fromHand: false);
                Check("Tribe-Infecting Virus: not once per turn — it can be activated again while a card is in hand",
                    again, $"again={again} hand={Names(p.Hand)}");
            }

            // An empty hand: no discard cost, no activation.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var virus = PlaceMonster(e, e.Player, TribeInfectingVirus, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Gaia, 0, BattlePosition.Attack, true);
                var can = e.CanActivateSpellTrap(e.Player, virus, fromHand: false);
                Check("Tribe-Infecting Virus: with an empty hand it cannot be activated", !can, $"can={can}");
            }

            // The Virus leaves the field before its effect resolves: the declared Type still applies (Lua label).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var virus = e.CreateCardInstance(TribeInfectingVirus);
                virus.FaceUp = true;
                p.Graveyard.Add(virus);
                var token = TypeDeclarationRuntime.MonsterTypeTokens(e).FirstOrDefault(t => t?.Def?.race == "Warrior");
                TypeDeclarationRuntime.RecordDeclaredType(e, virus, token);
                var declared = TypeDeclarationRuntime.DeclaredTypeOf(virus);
                Check("Tribe-Infecting Virus: a Type declared by a monster no longer on the field is still recorded",
                    token != null && declared == "Warrior", $"token={token?.Def?.race ?? "null"} declared={declared ?? "null"}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static void Shape(CardDatabase db, Chk Check, int id, string label, Func<EffectClause, bool> clause)
        {
            var def = db.Get(id);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            var st = def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented;
            var cl = prog?.ClauseList?.Where(c => c != null).ToList() ?? new List<EffectClause>();
            Check(label + " (FullyCompiled, Implemented, one clause)",
                prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && cl.Count == 1 &&
                clause(cl[0]),
                $"status={st} {Describe(prog)}");
        }

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static CardInstance PlaceFaceUpSt(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 20 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++)
                e.PassResponse();
        }

        /// <summary>Every target prompt picks <paramref name="target"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, CardInstance target)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var ok = e.TrySelectEffectTarget(target);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked && !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse && !e.Chain.HasLinks;
        }

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null).Select(EffectVocabulary.Describe));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] " +
                   $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
