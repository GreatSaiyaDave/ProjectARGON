using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FlipMassAndOpponentPicks batch: The Spell Absorbing Life, Dark Cat with White Tail, Huge Revolution, Magical
    /// Merchant, Pixie Knight, Pineapple Blast, Dimension Jar. Compile shapes through the real compiler, near misses that
    /// stay fail-closed, and mini-duels through the engine entry points (TryActivateSpellTrap, TryFlipSummon,
    /// TryNormalSummon, TryAttack, TrySelectEffectTarget, the summon response windows). Picks made by the player who did
    /// NOT activate the effect are answered by the human through the card picker and by the AI on its own.
    /// Turn 1 is always the Player's, turn 2 the AI's. Runtime: <see cref="FlipMassAndOpponentPicksRuntime"/>.
    /// </summary>
    public static class FlipMassAndOpponentPicksRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SpellAbsorbingLife = 99517131;
        const int DarkCat = 8634636;
        const int HugeRevolution = 65396880;
        const int MagicalMerchant = 32362575;
        const int PixieKnight = 35429292;
        const int PineappleBlast = 90669991;
        const int DimensionJar = 73414375;
        const int Ceasefire = 36468556;

        const int PeopleRunningAbout = 12143771;
        const int OppressedPeople = 58538870;
        const int UnitedResistance = 85936485;

        const int ManEaterBug = 54652250;     // FLIP: destroy 1 monster — must NOT activate when flipped by the Trap
        const int Kuriboh = 40640057;         // Effect Monster 300/200
        const int Celtic = 91152256;          // Normal 1400/1200
        const int GiantSoldier = 13039848;    // Normal 1300/2000
        const int Hitotsu = 76184692;         // Normal 1200/1000
        const int DarkMagician = 46986414;    // Normal 2500/2100
        const int PotOfGreed = 55144522;
        const int PrematureBurial = 70828912;
        const int Mst = 5318639;
        const int TrapHole = 4206964;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("FlipMassAndOpponentPicks: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            SpellAbsorbingLifeDuels(db, pDeck, aDeck, Check);
            DarkCatDuels(db, pDeck, aDeck, Check);
            HugeRevolutionDuels(db, pDeck, aDeck, Check);
            MagicalMerchantDuels(db, pDeck, aDeck, Check);
            PixieKnightDuels(db, pDeck, aDeck, Check);
            PineappleBlastDuels(db, pDeck, aDeck, Check);
            DimensionJarDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A fresh duel with empty boards, hands, GYs and banished piles; the player is in their Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            Empty(e);
            return e;
        }

        /// <summary>The AI's turn 2 Main Phase 1 with empty boards, hands, GYs and banished piles.</summary>
        static DuelEngine SetupOpponentTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ReachOpponentMain(e);
            if (e.IsAwaitingResponse) e.PassResponse();
            Empty(e);
            return e;
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static CardInstance SetTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var t = PlaceSetTrap(e, who, id, zone);
            t.SetThisTurn = false;
            return t;
        }

        static CardInstance FaceDown(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);
            m.SetThisTurn = false;
            m.SummonedThisTurn = false;
            return m;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt (Cancel when none); pass every window.</summary>
        static int Drive(DuelEngine e, Func<CardInstance, bool> want, List<List<CardInstance>> offers = null)
        {
            var picked = 0;
            for (var i = 0; i < 30; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var legal = Legal(e);
                    offers?.Add(legal);
                    var pick = legal.FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    if (ok) picked++;
                    else e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

        static void Settle(DuelEngine e)
        {
            Drive(e, _ => false);
            DrainCombat(e);
            Drive(e, _ => false);
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.Banished.Contains(c)) at.Add(tag + ".ban");
                if (who.TryFindMonster(c, out _)) at.Add(tag + ".mz");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz");
            }

            return string.Join("+", at);
        }

        static bool InHandById(DuelistState who, int id) => who.Hand.Any(h => h != null && h.CardId == id);

        static CardDef Variant(CardDatabase db, int id, string desc)
        {
            var d = db.Get(id);
            return new CardDef
            {
                id = 980000000 + (id % 1000000),
                name = d.name + " (near miss)",
                type = d.type,
                frameType = d.frameType,
                race = d.race,
                attribute = d.attribute,
                level = d.level,
                atk = d.atk,
                def = d.def,
                archetype = d.archetype,
                desc = desc
            };
        }

        static bool NotCompiled(CardDef def)
        {
            var prog = CardTextEffectCompiler.Compile(def);
            return prog == null || !prog.FullyCompiled;
        }

        static EffectClause Only(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var sal = CardTextEffectCompiler.Compile(db.Get(SpellAbsorbingLife));
            Check("The Spell Absorbing Life compiles to flip-all (no FLIP effects) then +400 per face-up Effect Monster",
                sal != null && sal.FullyCompiled && sal.ClauseList.Count == 2 &&
                sal.ClauseList[0].Action == EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects &&
                sal.ClauseList[0].SuppressFlipEffects && sal.ClauseList[0].ActivationCondition == "AnyMonsterOnField" &&
                sal.ClauseList[1].Action == EffectActionKind.GainLifePoints && sal.ClauseList[1].Amount == 400 &&
                sal.ClauseList[1].ScaleAmountByFaceUpEffectMonsters &&
                CardEffectStatus.Classify(db.Get(SpellAbsorbingLife)) == CardEffectStatusKind.Implemented,
                sal == null ? "null" : string.Join(" | ", sal.ClauseList.Select(c => $"{c.Action} {c.Amount}")));

            var cat = Only(db, DarkCat, out var catProg);
            Check("Dark Cat with White Tail compiles to a FLIP 2-opponent + 1-own return to the hand",
                catProg != null && catProg.FullyCompiled && cat != null && cat.Timing == EffectTiming.Flip &&
                cat.Action == EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands &&
                cat.OpponentTargetCount == 2 && cat.ControllerTargetCount == 1 && !cat.RequiresTargetChoice,
                cat == null ? "null" : $"{cat.Action} opp={cat.OpponentTargetCount} own={cat.ControllerTargetCount}");

            var rev = Only(db, HugeRevolution, out var revProg);
            Check("Huge Revolution compiles to the named Main Phase condition + hand-to-GY and destroy-all",
                revProg != null && revProg.FullyCompiled && rev != null && rev.Timing == EffectTiming.Activate &&
                rev.Action == EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards &&
                rev.ActivationCondition == FlipMassAndOpponentPicksRuntime.YourMainPhaseWithNamedFaceUp &&
                rev.ConditionName == "People Running About|Oppressed People|United Resistance",
                rev == null ? "null" : $"{rev.Action} cond={rev.ActivationCondition} names={rev.ConditionName}");

            var merchant = Only(db, MagicalMerchant, out var merchantProg);
            Check("Magical Merchant compiles to a FLIP excavate-until-Spell/Trap",
                merchantProg != null && merchantProg.FullyCompiled && merchant != null &&
                merchant.Timing == EffectTiming.Flip &&
                merchant.Action == EffectActionKind.ExcavateUntilSpellTrapAddItSendRestToGy,
                merchant == null ? "null" : $"{merchant.Timing} {merchant.Action}");

            var pixie = Only(db, PixieKnight, out var pixieProg);
            Check("Pixie Knight compiles to a by-battle GY trigger whose pick the opponent makes",
                pixieProg != null && pixieProg.FullyCompiled && pixie != null &&
                pixie.Timing == EffectTiming.SentFromFieldToGy && pixie.RequiresThisDestroyedByBattle &&
                !pixie.IsOptional && pixie.Action == EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck,
                pixie == null ? "null" : $"{pixie.Timing} {pixie.Action} battle={pixie.RequiresThisDestroyedByBattle}");

            var pine = Only(db, PineappleBlast, out var pineProg);
            Check("Pineapple Blast compiles to an own-Normal-Summon answer with the opponent-chooses destroy",
                pineProg != null && pineProg.FullyCompiled && pine != null &&
                pine.Timing == EffectTiming.OpponentNormalOrFlipSummon && pine.AnswersControllerSummon &&
                !pine.AnswersSpecialSummon &&
                pine.ActivationCondition == FlipMassAndOpponentPicksRuntime.YourNormalSummonOpponentControlsMore &&
                pine.Action == EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses,
                pine == null ? "null" : $"{pine.Timing} {pine.Action} cond={pine.ActivationCondition}");

            var jar = Only(db, DimensionJar, out var jarProg);
            Check("Dimension Jar compiles to a FLIP each-player banish of up to 3 monsters",
                jarProg != null && jarProg.FullyCompiled && jar != null && jar.Timing == EffectTiming.Flip &&
                jar.Action == EffectActionKind.EachPlayerMayBanishUpToMonstersFromOpponentGy && jar.Amount == 3,
                jar == null ? "null" : $"{jar.Action} {jar.Amount}");

            // Near misses: look-alike sentences stay fail-closed; Ceasefire keeps its own (damage) shape.
            var cease = CardTextEffectCompiler.Compile(db.Get(Ceasefire));
            Check("Ceasefire keeps its burn (the LP-gain branch is The Spell Absorbing Life's only)",
                cease != null && cease.FullyCompiled &&
                cease.ClauseList.Exists(c => c.Action == EffectActionKind.InflictDamageToOpponent &&
                                             c.ScaleAmountByFaceUpEffectMonsters) &&
                !cease.ClauseList.Exists(c => c.Action == EffectActionKind.GainLifePoints),
                cease == null ? "null" : string.Join(" | ", cease.ClauseList.Select(c => c.Action.ToString())));
            Check("near miss: Spell Absorbing Life without the 'not activated' sentence stays not FullyCompiled",
                NotCompiled(Variant(db, SpellAbsorbingLife,
                    "Flip all face-down Defense Position Monster Cards on the field face-up. Increase your Life Points " +
                    "by 400 points for each Effect Monster Card on the field.")));
            Check("near miss: Dark Cat returning to the controller's hand (not owners') stays not FullyCompiled",
                NotCompiled(Variant(db, DarkCat,
                    "FLIP: Return 2 Monster Cards on your opponent's side of the field and 1 Monster Card on your side " +
                    "of the field to your hand.")));
            Check("near miss: Huge Revolution destroying only monsters stays not FullyCompiled",
                NotCompiled(Variant(db, HugeRevolution,
                    "This card can only be activated during your Main Phase when \"People Running About\", \"Oppressed " +
                    "People\", and \"United Resistance\" are face-up on your side of the field. Send all cards in your " +
                    "opponent's hand to the Graveyard and destroy all monsters your opponent controls.")));
            Check("near miss: excavate until a monster stays not FullyCompiled",
                NotCompiled(Variant(db, MagicalMerchant,
                    "FLIP: Excavate cards from the top of your Deck until you excavate a monster, then add that card to " +
                    "your hand, also send the remaining cards to the GY.")));
            Check("near miss: Pixie Knight where YOU select stays not FullyCompiled",
                NotCompiled(Variant(db, PixieKnight,
                    "When this card is sent to the Graveyard as a result of battle, select 1 Spell Card from your " +
                    "Graveyard, and place that card on the top of your Deck.")));
            Check("near miss: Pineapple Blast answering the opponent's Normal Summon stays not FullyCompiled",
                NotCompiled(Variant(db, PineappleBlast,
                    "When your opponent Normal Summons a monster, if your opponent controls more monsters than you do: " +
                    "Destroy your opponent's monsters so that your opponent controls the same number of monsters as you " +
                    "(your opponent chooses which of their monsters are destroyed).")));
            // Rules choice (recorded): Dimension Jar's print says "monsters"; the Lua filter (Card.IsAbleToRemove) takes
            // any card. ARGON follows the printed text (ygo-gamedev skill: printed text first, Lua is a linter oracle),
            // so only monsters are offered, and a "cards" print does not reuse this atom.
            Check("near miss: Dimension Jar banishing any cards stays not FullyCompiled",
                NotCompiled(Variant(db, DimensionJar,
                    "FLIP: Both players can remove from play up to 3 cards from their opponent's Graveyard.")));
        }

        // ───────────────────────────── The Spell Absorbing Life ─────────────────────────────

        static void SpellAbsorbingLifeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Every face-down monster is flipped face-up with no FLIP effect (Man-Eater Bug destroys nothing); the LP
            // gain counts the face-up Effect Monsters on both fields after the flips: Man-Eater Bug + Kuriboh = 800.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var bug = FaceDown(e, p, ManEaterBug, 0);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var kuriboh = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
                var soldier = FaceDown(e, o, GiantSoldier, 1);
                var sal = SetTrap(e, p, SpellAbsorbingLife, 0);
                var lp = p.LifePoints;
                var can = e.CanActivateSpellTrap(p, sal, fromHand: false);
                var act = e.TryActivateSpellTrap(p, sal, fromHand: false);
                Settle(e);
                Check("The Spell Absorbing Life: flips with no FLIP effect, +400 per face-up Effect Monster (both fields)",
                    can && act && bug.FaceUp && soldier.FaceUp && bug.Position == BattlePosition.Defense &&
                    soldier.Position == BattlePosition.Defense && p.LifePoints == lp + 800 &&
                    o.TryFindMonster(kuriboh, out _) && o.TryFindMonster(soldier, out _) &&
                    p.TryFindMonster(celtic, out _) && p.Graveyard.Contains(sal),
                    $"can={can} act={act} bug={bug.FaceUp}/{bug.Position} soldier={soldier.FaceUp}/{soldier.Position} " +
                    $"lp={lp}->{p.LifePoints} kuriboh={Where(e, kuriboh)} sal={Where(e, sal)}");
            }

            // Lua s.target: no monster on the field — it cannot be activated.
            {
                var e = Setup(db, pDeck, aDeck);
                var sal = SetTrap(e, e.Player, SpellAbsorbingLife, 0);
                Check("The Spell Absorbing Life: cannot be activated with no monster on the field",
                    !e.CanActivateSpellTrap(e.Player, sal, fromHand: false));
            }
        }

        // ───────────────────────────── Dark Cat with White Tail ─────────────────────────────

        static void DarkCatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human Flip Summon: the player targets 2 of the opponent's 3 monsters and 1 of their own 2 (Dark Cat
            // counts); the three return to their owners' hands together.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var cat = FaceDown(e, p, DarkCat, 0);
                var mine = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var o1 = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var o2 = PlaceMonster(e, o, Hitotsu, 1, BattlePosition.Attack, true);
                var o3 = FaceDown(e, o, GiantSoldier, 2);
                var flipped = e.TryFlipSummon(p, cat);
                var offers = new List<List<CardInstance>>();
                var picks = Drive(e, c => c == o1 || c == o3 || c == mine, offers);
                Settle(e);
                var firstOffer = offers.FirstOrDefault() ?? new List<CardInstance>();
                var ownOffer = offers.LastOrDefault() ?? new List<CardInstance>();
                Check("Dark Cat with White Tail: 2 opponent targets + 1 own target return to their owners' hands",
                    flipped && picks == 3 && firstOffer.Count == 3 && firstOffer.Contains(o3) &&
                    ownOffer.Contains(cat) && ownOffer.Contains(mine) &&
                    o.Hand.Contains(o1) && o.Hand.Contains(o3) && p.Hand.Contains(mine) &&
                    o.TryFindMonster(o2, out _) && p.TryFindMonster(cat, out _),
                    $"flipped={flipped} picks={picks} offers={string.Join(" / ", offers.Select(l => l.Count))} " +
                    $"o1={Where(e, o1)} o2={Where(e, o2)} o3={Where(e, o3)} mine={Where(e, mine)} cat={Where(e, cat)}");
            }

            // Only 1 opponent monster: no targets, nothing returns, no prompt.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var cat = FaceDown(e, p, DarkCat, 0);
                var o1 = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(p, cat);
                var prompted = e.IsAwaitingEffectTarget;
                Settle(e);
                Check("Dark Cat with White Tail: without 2 opponent monsters it takes no targets and does nothing",
                    flipped && !prompted && o.TryFindMonster(o1, out _) && p.TryFindMonster(cat, out _) &&
                    p.Hand.Count == 0 && o.Hand.Count == 0,
                    $"flipped={flipped} prompted={prompted} o1={Where(e, o1)} cat={Where(e, cat)}");
            }

            // The AI's Dark Cat: it returns the player's 2 strongest monsters and its own Dark Cat.
            {
                var e = SetupOpponentTurn(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var cat = FaceDown(e, o, DarkCat, 0);
                var weak = PlaceMonster(e, p, Hitotsu, 0, BattlePosition.Attack, true);
                var dm = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var flipped = e.TryFlipSummon(o, cat);
                Settle(e);
                Check("Dark Cat with White Tail (AI): the player's 2 strongest and the AI's Dark Cat return",
                    flipped && p.Hand.Contains(dm) && p.Hand.Contains(celtic) && p.TryFindMonster(weak, out _) &&
                    o.Hand.Contains(cat),
                    $"flipped={flipped} dm={Where(e, dm)} celtic={Where(e, celtic)} weak={Where(e, weak)} cat={Where(e, cat)}");
            }

            // The Flip Summon's FLIP goes on a Chain the AI may answer (a Set Mystical Space Typhoon): the targets are
            // chosen as the effect activates and used when the link resolves.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var cat = FaceDown(e, p, DarkCat, 0);
                var o1 = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var o2 = PlaceMonster(e, o, Hitotsu, 1, BattlePosition.Attack, true);
                var o3 = PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
                SetTrap(e, o, Mst, 0);
                SetTrap(e, p, TrapHole, 1); // something the AI's MST could answer with
                var log = new List<string>();
                e.OnLog += log.Add;
                var flipped = e.TryFlipSummon(p, cat);
                var pickedAtActivation = e.IsAwaitingEffectTarget;
                var picks = Drive(e, c => c == o1 || c == o2 || c == cat);
                Settle(e);
                e.OnLog -= log.Add;
                var onChain = log.Any(l => l.Contains("Chain Link 1") && l.Contains(cat.Name));
                Check("Dark Cat with White Tail: on a Chain, targets chosen as it activates and returned on resolution",
                    flipped && onChain && pickedAtActivation && picks >= 2 && o.Hand.Contains(o1) &&
                    o.Hand.Contains(o2) && p.Hand.Contains(cat) && o.TryFindMonster(o3, out _),
                    $"flipped={flipped} onChain={onChain} atAct={pickedAtActivation} picks={picks} o1={Where(e, o1)} " +
                    $"o2={Where(e, o2)} o3={Where(e, o3)} cat={Where(e, cat)}");
            }
        }

        // ───────────────────────────── Huge Revolution ─────────────────────────────

        static void HugeRevolutionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The three named monsters face-up, the player's Main Phase: the opponent's hand goes to the GY and every
            // card they control is destroyed; the player's own cards stay.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, p, PeopleRunningAbout, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, OppressedPeople, 1, BattlePosition.Defense, true);
                var c = PlaceMonster(e, p, UnitedResistance, 2, BattlePosition.Attack, true);
                var rev = SetTrap(e, p, HugeRevolution, 0);
                var om1 = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var om2 = FaceDown(e, o, GiantSoldier, 1);
                var ost = SetTrap(e, o, PotOfGreed, 0);
                var h1 = PutInHand(e, o, Celtic);
                var h2 = PutInHand(e, o, PotOfGreed);
                var can = e.CanActivateSpellTrap(p, rev, fromHand: false);
                var act = e.TryActivateSpellTrap(p, rev, fromHand: false);
                Settle(e);
                Check("Huge Revolution: the opponent's hand to the GY and every card they control destroyed",
                    can && act && o.Hand.Count == 0 && o.Graveyard.Contains(h1) && o.Graveyard.Contains(h2) &&
                    o.Graveyard.Contains(om1) && o.Graveyard.Contains(om2) && o.Graveyard.Contains(ost) &&
                    o.MonsterCount == 0 && !o.SpellTrapsOnField().Any() &&
                    p.TryFindMonster(a, out _) && p.TryFindMonster(b, out _) && p.TryFindMonster(c, out _),
                    $"can={can} act={act} hand={o.Hand.Count} gy={o.Graveyard.Count} om1={Where(e, om1)} " +
                    $"om2={Where(e, om2)} ost={Where(e, ost)} h1={Where(e, h1)}");
            }

            // One name missing (face-down "United Resistance"): it cannot be activated.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, PeopleRunningAbout, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, OppressedPeople, 1, BattlePosition.Attack, true);
                FaceDown(e, p, UnitedResistance, 2);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var rev = SetTrap(e, p, HugeRevolution, 0);
                Check("Huge Revolution: cannot be activated unless all three named monsters are face-up",
                    !e.CanActivateSpellTrap(p, rev, fromHand: false));
            }

            // The opponent's turn: not "your Main Phase" — it cannot be activated. The AI's own Set copy in its Main Phase
            // is offered to SimpleAi.
            {
                var e = SetupOpponentTurn(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, PeopleRunningAbout, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, OppressedPeople, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, UnitedResistance, 2, BattlePosition.Attack, true);
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var rev = SetTrap(e, p, HugeRevolution, 0);
                var playerCan = e.CanActivateSpellTrap(p, rev, fromHand: false);

                PlaceMonster(e, o, PeopleRunningAbout, 1, BattlePosition.Attack, true);
                PlaceMonster(e, o, OppressedPeople, 2, BattlePosition.Attack, true);
                PlaceMonster(e, o, UnitedResistance, 3, BattlePosition.Attack, true);
                var aiRev = SetTrap(e, o, HugeRevolution, 1);
                var aiWants = FlipMassAndOpponentPicksRuntime.AiWantsToActivateSetTrap(e, o, aiRev);
                var aiCan = e.CanActivateSpellTrap(o, aiRev, fromHand: false);
                var aiAct = aiWants && aiCan && e.TryActivateSpellTrap(o, aiRev, fromHand: false);
                Settle(e);
                Check("Huge Revolution: not in the opponent's turn; the AI uses its own copy in its Main Phase",
                    !playerCan && aiWants && aiCan && aiAct && p.MonsterCount == 0 && p.Graveyard.Contains(rev),
                    $"playerCan={playerCan} aiWants={aiWants} aiCan={aiCan} aiAct={aiAct} pMonsters={p.MonsterCount} " +
                    $"rev={Where(e, rev)}");
            }
        }

        // ───────────────────────────── Magical Merchant ─────────────────────────────

        static void MagicalMerchantDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Deck (top first): Celtic, Giant Soldier, Pot of Greed, Kuriboh, Trap Hole — Pot is added, the two
            // monsters above it go to the GY, the rest stays.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                foreach (var id in new[] { Celtic, GiantSoldier, PotOfGreed, Kuriboh, TrapHole }) p.Deck.Add(id);
                var merchant = FaceDown(e, p, MagicalMerchant, 0);
                var flipped = e.TryFlipSummon(p, merchant);
                Settle(e);
                Check("Magical Merchant: the first Spell/Trap is added, the cards above it go to the GY",
                    flipped && InHandById(p, PotOfGreed) && p.Hand.Count == 1 && p.Graveyard.Count == 2 &&
                    p.Graveyard.Any(g => g.CardId == Celtic) && p.Graveyard.Any(g => g.CardId == GiantSoldier) &&
                    p.Deck.Count == 2 && p.Deck[0] == Kuriboh && p.Deck[1] == TrapHole,
                    $"flipped={flipped} hand={string.Join(",", p.Hand.Select(h => h.Name))} " +
                    $"gy={string.Join(",", p.Graveyard.Select(g => g.Name))} deck={p.Deck.Count}");
            }

            // No Spell/Trap: the Deck is revealed and shuffled; nothing moves.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                foreach (var id in new[] { Celtic, GiantSoldier, Kuriboh, Hitotsu }) p.Deck.Add(id);
                var merchant = FaceDown(e, p, MagicalMerchant, 0);
                var flipped = e.TryFlipSummon(p, merchant);
                Settle(e);
                Check("Magical Merchant: no Spell/Trap in the Deck — nothing is added or sent, the Deck stays whole",
                    flipped && p.Hand.Count == 0 && p.Graveyard.Count == 0 && p.Deck.Count == 4,
                    $"flipped={flipped} hand={p.Hand.Count} gy={p.Graveyard.Count} deck={p.Deck.Count}");
            }
        }

        // ───────────────────────────── Pixie Knight ─────────────────────────────

        static bool ReachPlayerBattleTurn3(DuelEngine e)
        {
            for (var t = 0; t < 8 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.TurnPlayer != e.Player) return false;
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            if (e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(e.Player);
            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            DrainCombat(e);
            return e.Phase == DuelPhase.Battle && e.TurnPlayer == e.Player;
        }

        /// <summary>
        /// The player's turn-3 Battle Phase: the player's Pixie Knight attacks the AI's Pixie Knight (both 1300 ATK, both
        /// destroyed by battle). SEGOC: the player's trigger is Chain Link 1 (its Spell chosen by the AI), the AI's is
        /// Chain Link 2 (its Spell chosen by the human); CL2 resolves first. The AI's GY holds Pot of Greed and
        /// Premature Burial (2 choices: the human is asked), the player's 1 Pot of Greed when <paramref name="playerSpell"/>.
        /// </summary>
        static DuelEngine TwoPixies(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool playerSpell,
            out CardInstance pSpell, out CardInstance oSpell, out bool ok)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachPlayerBattleTurn3(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Graveyard.Clear();
            o.Graveyard.Clear();
            var mine = PlaceMonster(e, p, PixieKnight, 0, BattlePosition.Attack, true);
            mine.SummonedThisTurn = false;
            var theirs = PlaceMonster(e, o, PixieKnight, 0, BattlePosition.Attack, true);
            oSpell = AddToGy(e, o, PotOfGreed);
            AddToGy(e, o, PrematureBurial);
            pSpell = playerSpell ? AddToGy(e, p, PotOfGreed) : null;
            e.NotifyPublic();
            ok = reached && e.TryAttack(p, mine, theirs);
            return e;
        }

        /// <summary>Steps the battle to the human's first target prompt, runs <paramref name="atPrompt"/>, picks
        /// <paramref name="pick"/>; then passes and picks <paramref name="pick"/>-matching cards to the end.</summary>
        static int RunTwoPixies(DuelEngine e, CardInstance pick, Action atPrompt, out List<CardInstance> offer)
        {
            offer = new List<CardInstance>();
            for (var i = 0; i < 20 && !e.IsAwaitingEffectTarget; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else break;
            }

            if (!e.IsAwaitingEffectTarget) return 0;
            offer = Legal(e);
            atPrompt();
            var picks = e.TrySelectEffectTarget(pick) ? 1 : 0;
            if (picks == 0) e.CancelEffectTargeting();
            picks += Drive(e, c => c == pick);
            Settle(e);
            return picks;
        }

        static void PixieKnightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The AI destroys the player's Pixie Knight by battle: the AI (the opponent) chooses the Spell; it goes on
            // top of the player's Deck.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Graveyard.Clear();
                var pixie = PlaceMonster(e, p, PixieKnight, 0, BattlePosition.Attack, true);
                var pot = AddToGy(e, p, PotOfGreed);
                var burial = AddToGy(e, p, PrematureBurial);
                AddToGy(e, p, Celtic);
                var attacker = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                attacker.SummonedThisTurn = false;
                var attacked = e.TryAttack(o, attacker, pixie);
                Settle(e);
                var top = p.Deck.Count > 0 ? p.Deck[0] : 0;
                var moved = (top == PotOfGreed && !p.Graveyard.Contains(pot)) ||
                            (top == PrematureBurial && !p.Graveyard.Contains(burial));
                Check("Pixie Knight: destroyed by the AI's attack, the AI places 1 Spell from the player's GY on top",
                    reached && attacked && p.Graveyard.Contains(pixie) && moved &&
                    p.Graveyard.Count(g => g.Def != null && g.Def.IsSpell) == 1,
                    $"reached={reached} attacked={attacked} pixie={Where(e, pixie)} top={top} " +
                    $"gy={string.Join(",", p.Graveyard.Select(g => g.Name))}");
            }

            // The player destroys the AI's Pixie Knight: the human (the AI's opponent) is asked and their choice goes
            // on top of the AI's Deck.
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachPlayerBattleTurn3(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.Graveyard.Clear();
                var pixie = PlaceMonster(e, o, PixieKnight, 0, BattlePosition.Attack, true);
                var pot = AddToGy(e, o, PotOfGreed);
                var burial = AddToGy(e, o, PrematureBurial);
                var attacker = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                attacker.SummonedThisTurn = false;
                var attacked = e.TryAttack(p, attacker, pixie);
                for (var i = 0; i < 12 && !e.IsAwaitingEffectTarget; i++)
                {
                    if (e.IsAwaitingResponse) e.PassResponse();
                    else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                    else break;
                }

                var offers = new List<List<CardInstance>>();
                var picks = Drive(e, c => c == burial, offers);
                Settle(e);
                var offer = offers.FirstOrDefault() ?? new List<CardInstance>();
                Check("Pixie Knight: the human opponent chooses the Spell; it goes on top of the AI's Deck",
                    reached && attacked && picks == 1 && offer.Count == 2 && offer.Contains(pot) &&
                    o.Deck.Count > 0 && o.Deck[0] == PrematureBurial && !o.Graveyard.Contains(burial) &&
                    o.Graveyard.Contains(pot) && o.Graveyard.Contains(pixie),
                    $"reached={reached} attacked={attacked} picks={picks} offer={offer.Count} " +
                    $"top={(o.Deck.Count > 0 ? o.Deck[0] : 0)} burial={Where(e, burial)} pixie={Where(e, pixie)}");
            }

            // Lua s.target (EFFECT_FLAG_CARD_TARGET, SelectTarget by 1-tp): the opponent targets the Spell as the trigger
            // activates; s.operation acts only on that target, if it is still in the GY. Two Pixie Knights destroyed by
            // the same battle are one Chain: the player's (CL1) takes its target before the AI's (CL2) activates, and
            // resolves after CL2. (a) CL1's target leaves the GY before CL1 resolves while another Spell arrives: no
            // effect — the newcomer is not taken instead.
            {
                var e = TwoPixies(db, pDeck, aDeck, true, out var pot, out var oSpell, out var ok);
                var p = e.Player;
                var o = e.Opponent;
                CardInstance late = null;
                var deck = p.Deck.Count;
                var picks = RunTwoPixies(e, oSpell, () =>
                {
                    // Before CL1 resolves: its target is banished and another Spell reaches the GY.
                    p.Graveyard.Remove(pot);
                    p.Banished.Add(pot);
                    late = AddToGy(e, p, PrematureBurial);
                }, out var offer);
                Check("Pixie Knight: CL1's target left the GY before it resolved — no effect, the newcomer stays in the GY",
                    ok && picks == 1 && offer.Count == 2 && offer.Contains(oSpell) && late != null &&
                    p.Graveyard.Contains(late) && p.Deck.Count == deck && p.Banished.Contains(pot),
                    $"ok={ok} picks={picks} offer={offer.Count} late={Where(e, late)} deck {deck}->{p.Deck.Count}");
                Check("Pixie Knight: the AI's (CL2) places the human's chosen Spell on top of the AI's Deck",
                    ok && o.Deck.Count > 0 && o.Deck[0] == PotOfGreed && !o.Graveyard.Contains(oSpell),
                    $"top={(o.Deck.Count > 0 ? o.Deck[0] : 0)} oSpell={Where(e, oSpell)}");
            }

            // (b) No Spell in the GY when CL1 activates: no target, so a Spell that arrives before it resolves stays.
            {
                var e = TwoPixies(db, pDeck, aDeck, false, out _, out var oSpell, out var ok);
                var p = e.Player;
                CardInstance late = null;
                var deck = p.Deck.Count;
                var picks = RunTwoPixies(e, oSpell, () => late = AddToGy(e, p, PrematureBurial), out _);
                Check("Pixie Knight: no Spell when it activated — a Spell sent to the GY before it resolves stays there",
                    ok && picks == 1 && late != null && p.Graveyard.Contains(late) && p.Deck.Count == deck,
                    $"ok={ok} picks={picks} late={Where(e, late)} deck {deck}->{p.Deck.Count}");
            }

            // Destroyed by a card effect (not battle): no effect.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var pixie = PlaceMonster(e, p, PixieKnight, 0, BattlePosition.Attack, true);
                var pot = AddToGy(e, p, PotOfGreed);
                var top = p.Deck.Count > 0 ? p.Deck[0] : 0;
                e.DestroyMonsterPublic(p, pixie);
                Settle(e);
                Check("Pixie Knight: destroyed by a card effect, nothing moves",
                    p.Graveyard.Contains(pixie) && p.Graveyard.Contains(pot) &&
                    (p.Deck.Count > 0 ? p.Deck[0] : 0) == top,
                    $"pixie={Where(e, pixie)} pot={Where(e, pot)}");
            }
        }

        // ───────────────────────────── Pineapple Blast ─────────────────────────────

        static void PineappleBlastDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player Normal Summons with the AI controlling 3 monsters: the player's own summon window offers
            // Pineapple Blast; the AI chooses which 2 of its monsters are destroyed (its weakest) and keeps 1.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var dm = PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                var w1 = PlaceMonster(e, o, Hitotsu, 1, BattlePosition.Attack, true);
                var w2 = PlaceMonster(e, o, Kuriboh, 2, BattlePosition.Attack, true);
                var pine = SetTrap(e, p, PineappleBlast, 0);
                var summon = PutInHand(e, p, Celtic);
                var summoned = e.TryNormalSummon(p, summon, asSet: false);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p &&
                             e.PendingResponse.LegalCards.Contains(pine);
                var act = window && e.TryActivateSpellTrap(p, pine, fromHand: false);
                Settle(e);
                Check("Pineapple Blast: after your Normal Summon the opponent destroys down to your count (their choice)",
                    summoned && window && act && o.MonsterCount == 1 && o.TryFindMonster(dm, out _) &&
                    o.Graveyard.Contains(w1) && o.Graveyard.Contains(w2) && p.Graveyard.Contains(pine),
                    $"summoned={summoned} window={window} act={act} oMonsters={o.MonsterCount} dm={Where(e, dm)} " +
                    $"w1={Where(e, w1)} w2={Where(e, w2)} pine={Where(e, pine)}");
            }

            // Not offered: your Normal Summon when they do not control more; your Set; the opponent's Normal Summon.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, o, DarkMagician, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Hitotsu, 0, BattlePosition.Attack, true);
                var pine = SetTrap(e, p, PineappleBlast, 0);
                var summon = PutInHand(e, p, Celtic);
                e.TryNormalSummon(p, summon, asSet: false);
                var offeredEqual = e.IsAwaitingResponse && e.PendingResponse?.LegalCards?.Contains(pine) == true;
                Settle(e);

                var e2 = Setup(db, pDeck, aDeck);
                var p2 = e2.Player;
                PlaceMonster(e2, e2.Opponent, DarkMagician, 0, BattlePosition.Attack, true);
                PlaceMonster(e2, e2.Opponent, Hitotsu, 1, BattlePosition.Attack, true);
                var pine2 = SetTrap(e2, p2, PineappleBlast, 0);
                var set = PutInHand(e2, p2, Celtic);
                e2.TryNormalSummon(p2, set, asSet: true);
                var offeredSet = e2.IsAwaitingResponse && e2.PendingResponse?.LegalCards?.Contains(pine2) == true;
                Settle(e2);

                var e3 = SetupOpponentTurn(db, pDeck, aDeck);
                var p3 = e3.Player;
                var o3 = e3.Opponent;
                PlaceMonster(e3, o3, DarkMagician, 0, BattlePosition.Attack, true);
                PlaceMonster(e3, o3, Hitotsu, 1, BattlePosition.Attack, true);
                var pine3 = SetTrap(e3, p3, PineappleBlast, 0);
                var aiSummon = PutInHand(e3, o3, Celtic);
                e3.TryNormalSummon(o3, aiSummon, asSet: false);
                var offeredOpp = e3.IsAwaitingResponse && e3.PendingResponse?.LegalCards?.Contains(pine3) == true;
                Settle(e3);
                Check("Pineapple Blast: not offered for equal counts, a Set, or the opponent's Normal Summon",
                    !offeredEqual && !offeredSet && !offeredOpp,
                    $"equal={offeredEqual} set={offeredSet} opp={offeredOpp}");
            }

            // The AI's Pineapple Blast after its own Normal Summon: the human chooses which of their monsters are
            // destroyed (2 of 3), and only those are destroyed.
            {
                var e = SetupOpponentTurn(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var keep = PlaceMonster(e, p, Hitotsu, 0, BattlePosition.Attack, true);
                var d1 = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Attack, true);
                var d2 = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var pine = SetTrap(e, o, PineappleBlast, 0);
                var summon = PutInHand(e, o, GiantSoldier);
                var summoned = e.TryNormalSummon(o, summon, asSet: false);
                var offers = new List<List<CardInstance>>();
                var picks = Drive(e, c => c == d1 || c == d2, offers);
                Settle(e);
                var first = offers.FirstOrDefault() ?? new List<CardInstance>();
                Check("Pineapple Blast (AI): the human chooses which of their monsters are destroyed",
                    summoned && o.Graveyard.Contains(pine) && picks == 2 && first.Count == 3 &&
                    p.Graveyard.Contains(d1) && p.Graveyard.Contains(d2) && p.TryFindMonster(keep, out _) &&
                    p.MonsterCount == 1,
                    $"summoned={summoned} pine={Where(e, pine)} picks={picks} offers={string.Join("/", offers.Select(l => l.Count))} " +
                    $"keep={Where(e, keep)} d1={Where(e, d1)} d2={Where(e, d2)}");
            }
        }

        // ───────────────────────────── Dimension Jar ─────────────────────────────

        static void DimensionJarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player (turn player) flips Dimension Jar: the human first — the AI's GY monsters are offered (not its
            // Spell: the print says "monsters", the Lua filter would take any card), the human banishes 2 then stops;
            // then the AI banishes the 3 strongest monsters in the player's GY.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var oDm = AddToGy(e, o, DarkMagician);
                var oCeltic = AddToGy(e, o, Celtic);
                var oKuri = AddToGy(e, o, Kuriboh);
                var oPot = AddToGy(e, o, PotOfGreed);
                var pDm = AddToGy(e, p, DarkMagician);
                var pSoldier = AddToGy(e, p, GiantSoldier);
                var pCeltic = AddToGy(e, p, Celtic);
                var pKuri = AddToGy(e, p, Kuriboh);
                var jar = FaceDown(e, p, DimensionJar, 0);
                var flipped = e.TryFlipSummon(p, jar);
                var offers = new List<List<CardInstance>>();
                var picks = Drive(e, c => c == oDm || c == oKuri, offers);
                Settle(e);
                var first = offers.FirstOrDefault() ?? new List<CardInstance>();
                Check("Dimension Jar: each player banishes up to 3 monsters from the opponent's GY (turn player first)",
                    flipped && picks == 2 && first.Count == 3 && !first.Contains(oPot) &&
                    o.Banished.Contains(oDm) && o.Banished.Contains(oKuri) && o.Graveyard.Contains(oCeltic) &&
                    o.Graveyard.Contains(oPot) &&
                    p.Banished.Contains(pDm) && p.Banished.Contains(pSoldier) && p.Banished.Contains(pCeltic) &&
                    p.Graveyard.Contains(pKuri),
                    $"flipped={flipped} picks={picks} offers={string.Join("/", offers.Select(l => l.Count))} " +
                    $"oDm={Where(e, oDm)} oKuri={Where(e, oKuri)} oCeltic={Where(e, oCeltic)} pDm={Where(e, pDm)} " +
                    $"pSoldier={Where(e, pSoldier)} pCeltic={Where(e, pCeltic)} pKuri={Where(e, pKuri)}");
            }

            // Cancel at once: the human banishes nothing; the AI still banishes from the player's GY.
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var oDm = AddToGy(e, o, DarkMagician);
                var pDm = AddToGy(e, p, DarkMagician);
                var jar = FaceDown(e, p, DimensionJar, 0);
                var flipped = e.TryFlipSummon(p, jar);
                var asked = e.IsAwaitingEffectTarget && Legal(e).Contains(oDm);
                Drive(e, _ => false);
                Settle(e);
                Check("Dimension Jar: Cancel banishes nothing for that player (a lone monster is still asked)",
                    flipped && asked && o.Graveyard.Contains(oDm) && p.Banished.Contains(pDm),
                    $"flipped={flipped} asked={asked} oDm={Where(e, oDm)} pDm={Where(e, pDm)}");
            }
        }
    }
}
