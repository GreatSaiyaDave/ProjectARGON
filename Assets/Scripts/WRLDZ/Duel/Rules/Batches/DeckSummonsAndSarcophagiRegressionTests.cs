using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DeckSummonsAndSarcophagi batch (AST): Enchanting Fitting Room, Monster Gate, Ninjitsu Art of Transformation,
    /// Human-Wave Tactics, The First / Second / Third Sarcophagus and Spirit of the Pharaoh. Compile shapes, mini-duels
    /// through the real entry points (TryActivateSpellTrap, TrySelectEffectTarget, TryAttack, TryEndTurnSafe) and
    /// fail-closed near misses. Runtime: <see cref="DeckSummonsAndSarcophagiRuntime"/>.
    /// </summary>
    public static class DeckSummonsAndSarcophagiRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FittingRoom = 30531525;
        const int MonsterGate = 43040603;
        const int Ninjitsu = 70861343;
        const int HumanWave = 30353551;
        const int First = 31076103;
        const int Second = 4081094;
        const int Third = 78697395;
        const int Pharaoh = 25343280;

        const int DarkGray = 9159938;          // Lv3 Beast Normal 800/900
        const int Niwatori = 7805359;          // Lv3 Winged Beast Normal 900/800
        const int SkullServant = 32274490;     // Lv1 Zombie Normal 300/200
        const int Celtic = 91152256;           // Lv4 Warrior Normal 1400/1200
        const int MysticalElf = 15025844;      // Lv4 Spellcaster Normal 800/2000
        const int DarkMagician = 46986414;     // Lv7 Normal
        const int PotOfGreed = 55144522;
        const int Raigeki = 12580477;
        const int WallShadow = 63162310;       // "Cannot be Normal Summoned/Set" (Semi-Nomi)
        const int Mst = 5318639;
        const int MonsterReborn = 83764718;
        const int ArmedNinja = 9076207;        // Lv1 "Ninja"
        const int Sasuke = 4041838;            // Ninja Grandmaster Sasuke, Lv4 "Ninja"
        const int NeoBug = 16587243;           // Lv4 Insect
        const int HerculesBeetle = 52584282;   // Lv5 Insect
        const int InsectPrincess = 37957847;   // Lv6 Insect
        const int Kagemusha = 15401633;        // Lv2 Warrior Normal 800/400
        const int HarpieGirl = 34100324;       // Lv2 Winged Beast Normal 500/500
        const int FiendScorpion = 26566878;    // Lv2 Insect Normal 900/200
        const int ArmoredZombie = 20277860;    // Lv3 Zombie Normal 1500/0
        const int PharaohsServant = 52550973;  // Lv2 Zombie Normal 900/0
        const int PharaonicProtector = 89959682; // Lv2 Zombie Normal 900/0

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DeckSummonsAndSarcophagi: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            FittingRoomDuels(db, pDeck, aDeck, Check);
            MonsterGateDuels(db, pDeck, aDeck, Check);
            NinjitsuDuels(db, pDeck, aDeck, Check);
            HumanWaveDuels(db, pDeck, aDeck, Check);
            SarcophagusDuels(db, pDeck, aDeck, Check);
            PharaohRules(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static EffectClause Only(CardDatabase db, int id, EffectActionKind action, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.FullyCompiled
                ? prog.ClauseList.FirstOrDefault(c => c != null && c.Action == action)
                : null;
        }

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var fr = Only(db, FittingRoom, EffectActionKind.ExcavateTopSpecialSummonLowLevelNormalsShuffleRest, out var frp);
            Check("Enchanting Fitting Room: pay 800, excavate 4, Level 3 or lower Normal Monsters",
                fr != null && frp.ClauseList.Count == 1 && fr.Timing == EffectTiming.Activate && fr.PayLpAmount == 800 &&
                fr.Amount == 4 && fr.SummonLevelMax == 3 && fr.RequiresNormalMonster &&
                CardEffectStatus.Classify(db.Get(FittingRoom)) == CardEffectStatusKind.Implemented,
                $"clauses={frp?.ClauseList.Count}");

            var mg = Only(db, MonsterGate, EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy, out var mgp);
            Check("Monster Gate: Tribute 1 (cost), excavate until a Normal Summonable monster",
                mg != null && mgp.ClauseList.Count == 1 && mg.RequiresTributeCount == 1 && !mg.RequiresTargetChoice &&
                CardEffectStatus.Classify(db.Get(MonsterGate)) == CardEffectStatusKind.Implemented,
                $"clauses={mgp?.ClauseList.Count}");

            var nj = Only(db, Ninjitsu, EffectActionKind.SpecialSummonRaceLevelUpToTributedPlusFromHandOrDeckLinked, out var njp);
            Check("Ninjitsu Art of Transformation: Tribute 1 face-up Ninja, Beast/Winged Beast/Insect Level <= L+3, linked",
                nj != null && njp.ClauseList.Count == 1 && nj.RequiresTributeCount == 1 && nj.TributeFaceUpOnly &&
                nj.TributeSeriesName == "Ninja" && nj.RaceFilter == "Beast|Winged Beast|Insect" &&
                nj.TributedLevelOffset == 3 && nj.DestroyHostWhenThisLeaves && nj.StaysOnField &&
                CardEffectStatus.Classify(db.Get(Ninjitsu)) == CardEffectStatusKind.Implemented,
                $"clauses={njp?.ClauseList.Count} races={nj?.RaceFilter}");

            var hw = Only(db, HumanWave, EffectActionKind.SpecialSummonLowLevelNormalsFromDeckPerOwnBattleDestroyedAtEndPhase,
                out var hwp);
            Check("Human-Wave Tactics: every End Phase, Level 2 or lower Normal Monsters per battle record",
                hw != null && hwp.ClauseList.Count == 1 && hw.Timing == EffectTiming.EndPhase && hw.FiresAtEachPlayersPhase &&
                hw.SummonLevelMax == 2 && hw.RequiresNormalMonster &&
                CardEffectStatus.Classify(db.Get(HumanWave)) == CardEffectStatusKind.Implemented,
                $"clauses={hwp?.ClauseList.Count}");

            var fp = CardTextEffectCompiler.Compile(db.Get(First));
            var place = fp?.ClauseList.FirstOrDefault(c => c?.Action == EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck);
            var send = fp?.ClauseList.FirstOrDefault(c => c?.Action == EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField);
            var rider = fp?.ClauseList.FirstOrDefault(c =>
                c?.Action == EffectActionKind.SendThisAndPlacedToGyThenSpecialSummonNamedFromHandOrDeck);
            Check("The First Sarcophagus: opponent's End Phase placement Second → Third, leave-field send, Pharaoh summon",
                fp != null && fp.FullyCompiled && fp.ClauseList.Count == 3 && place != null && place.OpponentTurnOnly &&
                place.OncePerTurn && place.NamedCard == "The Second Sarcophagus|The Third Sarcophagus" && send != null &&
                send.NamedCard == place.NamedCard && rider != null && rider.NamedCard == "Spirit of the Pharaoh" &&
                rider.Amount == 3 && CardEffectStatus.Classify(db.Get(First)) == CardEffectStatusKind.Implemented,
                $"clauses={fp?.ClauseList.Count}");

            Check("The Second / Third Sarcophagus: Structural through The First's placement",
                CardEffectStatus.Classify(db.Get(Second)) == CardEffectStatusKind.Structural &&
                CardEffectStatus.Classify(db.Get(Third)) == CardEffectStatusKind.Structural &&
                DestinyBoardRuntime.PlacerOf(db.Get(Second)) == "The First Sarcophagus" &&
                DestinyBoardRuntime.HasLivePlacementPath(db.Get(Third)));

            var ph = Only(db, Pharaoh, EffectActionKind.SpecialSummonUpToTargetedLowLevelRaceNormalsFromGy, out var php);
            Check("Spirit of the Pharaoh: optional SS trigger, up to 4 Level 2 or lower Zombie Normals from the GY",
                ph != null && php.ClauseList.Count == 1 && ph.Timing == EffectTiming.ThisCardSummoned &&
                ph.RequiresThisSpecialSummoned && ph.IsOptional && ph.Amount == 4 && ph.SummonLevelMax == 2 &&
                ph.RaceFilter == "Zombie" && ph.RequiresNormalMonster &&
                DeckSummonsAndSarcophagiRuntime.NomiSummonerOf(db.Get(Pharaoh).desc) == "The First Sarcophagus" &&
                CardEffectStatus.Classify(db.Get(Pharaoh)) == CardEffectStatusKind.Implemented,
                $"clauses={php?.ClauseList.Count}");

            // Near misses: look-alike sentences stay out of this batch's actions.
            static CardDef Fake(CardDef from, string desc) => new CardDef
            {
                id = 0, name = from.name + " (variant)", type = from.type, frameType = from.frameType, desc = desc,
                atk = from.atk, def = from.def, level = from.level, race = from.race, attribute = from.attribute
            };
            bool Has(CardDef d, EffectActionKind a)
            {
                var p = CardTextEffectCompiler.Compile(d);
                return p != null && p.ClauseList.Exists(c => c != null && c.Action == a);
            }

            var frDef = db.Get(FittingRoom);
            var mgDef = db.Get(MonsterGate);
            var phDef = db.Get(Pharaoh);
            Check("Near misses: 'monsters' without Normal, an any-monster excavation, a Level-unbounded revival do not compile here",
                !Has(Fake(frDef, "Pay 800 LP; excavate the top 4 cards of your Deck and Special Summon all excavated Level 3 " +
                                 "or lower monsters, also, after that, shuffle the rest back into your Deck."),
                    EffectActionKind.ExcavateTopSpecialSummonLowLevelNormalsShuffleRest) &&
                !Has(Fake(mgDef, "Tribute 1 monster; excavate cards from the top of your Deck until you excavate a monster. " +
                                 "Special Summon it, also send the other excavated cards to the GY."),
                    EffectActionKind.ExcavateUntilNormalSummonableSpecialSummonItSendRestToGy) &&
                !Has(Fake(phDef, "When this card is Special Summoned, you can Special Summon up to 4 Zombie-Type monsters " +
                                 "from your Graveyard."),
                    EffectActionKind.SpecialSummonUpToTargetedLowLevelRaceNormalsFromGy));

            // The Nomi rule is live only while X compiles to the summon that names the card.
            var orphan = Fake(phDef, "This card cannot be Normal Summoned or Set. This card cannot be Special Summoned " +
                                     "except by the effect of \"Pot of Greed\". When this card is Special Summoned, you can " +
                                     "Special Summon up to 4 Level 2 or lower Zombie-Type Normal Monsters from your Graveyard.");
            Check("Spirit of the Pharaoh rule: a named card that does not compile to its summon leaves the card not Implemented",
                DeckSummonsAndSarcophagiRuntime.NomiSummonerOf(orphan.desc) == "Pot of Greed" &&
                !DeckSummonsAndSarcophagiRuntime.NamedEffectSummonIsLive(orphan) &&
                DeckSummonsAndSarcophagiRuntime.NamedEffectSummonIsLive(phDef));
        }

        // ───────────────────────────── Enchanting Fitting Room ─────────────────────────────

        static void FittingRoomDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Human, 2 free zones, 3 qualifying monsters: choose 2 (one in Defense), the third to the GY; the rest stay.
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { DarkGray, Celtic, Niwatori, SkullServant, MysticalElf, DarkMagician, Celtic, Celtic });
                for (var z = 0; z < 3; z++) PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
                var card = PutInHand(e, p, FittingRoom);
                var lp = p.LifePoints;
                var can = e.CanActivateSpellTrap(p, card, fromHand: true);
                var act = can && e.TryActivateSpellTrap(p, card, fromHand: true);
                PassResponses(e);
                var legal = Legal(e);
                var offered = e.IsAwaitingEffectTarget && FlipMassEffectsRuntime.IsMassSummonPick(e.PendingActivation) &&
                              legal.Count == 3 && legal.All(c => c.CardId == DarkGray || c.CardId == Niwatori ||
                                                                  c.CardId == SkullServant);
                var gray = legal.FirstOrDefault(c => c.CardId == DarkGray);
                var bird = legal.FirstOrDefault(c => c.CardId == Niwatori);
                var skull = legal.FirstOrDefault(c => c.CardId == SkullServant);
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var p1 = gray != null && e.TrySelectEffectTarget(gray);
                var p2 = bird != null && e.TrySelectEffectTarget(bird);
                Drive(e, _ => true);
                Check("Enchanting Fitting Room: 2 zones for 3 — the chosen 2 face-up (one in Defense), the third to the GY",
                    reached && act && offered && p1 && p2 && p.LifePoints == lp - 800 &&
                    p.TryFindMonster(gray, out _) && gray.FaceUp && gray.Position == BattlePosition.Defense &&
                    p.TryFindMonster(bird, out _) && bird.FaceUp && bird.Position == BattlePosition.Attack &&
                    p.Graveyard.Contains(skull) && p.Deck.Count == 5 && p.Deck.Count(id => id == Celtic) == 3 &&
                    p.Deck.Contains(MysticalElf) && p.Graveyard.Contains(card) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} can={can} act={act} offered={offered} legal={Names(legal)} p1={p1} p2={p2} " +
                    $"lp={p.LifePoints} deck={p.Deck.Count} gray={Where(e, gray)} bird={Where(e, bird)} skull={Where(e, skull)}");
            }

            // AI, every zone free: every qualifying monster face-up, the others back in the Deck.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { SkullServant, Celtic, DarkGray, Raigeki, DarkMagician, Celtic });
                var card = PutInHand(e, o, FittingRoom);
                var act = e.TryActivateSpellTrap(o, card, fromHand: true);
                Drive(e, _ => true);
                Check("Enchanting Fitting Room (AI): both qualifying monsters face-up, Celtic and Raigeki back in the Deck",
                    reached && act && OnFieldFaceUp(o, SkullServant) && OnFieldFaceUp(o, DarkGray) &&
                    o.MonstersOnField().Count(m => m != null) == 2 && o.Deck.Count == 4 && o.Deck.Contains(Raigeki) &&
                    o.Deck.Contains(Celtic) && o.Graveyard.Contains(card) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} act={act} field={Names(o.MonstersOnField().Where(m => m != null))} deck={o.Deck.Count}");
            }

            // Fail closed: 3 cards in the Deck, or no free Monster Zone — cannot be activated.
            {
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { DarkGray, Niwatori, SkullServant });
                var card = PutInHand(e, p, FittingRoom);
                var small = !e.CanActivateSpellTrap(p, card, fromHand: true);
                p.Deck.Add(Celtic);
                for (var z = 0; z < 5; z++) PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
                var full = !e.CanActivateSpellTrap(p, card, fromHand: true);
                Check("Enchanting Fitting Room: fewer than 4 cards in the Deck, or a full field — cannot activate", small && full,
                    $"small={small} full={full}");
            }
        }

        // ───────────────────────────── Monster Gate ─────────────────────────────

        static void MonsterGateDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { PotOfGreed, Raigeki, WallShadow, MysticalElf, DarkMagician });
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var card = PutInHand(e, p, MonsterGate);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                var cost = e.IsAwaitingEffectTarget && e.PendingActivation.AwaitingIgnitionCost &&
                           Legal(e).Count == 1 && Legal(e)[0] == celtic;
                var paid = cost && e.TrySelectEffectTarget(celtic);
                PassResponses(e);
                var offer = Legal(e);
                var asked = e.IsAwaitingEffectTarget && offer.Count == 1 && offer[0].CardId == MysticalElf;
                Drive(e, _ => true);
                var elf = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == MysticalElf);
                Check("Monster Gate: Tribute (cost), the cards above the first Normal Summonable monster to the GY, it is summoned",
                    reached && act && cost && paid && asked && elf != null && elf.FaceUp && p.Graveyard.Contains(celtic) &&
                    p.Graveyard.Any(g => g.CardId == PotOfGreed) && p.Graveyard.Any(g => g.CardId == Raigeki) &&
                    p.Graveyard.Any(g => g.CardId == WallShadow) && p.Deck.Count == 1 && p.Deck[0] == DarkMagician &&
                    p.Graveyard.Contains(card),
                    $"reached={reached} act={act} cost={cost} paid={paid} asked={asked} offer={Names(offer)} " +
                    $"gy={Names(p.Graveyard)} deck={p.Deck.Count}");
            }

            // AI on a full field: the Tribute frees the zone.
            {
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Raigeki, Celtic, DarkMagician });
                for (var z = 0; z < 5; z++) PlaceMonster(e, o, Kagemusha, z, BattlePosition.Attack, true);
                var card = PutInHand(e, o, MonsterGate);
                var can = e.CanActivateSpellTrap(o, card, fromHand: true);
                var act = can && e.TryActivateSpellTrap(o, card, fromHand: true);
                Drive(e, _ => true);
                Check("Monster Gate (AI): a full field is fine — the Tribute frees the zone for Celtic",
                    reached && act && OnFieldFaceUp(o, Celtic) && o.MonstersOnField().Count(m => m != null) == 5 &&
                    o.Graveyard.Any(g => g.CardId == Raigeki) && o.Graveyard.Any(g => g.CardId == Kagemusha) &&
                    o.Deck.Count == 1,
                    $"reached={reached} can={can} act={act} field={Names(o.MonstersOnField().Where(m => m != null))} " +
                    $"gy={Names(o.Graveyard)}");
            }

            // Fail closed: no monster in the Deck that can be Normal Summoned/Set, or nothing to Tribute.
            {
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { PotOfGreed, WallShadow, Raigeki });
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var card = PutInHand(e, p, MonsterGate);
                var noTarget = !e.CanActivateSpellTrap(p, card, fromHand: true);
                p.Deck.Add(MysticalElf);
                var withElf = e.CanActivateSpellTrap(p, card, fromHand: true);
                ClearBoard(e);
                var noTribute = !e.CanActivateSpellTrap(p, card, fromHand: true);
                Check("Monster Gate: no Normal Summonable monster in the Deck, or no Tribute — cannot activate",
                    noTarget && withElf && noTribute, $"noTarget={noTarget} withElf={withElf} noTribute={noTribute}");
            }
        }

        // ───────────────────────────── Ninjitsu Art of Transformation ─────────────────────────────

        static void NinjitsuDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            CardInstance trap = null, bug = null;
            DuelEngine e = null;
            {
                e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { HerculesBeetle, NeoBug, Celtic, Celtic });
                var ninja = PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                trap = PlaceSetTrap(e, p, Ninjitsu, 0);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                var cost = Legal(e);
                var costOk = e.IsAwaitingEffectTarget && cost.Count == 1 && cost[0] == ninja;
                var paid = costOk && e.TrySelectEffectTarget(ninja);
                PassResponses(e);
                var offer = Legal(e);
                var asked = e.IsAwaitingEffectTarget && offer.Count == 1 && offer[0].CardId == NeoBug;
                e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Defense);
                var picked = asked && e.TrySelectEffectTarget(offer[0]);
                Drive(e, _ => true);
                bug = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == NeoBug);
                Check("Ninjitsu Art: Tribute the face-up Ninja (Lv1), only a Level 4 or lower Insect (Neo Bug), linked",
                    reached && act && costOk && paid && asked && picked && bug != null && bug.FaceUp &&
                    bug.Position == BattlePosition.Defense && p.Graveyard.Contains(ninja) && p.TryFindMonster(celtic, out _) &&
                    p.TryFindSpellTrap(trap, out _) && trap.FaceUp && trap.EquippedTo == bug && p.Deck.Contains(HerculesBeetle),
                    $"reached={reached} act={act} cost={Names(cost)} paid={paid} offer={Names(offer)} picked={picked} " +
                    $"bug={Where(e, bug)} trap={Where(e, trap)} link={trap.EquippedTo?.Name ?? "-"}");

                // This card leaves the field: that monster is destroyed.
                TextEffectRuntime.DestroyCard(e, trap, null);
                Check("Ninjitsu Art: when it leaves the field, the summoned monster is destroyed",
                    p.Graveyard.Contains(trap) && p.Graveyard.Contains(bug) && !p.TryFindMonster(bug, out _),
                    $"trap={Where(e, trap)} bug={Where(e, bug)}");
            }

            // The monster leaves first: the Trap stays face-up, unlinked.
            {
                e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, Celtic, Celtic });
                PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Attack, true);
                trap = PlaceSetTrap(e, p, Ninjitsu, 0);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Drive(e, _ => true);
                bug = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == NeoBug);
                if (bug != null) TextEffectRuntime.DestroyCard(e, bug, null);
                Check("Ninjitsu Art: the monster destroyed first — the Trap stays on the field",
                    act && bug != null && p.Graveyard.Contains(bug) && p.TryFindSpellTrap(trap, out _) && trap.FaceUp &&
                    trap.EquippedTo == null,
                    $"act={act} bug={Where(e, bug)} trap={Where(e, trap)}");
            }

            // CANCEL at the Tribute pick: nothing is Tributed and the Trap stays Set.
            {
                e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, Celtic });
                var ninja = PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Attack, true);
                var t = PlaceSetTrap(e, p, Ninjitsu, 0);
                var act = e.TryActivateSpellTrap(p, t, fromHand: false);
                var asked = e.IsAwaitingEffectTarget && Legal(e).Contains(ninja);
                var cancelled = asked && e.CancelEffectTargeting();
                Drive(e, _ => false);
                Check("Ninjitsu Art: CANCEL at the Tribute — the Ninja stays, the Trap stays Set, nothing is summoned",
                    act && asked && cancelled && p.TryFindMonster(ninja, out _) && p.TryFindSpellTrap(t, out _) && !t.FaceUp &&
                    p.Deck.Contains(NeoBug) && !e.Chain.HasLinks,
                    $"act={act} asked={asked} cancelled={cancelled} ninja={Where(e, ninja)} trap={Where(e, t)}");
            }

            // A free-chain Trap: it answers the opponent's attack, Tributing the attacked Ninja as it is activated.
            {
                e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, Celtic, Celtic });
                var ninja = PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var t = PlaceSetTrap(e, p, Ninjitsu, 0);
                e.NotifyPublic();
                var atk = e.TryAttack(o, celtic, ninja);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, t, fromHand: false);
                var cost = Legal(e);
                var paid = e.IsAwaitingEffectTarget && cost.Count == 1 && cost[0] == ninja && e.TrySelectEffectTarget(ninja);
                Drive(e, c => c.CardId == NeoBug);
                DrainCombat(e);
                var summoned = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == NeoBug);
                Check("Ninjitsu Art: answers an attack — Tribute paid as it is activated, Neo Bug summoned and linked",
                    battle && atk && window && act && paid && summoned != null && summoned.FaceUp &&
                    p.Graveyard.Contains(ninja) && t.EquippedTo == summoned && p.TryFindSpellTrap(t, out _) &&
                    !e.IsAwaitingEffectTarget,
                    $"battle={battle} atk={atk} window={window} act={act} cost={Names(cost)} paid={paid} " +
                    $"bug={Where(e, summoned)} trap={Where(e, t)}");
            }

            // AI: Sasuke (Lv4) → Level 7 or lower; the strongest from the hand or Deck (Insect Princess in the hand).
            {
                e = OpponentMain(db, pDeck, aDeck, out var reached);
                var o = e.Opponent;
                o.Deck.Clear();
                o.Deck.AddRange(new[] { HerculesBeetle, Celtic, Celtic });
                var sasuke = PlaceMonster(e, o, Sasuke, 0, BattlePosition.Attack, true);
                var princess = PutInHand(e, o, InsectPrincess);
                var t = PlaceSetTrap(e, o, Ninjitsu, 0);
                var act = e.TryActivateSpellTrap(o, t, fromHand: false);
                Drive(e, _ => true);
                Check("Ninjitsu Art (AI): Sasuke Tributed, Insect Princess (Lv6) from the hand, linked",
                    reached && act && o.Graveyard.Contains(sasuke) && o.TryFindMonster(princess, out _) && princess.FaceUp &&
                    t.EquippedTo == princess && o.Deck.Contains(HerculesBeetle),
                    $"reached={reached} act={act} princess={Where(e, princess)} sasuke={Where(e, sasuke)}");
            }

            // Fail closed: a face-down Ninja, a non-Ninja, or no Type/Level match in the hand or Deck.
            {
                e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, Celtic });
                var ninja = PlaceMonster(e, p, ArmedNinja, 0, BattlePosition.Defense, false);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var t = PlaceSetTrap(e, p, Ninjitsu, 0);
                var faceDown = !e.CanActivateSpellTrap(p, t, fromHand: false);
                ninja.FaceUp = true;
                ninja.Position = BattlePosition.Attack;
                var faceUp = e.CanActivateSpellTrap(p, t, fromHand: false);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { HerculesBeetle, Celtic });
                var noMatch = !e.CanActivateSpellTrap(p, t, fromHand: false);
                Check("Ninjitsu Art: needs a face-up Ninja and a matching Beast / Winged Beast / Insect",
                    faceDown && faceUp && noMatch, $"faceDown={faceDown} faceUp={faceUp} noMatch={noMatch}");
            }
        }

        // ───────────────────────────── Human-Wave Tactics ─────────────────────────────

        static void HumanWaveDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The Continuous Traps' activation only places them (Human-Wave Tactics, The First Sarcophagus).
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var hw = PlaceSetTrap(e, p, HumanWave, 0);
                var first = PlaceSetTrap(e, p, First, 1);
                var a1 = e.TryActivateSpellTrap(p, hw, fromHand: false);
                Drive(e, _ => true);
                var a2 = e.TryActivateSpellTrap(p, first, fromHand: false);
                Drive(e, _ => true);
                Check("Human-Wave Tactics / The First Sarcophagus: activated from the Set, they stay face-up",
                    reached && a1 && a2 && p.TryFindSpellTrap(hw, out _) && hw.FaceUp && p.TryFindSpellTrap(first, out _) &&
                    first.FaceUp && !e.IsAwaitingEffectTarget && DestinyBoardRuntime.PlacedCount(e, first) == 0,
                    $"reached={reached} a1={a1} a2={a2} hw={Where(e, hw)} first={Where(e, first)}");
            }

            // The human's Kagemusha (Lv2 Normal) destroyed by battle: in that End Phase, 1 chosen from the Deck.
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var hw = PlaceFaceUp(e, p, HumanWave, 0);
                var kage = PlaceMonster(e, p, Kagemusha, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, HarpieGirl, FiendScorpion, ArmoredZombie });
                e.NotifyPublic();
                var atk = e.TryAttack(o, celtic, kage);
                DrainCombat(e);
                var counted = DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, p, 2) == 1;
                e.TryEndTurnSafe(o);
                var offer = Legal(e);
                var asked = e.IsAwaitingEffectTarget && e.Phase == DuelPhase.End && offer.Count == 2 &&
                            offer.All(c => c.CardId == HarpieGirl || c.CardId == FiendScorpion);
                var scorpion = offer.FirstOrDefault(c => c.CardId == FiendScorpion);
                var chose = scorpion != null && e.TrySelectEffectTarget(scorpion);
                Drive(e, _ => true);
                Check("Human-Wave Tactics: 1 Level 2 Normal destroyed by battle → 1 chosen from the Deck at the End Phase",
                    battle && atk && counted && asked && chose && p.TryFindMonster(scorpion, out _) && scorpion.FaceUp &&
                    InDeckOrHand(p, HarpieGirl) && InDeckOrHand(p, ArmoredZombie) && p.Graveyard.Contains(kage) &&
                    p.TryFindSpellTrap(hw, out _) && e.TurnPlayer == p && !e.IsAwaitingEffectTarget,
                    $"battle={battle} atk={atk} counted={counted} asked={asked} offer={Names(offer)} chose={chose} " +
                    $"scorpion={Where(e, scorpion)} turn={e.TurnPlayer?.Name} phase={e.Phase}");
            }

            // Both battlers destroyed (Lv2 vs Lv2): each player's copy counts its own; one free zone for two picks.
            {
                var e = Fresh(db, pDeck, aDeck);
                var battle = ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceFaceUp(e, p, HumanWave, 0);
                PlaceFaceUp(e, o, HumanWave, 0);
                var pk = PlaceMonster(e, p, Kagemusha, 0, BattlePosition.Attack, true);
                var ok1 = PlaceMonster(e, o, Kagemusha, 0, BattlePosition.Attack, true);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { HarpieGirl, Celtic });
                o.Deck.Clear();
                o.Deck.AddRange(new[] { FiendScorpion, Celtic, Celtic, Celtic });
                e.NotifyPublic();
                var atk = e.TryAttack(o, ok1, pk);
                DrainCombat(e);
                var both = DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, p, 2) == 1 &&
                           DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, o, 2) == 1;
                e.TryEndTurnSafe(o);
                Drive(e, _ => true);
                Check("Human-Wave Tactics: a mutual destruction counts for each controller — both summon 1",
                    battle && atk && both && OnFieldFaceUp(p, HarpieGirl) && OnFieldFaceUp(o, FiendScorpion) &&
                    p.Graveyard.Contains(pk) && o.Graveyard.Contains(ok1),
                    $"battle={battle} atk={atk} both={both} p={Names(p.MonstersOnField().Where(m => m != null))} " +
                    $"o={Names(o.MonstersOnField().Where(m => m != null))}");
            }

            // A Level 3 Normal destroyed by battle counts for nothing.
            {
                var e = Fresh(db, pDeck, aDeck);
                ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceFaceUp(e, p, HumanWave, 0);
                var zombie = PlaceMonster(e, p, ArmoredZombie, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { HarpieGirl, FiendScorpion });
                e.NotifyPublic();
                var atk = e.TryAttack(o, celtic, zombie);
                DrainCombat(e);
                e.TryEndTurnSafe(o);
                Drive(e, _ => true);
                Check("Human-Wave Tactics: a Level 3 Normal Monster destroyed by battle — nothing is summoned",
                    atk && p.Graveyard.Contains(zombie) && p.MonstersOnField().All(m => m == null) &&
                    p.Deck.Count + p.Hand.Count == 2,
                    $"atk={atk} zombie={Where(e, zombie)} deck={p.Deck.Count} hand={p.Hand.Count}");
            }

            // Two destroyed, one free zone: 2 selected, 1 summoned, the other to the GY (AI).
            {
                var e = Fresh(db, pDeck, aDeck);
                ReachOpponentBattle(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                PlaceFaceUp(e, o, HumanWave, 0);
                var k1 = PlaceMonster(e, o, Kagemusha, 0, BattlePosition.Defense, true);
                var k2 = PlaceMonster(e, o, Kagemusha, 1, BattlePosition.Defense, true);
                for (var z = 2; z < 5; z++) PlaceMonster(e, o, Celtic, z, BattlePosition.Defense, true);
                o.Deck.Clear();
                o.Deck.AddRange(new[] { HarpieGirl, FiendScorpion, Celtic, Celtic });
                // The opponent's monsters are destroyed in the human's battles: reach the human's Battle Phase.
                e.TryEndTurnSafe(o);
                Drive(e, _ => true);
                var inBattle = e.TurnPlayer == p && e.TryEnterBattlePhase(p);
                var a1 = PlaceMonster(e, p, DarkMagician, 0, BattlePosition.Attack, true);
                var a2 = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var hit1 = e.TryAttack(p, a1, k1);
                DrainCombat(e);
                var hit2 = e.TryAttack(p, a2, k2);
                DrainCombat(e);
                var two = DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, o, 2) == 2;
                PlaceMonster(e, o, Celtic, 0, BattlePosition.Defense, true); // one zone left
                e.TryEndTurnSafe(p);
                Drive(e, _ => true);
                var summoned = o.MonstersOnField().Count(m => m != null && (m.CardId == HarpieGirl || m.CardId == FiendScorpion));
                var sent = o.Graveyard.Count(g => g.CardId == HarpieGirl || g.CardId == FiendScorpion);
                Check("Human-Wave Tactics (AI): 2 destroyed, 1 free zone — 1 summoned, the other selected one to the GY",
                    inBattle && hit1 && hit2 && two && summoned == 1 && sent == 1 && o.Deck.Count + o.Hand.Count == 2,
                    $"inBattle={inBattle} hit1={hit1} hit2={hit2} two={two} summoned={summoned} sent={sent} " +
                    $"deck={o.Deck.Count} hand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── The Sarcophagi ─────────────────────────────

        static void SarcophagusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The whole sequence for the human: Second, then Third, in the opponent's End Phases; then all three to the
            // GY and Spirit of the Pharaoh from the Deck; its trigger revives the Level 2 or lower Zombie Normals.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, Second, Celtic, Third, Celtic, Pharaoh, Celtic, Celtic, Celtic, Celtic });
                o.Deck.Clear();
                o.Deck.AddRange(Enumerable.Repeat(Celtic, 12));
                var first = PlaceFaceUp(e, p, First, 0);
                var servant = Gy(e, p, PharaohsServant);
                var skull = Gy(e, p, SkullServant);
                var armored = Gy(e, p, ArmoredZombie);
                var cannotSet = !e.CanSetSpellTrap(p, PutInHand(e, p, Second));
                p.Hand.Clear();
                e.NotifyPublic();
                var trail = new List<string>();
                CardInstance second = null, third = null;
                for (var i = 0; i < 4 && !e.GameOver; i++)
                {
                    var tp = e.TurnPlayer;
                    e.TryEndTurnSafe(tp);
                    Drive(e, _ => true);
                    second ??= p.SpellTrapsOnField().FirstOrDefault(c => c != null && c.CardId == Second);
                    third ??= p.SpellTrapsOnField().FirstOrDefault(c => c != null && c.CardId == Third);
                    trail.Add($"t{i + 1}:{(tp == p ? "P" : "O")}:{DestinyBoardRuntime.PlacedCount(e, first)}:" +
                              $"{Names(p.SpellTrapsOnField().Where(c => c != null))}");
                }

                var pharaoh = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == Pharaoh);
                // The Third is placed and sent in the same End Phase.
                third ??= p.Graveyard.FirstOrDefault(c => c.CardId == Third);
                Check("The First Sarcophagus: Second, then Third (opponent's End Phases), then all 3 to the GY → Pharaoh",
                    cannotSet && second != null && third != null && p.Graveyard.Contains(first) && p.Graveyard.Contains(second) &&
                    p.Graveyard.Contains(third) && pharaoh != null && pharaoh.FaceUp && pharaoh.ProcedureCompleted &&
                    !p.Deck.Contains(Pharaoh) && !p.Deck.Contains(Second) && !p.Deck.Contains(Third),
                    $"cannotSet={cannotSet} trail=[{string.Join(" ", trail)}] first={Where(e, first)} second={Where(e, second)} " +
                    $"third={Where(e, third)} pharaoh={Where(e, pharaoh)}");
                Check("Spirit of the Pharaoh: its trigger revives the Level 2 or lower Zombie Normals (not the Level 3)",
                    pharaoh != null && p.TryFindMonster(servant, out _) && servant.FaceUp && p.TryFindMonster(skull, out _) &&
                    skull.FaceUp && p.Graveyard.Contains(armored),
                    $"servant={Where(e, servant)} skull={Where(e, skull)} armored={Where(e, armored)}");
            }

            // Leave-field send: the Second destroyed → The First goes too; The First destroyed → the Second goes.
            foreach (var destroyFirst in new[] { false, true })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
                o.Deck.Clear();
                o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
                var first = PlaceFaceUp(e, p, First, 0);
                var second = PlaceFaceUp(e, p, Second, 1);
                var other = PlaceSetTrap(e, p, Mst, 2);
                e.NotifyPublic();
                var reached = ReachOpponentMain(e);
                var mst = PutInHand(e, o, Mst);
                var victim = destroyFirst ? first : second;
                var act = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
                Drive(e, c => c == victim);
                Check(destroyFirst
                        ? "The First Sarcophagus destroyed: The Second goes to the GY too"
                        : "The Second Sarcophagus destroyed: The First goes to the GY too",
                    act && p.Graveyard.Contains(first) && p.Graveyard.Contains(second) && p.TryFindSpellTrap(other, out _),
                    $"reached={reached} act={act} first={Where(e, first)} second={Where(e, second)} other={Where(e, other)}");
            }

            // AI: its First places in the human's End Phases, and the Pharaoh comes from its hand.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.AddRange(Enumerable.Repeat(Celtic, 12));
                o.Deck.Clear();
                o.Deck.AddRange(new[] { Celtic, Second, Celtic, Third, Celtic, Celtic, Celtic, Celtic });
                var first = PlaceFaceUp(e, o, First, 0);
                var inHand = PutInHand(e, o, Pharaoh);
                e.NotifyPublic();
                for (var i = 0; i < 3 && !e.GameOver; i++)
                {
                    e.TryEndTurnSafe(e.TurnPlayer);
                    Drive(e, _ => true);
                }

                Check("The First Sarcophagus (AI): placed in the human's End Phases, Pharaoh Special Summoned from the hand",
                    o.Graveyard.Contains(first) && o.TryFindMonster(inHand, out _) && inHand.FaceUp &&
                    !e.IsAwaitingEffectTarget,
                    $"first={Where(e, first)} pharaoh={Where(e, inHand)} st={Names(o.SpellTrapsOnField().Where(c => c != null))}");
            }

            // Fewer free zones than the placed set wants: the summon is refused when no Monster Zone is free.
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Second, Third, Pharaoh, Celtic, Celtic, Celtic, Celtic, Celtic });
                o.Deck.Clear();
                o.Deck.AddRange(Enumerable.Repeat(Celtic, 12));
                var first = PlaceFaceUp(e, p, First, 0);
                for (var z = 0; z < 5; z++) PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
                e.NotifyPublic();
                for (var i = 0; i < 4 && !e.GameOver; i++)
                {
                    e.TryEndTurnSafe(e.TurnPlayer);
                    Drive(e, _ => true);
                }

                var second = p.Graveyard.FirstOrDefault(c => c.CardId == Second);
                var third = p.Graveyard.FirstOrDefault(c => c.CardId == Third);
                Check("The First Sarcophagus: all 3 face-up but no free Monster Zone — sent, no Pharaoh",
                    second != null && third != null && p.Graveyard.Contains(first) &&
                    InDeckOrHand(p, Pharaoh) && p.MonstersOnField().All(m => m == null || m.CardId != Pharaoh),
                    $"first={Where(e, first)} second={Where(e, second)} third={Where(e, third)} deckPharaoh={p.Deck.Contains(Pharaoh)}");
            }
        }

        // ───────────────────────────── Spirit of the Pharaoh rules ─────────────────────────────

        static void PharaohRules(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var inHand = PutInHand(e, p, Pharaoh);
            var noNormal = !e.CanNormalSummonOrSet(p, inHand);
            p.Hand.Clear();
            var inGy = Gy(e, p, Pharaoh);
            inGy.ProcedureCompleted = true; // even a properly summoned copy
            var reborn = PutInHand(e, p, MonsterReborn);
            var legal = !SpecialSummonLegality.CanBeSpecialSummoned(e, p, inGy, reborn, SummonOrigin.Graveyard);
            var act = e.CanActivateSpellTrap(p, reborn, fromHand: true) && e.TryActivateSpellTrap(p, reborn, fromHand: true);
            Drive(e, _ => true);
            var direct = !e.SpecialSummonToField(p, inGy, BattlePosition.Attack, true, SummonKind.SpecialSummon, -1);
            Check("Spirit of the Pharaoh: no Normal Summon, and no Special Summon but The First Sarcophagus's (Monster Reborn)",
                reached && noNormal && legal && !p.MonstersOnField().Any(m => m == inGy) && direct,
                $"reached={reached} noNormal={noNormal} legal={legal} rebornAct={act} direct={direct} pharaoh={Where(e, inGy)}");

            // The trigger with fewer free zones than candidates: at most that many targets; CANCEL first declines.
            {
                var e2 = PlayerMain(db, pDeck, aDeck, out _);
                var p2 = e2.Player;
                var s1 = Gy(e2, p2, SkullServant);
                var s2 = Gy(e2, p2, PharaohsServant);
                var s3 = Gy(e2, p2, PharaonicProtector);
                for (var z = 1; z < 4; z++) PlaceMonster(e2, p2, Celtic, z, BattlePosition.Attack, true);
                var ph = e2.CreateCardInstance(Pharaoh);
                var prev = TributeSummonsRuntime.BeginNamedSummon(e2.CreateCardInstance(First));
                bool summoned;
                try
                {
                    summoned = e2.SpecialSummonToField(p2, ph, BattlePosition.Attack, true, SummonKind.SpecialSummon, 0);
                }
                finally
                {
                    TributeSummonsRuntime.EndNamedSummon(prev);
                }

                var offer = Legal(e2);
                var asked = summoned && e2.IsAwaitingEffectTarget && offer.Count == 3;
                var one = asked && e2.TrySelectEffectTarget(s3);
                var stop = e2.IsAwaitingEffectTarget && e2.CancelEffectTargeting();
                Drive(e2, _ => false);
                Check("Spirit of the Pharaoh: 1 free zone → 1 target; the targeted Zombie is Special Summoned face-up",
                    summoned && asked && one && p2.TryFindMonster(s3, out _) && s3.FaceUp && p2.Graveyard.Contains(s1) &&
                    p2.Graveyard.Contains(s2) && !e2.IsAwaitingEffectTarget,
                    $"summoned={summoned} asked={asked} offer={Names(offer)} one={one} stop={stop} s3={Where(e2, s3)}");
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3); t++)
            {
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3) break;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static CardInstance PlaceFaceUp(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance Gy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static bool InDeckOrHand(DuelistState who, int id) =>
            who.Deck.Contains(id) || who.Hand.Any(h => h != null && h.CardId == id);

        static bool OnFieldFaceUp(DuelistState who, int id) =>
            who.MonstersOnField().Any(m => m != null && m.CardId == id && m.FaceUp);

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>()).Select(c => c?.Name ?? "-"));

        static void PassResponses(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++)
                e.PassResponse();
        }

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/> (else CANCEL); every window passes.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 30; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
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

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.Banished.Contains(c)) at.Add(tag + ".banished");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz" + (c.FaceUp ? "(up)" : "(set)"));
                if (who.TryFindMonster(c, out _)) at.Add(tag + ".mz" + (c.FaceUp ? "(up)" : "(down)"));
            }

            return at.Count == 0 ? "nowhere" : string.Join("+", at);
        }
    }
}
