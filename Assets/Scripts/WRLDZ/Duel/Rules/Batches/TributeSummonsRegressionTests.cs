using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TributeSummons batch (v141 curriculum): Tribute Doll, Insect Imitation, Magical Labyrinth (+ Wall Shadow's
    /// summon lock) and Soul Exchange. Compile shapes, mini-duels through the real engine entry points
    /// (TryActivateSpellTrap, TrySelectEffectTarget, TryNormalSummon, TryEnterBattlePhase, CanAttack, EndTurn) and
    /// fail-closed near misses. Runtime: <see cref="TributeSummonsRuntime"/>.
    /// </summary>
    public static class TributeSummonsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int TributeDoll = 2903036;
        const int SoulExchange = 68005187;
        const int InsectImitation = 96965364;
        const int MagicalLabyrinth = 64389297;
        const int WallShadow = 63162310;        // Lv7, "Cannot be Normal Summoned/Set. Must first be SS with Magical Labyrinth"
        const int LabyrinthWall = 67284908;     // Lv5 Normal, 0/3000
        const int MaskOfRestrict = 29549364;

        const int DarkMagician = 46986414;      // Lv7 Normal 2500
        const int Bewd = 89631139;              // Lv8 Normal 3000
        const int YamataDragon = 76862289;      // Lv7 Spirit — "This card cannot be Special Summoned."
        const int Celtic = 91152256;            // Lv4 Warrior 1400
        const int MysticalElf = 15025844;       // Lv4 Spellcaster 800/2000
        const int SummonedSkull = 70781052;     // Lv6 Fiend 2500
        const int HerculesBeetle = 52584282;    // Lv5 Insect
        const int NeoBug = 16587243;            // Lv4 Insect
        const int InsectPrincess = 37957847;    // Lv6 Insect
        const int PerfectlyUltimateGreatMoth = 48579379; // Lv8 Insect, Special Summon monster
        const int HamburgerRecipe = 80811661;   // Ritual Spell → Hungry Burger, Levels 6+
        const int HungryBurger = 30243636;      // Ritual Monster Lv6
        const int HystericFairy = 21297224;     // "Tribute 2 monsters on your side of the field" → +1000 LP
        const int UltraEvolutionPill = 22431243; // Tribute 1 Reptile; SS 1 Dinosaur from the hand
        const int ArmoredLizard = 15480588;     // Lv4 Reptile
        const int Gagagigo = 49003308;          // Lv4 Reptile
        const int Uraby = 1784619;              // Lv4 Dinosaur
        const int ShareThePain = 56830749;      // Tribute 1 monster; make your opponent Tribute 1 monster
        const int MonsterReborn = 83764718;
        const int HarpieLady = 76812113;
        const int HarpieLadySisters = 12206212; // "Must first be Special Summoned with "Elegant Egotist""
        const int ElegantEgotist = 90219263;
        const int Multiply = 40703222;          // Tribute 1 face-up "Kuriboh"; as many Kuriboh Tokens as possible
        const int Kuriboh = 40640057;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TributeSummons: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            TributeDollDuels(db, pDeck, aDeck, Check);
            InsectImitationDuels(db, pDeck, aDeck, Check);
            MagicalLabyrinthDuels(db, pDeck, aDeck, Check);
            SoulExchangeDuels(db, pDeck, aDeck, Check);
            ReviewFixDuels(db, pDeck, aDeck, Check);
            NearMisses(db, Check);
        }

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses={prog.ClauseList.Count} " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        /// <summary>
        /// The player's own Main Phase 1 on a turn where a Battle Phase is allowed (turn 3+), and later than
        /// <paramref name="afterTurn"/> (pass the current turn number to reach the player's next turn).
        /// </summary>
        static bool ReachPlayerMain1(DuelEngine engine, int afterTurn = 0)
        {
            var minTurn = Math.Max(3, afterTurn + 1);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= minTurn &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == engine.Player && engine.TurnNumber >= minTurn &&
                   engine.Phase == DuelPhase.Main1;
        }

        /// <summary>A fresh duel at the player's Main Phase 1 (turn 3+), both boards and hands empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachPlayerMain1(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>Pick <paramref name="t"/> when the engine is waiting for a pick (a lone offer may auto-resolve).</summary>
        static bool PickIfAwaiting(DuelEngine engine, CardInstance t)
        {
            if (!engine.IsAwaitingEffectTarget) return true;
            return engine.IsLegalEffectTarget(t) && engine.TrySelectEffectTarget(t);
        }

        static bool OnField(DuelistState who, int cardId) =>
            who.MonstersOnField().Any(m => m != null && m.CardId == cardId);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var doll = CardTextEffectCompiler.Compile(db.Get(TributeDoll));
            Check("Tribute Doll FullyCompiled: Tribute 1 → exact Level 7, NS/Set-able hand monster, cannot attack",
                doll != null && doll.FullyCompiled && doll.ClauseList.Count == 1 &&
                doll.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                            c.Action == EffectActionKind.SpecialSummonFromHand &&
                                            c.Zone == EffectZoneFilter.ControllerHandMonsters &&
                                            c.RequiresTributeCount == 1 && c.RequiresExactLevel == 7 &&
                                            !c.AmountIsLevel && c.RequiresCanBeNormalSummonedOrSet &&
                                            c.SummonCannotAttackThisTurn && c.RequiresTargetChoice &&
                                            !c.IsPsctTarget),
                Describe(doll));

            var ii = CardTextEffectCompiler.Compile(db.Get(InsectImitation));
            Check("Insect Imitation FullyCompiled: Tribute 1 → Deck Insect of the Tributed Level + 1",
                ii != null && ii.FullyCompiled && ii.ClauseList.Count == 1 &&
                ii.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.SpecialSummonFromDeck &&
                                          c.Zone == EffectZoneFilter.ControllerDeckMonsters &&
                                          c.RaceFilter == "Insect" && c.RequiresTributeCount == 1 &&
                                          c.MatchTributedLevel && c.TributedLevelOffset == 1 &&
                                          !c.IsPsctTarget),
                Describe(ii));

            var lab = CardTextEffectCompiler.Compile(db.Get(MagicalLabyrinth));
            Check("Magical Labyrinth FullyCompiled: Equip only to Labyrinth Wall + Tribute it → Wall Shadow from Deck",
                lab != null && lab.FullyCompiled && lab.ClauseList.Count == 2 &&
                lab.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                                           c.EquipHostName == "Labyrinth Wall" &&
                                           c.Zone == EffectZoneFilter.ControllerMonsters && c.StaysOnField) &&
                lab.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SpecialSummonNamed &&
                                           c.NamedCard == "Wall Shadow" && c.FromDeck && !c.FromHand &&
                                           c.RequiresTributeEquippedMonster && c.IsOptional &&
                                           c.RequiresTributeCount == 0),
                Describe(lab));
            Check("Wall Shadow classifies Structural (its Magical Labyrinth summon path is live)",
                CardEffectStatus.Classify(db.Get(WallShadow)) == CardEffectStatusKind.Structural);

            var se = CardTextEffectCompiler.Compile(db.Get(SoulExchange));
            Check("Soul Exchange FullyCompiled: target opp monster, Tribute it as if controlled, no Battle Phase",
                se != null && se.FullyCompiled && se.ClauseList.Count == 1 &&
                se.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                          c.Action == EffectActionKind.TributeTargetAsIfControlledThisTurn &&
                                          c.Zone == EffectZoneFilter.OppMonsters && c.RequiresTargetChoice &&
                                          c.IsPsctTarget && c.ForbidsBattlePhaseThisTurn),
                Describe(se));

            foreach (var id in new[] { TributeDoll, InsectImitation, MagicalLabyrinth, SoulExchange })
                Check($"{db.Get(id)?.name} classifies Implemented",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented);
            Check("Soul Exchange's action maps to a shared resolution kind",
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.TributeTargetAsIfControlledThisTurn }) ==
                EffectResolutionKind.TakeControl);
        }

        // ───────────────────────────── Tribute Doll ─────────────────────────────

        static void TributeDollDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Tribute (cost), pick only a Level 7 that can be Normal Summoned/Set; it cannot attack this turn.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var dm = PutInHand(engine, p, DarkMagician);
                var shadow = PutInHand(engine, p, WallShadow);
                var yamata = PutInHand(engine, p, YamataDragon);
                var bewd = PutInHand(engine, p, Bewd);
                var doll = PutInHand(engine, p, TributeDoll);
                var can = engine.CanActivateSpellTrap(p, doll, fromHand: true);
                var started = can && engine.TryActivateSpellTrap(p, doll, fromHand: true);
                var costPick = engine.IsAwaitingEffectTarget && engine.PendingActivation.AwaitingIgnitionCost;
                var paid = started && PickIfAwaiting(engine, celtic) && p.Graveyard.Contains(celtic);
                var offer = engine.IsAwaitingEffectTarget;
                var dmLegal = offer && engine.IsLegalEffectTarget(dm);
                var othersIllegal = offer && !engine.IsLegalEffectTarget(shadow) &&
                                    !engine.IsLegalEffectTarget(yamata) && !engine.IsLegalEffectTarget(bewd);
                var done = offer && engine.TrySelectEffectTarget(dm);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Tribute Doll: Tribute the monster (cost), then only the NS/Set-able Level 7 is offered",
                    reached && can && started && costPick && paid && dmLegal && othersIllegal && done &&
                    p.TryFindMonster(dm, out _) && dm.FaceUp && p.Graveyard.Contains(doll) &&
                    p.Hand.Contains(shadow) && p.Hand.Contains(yamata) && p.Hand.Contains(bewd),
                    $"reached={reached} can={can} started={started} costPick={costPick} paid={paid} " +
                    $"offer={offer} dmLegal={dmLegal} othersIllegal={othersIllegal} done={done}");

                var bp = engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Tribute Doll: the summoned monster cannot attack this turn",
                    bp && engine.Phase == DuelPhase.Battle && !engine.CanAttack(p, dm),
                    $"bp={bp} phase={engine.Phase} canAttack={engine.CanAttack(p, dm)}");

                var nextTurn = ReachPlayerMain1(engine, engine.TurnNumber) && engine.TryEnterBattlePhase(p);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Tribute Doll: the attack lock ends with the turn",
                    nextTurn && p.TryFindMonster(dm, out _) && engine.CanAttack(p, dm),
                    $"nextTurn={nextTurn} onField={p.TryFindMonster(dm, out _)}");
            }

            // A full field: the Tribute frees the zone the summon needs (ygopro ft>-1 with your own Tribute).
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                for (var z = 0; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);
                var dm = PutInHand(engine, p, DarkMagician);
                var doll = PutInHand(engine, p, TributeDoll);
                var can = engine.CanActivateSpellTrap(p, doll, fromHand: true);
                var started = can && engine.TryActivateSpellTrap(p, doll, fromHand: true);
                var tribute = engine.IsAwaitingEffectTarget ? engine.PendingActivation.LegalTargets.FirstOrDefault() : null;
                var paid = started && (tribute == null || PickIfAwaiting(engine, tribute));
                var done = paid && PickIfAwaiting(engine, dm);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Tribute Doll: legal on a full field (the Tribute frees a zone)",
                    reached && can && done && p.TryFindMonster(dm, out _) && p.MonsterCount == 5,
                    $"can={can} started={started} paid={paid} done={done} monsters={p.MonsterCount}");
            }

            // Shared rule, same shape: Ultra Evolution Pill on a full field (its Reptile Tribute frees the zone).
            {
                const int pill = 22431243, armoredLizard = 15480588, uraby = 1784619;
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                PlaceMonster(engine, p, armoredLizard, 0, BattlePosition.Attack, true);
                for (var z = 1; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);
                PutInHand(engine, p, uraby);
                var card = PutInHand(engine, p, pill);
                Check("Ultra Evolution Pill: legal on a full field when the Tribute is your own monster",
                    engine.CanActivateSpellTrap(p, card, fromHand: true));
            }

            // The AI activates it without a prompt (auto cost, strongest legal pick).
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var o = engine.Opponent;
                o.Hand.Clear();
                var fodder = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var dm = PutInHand(engine, o, DarkMagician);
                PutInHand(engine, o, WallShadow);
                var doll = PutInHand(engine, o, TributeDoll);
                var ok = engine.TryActivateSpellTrap(o, doll, fromHand: true);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Tribute Doll: the AI resolves it with no pending pick",
                    reached && ok && !engine.IsAwaitingEffectTarget && o.Graveyard.Contains(fodder) &&
                    o.TryFindMonster(dm, out _) && dm.CannotAttackDuringTurnNumber == engine.TurnNumber,
                    $"reached={reached} ok={ok} awaiting={engine.IsAwaitingEffectTarget}");
            }

            // Fail closed: no Normal-Summonable Level 7 in hand; nothing to Tribute; Mask of Restrict.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                PutInHand(engine, p, WallShadow);
                PutInHand(engine, p, YamataDragon);
                PutInHand(engine, p, Bewd);
                var doll = PutInHand(engine, p, TributeDoll);
                Check("Tribute Doll: a Special Summon-only / cannot-be-Special-Summoned / Level 8 hand is not enough",
                    !engine.CanActivateSpellTrap(p, doll, fromHand: true));

                var engine2 = PlayerMain1(db, pDeck, aDeck, out _);
                var p2 = engine2.Player;
                PutInHand(engine2, p2, DarkMagician);
                var doll2 = PutInHand(engine2, p2, TributeDoll);
                Check("Tribute Doll: no monster to Tribute → cannot activate",
                    !engine2.CanActivateSpellTrap(p2, doll2, fromHand: true));

                var engine3 = PlayerMain1(db, pDeck, aDeck, out _);
                var p3 = engine3.Player;
                PlaceMonster(engine3, p3, Celtic, 1, BattlePosition.Attack, true);
                PutInHand(engine3, p3, DarkMagician);
                var mask = PlaceSetTrap(engine3, engine3.Opponent, MaskOfRestrict, 0);
                mask.FaceUp = true;
                var doll3 = PutInHand(engine3, p3, TributeDoll);
                Check("Tribute Doll: Mask of Restrict blocks the Tribute cost",
                    !engine3.CanActivateSpellTrap(p3, doll3, fromHand: true));
            }
        }

        // ───────────────────────────── Insect Imitation ─────────────────────────────

        static void InsectImitationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, InsectPrincess, HerculesBeetle, DarkMagician });
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);   // Lv4 → Lv5 Insect
                var skull = PlaceMonster(engine, p, SummonedSkull, 2, BattlePosition.Attack, true); // Lv6 → no Lv7 Insect
                var card = PutInHand(engine, p, InsectImitation);
                var can = engine.CanActivateSpellTrap(p, card, fromHand: true);
                var started = can && engine.TryActivateSpellTrap(p, card, fromHand: true);
                var costOffer = engine.IsAwaitingEffectTarget && engine.PendingActivation.AwaitingIgnitionCost;
                var skullIllegal = !costOffer || !engine.IsLegalEffectTarget(skull);
                var paid = started && PickIfAwaiting(engine, celtic) && p.Graveyard.Contains(celtic);
                var deckOffer = engine.IsAwaitingEffectTarget
                    ? engine.PendingActivation.LegalTargets.Select(t => t.CardId).ToList()
                    : new System.Collections.Generic.List<int>();
                var onlyBeetle = deckOffer.Count == 1 && deckOffer[0] == HerculesBeetle;
                var beetle = engine.IsAwaitingEffectTarget ? engine.PendingActivation.LegalTargets[0] : null;
                var done = beetle != null && engine.TrySelectEffectTarget(beetle);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Insect Imitation: only a Tribute with a Level+1 Insect in the Deck; then exactly that Insect",
                    reached && can && started && costOffer && skullIllegal && paid && onlyBeetle && done &&
                    OnField(p, HerculesBeetle) && !p.Deck.Contains(HerculesBeetle) &&
                    p.Deck.Contains(NeoBug) && p.Deck.Contains(InsectPrincess) &&
                    p.TryFindMonster(skull, out _) && p.Graveyard.Contains(card),
                    $"reached={reached} can={can} started={started} costOffer={costOffer} skullIllegal={skullIllegal} " +
                    $"paid={paid} offer=[{string.Join(",", deckOffer)}] done={done}");
            }

            // The Level is the Tributed monster's: a Level 5 Tribute calls a Level 6 Insect (AI, no prompt).
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var o = engine.Opponent;
                o.Hand.Clear();
                o.Deck.Clear();
                o.Deck.AddRange(new[] { HerculesBeetle, InsectPrincess });
                var wall = PlaceMonster(engine, o, LabyrinthWall, 0, BattlePosition.Defense, true); // Lv5
                var card = PutInHand(engine, o, InsectImitation);
                var ok = engine.TryActivateSpellTrap(o, card, fromHand: true);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Insect Imitation: AI Tributes a Level 5 and summons the Level 6 Insect",
                    reached && ok && !engine.IsAwaitingEffectTarget && o.Graveyard.Contains(wall) &&
                    OnField(o, InsectPrincess) && o.Deck.Contains(HerculesBeetle),
                    $"reached={reached} ok={ok} awaiting={engine.IsAwaitingEffectTarget}");
            }

            // Fail closed: no Level+1 Insect; a Level+1 non-Insect; a Level+1 Special Summon-only Insect.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { NeoBug, InsectPrincess, SummonedSkull });
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, InsectImitation);
                Check("Insect Imitation: no Level 5 Insect in the Deck → cannot activate (a Level 5 Fiend is not one)",
                    !engine.CanActivateSpellTrap(p, card, fromHand: true));

                var engine2 = PlayerMain1(db, pDeck, aDeck, out _);
                var p2 = engine2.Player;
                p2.Deck.Clear();
                p2.Deck.Add(PerfectlyUltimateGreatMoth);
                PlaceMonster(engine2, p2, DarkMagician, 1, BattlePosition.Attack, true);
                var card2 = PutInHand(engine2, p2, InsectImitation);
                Check("Insect Imitation: a Special Summon-only Level 8 Insect cannot be called",
                    !engine2.CanActivateSpellTrap(p2, card2, fromHand: true));
            }
        }

        // ───────────────────────────── Magical Labyrinth / Wall Shadow ─────────────────────────────

        static void MagicalLabyrinthDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, WallShadow, Celtic });
                var wall = PlaceMonster(engine, p, LabyrinthWall, 1, BattlePosition.Defense, true);
                var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var lab = PutInHand(engine, p, MagicalLabyrinth);
                var can = engine.CanActivateSpellTrap(p, lab, fromHand: true);
                var started = can && engine.TryActivateSpellTrap(p, lab, fromHand: true);
                var celticIllegal = !engine.IsAwaitingEffectTarget || !engine.IsLegalEffectTarget(celtic);
                var equipped = started && PickIfAwaiting(engine, wall) && lab.EquippedTo == wall;
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Magical Labyrinth: equips only to Labyrinth Wall — and the equip does not summon Wall Shadow",
                    reached && can && started && celticIllegal && equipped && p.TryFindSpellTrap(lab, out _) &&
                    !OnField(p, WallShadow) && p.Deck.Contains(WallShadow),
                    $"reached={reached} can={can} started={started} celticIllegal={celticIllegal} equipped={equipped}");

                var canIgnite = engine.CanActivateSpellTrap(p, lab, fromHand: false);
                var ignited = canIgnite && engine.TryActivateSpellTrap(p, lab, fromHand: false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var shadow = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == WallShadow);
                Check("Magical Labyrinth: Tribute the equipped Labyrinth Wall → Wall Shadow from the Deck",
                    canIgnite && ignited && shadow != null && shadow.FaceUp && p.Graveyard.Contains(wall) &&
                    p.Graveyard.Contains(lab) && !p.Deck.Contains(WallShadow) && p.TryFindMonster(celtic, out _),
                    $"canIgnite={canIgnite} ignited={ignited} shadow={shadow != null} wallGy={p.Graveyard.Contains(wall)} " +
                    $"labGy={p.Graveyard.Contains(lab)}");
            }

            // Wall Shadow keeps its lock: never Normal Summoned/Set, never a Tribute Doll pick.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var shadow = PutInHand(engine, p, WallShadow);
                Check("Wall Shadow: cannot be Normal Summoned/Set even with 2 Tributes",
                    !engine.CanNormalSummonOrSet(p, shadow));
            }

            // Fail closed: no Labyrinth Wall; no Wall Shadow left in the Deck; Mask of Restrict; host not yours.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var lab = PutInHand(engine, p, MagicalLabyrinth);
                Check("Magical Labyrinth: no Labyrinth Wall → cannot activate",
                    !engine.CanActivateSpellTrap(p, lab, fromHand: true));

                bool EquipOnWall(DuelEngine e, out CardInstance labCard, out CardInstance wall)
                {
                    var pl = e.Player;
                    wall = PlaceMonster(e, pl, LabyrinthWall, 1, BattlePosition.Defense, true);
                    labCard = PutInHand(e, pl, MagicalLabyrinth);
                    var ok = e.TryActivateSpellTrap(pl, labCard, fromHand: true) && PickIfAwaiting(e, wall);
                    if (e.IsAwaitingResponse) e.PassResponse();
                    return ok && labCard.EquippedTo == wall;
                }

                var e2 = PlayerMain1(db, pDeck, aDeck, out _);
                e2.Player.Deck.Clear();
                e2.Player.Deck.Add(Celtic);
                var eq2 = EquipOnWall(e2, out var lab2, out _);
                Check("Magical Labyrinth: no Wall Shadow in the Deck → the Tribute effect cannot be activated",
                    eq2 && !e2.CanActivateSpellTrap(e2.Player, lab2, fromHand: false));

                var e3 = PlayerMain1(db, pDeck, aDeck, out _);
                e3.Player.Deck.Clear();
                e3.Player.Deck.Add(WallShadow);
                var eq3 = EquipOnWall(e3, out var lab3, out var wall3);
                var mask = PlaceSetTrap(e3, e3.Opponent, MaskOfRestrict, 0);
                mask.FaceUp = true;
                var blocked = !e3.CanActivateSpellTrap(e3.Player, lab3, fromHand: false) &&
                              !e3.TryActivateSpellTrap(e3.Player, lab3, fromHand: false);
                Check("Magical Labyrinth: Mask of Restrict blocks Tributing the equipped monster",
                    eq3 && blocked && e3.Player.TryFindMonster(wall3, out _) && !OnField(e3.Player, WallShadow));

                var e4 = PlayerMain1(db, pDeck, aDeck, out _);
                e4.Player.Deck.Clear();
                e4.Player.Deck.Add(WallShadow);
                var eq4 = EquipOnWall(e4, out var lab4, out var wall4);
                // The equipped monster changes control: you no longer control it.
                e4.Player.MonsterZones[1].Occupant = null;
                e4.Opponent.MonsterZones[1].Occupant = wall4;
                Check("Magical Labyrinth: you must control the equipped monster",
                    eq4 && !e4.CanActivateSpellTrap(e4.Player, lab4, fromHand: false));
            }
        }

        // ───────────────────────────── Soul Exchange ─────────────────────────────

        static bool ActivateSoulExchange(DuelEngine engine, DuelistState who, CardInstance target,
            out CardInstance card)
        {
            card = PutInHand(engine, who, SoulExchange);
            if (!engine.CanActivateSpellTrap(who, card, fromHand: true)) return false;
            if (!engine.TryActivateSpellTrap(who, card, fromHand: true)) return false;
            var ok = PickIfAwaiting(engine, target);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            return ok && TributeSummonsRuntime.IsForcedTribute(who, target);
        }

        static void SoulExchangeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // A face-down target: Tribute Summon with it (auto-included, first), no Battle Phase this turn.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var setMon = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
                var other = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, setMon, out var se);
                Check("Soul Exchange: a face-down opponent's monster is a legal target; oath applies",
                    reached && granted && p.Graveyard.Contains(se) && !engine.CanConductBattlePhase,
                    $"reached={reached} granted={granted} canBp={engine.CanConductBattlePhase}");

                var skull = PutInHand(engine, p, SummonedSkull);
                engine.PendingTributes.Add(mine); // the player taps their own monster
                var summoned = engine.TryNormalSummon(p, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange: the Tribute Summon must Tribute the target (it goes to its owner's GY)",
                    summoned && p.TryFindMonster(skull, out _) && o.Graveyard.Contains(setMon) &&
                    !p.Graveyard.Contains(setMon) && p.TryFindMonster(mine, out _) && o.TryFindMonster(other, out _),
                    $"summoned={summoned} targetOppGy={o.Graveyard.Contains(setMon)} mineOnField={p.TryFindMonster(mine, out _)}");

                var bp = engine.TryEnterBattlePhase(p);
                Check("Soul Exchange: you cannot conduct your Battle Phase that turn",
                    !bp && engine.Phase == DuelPhase.Main1, $"bp={bp} phase={engine.Phase}");

                var next = ReachPlayerMain1(engine, engine.TurnNumber);
                Check("Soul Exchange: the grant and the Battle Phase lock end with the turn",
                    next && TributeSummonsRuntime.ForcedTributes(p).Count == 0 && engine.CanConductBattlePhase,
                    $"next={next} canBp={engine.CanConductBattlePhase}");
            }

            // A 2-Tribute Summon: the target plus one of yours; its Tribute value counts toward the Summon.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                var dm = PutInHand(engine, p, DarkMagician);
                var legal = engine.CanNormalSummonOrSet(p, dm);
                var summoned = engine.TryNormalSummon(p, dm, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange: a Level 7 Tribute Summon with 1 monster of yours + the target",
                    granted && legal && summoned && o.Graveyard.Contains(target) && p.Graveyard.Contains(mine) &&
                    p.TryFindMonster(dm, out _),
                    $"granted={granted} legal={legal} summoned={summoned}");
            }

            // Tribute costs: Tribute Doll must Tribute the target (the only offer).
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                var dm = PutInHand(engine, p, DarkMagician);
                var doll = PutInHand(engine, p, TributeDoll);
                var started = engine.CanActivateSpellTrap(p, doll, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, doll, fromHand: true);
                var onlyTarget = !engine.IsAwaitingEffectTarget ||
                                 (engine.IsLegalEffectTarget(target) && !engine.IsLegalEffectTarget(mine));
                var paid = started && PickIfAwaiting(engine, target);
                var done = paid && PickIfAwaiting(engine, dm);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange + Tribute Doll: the cost must Tribute the target, not your own monster",
                    granted && started && onlyTarget && done && o.Graveyard.Contains(target) &&
                    p.TryFindMonster(mine, out _) && p.TryFindMonster(dm, out _),
                    $"granted={granted} started={started} onlyTarget={onlyTarget} paid={paid} done={done}");
            }

            // Ritual Tributes include the target.
            if (db.Get(HamburgerRecipe) != null && db.Get(HungryBurger) != null)
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);   // Lv4
                var granted = ActivateSoulExchange(engine, p, target, out _);
                var a = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);   // Lv4
                var b = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);        // Lv4
                var burger = PutInHand(engine, p, HungryBurger);
                var recipe = PutInHand(engine, p, HamburgerRecipe);
                var ok = engine.CanActivateSpellTrap(p, recipe, fromHand: true) &&
                         engine.TryActivateSpellTrap(p, recipe, fromHand: true);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var ownTributed = (p.Graveyard.Contains(a) ? 1 : 0) + (p.Graveyard.Contains(b) ? 1 : 0);
                Check("Soul Exchange + Ritual: the Tributes include the target (plus 1 of yours for Level 6)",
                    granted && ok && p.TryFindMonster(burger, out _) && o.Graveyard.Contains(target) &&
                    ownTributed == 1,
                    $"granted={granted} ok={ok} targetGy={o.Graveyard.Contains(target)} ownTributed={ownTributed}");
            }

            // The grant ends if the target leaves the field; the summon then uses your own monster.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                engine.SendCardToGrave(o, target);
                var skull = PutInHand(engine, p, SummonedSkull);
                var summoned = engine.TryNormalSummon(p, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange: a target that left the field no longer binds your Tributes",
                    granted && TributeSummonsRuntime.ForcedTributes(p).Count == 0 && summoned &&
                    p.Graveyard.Contains(mine) && p.TryFindMonster(skull, out _),
                    $"granted={granted} summoned={summoned}");
            }

            // Turning the target face-down ends the grant (ygopro RESETS_STANDARD includes RESET_TURN_SET).
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var target = PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                target.FaceUp = false;
                target.Position = BattlePosition.Defense;
                Check("Soul Exchange: a target turned face-down no longer binds your Tributes",
                    granted && TributeSummonsRuntime.ForcedTributes(p).Count == 0);
            }

            // A full field: Tributing only the opponent's monster leaves no zone — the Summon is illegal.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                for (var z = 0; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, MysticalElf, z, BattlePosition.Defense, true);
                var skull = PutInHand(engine, p, SummonedSkull);
                var legal = engine.CanNormalSummonOrSet(p, skull);
                var summoned = engine.TryNormalSummon(p, skull, false);
                Check("Soul Exchange: on a full field the forced Tribute cannot make room — no Summon, nothing paid",
                    granted && !legal && !summoned && o.TryFindMonster(target, out _) && p.MonsterCount == 5,
                    $"granted={granted} legal={legal} summoned={summoned}");
            }

            // The AI side: an automatic Tribute Summon uses the player's monster.
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                var target = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var own = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Attack, true);
                var se = PutInHand(engine, o, SoulExchange);
                var ok = engine.TryActivateSpellTrap(o, se, fromHand: true);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var skull = PutInHand(engine, o, SummonedSkull);
                var summoned = engine.TryNormalSummon(o, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange: the AI's own Tribute Summon Tributes the player's targeted monster",
                    reached && ok && !engine.IsAwaitingEffectTarget && summoned && p.Graveyard.Contains(target) &&
                    o.TryFindMonster(own, out _) && o.TryFindMonster(skull, out _),
                    $"reached={reached} ok={ok} summoned={summoned}");
            }

            // Fail closed: Main Phase 2; Mask of Restrict still forbids the Tribute it grants.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var m2 = engine.TryEnterMainPhase2(p);
                var se = PutInHand(engine, p, SoulExchange);
                Check("Soul Exchange: cannot be activated in Main Phase 2",
                    m2 && engine.Phase == DuelPhase.Main2 && !engine.CanActivateSpellTrap(p, se, fromHand: true));

                var e2 = PlayerMain1(db, pDeck, aDeck, out _);
                var target = PlaceMonster(e2, e2.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var mask = PlaceSetTrap(e2, e2.Opponent, MaskOfRestrict, 0);
                mask.FaceUp = true;
                var granted = ActivateSoulExchange(e2, e2.Player, target, out _);
                var skull = PutInHand(e2, e2.Player, SummonedSkull);
                Check("Soul Exchange: under Mask of Restrict the target still cannot be Tributed",
                    granted && !e2.CanNormalSummonOrSet(e2.Player, skull) && e2.Opponent.TryFindMonster(target, out _));
            }
        }

        // ───────────────────────────── review fixes (two independent reviews) ─────────────────────────────

        static string Offers(DuelEngine engine) => engine.IsAwaitingEffectTarget
            ? string.Join(",", engine.PendingActivation.LegalTargets.Select(t => t.Name + (t.FaceUp ? "" : "(fd)")))
            : "none";

        static void ReviewFixDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two Soul Exchanges: each copy adds its own "must" — a Level 7 with both targets, no monster of yours.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                var both = TributeSummonsRuntime.IsForcedTribute(p, a) && TributeSummonsRuntime.IsForcedTribute(p, b);
                var dm = PutInHand(engine, p, DarkMagician);
                var legal = engine.CanNormalSummonOrSet(p, dm);
                var summoned = legal && engine.TryNormalSummon(p, dm, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange x2: both grants hold — a Level 7 Tribute Summon with both targets",
                    reached && g1 && g2 && both && legal && summoned && o.Graveyard.Contains(a) &&
                    o.Graveyard.Contains(b) && p.TryFindMonster(dm, out _),
                    $"g1={g1} g2={g2} both={both} legal={legal} summoned={summoned}");
            }

            // The second grant does not replace the first: with the second target gone, the first still counts.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                engine.SendCardToGrave(o, b);
                var skull = PutInHand(engine, p, SummonedSkull);
                var legal = engine.CanNormalSummonOrSet(p, skull);
                var summoned = legal && engine.TryNormalSummon(p, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange x2: the first target stays Tributable after the second one leaves",
                    g1 && g2 && legal && summoned && o.Graveyard.Contains(a) && p.TryFindMonster(skull, out _),
                    $"g1={g1} g2={g2} legal={legal} summoned={summoned}");
            }

            // Two "musts", one Tribute needed: a target is Tributed, never your own monster.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                var skull = PutInHand(engine, p, SummonedSkull);
                engine.PendingTributes.Add(mine); // the player taps their own monster
                var summoned = engine.TryNormalSummon(p, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var targetsTributed = (o.Graveyard.Contains(a) ? 1 : 0) + (o.Graveyard.Contains(b) ? 1 : 0);
                Check("Soul Exchange x2: a 1-Tribute Summon Tributes one target, not your own monster",
                    g1 && g2 && summoned && targetsTributed == 1 && p.TryFindMonster(mine, out _) &&
                    p.TryFindMonster(skull, out _),
                    $"g1={g1} g2={g2} summoned={summoned} targetsTributed={targetsTributed}");
            }

            // Two "musts", one Tribute needed: the player chooses which target by tapping it.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                var skull = PutInHand(engine, p, SummonedSkull);
                engine.ToggleTribute(p, b);
                var tapped = engine.PendingTributes.Contains(b);
                var summoned = engine.TryNormalSummon(p, skull, false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange x2: tapping the second target Tributes it, the first stays",
                    g1 && g2 && tapped && summoned && o.Graveyard.Contains(b) && o.TryFindMonster(a, out _) &&
                    p.TryFindMonster(skull, out _),
                    $"g1={g1} g2={g2} tapped={tapped} summoned={summoned}");
            }

            // Tribute costs with two "musts": only the targets are offered, and a 2-Tribute cost pays both.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var fairy = PlaceMonster(engine, p, HystericFairy, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                var lp = p.LifePoints;
                var started = engine.CanActivateSpellTrap(p, fairy, fromHand: false) &&
                              engine.TryActivateSpellTrap(p, fairy, fromHand: false);
                var offer1 = Offers(engine);
                var onlyTargets = engine.IsAwaitingEffectTarget && engine.IsLegalEffectTarget(a) &&
                                  engine.IsLegalEffectTarget(b) && !engine.IsLegalEffectTarget(mine) &&
                                  !engine.IsLegalEffectTarget(fairy);
                var pick1 = PickIfAwaiting(engine, b);
                var offer2 = Offers(engine);
                var pick2 = PickIfAwaiting(engine, a);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange x2 + Hysteric Fairy: the cost offers only the two targets and Tributes both",
                    g1 && g2 && started && onlyTargets && pick1 && pick2 && o.Graveyard.Contains(a) &&
                    o.Graveyard.Contains(b) && p.TryFindMonster(fairy, out _) && p.TryFindMonster(mine, out _) &&
                    p.LifePoints == lp + 1000 && !engine.IsAwaitingEffectTarget,
                    $"started={started} offer1=[{offer1}] offer2=[{offer2}] lp={lp}->{p.LifePoints}");
            }

            // Ritual with two "musts": both targets (Levels 4 + 4 ≥ 6) are the Tributes; your monster stays.
            if (db.Get(HamburgerRecipe) != null && db.Get(HungryBurger) != null)
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var a = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var g1 = ActivateSoulExchange(engine, p, a, out _);
                var g2 = ActivateSoulExchange(engine, p, b, out _);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var burger = PutInHand(engine, p, HungryBurger);
                var recipe = PutInHand(engine, p, HamburgerRecipe);
                var ok = engine.CanActivateSpellTrap(p, recipe, fromHand: true) &&
                         engine.TryActivateSpellTrap(p, recipe, fromHand: true);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange x2 + Ritual: both targets are the Tributes, your own monster stays",
                    g1 && g2 && ok && p.TryFindMonster(burger, out _) && o.Graveyard.Contains(a) &&
                    o.Graveyard.Contains(b) && p.TryFindMonster(mine, out _),
                    $"g1={g1} g2={g2} ok={ok} aGy={o.Graveyard.Contains(a)} bGy={o.Graveyard.Contains(b)} " +
                    $"mine={p.TryFindMonster(mine, out _)}");
            }

            // Turned face-down ends the grant, and it stays ended when the target is flipped face-up again.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var target = PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                target.FaceUp = false;
                target.Position = BattlePosition.Defense;
                var ended = TributeSummonsRuntime.ForcedTributes(p).Count == 0;
                target.FaceUp = true;
                var stillEnded = TributeSummonsRuntime.ForcedTributes(p).Count == 0;
                Check("Soul Exchange: turned face-down then face-up again — the grant stays ended",
                    granted && ended && stillEnded, $"granted={granted} ended={ended} stillEnded={stillEnded}");
            }

            // A face-down target keeps the grant when flipped face-up, and loses it when turned face-down again.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var target = PlaceMonster(engine, engine.Opponent, MysticalElf, 0, BattlePosition.Defense, false);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                target.FaceUp = true;
                target.Position = BattlePosition.Attack;
                var keeps = TributeSummonsRuntime.IsForcedTribute(p, target);
                target.FaceUp = false;
                target.Position = BattlePosition.Defense;
                var ended = TributeSummonsRuntime.ForcedTributes(p).Count == 0;
                Check("Soul Exchange: a face-down target flipped up keeps the grant; turned face-down again ends it",
                    granted && keeps && ended, $"granted={granted} keeps={keeps} ended={ended}");
            }

            // Ultra Evolution Pill: an opponent's face-down monster is not a Reptile you can Tribute (hidden Type).
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var setLizard = PlaceMonster(engine, o, ArmoredLizard, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, setLizard, out _);
                PutInHand(engine, p, Uraby);
                var pill = PutInHand(engine, p, UltraEvolutionPill);
                var can = engine.CanActivateSpellTrap(p, pill, fromHand: true);
                Check("Soul Exchange + Ultra Evolution Pill: a face-down opponent's Reptile cannot be the cost",
                    granted && !can && o.TryFindMonster(setLizard, out _), $"granted={granted} can={can}");
            }
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var setLizard = PlaceMonster(engine, o, ArmoredLizard, 0, BattlePosition.Defense, false);
                var myReptile = PlaceMonster(engine, p, Gagagigo, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, setLizard, out _);
                var uraby = PutInHand(engine, p, Uraby);
                var pill = PutInHand(engine, p, UltraEvolutionPill);
                var started = engine.CanActivateSpellTrap(p, pill, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, pill, fromHand: true);
                var offers = Offers(engine);
                var fdOffered = engine.IsAwaitingEffectTarget && engine.IsLegalEffectTarget(setLizard);
                PickIfAwaiting(engine, myReptile);
                PickIfAwaiting(engine, uraby);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange (face-down Reptile) + Ultra Evolution Pill: your own Reptile is the cost",
                    granted && started && !fdOffered && p.Graveyard.Contains(myReptile) &&
                    o.TryFindMonster(setLizard, out _) && p.TryFindMonster(uraby, out _),
                    $"granted={granted} started={started} offers=[{offers}]");
            }
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var lizard = PlaceMonster(engine, o, ArmoredLizard, 0, BattlePosition.Attack, true);
                var myReptile = PlaceMonster(engine, p, Gagagigo, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, lizard, out _);
                var uraby = PutInHand(engine, p, Uraby);
                var pill = PutInHand(engine, p, UltraEvolutionPill);
                var started = engine.CanActivateSpellTrap(p, pill, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, pill, fromHand: true);
                var offers = Offers(engine);
                var onlyTarget = !engine.IsAwaitingEffectTarget ||
                                 (engine.IsLegalEffectTarget(lizard) && !engine.IsLegalEffectTarget(myReptile));
                PickIfAwaiting(engine, lizard);
                PickIfAwaiting(engine, uraby);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange (face-up Reptile) + Ultra Evolution Pill: the target is the cost it must pay",
                    granted && started && onlyTarget && o.Graveyard.Contains(lizard) &&
                    p.TryFindMonster(myReptile, out _) && p.TryFindMonster(uraby, out _),
                    $"granted={granted} started={started} offers=[{offers}]");
            }

            // Multiply on a full field: the opponent's Kuriboh (the target) frees no zone, so your own is the cost.
            if (db.Get(Multiply) != null && db.Get(Kuriboh) != null)
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var theirs = PlaceMonster(engine, o, Kuriboh, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, theirs, out _);
                var mine = PlaceMonster(engine, p, Kuriboh, 0, BattlePosition.Attack, true);
                for (var z = 1; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, Multiply);
                var started = engine.CanActivateSpellTrap(p, card, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, card, fromHand: true);
                var offers = Offers(engine);
                var theirsOffered = engine.IsAwaitingEffectTarget && engine.IsLegalEffectTarget(theirs);
                PickIfAwaiting(engine, mine);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var tokens = p.MonstersOnField().Count(m => m != null && m.IsToken);
                Check("Soul Exchange + Multiply on a full field: your own Kuriboh is the cost, a Token takes its zone",
                    granted && started && !theirsOffered && p.Graveyard.Contains(mine) &&
                    o.TryFindMonster(theirs, out _) && tokens == 1,
                    $"granted={granted} started={started} offers=[{offers}] tokens={tokens}");
            }

            // Share the Pain: the cost cannot be the opponent's last monster — pay with yours, they Tribute theirs.
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                var stp = PutInHand(engine, p, ShareThePain);
                var started = engine.CanActivateSpellTrap(p, stp, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, stp, fromHand: true);
                var offers = Offers(engine);
                var targetOffered = engine.IsAwaitingEffectTarget && engine.IsLegalEffectTarget(target);
                PickIfAwaiting(engine, mine);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count > 0)
                    engine.TrySelectEffectTarget(engine.PendingActivation.LegalTargets[0]);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange + Share the Pain: the opponent's only monster is not the cost — both are Tributed",
                    granted && started && !targetOffered && p.Graveyard.Contains(mine) && o.Graveyard.Contains(target),
                    $"granted={granted} started={started} offers=[{offers}] mineGy={p.Graveyard.Contains(mine)} " +
                    $"targetGy={o.Graveyard.Contains(target)}");
            }
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var target = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var other = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Attack, true);
                var mine = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var granted = ActivateSoulExchange(engine, p, target, out _);
                var stp = PutInHand(engine, p, ShareThePain);
                var started = engine.CanActivateSpellTrap(p, stp, fromHand: true) &&
                              engine.TryActivateSpellTrap(p, stp, fromHand: true);
                var offers = Offers(engine);
                var onlyTarget = !engine.IsAwaitingEffectTarget ||
                                 (engine.IsLegalEffectTarget(target) && !engine.IsLegalEffectTarget(mine));
                PickIfAwaiting(engine, target);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count > 0)
                    engine.TrySelectEffectTarget(engine.PendingActivation.LegalTargets[0]);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                Check("Soul Exchange + Share the Pain: with another opponent's monster left, the target is the cost",
                    granted && started && onlyTarget && o.Graveyard.Contains(target) && o.Graveyard.Contains(other) &&
                    p.TryFindMonster(mine, out _),
                    $"granted={granted} started={started} offers=[{offers}]");
            }

            SemiNomiRevives(db, pDeck, aDeck, Check);
        }

        /// <summary>Monster Reborn on <paramref name="target"/> (lone GY monster): whether it came back.</summary>
        static bool RebornRevives(DuelEngine engine, DuelistState who, CardInstance target, out bool canActivate)
        {
            var mr = PutInHand(engine, who, MonsterReborn);
            canActivate = engine.CanActivateSpellTrap(who, mr, fromHand: true);
            if (canActivate && engine.TryActivateSpellTrap(who, mr, fromHand: true))
                PickIfAwaiting(engine, target);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            return who.TryFindMonster(target, out _);
        }

        static DuelEngine EmptyGraveyards(DuelEngine engine)
        {
            engine.Player.Graveyard.Clear();
            engine.Opponent.Graveyard.Clear();
            engine.Player.Banished.Clear();
            engine.Opponent.Banished.Clear();
            return engine;
        }

        // "Must first be Special Summoned with "X"": only a properly summoned copy comes back from the GY.
        static void SemiNomiRevives(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = EmptyGraveyards(PlayerMain1(db, pDeck, aDeck, out _));
                var p = engine.Player;
                var shadow = PutInHand(engine, p, WallShadow);
                engine.SendCardToGrave(p, shadow); // discarded: never properly summoned
                var revived = RebornRevives(engine, p, shadow, out var can);
                Check("Wall Shadow: a copy that was never properly summoned cannot be revived by Monster Reborn",
                    !can && !revived && p.Graveyard.Contains(shadow), $"can={can} revived={revived}");
            }
            {
                var engine = EmptyGraveyards(PlayerMain1(db, pDeck, aDeck, out _));
                var p = engine.Player;
                p.Deck.Clear();
                p.Deck.Add(WallShadow);
                var wall = PlaceMonster(engine, p, LabyrinthWall, 1, BattlePosition.Defense, true);
                var lab = PutInHand(engine, p, MagicalLabyrinth);
                var eq = engine.TryActivateSpellTrap(p, lab, fromHand: true) && PickIfAwaiting(engine, wall);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var ign = engine.TryActivateSpellTrap(p, lab, fromHand: false);
                if (engine.IsAwaitingResponse) engine.PassResponse();
                var shadow = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == WallShadow);
                var proper = shadow != null && shadow.ProcedureCompleted;
                if (shadow != null) engine.SendCardToGrave(p, shadow); // destroyed after its proper summon
                p.Graveyard.RemoveAll(c => c != shadow);
                var revived = shadow != null && RebornRevives(engine, p, shadow, out _);
                Check("Wall Shadow: properly summoned by Magical Labyrinth, it can be revived by Monster Reborn",
                    eq && ign && proper && revived, $"eq={eq} ign={ign} proper={proper} revived={revived}");

                if (revived)
                {
                    engine.ReturnCardToHand(shadow); // back to the hand: it must be properly summoned again
                    engine.SendCardToGrave(p, shadow);
                    var again = RebornRevives(engine, p, shadow, out var can2);
                    Check("Wall Shadow: returned to the hand and discarded, it cannot be revived any more",
                        !can2 && !again && p.Graveyard.Contains(shadow), $"can={can2} again={again}");
                }
            }
            {
                var engine = EmptyGraveyards(PlayerMain1(db, pDeck, aDeck, out _));
                var p = engine.Player;
                var sisters = PutInHand(engine, p, HarpieLadySisters);
                engine.SendCardToGrave(p, sisters);
                var revived = RebornRevives(engine, p, sisters, out var can);
                Check("Harpie Lady Sisters: never summoned by Elegant Egotist → Monster Reborn cannot revive it",
                    !can && !revived, $"can={can} revived={revived}");

                var e2 = EmptyGraveyards(PlayerMain1(db, pDeck, aDeck, out _));
                var p2 = e2.Player;
                PlaceMonster(e2, p2, HarpieLady, 0, BattlePosition.Attack, true);
                p2.Deck.Clear();
                p2.Deck.Add(HarpieLadySisters);
                var ego = PutInHand(e2, p2, ElegantEgotist);
                var ok = e2.TryActivateSpellTrap(p2, ego, fromHand: true);
                if (e2.IsAwaitingResponse) e2.PassResponse();
                var summoned = p2.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == HarpieLadySisters);
                if (summoned != null) e2.SendCardToGrave(p2, summoned);
                p2.Graveyard.RemoveAll(c => c != summoned);
                var back = summoned != null && RebornRevives(e2, p2, summoned, out _);
                Check("Harpie Lady Sisters: summoned by Elegant Egotist, then sent to the GY — Monster Reborn revives it",
                    ok && summoned != null && back, $"ok={ok} summoned={summoned != null} back={back}");
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static CompiledCardProgram Synthetic(int id, string type, string race, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = "TributeSummons near-miss " + id,
                type = type,
                race = race,
                frameType = "spell",
                desc = desc
            });

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var dollLoose = Synthetic(90014101, "Spell Card", "Normal",
                "Tribute 1 monster; Special Summon 1 Level 7 monster from your hand.");
            Check("Near-miss: a Tribute Doll shape without the NS/Set clause and attack lock is not Tribute Doll's clause",
                dollLoose == null || !dollLoose.ClauseList.Exists(c => c != null &&
                    (c.RequiresExactLevel > 0 || c.RequiresCanBeNormalSummonedOrSet || c.SummonCannotAttackThisTurn)),
                Describe(dollLoose));

            var meta = CardTextEffectCompiler.Compile(db.Get(46411259)); // Metamorphosis: same Level, Extra Deck Fusion
            // SummonTriggersAndTributes batch (v163) implements Metamorphosis as its own Extra Deck shape: the guard now
            // pins that it never compiles to Insect Imitation's Level-matched Deck summon.
            Check("Near-miss: Metamorphosis (same-Level Fusion from the Extra Deck) is not a Level-matched Deck summon",
                meta != null && meta.FullyCompiled && meta.ClauseList.Count == 1 &&
                meta.ClauseList[0].Action == EffectActionKind.SpecialSummonFusionFromExtra &&
                meta.ClauseList[0].MatchTributedLevel && meta.ClauseList[0].TributedLevelOffset == 0 &&
                !meta.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SpecialSummonFromDeck),
                Describe(meta));

            var namedEquip = Synthetic(90014102, "Spell Card", "Equip",
                "Equip only to \"Labyrinth Wall\". It gains 500 ATK.");
            Check("Near-miss: another named-Equip sentence pair does not borrow Magical Labyrinth's atoms",
                namedEquip == null || !namedEquip.ClauseList.Exists(c => c != null &&
                    (c.EquipHostName == "Labyrinth Wall" || c.RequiresTributeEquippedMonster)),
                Describe(namedEquip));

            var seHalf = Synthetic(90014103, "Spell Card", "Normal",
                "Target 1 monster your opponent controls; this turn, if you Tribute a monster, you must Tribute that target, as if you controlled it.");
            Check("Near-miss: Soul Exchange without its Battle Phase oath does not compile to the grant",
                seHalf == null || !seHalf.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.TributeTargetAsIfControlledThisTurn),
                Describe(seHalf));

            var dollInside = Synthetic(90014104, "Spell Card", "Normal",
                "If you control a Spellcaster monster: Tribute 1 monster; Special Summon 1 Level 7 monster from your hand that can be Normal Summoned/Set. It cannot attack this turn.");
            Check("Near-miss: a conditioned Tribute Doll sentence is not FullyCompiled",
                dollInside == null || !dollInside.FullyCompiled, Describe(dollInside));
        }
    }
}
