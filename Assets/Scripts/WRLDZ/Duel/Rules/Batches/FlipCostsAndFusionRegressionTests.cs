using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FlipCostsAndFusion batch: Fire Sorcerer and Lady Assailant of Flames (FLIP effects with a cost), Supply (FLIP: 2
    /// monsters sent to the GY as Fusion Material), Summoner of Illusions (FLIP: Tribute, then a Fusion Monster from the
    /// Extra Deck destroyed in the End Phase) and De-Fusion (return a Fusion Monster, then you can Summon its materials).
    /// Compile shapes, mini-duels through the real entry points (TryFlipSummon, MonsterEffects.OnFlipSummoned,
    /// Polymerization / De-Fusion through TryActivateSpellTrap, TrySelectEffectTarget, CancelEffectTargeting, the End
    /// Phase) and near misses. Runtime: <see cref="FlipCostsAndFusionRuntime"/>.
    /// </summary>
    public static class FlipCostsAndFusionRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FireSorcerer = 27132350;
        const int LadyAssailant = 90147755;
        const int SummonerOfIllusions = 14644902;
        const int Supply = 44072894;
        const int DeFusion = 95286165;
        const int Polymerization = 24094653;
        const int FlameSwordsman = 45231177;       // "Flame Manipulator" + "Masaki the Legendary Swordsman"
        const int FlameManipulator = 34460851;     // Normal 900/1000
        const int Masaki = 44287299;               // Normal 1100/1100
        const int GaiaDragonChampion = 66889139;   // Fusion 2600/2100
        const int CelticGuardian = 91152256;       // Normal 1400/1200
        const int MysticalElf = 15025844;          // Normal 800/2000
        const int GiantSoldierOfStone = 13039848;  // Normal 1300/2000
        const int DarkMagician = 46986414;         // Normal 2500/2100
        const int DarkPaladin = 98502113;          // Fusion: "Must be Fusion Summoned."
        const int FlameWingman = 35809262;         // Fusion: "Must be Fusion Summoned and cannot be Special Summoned by other ways."
        const int XyDragonCannon = 2111707;        // Fusion: "Must first be Special Summoned (from your Extra Deck) by banishing …"
        const int XyzDragonCannon = 91998119;      // Fusion: same contact procedure

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("FlipCostsAndFusion: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            FlipCostDuels(db, pDeck, aDeck, Check);
            SupplyDuels(db, pDeck, aDeck, Check);
            SummonerDuels(db, pDeck, aDeck, Check);
            DeFusionDuels(db, pDeck, aDeck, Check);
            NearMisses(Check);
        }

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            EffectClause Only(int id, out CompiledCardProgram prog)
            {
                prog = CardTextEffectCompiler.Compile(db.Get(id));
                return prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            }

            var fs = Only(FireSorcerer, out var fsProg);
            Check("Fire Sorcerer: one Flip burn of 800 with a cost of 2 random hand cards banished",
                fs != null && fs.Timing == EffectTiming.Flip && fs.Action == EffectActionKind.InflictDamageToOpponent &&
                fs.Amount == 800 && fs.CostBanishRandomHandCards == 2 && fs.CostBanishTopDeckCards == 0,
                Describe(fsProg));
            var la = Only(LadyAssailant, out var laProg);
            Check("Lady Assailant of Flames: one Flip burn of 800 with a cost of the top 3 Deck cards banished",
                la != null && la.Timing == EffectTiming.Flip && la.Action == EffectActionKind.InflictDamageToOpponent &&
                la.Amount == 800 && la.CostBanishTopDeckCards == 3 && la.CostBanishRandomHandCards == 0,
                Describe(laProg));
            var su = Only(Supply, out var suProg);
            Check("Supply: one Flip target of exactly 2 Fusion Material monsters in your GY, added to the hand",
                su != null && su.Timing == EffectTiming.Flip && su.Action == EffectActionKind.AddFromGyToHand &&
                su.Zone == EffectZoneFilter.ControllerGyMonsters && su.RequiresSentAsFusionMaterial &&
                su.TargetCount == 2 && !su.TargetUpTo && su.RequiresTargetChoice && su.IsPsctTarget,
                Describe(suProg));
            var si = Only(SummonerOfIllusions, out var siProg);
            Check("Summoner of Illusions: one Flip Tribute-then-Fusion-from-Extra-Deck clause",
                si != null && si.Timing == EffectTiming.Flip &&
                si.Action == EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase &&
                !si.RequiresTargetChoice, Describe(siProg));
            var df = Only(DeFusion, out var dfProg);
            Check("De-Fusion: one targeted Activate clause on a face-up Fusion Monster on either field",
                df != null && df.Timing == EffectTiming.Activate &&
                df.Action == EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials &&
                df.Zone == EffectZoneFilter.FieldFaceUpFusionMonsters && df.Side == EffectSide.Both &&
                df.RequiresTargetChoice && df.IsPsctTarget, Describe(dfProg));
            foreach (var id in new[] { FireSorcerer, LadyAssailant, Supply, SummonerOfIllusions, DeFusion })
                Check($"#{id} {db.Get(id)?.name} classifies Implemented",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(db.Get(id)).ToString());
            Check("The two new actions map to the Special Summon resolution kind",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase }) ==
                EffectResolutionKind.SpecialSummon &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials }) ==
                EffectResolutionKind.SpecialSummon);
        }

        // ───────────────────────────── FLIP costs ─────────────────────────────

        static void FlipCostDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Fire Sorcerer Flip Summoned with 3 cards in hand: 2 of them are banished face-up, 800 damage.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var hand = new[] { PutInHand(e, p, CelticGuardian), PutInHand(e, p, MysticalElf),
                    PutInHand(e, p, GiantSoldierOfStone) };
                var fs = PlaceMonster(e, p, FireSorcerer, 0, BattlePosition.Defense, false);
                var lp = e.Opponent.LifePoints;
                var flipped = e.TryFlipSummon(p, fs);
                Settle(e);
                var banished = p.Banished.Where(b => hand.Contains(b)).ToList();
                Check("Fire Sorcerer: Flip Summon banishes 2 random hand cards face-up and inflicts 800",
                    flipped && fs.FaceUp && p.Hand.Count == 1 && banished.Count == 2 && banished.All(b => b.FaceUp) &&
                    e.Opponent.LifePoints == lp - 800,
                    $"flipped={flipped} hand={p.Hand.Count} banished={banished.Count} lp={lp}->{e.Opponent.LifePoints}");
            }

            // Fire Sorcerer with 1 card in hand: the FLIP effect cannot be activated — no banish, no damage.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var only = PutInHand(e, p, CelticGuardian);
                var fs = PlaceMonster(e, p, FireSorcerer, 0, BattlePosition.Defense, false);
                var lp = e.Opponent.LifePoints;
                var flipped = e.TryFlipSummon(p, fs);
                Settle(e);
                Check("Fire Sorcerer: with fewer than 2 cards in hand the FLIP effect is not activated",
                    flipped && fs.FaceUp && p.Hand.Count == 1 && p.Hand.Contains(only) && p.Banished.Count == 0 &&
                    e.Opponent.LifePoints == lp,
                    $"flipped={flipped} hand={p.Hand.Count} banished={p.Banished.Count} lp={lp}->{e.Opponent.LifePoints}");
            }

            // Lady Assailant of Flames: the top 3 cards of the Deck are banished face-up, then 800 damage.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, MysticalElf, GiantSoldierOfStone, DarkMagician });
                var lady = PlaceMonster(e, p, LadyAssailant, 0, BattlePosition.Defense, false);
                var lp = e.Opponent.LifePoints;
                var flipped = e.TryFlipSummon(p, lady);
                Settle(e);
                var ids = p.Banished.Select(b => b.CardId).ToList();
                Check("Lady Assailant of Flames: Flip Summon banishes the top 3 Deck cards face-up and inflicts 800",
                    flipped && p.Deck.Count == 1 && p.Deck[0] == DarkMagician && ids.Count == 3 &&
                    ids.Contains(CelticGuardian) && ids.Contains(MysticalElf) && ids.Contains(GiantSoldierOfStone) &&
                    p.Banished.All(b => b.FaceUp) && e.Opponent.LifePoints == lp - 800,
                    $"flipped={flipped} deck={p.Deck.Count} banished={string.Join(",", ids)} lp={lp}->{e.Opponent.LifePoints}");
            }

            // Lady Assailant with only 2 cards in the Deck: no cost, no effect.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { CelticGuardian, MysticalElf });
                var lady = PlaceMonster(e, p, LadyAssailant, 0, BattlePosition.Defense, false);
                var lp = e.Opponent.LifePoints;
                var flipped = e.TryFlipSummon(p, lady);
                Settle(e);
                Check("Lady Assailant of Flames: with fewer than 3 Deck cards the FLIP effect is not activated",
                    flipped && p.Deck.Count == 2 && p.Banished.Count == 0 && e.Opponent.LifePoints == lp,
                    $"flipped={flipped} deck={p.Deck.Count} banished={p.Banished.Count} lp={lp}->{e.Opponent.LifePoints}");
            }

            // The AI's Lady Assailant flipped by an effect: its own Deck pays, the player takes 800.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var o = e.Opponent;
                var deckBefore = o.Deck.Count;
                var lady = PlaceMonster(e, o, LadyAssailant, 0, BattlePosition.Defense, true);
                var lp = e.Player.LifePoints;
                MonsterEffects.OnFlipSummoned(e, o, lady);
                Settle(e);
                Check("Lady Assailant of Flames (AI): the opponent banishes its own top 3 cards; the player takes 800",
                    deckBefore >= 3 && o.Deck.Count == deckBefore - 3 && o.Banished.Count == 3 &&
                    e.Player.LifePoints == lp - 800,
                    $"deck={deckBefore}->{o.Deck.Count} banished={o.Banished.Count} lp={lp}->{e.Player.LifePoints}");
            }
        }

        // ───────────────────────────── Supply ─────────────────────────────

        static void SupplyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The 2 Fusion Materials are the only legal targets (a monster that just went to the GY is not); both
            // go to the hand.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                var celtic = e.CreateCardInstance(CelticGuardian);
                p.Graveyard.Add(celtic);
                var supply = PlaceMonster(e, p, Supply, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, supply);
                var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Supply: Polymerization stamps both materials; only they are offered (2 picks)",
                    fused != null && manip.SentAsFusionMaterialFor == fused && masaki.SentAsFusionMaterialFor == fused &&
                    legal.Count == 2 && legal.Contains(manip) && legal.Contains(masaki) && !legal.Contains(celtic) &&
                    e.PendingActivation?.TargetPicksRemaining == 2,
                    $"fused={fused?.Name} legal={string.Join(",", legal.Select(t => t.Name))}");
                var ok = e.TrySelectEffectTarget(manip) && e.TrySelectEffectTarget(masaki);
                Settle(e);
                Check("Supply: both Fusion Materials are added to the hand",
                    ok && p.Hand.Contains(manip) && p.Hand.Contains(masaki) && p.Graveyard.Contains(celtic) &&
                    !e.IsAwaitingEffectTarget,
                    $"ok={ok} hand={string.Join(",", p.Hand.Select(h => h.Name))}");
                e.NotifyPublic();
                Check("Supply: a material that left the GY loses its Fusion Material stamp",
                    manip.SentAsFusionMaterialFor == null && masaki.SentAsFusionMaterialFor == null);
            }

            // A material that left the GY and came back is no longer a Fusion Material: 1 legal → no effect.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                FuseFlameSwordsman(e, p, out var manip, out var masaki);
                p.Graveyard.Remove(masaki);
                p.Hand.Add(masaki);
                e.NotifyPublic();
                e.SendCardToGrave(p, masaki);
                var supply = PlaceMonster(e, p, Supply, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, supply);
                Settle(e);
                Check("Supply: with fewer than 2 Fusion Materials in the GY it has no targets and does nothing",
                    !e.IsAwaitingEffectTarget && p.Graveyard.Contains(manip) && p.Graveyard.Contains(masaki) &&
                    masaki.SentAsFusionMaterialFor == null && manip.SentAsFusionMaterialFor != null,
                    $"pending={e.IsAwaitingEffectTarget} manipGy={p.Graveyard.Contains(manip)}");
            }

            // The AI's Supply: its own Polymerization on its turn, then the FLIP takes both materials automatically.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                var o = e.Opponent;
                ClearSide(o);
                var fused = FuseFlameSwordsman(e, o, out var manip, out var masaki);
                var supply = PlaceMonster(e, o, Supply, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, o, supply);
                Settle(e);
                Check("Supply (AI): the opponent adds both of its Fusion Materials to its hand",
                    reached && fused != null && o.Hand.Contains(manip) && o.Hand.Contains(masaki) &&
                    !e.IsAwaitingEffectTarget,
                    $"reached={reached} fused={fused?.Name} hand={string.Join(",", o.Hand.Select(h => h.Name))}");
            }
        }

        // ───────────────────────────── Summoner of Illusions ─────────────────────────────

        static void SummonerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Player: choose the Tribute, then the Fusion Monster and its position; destroyed in the End Phase.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.AddRange(new[] { FlameSwordsman, GaiaDragonChampion });
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Defense, false);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                var tributeOffer = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Summoner of Illusions: the Tribute choice offers your other monsters (face-down too), not itself",
                    e.IsAwaitingEffectTarget && tributeOffer.Count == 2 && tributeOffer.Contains(celtic) &&
                    tributeOffer.Contains(elf) && !tributeOffer.Contains(summoner) &&
                    !SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation),
                    $"offer={string.Join(",", tributeOffer.Select(t => t.Name))}");
                var tributed = e.TrySelectEffectTarget(celtic);
                var fusionOffer = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Summoner of Illusions: after the Tribute, every Fusion Monster in the Extra Deck is offered",
                    tributed && p.Graveyard.Contains(celtic) && fusionOffer.Count == 2 &&
                    fusionOffer.Any(f => f.CardId == GaiaDragonChampion) &&
                    fusionOffer.Any(f => f.CardId == FlameSwordsman) &&
                    SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation),
                    $"tributed={tributed} offer={string.Join(",", fusionOffer.Select(f => f.Name))}");
                var gaia = fusionOffer.FirstOrDefault(f => f.CardId == GaiaDragonChampion);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var summoned = gaia != null && e.TrySelectEffectTarget(gaia);
                Settle(e);
                Check("Summoner of Illusions: the chosen Fusion Monster is Special Summoned in face-up Defense Position",
                    summoned && p.TryFindMonster(gaia, out _) && gaia.FaceUp &&
                    gaia.Position == BattlePosition.Defense && !p.ExtraDeck.Contains(GaiaDragonChampion) &&
                    p.ExtraDeck.Contains(FlameSwordsman) && gaia.TempDestroyOnEndOfTurn == e.TurnNumber &&
                    !e.IsAwaitingEffectTarget,
                    $"summoned={summoned} pos={gaia?.Position} faceUp={gaia?.FaceUp} extra={p.ExtraDeck.Count}");
                var reached = ReachOpponentMain(e);
                Check("Summoner of Illusions: the Fusion Monster is destroyed during the End Phase; the rest stays",
                    reached && !p.TryFindMonster(gaia, out _) && p.Graveyard.Contains(gaia) &&
                    p.TryFindMonster(elf, out _) && p.TryFindMonster(summoner, out _),
                    $"reached={reached} gaiaInGy={p.Graveyard.Contains(gaia)}");
            }

            // Player CANCEL: both choices are mandatory and are made automatically (weakest Tribute, strongest Fusion).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.AddRange(new[] { FlameSwordsman, GaiaDragonChampion });
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                e.CancelEffectTargeting();
                e.CancelEffectTargeting();
                Settle(e);
                Check("Summoner of Illusions: CANCEL still Tributes and Special Summons (mandatory effect)",
                    p.Graveyard.Contains(elf) && p.TryFindMonster(celtic, out _) &&
                    p.MonstersOnField().Any(m => m.CardId == GaiaDragonChampion) && !e.IsAwaitingEffectTarget,
                    $"field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
            }

            // AI: Tributes its weakest monster and Special Summons its strongest Fusion Monster.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var o = e.Opponent;
                o.ExtraDeck.Clear();
                o.ExtraDeck.AddRange(new[] { FlameSwordsman, GaiaDragonChampion });
                var summoner = PlaceMonster(e, o, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, o, summoner);
                Settle(e);
                var gaia = o.MonstersOnField().FirstOrDefault(m => m.CardId == GaiaDragonChampion);
                Check("Summoner of Illusions (AI): Tributes Mystical Elf, Special Summons Gaia the Dragon Champion",
                    o.Graveyard.Contains(elf) && o.TryFindMonster(celtic, out _) && gaia != null && gaia.FaceUp &&
                    gaia.TempDestroyOnEndOfTurn == e.TurnNumber && !o.ExtraDeck.Contains(GaiaDragonChampion),
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m.Name))}");
            }

            // No other monster: nothing is Tributed and nothing is Special Summoned.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(GaiaDragonChampion);
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                Check("Summoner of Illusions: with no other monster to Tribute it does nothing",
                    !e.IsAwaitingEffectTarget && p.ExtraDeck.Contains(GaiaDragonChampion) && p.MonsterCount == 1);
            }

            // Summoning conditions (Lua s.filter: IsCanBeSpecialSummoned(e,0,tp,false,false)): a Fusion Monster that
            // "Must be Fusion Summoned" (aux.fuslimit) or "Must first be Special Summoned … by banishing" (contact
            // procedure) cannot be Special Summoned from the Extra Deck by this effect. With only those there,
            // rfilter finds no Tribute and nothing happens.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.AddRange(new[] { DarkPaladin, XyDragonCannon });
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                var offer = e.PendingActivation?.LegalTargets?.Select(t => t.Name).ToList() ?? new List<string>();
                Settle(e);
                Check("Summoner of Illusions: with only Dark Paladin / XY-Dragon Cannon in the Extra Deck nothing is Tributed",
                    p.TryFindMonster(celtic, out _) && p.ExtraDeck.Count == 2 &&
                    !p.MonstersOnField().Any(m => m.CardId == DarkPaladin || m.CardId == XyDragonCannon),
                    $"offer={string.Join(",", offer)} field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
            }

            // Only the Fusion Monster with no summoning condition is offered after the Tribute.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.AddRange(new[] { DarkPaladin, FlameWingman, XyDragonCannon, XyzDragonCannon, FlameSwordsman });
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                var tributed = e.TrySelectEffectTarget(celtic);
                var fusionOffer = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("Summoner of Illusions: Fusion Monsters with a summoning condition are not offered",
                    tributed && fusionOffer.Count == 1 && fusionOffer[0].CardId == FlameSwordsman,
                    $"tributed={tributed} offer={string.Join(",", fusionOffer.Select(f => f.Name))}");
                Settle(e);
            }

            // No Fusion Monster in the Extra Deck: the other monster is not Tributed (Lua rfilter).
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                p.ExtraDeck.Clear();
                var summoner = PlaceMonster(e, p, SummonerOfIllusions, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                MonsterEffects.OnFlipSummoned(e, p, summoner);
                Check("Summoner of Illusions: with no Fusion Monster in the Extra Deck nothing is Tributed",
                    !e.IsAwaitingEffectTarget && p.TryFindMonster(celtic, out _));
            }
        }

        // ───────────────────────────── De-Fusion ─────────────────────────────

        static void DeFusionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Your Fusion Summoned monster: returned to the Extra Deck, then you Summon both materials.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                var df = PutInHand(e, p, DeFusion);
                var activated = e.TryActivateSpellTrap(p, df, fromHand: true);
                PickThenPass(e, fused);
                var offer = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Check("De-Fusion: the Fusion Monster returns to the Extra Deck; you are offered its 2 materials",
                    activated && fused != null && !p.TryFindMonster(fused, out _) && p.ExtraDeck.Contains(FlameSwordsman) &&
                    offer.Count == 2 && offer.Contains(manip) && offer.Contains(masaki) &&
                    SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation),
                    $"activated={activated} fused={fused?.Name} extra={p.ExtraDeck.Contains(FlameSwordsman)} " +
                    $"offer={string.Join(",", offer.Select(t => t.Name))}");
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var a = e.TrySelectEffectTarget(manip);
                var b = e.TrySelectEffectTarget(masaki);
                Settle(e);
                Check("De-Fusion: both materials are Special Summoned (Flame Manipulator face-up Defense)",
                    a && b && p.TryFindMonster(manip, out _) && p.TryFindMonster(masaki, out _) && manip.FaceUp &&
                    manip.Position == BattlePosition.Defense && masaki.FaceUp &&
                    masaki.Position == BattlePosition.Attack && p.Graveyard.Contains(df) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"a={a} b={b} manip={manip.Position}/{manip.FaceUp} masaki={masaki.Position} links={e.Chain.Count}");
            }

            // CANCEL declines the optional Summon: the materials stay in the GY.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                var df = PutInHand(e, p, DeFusion);
                e.TryActivateSpellTrap(p, df, fromHand: true);
                PickThenPass(e, fused);
                var offered = e.IsAwaitingEffectTarget;
                e.CancelEffectTargeting();
                Settle(e);
                Check("De-Fusion: CANCEL keeps the materials in the GY (\"you can\")",
                    offered && p.ExtraDeck.Contains(FlameSwordsman) && p.Graveyard.Contains(manip) &&
                    p.Graveyard.Contains(masaki) && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"offered={offered} links={e.Chain.Count}");
            }

            // A Fusion Monster that was not Fusion Summoned: returned to its owner's Extra Deck, nothing to Summon.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                o.ExtraDeck.Clear();
                var gaia = PlaceMonster(e, o, GaiaDragonChampion, 0, BattlePosition.Attack, true);
                var df = PutInHand(e, p, DeFusion);
                var activated = e.TryActivateSpellTrap(p, df, fromHand: true);
                PickThenPass(e, gaia);
                Check("De-Fusion: the opponent's Fusion Monster goes back to their Extra Deck; no Summon is offered",
                    activated && !o.TryFindMonster(gaia, out _) && o.ExtraDeck.Contains(GaiaDragonChampion) &&
                    !e.IsAwaitingEffectTarget && p.MonsterCount == 0,
                    $"activated={activated} extra={o.ExtraDeck.Contains(GaiaDragonChampion)} pending={e.IsAwaitingEffectTarget}");
            }

            // One material is no longer in the GY: the Fusion Monster still returns, nothing is Summoned.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var fused = FuseFlameSwordsman(e, p, out var manip, out var masaki);
                e.BanishCard(p, masaki);
                var df = PutInHand(e, p, DeFusion);
                e.TryActivateSpellTrap(p, df, fromHand: true);
                PickThenPass(e, fused);
                Check("De-Fusion: with a material missing from the GY nothing is Special Summoned",
                    p.ExtraDeck.Contains(FlameSwordsman) && p.Graveyard.Contains(manip) && !e.IsAwaitingEffectTarget &&
                    p.MonsterCount == 0, $"pending={e.IsAwaitingEffectTarget} field={p.MonsterCount}");
            }

            // No face-up Fusion Monster: De-Fusion cannot be activated.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, e.Opponent, GaiaDragonChampion, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, CelticGuardian, 1, BattlePosition.Attack, true);
                var df = PutInHand(e, p, DeFusion);
                Check("De-Fusion: cannot be activated without a face-up Fusion Monster on the field",
                    !e.CanActivateSpellTrap(p, df, fromHand: true));
            }

            // The AI resolving De-Fusion on its own Fusion Monster takes its materials back.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                var o = e.Opponent;
                ClearSide(o);
                var fused = FuseFlameSwordsman(e, o, out var manip, out var masaki);
                var df = PutInHand(e, o, DeFusion);
                var activated = e.TryActivateSpellTrap(o, df, fromHand: true);
                PickThenPass(e, fused);
                Check("De-Fusion (AI): its Fusion Monster returns and both materials are Special Summoned",
                    reached && activated && fused != null && o.ExtraDeck.Contains(FlameSwordsman) &&
                    o.TryFindMonster(manip, out _) && o.TryFindMonster(masaki, out _) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} activated={activated} field={string.Join(",", o.MonstersOnField().Select(m => m.Name))}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            CompiledCardProgram Compile(int id, string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "ARGON FlipCostsAndFusion near miss", type = type, race = race, desc = desc
                });

            bool Claims(CompiledCardProgram prog, EffectActionKind action) =>
                prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);

            {
                var prog = Compile(990159101, "Flip Effect Monster", "Spellcaster",
                    "FLIP: Select 2 cards from your hand and remove them from play to inflict 800 points of damage to " +
                    "your opponent's Life Points.");
                Check("Near miss: a chosen (not random) hand banish is not the Fire Sorcerer atom",
                    !prog.ClauseList.Exists(c => c != null && c.CostBanishRandomHandCards > 0), Describe(prog));
            }
            {
                var prog = Compile(990159102, "Flip Effect Monster", "Pyro",
                    "FLIP: Banish the top 3 cards of your opponent's Deck; inflict 800 damage to your opponent.");
                Check("Near miss: banishing the opponent's Deck is not the Lady Assailant cost",
                    !prog.ClauseList.Exists(c => c != null && c.CostBanishTopDeckCards > 0), Describe(prog));
            }
            {
                var prog = Compile(990159103, "Flip Effect Monster", "Warrior",
                    "FLIP: Return 2 monsters in your Graveyard to your hand.");
                Check("Near miss: a plain GY return is not stamped as Fusion Material",
                    !prog.ClauseList.Exists(c => c != null && c.RequiresSentAsFusionMaterial), Describe(prog));
            }
            {
                var prog = Compile(990159104, "Flip Effect Monster", "Spellcaster",
                    "FLIP: Tribute 1 other monster, and if you do, Special Summon 1 Fusion Monster from your Extra Deck.");
                Check("Near miss: a Summoner text without the End Phase destroy is not the Summoner atom",
                    !Claims(prog, EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase),
                    Describe(prog));
            }
            {
                var prog = Compile(990159105, "Spell Card", "Quick-Play",
                    "Target 1 Fusion Monster on the field; return that target to the Extra Deck.");
                Check("Near miss: a bare Fusion return is not the De-Fusion atom",
                    !Claims(prog, EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials), Describe(prog));
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

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

        static void ClearSide(DuelistState who)
        {
            who.Hand.Clear();
            for (var i = 0; i < who.MonsterZones.Length; i++) who.MonsterZones[i].Occupant = null;
            for (var i = 0; i < who.SpellTrapZones.Length; i++) who.SpellTrapZones[i].Occupant = null;
            who.Graveyard.Clear();
            who.Banished.Clear();
        }

        /// <summary>Polymerization from the hand: Flame Manipulator + Masaki → Flame Swordsman.</summary>
        static CardInstance FuseFlameSwordsman(DuelEngine e, DuelistState who, out CardInstance manip,
            out CardInstance masaki)
        {
            who.ExtraDeck.Clear();
            who.ExtraDeck.Add(FlameSwordsman);
            manip = PutInHand(e, who, FlameManipulator);
            masaki = PutInHand(e, who, Masaki);
            var poly = PutInHand(e, who, Polymerization);
            e.TryActivateSpellTrap(who, poly, fromHand: true);
            Settle(e);
            return who.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == FlameSwordsman);
        }

        /// <summary>Pass every response window (the AI answers its own).</summary>
        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 20 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>The activation's target pick takes <paramref name="target"/>; response windows pass.</summary>
        static void PickThenPass(DuelEngine e, CardInstance target)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget && !FlipCostsAndFusionRuntime.IsBatchPick(e.PendingActivation) &&
                    e.PendingActivation.LegalTargets.Contains(target))
                {
                    if (!e.TrySelectEffectTarget(target)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }
        }

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/{c.Amount}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
