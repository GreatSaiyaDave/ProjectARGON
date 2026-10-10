using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SpecialSummonEffects batch (v161, LOD): Skull Knight #2, Twin-Headed Behemoth, Winged Minion and The Puppet Magic
    /// of Dark Ruler. Compile shapes, mini-duels through the real engine entry points (TryNormalSummon with Tributes,
    /// DestroyCard + EndTurn, TryActivateSpellTrap for a monster ignition and a Spell, target picks, Cancel) and near
    /// misses. Gradius' Option stays fail-closed (see the batch notes). Runtime: <see cref="SpecialSummonEffectsRuntime"/>.
    /// </summary>
    public static class SpecialSummonEffectsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SkullKnight2 = 15653824;      // Fiend Lv3 1000/1200
        const int TwinHeadedBehemoth = 43586926; // Dragon Lv3 1500/1200
        const int PuppetMagic = 40703393;       // Normal Spell
        const int WingedMinion = 89258225;      // Fiend Lv2 700/700
        const int GradiusOption = 14291024;     // Machine Lv1 ?/?
        const int Gradius = 10992251;           // Machine Lv4 1200/800
        const int MonsterReborn = 83764718;

        const int SummonedSkull = 70781052;     // Fiend Lv6 2500/1200
        const int LusterDragon2 = 17658803;     // Dragon Lv6 2400/1400
        const int CelticGuardian = 91152256;    // Warrior Lv4 1400/1200
        const int OjamaGreen = 12482652;        // Beast Lv2 0/1000
        const int RyuKishin = 15303296;         // Fiend Lv3 1000/500
        const int GiantSoldier = 13039848;      // Rock Lv3 1300/2000
        const int ArchfiendSoldier = 49881766;  // Fiend Lv4 1900/1500

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("SpecialSummonEffects: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            SkullKnightDuels(db, pDeck, aDeck, Check);
            BehemothDuels(db, pDeck, aDeck, Check);
            WingedMinionDuels(db, pDeck, aDeck, Check);
            PuppetMagicDuels(db, pDeck, aDeck, Check);
            GradiusOptionDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var engine = Fresh(db, pDeck, aDeck);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static bool OnField(DuelistState who, CardInstance c) => c != null && who.TryFindMonster(c, out _);

        static void Drain(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var sk = Prog(db, SkullKnight2);
            Check("Skull Knight #2 compiles to the Tribute-material Deck summon of itself for a Fiend",
                sk.FullyCompiled && sk.ClauseList.Count == 1 &&
                sk.ClauseList[0].Action == EffectActionKind.SpecialSummonNamedFromDeckWhenTributedForRaceSummon &&
                sk.ClauseList[0].RaceFilter == "Fiend" && sk.ClauseList[0].NamedCard == "Skull Knight #2" &&
                Implemented(db, SkullKnight2), Describe(sk));

            var th = Prog(db, TwinHeadedBehemoth);
            var thc = th.ClauseList.FirstOrDefault();
            Check("Twin-Headed Behemoth compiles to an optional once-per-Duel End Phase GY revival at 1000/1000",
                th.FullyCompiled && th.ClauseList.Count == 1 && thc != null &&
                thc.Timing == EffectTiming.EndPhase &&
                thc.Action == EffectActionKind.SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn &&
                thc.ResolvesFromGy && thc.IsOptional && thc.OncePerDuel && thc.Amount == 1000 && thc.DefAmount == 1000 &&
                thc.NamedCard == "Twin-Headed Behemoth" && Implemented(db, TwinHeadedBehemoth), Describe(th));

            var wm = Prog(db, WingedMinion);
            var wmc = wm.ClauseList.FirstOrDefault();
            Check("Winged Minion compiles to a self-Tribute ignition: 1 face-up Fiend gains 700/700 while face-up",
                wm.FullyCompiled && wm.ClauseList.Count == 1 && wmc != null &&
                wmc.Timing == EffectTiming.Activate && wmc.Action == EffectActionKind.TargetGainsAtkDefWhileFaceUp &&
                wmc.Zone == EffectZoneFilter.FieldAnyMonster && wmc.RequiresTributeThis && wmc.RequiresTargetChoice &&
                wmc.RequiresTargetFaceUp && wmc.RaceFilter == "Fiend" && wmc.Amount == 700 && wmc.DefAmount == 700 &&
                Implemented(db, WingedMinion), Describe(wm));

            var pm = Prog(db, PuppetMagic);
            var pmc = pm.ClauseList.FirstOrDefault();
            Check("The Puppet Magic of Dark Ruler compiles to a GY Fiend target, exact-Level banish, then its summon",
                pm.FullyCompiled && pm.ClauseList.Count == 1 && pmc != null &&
                pmc.Timing == EffectTiming.Activate && pmc.Action == EffectActionKind.BanishLevelSumThenSpecialSummonTarget &&
                pmc.Zone == EffectZoneFilter.ControllerGyMonsters && pmc.RequiresTargetChoice &&
                pmc.RaceFilter == "Fiend" && Implemented(db, PuppetMagic), Describe(pm));

            var go = Prog(db, GradiusOption);
            Check("Gradius' Option compiles to its hand procedure (face-up Gradius, no Tribute), the ATK/DEF copy and the self-destruction",
                go.FullyCompiled && go.ClauseList.Count == 3 && Implemented(db, GradiusOption) &&
                go.ClauseList.Exists(c => c.Action == EffectActionKind.SpecialSummonFromHandProcedure &&
                                          c.RequiresFaceUpName == "Gradius" && c.Amount == 0) &&
                go.ClauseList.Exists(c => c.Action == EffectActionKind.AtkDefBecomeSelectedNamed && c.NamedCard == "Gradius") &&
                go.ClauseList.Exists(c => c.Action == EffectActionKind.DestroyThisWhenSelectedNamedLeavesField &&
                                          c.NamedCard == "Gradius"), Describe(go));
        }

        // ───────────────────────────── Skull Knight #2 ─────────────────────────────

        static void SkullKnightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Tribute Summon of a face-up Fiend: a copy comes from the Deck.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var knight = PlaceMonster(engine, p, SkullKnight2, 0, BattlePosition.Attack, true);
                var skull = PutInHand(engine, p, SummonedSkull);
                p.Deck.Insert(0, SkullKnight2);
                var copies = p.Deck.Count(id => id == SkullKnight2);
                engine.PendingTributes.Add(knight);
                var summoned = engine.TryNormalSummon(p, skull, false);
                Drain(engine);
                var newKnight = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == SkullKnight2);
                Check("Skull Knight #2: Tributed for Summoned Skull, it Special Summons a copy from the Deck",
                    summoned && OnField(p, skull) && p.Graveyard.Contains(knight) && newKnight != null &&
                    newKnight.FaceUp && p.Deck.Count(id => id == SkullKnight2) == copies - 1,
                    $"summoned={summoned} newKnight={newKnight != null} deckCopies={p.Deck.Count(id => id == SkullKnight2)} (was {copies})");
            }

            // A non-Fiend Tribute Summon: nothing.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var knight = PlaceMonster(engine, p, SkullKnight2, 0, BattlePosition.Attack, true);
                var luster = PutInHand(engine, p, LusterDragon2);
                p.Deck.Insert(0, SkullKnight2);
                var copies = p.Deck.Count(id => id == SkullKnight2);
                engine.PendingTributes.Add(knight);
                var summoned = engine.TryNormalSummon(p, luster, false);
                Drain(engine);
                Check("Skull Knight #2: Tributed for a Dragon (not a Fiend), nothing is Special Summoned",
                    summoned && OnField(p, luster) && p.MonsterCount == 1 &&
                    p.Deck.Count(id => id == SkullKnight2) == copies,
                    $"summoned={summoned} monsters={p.MonsterCount}");
            }

            // A Tribute Set of a Fiend is not a Tribute Summon of a face-up Fiend.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var knight = PlaceMonster(engine, p, SkullKnight2, 0, BattlePosition.Attack, true);
                var skull = PutInHand(engine, p, SummonedSkull);
                p.Deck.Insert(0, SkullKnight2);
                var copies = p.Deck.Count(id => id == SkullKnight2);
                engine.PendingTributes.Add(knight);
                var set = engine.TryNormalSummon(p, skull, true);
                Drain(engine);
                Check("Skull Knight #2: a Tribute Set of a Fiend summons nothing",
                    set && OnField(p, skull) && !skull.FaceUp && p.MonsterCount == 1 &&
                    p.Deck.Count(id => id == SkullKnight2) == copies,
                    $"set={set} monsters={p.MonsterCount}");
            }
        }

        // ───────────────────────────── Twin-Headed Behemoth ─────────────────────────────

        static void BehemothDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // AI: destroyed on its field during the player's turn, it comes back in the End Phase at 1000/1000, once per Duel.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var o = engine.Opponent;
                var turnPlayer = engine.TurnPlayer;
                var beh = PlaceMonster(engine, o, TwinHeadedBehemoth, 1, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(engine, beh, byPlayer: p);
                var inGy = o.Graveyard.Contains(beh);
                engine.TryEndTurnSafe(turnPlayer);
                Drain(engine);
                var back = OnField(o, beh);
                Check("Twin-Headed Behemoth (AI): destroyed on the field, it is Special Summoned in the End Phase with 1000/1000",
                    inGy && back && beh.FaceUp && beh.CurrentAtk == 1000 && beh.CurrentDef == 1000,
                    $"inGy={inGy} back={back} atk={beh.CurrentAtk} def={beh.CurrentDef} turn={engine.TurnNumber}");

                // Once per Duel: destroyed again (any later turn), it stays in the GY.
                if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                TextEffectRuntime.DestroyCard(engine, beh, byPlayer: p);
                var tp = engine.TurnPlayer;
                engine.TryEndTurnSafe(tp);
                Drain(engine);
                Check("Twin-Headed Behemoth (AI): once per Duel — destroyed again, it stays in the GY",
                    o.Graveyard.Contains(beh) && !OnField(o, beh),
                    $"inGy={o.Graveyard.Contains(beh)} onField={OnField(o, beh)}");
            }

            // Human: the End Phase asks; selecting it Special Summons it, and the turn then ends.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var turnPlayer = engine.TurnPlayer;
                var turn = engine.TurnNumber;
                var beh = PlaceMonster(engine, p, TwinHeadedBehemoth, 1, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(engine, beh, byPlayer: engine.Opponent);
                engine.TryEndTurnSafe(turnPlayer);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                            ReferenceEquals(engine.PendingActivation.Card, beh) &&
                            engine.PendingActivation.LegalTargets.Count == 1 &&
                            engine.PendingActivation.LegalTargets[0] == beh;
                var picked = asked && engine.TrySelectEffectTarget(beh);
                Drain(engine);
                Check("Twin-Headed Behemoth (human): the End Phase offers it; Yes summons it at 1000/1000 and the turn ends",
                    asked && picked && OnField(p, beh) && beh.CurrentAtk == 1000 && beh.CurrentDef == 1000 &&
                    engine.TurnNumber > turn,
                    $"asked={asked} picked={picked} onField={OnField(p, beh)} atk={beh.CurrentAtk} turn={engine.TurnNumber}");

                // Turned face-down, the ATK/DEF change ends (RESETS_STANDARD): flipped up again it is 1500/1200.
                beh.FaceUp = false;
                beh.Position = BattlePosition.Defense;
                engine.NotifyPublic();
                beh.FaceUp = true;
                beh.Position = BattlePosition.Attack;
                engine.NotifyPublic();
                Check("Twin-Headed Behemoth: turned face-down and back, its ATK/DEF are the printed 1500/1200 again",
                    beh.CurrentAtk == 1500 && beh.CurrentDef == 1200, $"atk={beh.CurrentAtk} def={beh.CurrentDef}");
            }

            // Human: Cancel declines (not used up); the turn ends with it in the GY.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var turnPlayer = engine.TurnPlayer;
                var turn = engine.TurnNumber;
                var beh = PlaceMonster(engine, p, TwinHeadedBehemoth, 1, BattlePosition.Attack, true);
                TextEffectRuntime.DestroyCard(engine, beh, byPlayer: engine.Opponent);
                engine.TryEndTurnSafe(turnPlayer);
                var asked = engine.IsAwaitingEffectTarget;
                var cancelled = asked && engine.CancelEffectTargeting();
                Drain(engine);
                Check("Twin-Headed Behemoth (human): Cancel declines — it stays in the GY and the turn ends",
                    asked && cancelled && p.Graveyard.Contains(beh) && engine.TurnNumber > turn &&
                    !SpecialSummonEffectsRuntime.OncePerDuelUsed(engine, p,
                        CardTextEffectCompiler.Compile(beh.Def).ClauseList[0], beh),
                    $"asked={asked} cancelled={cancelled} inGy={p.Graveyard.Contains(beh)} turn={engine.TurnNumber}");
            }

            // Sent to the GY without being destroyed (a Tribute): no trigger.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var turnPlayer = engine.TurnPlayer;
                var beh = PlaceMonster(engine, p, TwinHeadedBehemoth, 0, BattlePosition.Attack, true);
                var skull = PutInHand(engine, p, SummonedSkull);
                engine.PendingTributes.Add(beh);
                var summoned = engine.TryNormalSummon(p, skull, false);
                Drain(engine);
                engine.TryEndTurnSafe(turnPlayer);
                var asked = engine.IsAwaitingEffectTarget;
                Check("Twin-Headed Behemoth: Tributed (not destroyed), the End Phase does not offer it",
                    summoned && p.Graveyard.Contains(beh) && !asked && !OnField(p, beh),
                    $"summoned={summoned} asked={asked}");
            }
        }

        // ───────────────────────────── Winged Minion ─────────────────────────────

        static void WingedMinionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var minion = PlaceMonster(engine, p, WingedMinion, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, p, SummonedSkull, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, CelticGuardian, 2, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var can = engine.CanActivateSpellTrap(p, minion, fromHand: false);
                var activated = can && engine.TryActivateSpellTrap(p, minion, fromHand: false);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(skull))
                    engine.TrySelectEffectTarget(skull);
                Drain(engine);
                Check("Winged Minion: Tributes itself; the lone face-up Fiend gains 700 ATK/DEF (3200/1900)",
                    activated && p.Graveyard.Contains(minion) && skull.CurrentAtk == 3200 && skull.CurrentDef == 1900,
                    $"can={can} activated={activated} minionInGy={p.Graveyard.Contains(minion)} atk={skull.CurrentAtk} def={skull.CurrentDef}");

                engine.NotifyPublic();
                var stays = skull.CurrentAtk == 3200;
                skull.FaceUp = false;
                skull.Position = BattlePosition.Defense;
                engine.NotifyPublic();
                skull.FaceUp = true;
                skull.Position = BattlePosition.Attack;
                engine.NotifyPublic();
                Check("Winged Minion: the gain lasts across refreshes and ends once the monster is turned face-down",
                    stays && skull.CurrentAtk == 2500 && skull.CurrentDef == 1200,
                    $"stays={stays} atk={skull.CurrentAtk} def={skull.CurrentDef}");
            }

            // Either field: with only an opponent's Fiend, that is the target; a face-up Warrior never is.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var o = engine.Opponent;
                var minion = PlaceMonster(engine, p, WingedMinion, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, CelticGuardian, 1, BattlePosition.Attack, true);
                var opp = PlaceMonster(engine, o, ArchfiendSoldier, 0, BattlePosition.Attack, true);
                var setFiend = PlaceMonster(engine, o, RyuKishin, 1, BattlePosition.Defense, false);
                engine.NotifyPublic();
                var activated = engine.TryActivateSpellTrap(p, minion, fromHand: false);
                var legal = engine.PendingActivation?.LegalTargets.ToList();
                if (engine.IsAwaitingEffectTarget && legal != null && legal.Contains(opp))
                    engine.TrySelectEffectTarget(opp);
                Drain(engine);
                Check("Winged Minion: the opponent's face-up Fiend is a legal target (not a Warrior, not a Set Fiend)",
                    activated && opp.CurrentAtk == 2600 && opp.CurrentDef == 2200 &&
                    (legal == null || (!legal.Contains(setFiend) && legal.All(t => t == opp))),
                    $"activated={activated} atk={opp.CurrentAtk} legal={(legal == null ? "auto" : string.Join(",", legal.Select(t => t.Name)))}");
            }

            // No other face-up Fiend: it cannot be activated (it cannot target itself).
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var minion = PlaceMonster(engine, p, WingedMinion, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, CelticGuardian, 1, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var can = engine.CanActivateSpellTrap(p, minion, fromHand: false);
                var tried = !can && engine.TryActivateSpellTrap(p, minion, fromHand: false);
                Check("Winged Minion: with no other face-up Fiend it cannot be activated",
                    !can && !tried && OnField(p, minion), $"can={can} tried={tried}");
            }
        }

        // ───────────────────────────── The Puppet Magic of Dark Ruler ─────────────────────────────

        static void PuppetMagicDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // One exact group: Lv4 + Lv2 banished, then Summoned Skull (Lv6) from the GY.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var skull = engine.CreateCardInstance(SummonedSkull);
                p.Graveyard.Add(skull);
                var luster = engine.CreateCardInstance(LusterDragon2); // Lv6 Dragon: not a legal target
                p.Graveyard.Add(luster);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, p, OjamaGreen, 1, BattlePosition.Defense, false);
                var spell = PutInHand(engine, p, PuppetMagic);
                engine.NotifyPublic();
                var activated = engine.TryActivateSpellTrap(p, spell, fromHand: true);
                var legal = engine.PendingActivation?.LegalTargets.ToList();
                if (engine.IsAwaitingEffectTarget && legal != null && legal.Contains(skull))
                    engine.TrySelectEffectTarget(skull);
                Drain(engine);
                Check("Puppet Magic: banishes Lv4 + face-down Lv2 and Special Summons the Lv6 Fiend from the GY",
                    activated && OnField(p, skull) && p.Banished.Contains(celtic) && p.Banished.Contains(ojama) &&
                    p.Graveyard.Contains(luster) && (legal == null || !legal.Contains(luster)),
                    $"activated={activated} skullOnField={OnField(p, skull)} celticBanished={p.Banished.Contains(celtic)} " +
                    $"ojamaBanished={p.Banished.Contains(ojama)} legal={(legal == null ? "auto" : string.Join(",", legal.Select(t => t.Name)))}");
            }

            // Two exact groups (4+2 or 3+3): the human selects; the Chain waits; the other monsters stay.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var skull = engine.CreateCardInstance(SummonedSkull);
                p.Graveyard.Add(skull);
                var celtic = PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                var ojama = PlaceMonster(engine, p, OjamaGreen, 1, BattlePosition.Attack, true);
                var soldier = PlaceMonster(engine, p, GiantSoldier, 2, BattlePosition.Attack, true);
                var ryu = PlaceMonster(engine, p, RyuKishin, 3, BattlePosition.Attack, true);
                var spell = PutInHand(engine, p, PuppetMagic);
                engine.NotifyPublic();
                var activated = engine.TryActivateSpellTrap(p, spell, fromHand: true);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(skull) &&
                    !engine.PendingActivation.HoldsChainResolution)
                    engine.TrySelectEffectTarget(skull);
                Drain(engine);
                var askedBanish = engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                                  engine.PendingActivation.HoldsChainResolution &&
                                  engine.PendingActivation.LegalTargets.Count == 4;
                var first = askedBanish && engine.TrySelectEffectTarget(soldier);
                var narrowed = first && engine.IsAwaitingEffectTarget &&
                               engine.PendingActivation.LegalTargets.Count == 1 &&
                               engine.PendingActivation.LegalTargets[0] == ryu;
                // Cancel after a pick starts the selection over.
                var restarted = narrowed && engine.CancelEffectTargeting() && engine.IsAwaitingEffectTarget &&
                                engine.PendingActivation.LegalTargets.Count == 4;
                var a = restarted && engine.TrySelectEffectTarget(soldier);
                var b = a && engine.TrySelectEffectTarget(ryu);
                Drain(engine);
                Check("Puppet Magic: the human selects the 3 + 3 group (Cancel restarts it); the 4 + 2 stay; Skull is summoned",
                    activated && askedBanish && narrowed && restarted && b && OnField(p, skull) &&
                    p.Banished.Contains(soldier) && p.Banished.Contains(ryu) && OnField(p, celtic) && OnField(p, ojama) &&
                    !engine.IsAwaitingEffectTarget,
                    $"activated={activated} asked={askedBanish} narrowed={narrowed} restarted={restarted} b={b} " +
                    $"skull={OnField(p, skull)} pending={engine.PendingActivation?.Prompt}");
            }

            // No exact Level total: it cannot be activated.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                p.Graveyard.Add(engine.CreateCardInstance(SummonedSkull));
                PlaceMonster(engine, p, CelticGuardian, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Attack, true);
                var spell = PutInHand(engine, p, PuppetMagic);
                engine.NotifyPublic();
                var can = engine.CanActivateSpellTrap(p, spell, fromHand: true);
                Check("Puppet Magic: no group totalling Level 6 (4 + 3) — it cannot be activated",
                    !can && p.Hand.Contains(spell), $"can={can}");
            }
        }

        // ───────────────────────────── Gradius' Option ─────────────────────────────

        static void GradiusOptionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // One face-up Gradius: the procedure selects it; ATK/DEF copy it live; it leaves → the Option is destroyed.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var gradius = PlaceMonster(engine, p, Gradius, 0, BattlePosition.Attack, true);
                var option = PutInHand(engine, p, GradiusOption);
                engine.NotifyPublic();
                var normal = engine.TryNormalSummon(p, option, false);
                var can = engine.CanSpecialSummonProcedure(p, option);
                var summoned = can && engine.TrySpecialSummonProcedure(p, option);
                Drain(engine);
                engine.NotifyPublic();
                var copied = OnField(p, option) && option.CurrentAtk == 1200 && option.CurrentDef == 800;
                Check("Gradius' Option: cannot be Normal Summoned; Special Summoned by selecting the face-up Gradius (1200/800)",
                    !normal && can && summoned && copied,
                    $"normal={normal} can={can} summoned={summoned} onField={OnField(p, option)} atk={option.CurrentAtk} def={option.CurrentDef}");

                gradius.UntilEndOfTurnAtk += 500;
                engine.NotifyPublic();
                Check("Gradius' Option: its ATK follows the selected Gradius' current ATK (1700)",
                    option.CurrentAtk == 1700 && option.CurrentDef == 800, $"atk={option.CurrentAtk} def={option.CurrentDef}");

                TextEffectRuntime.DestroyCard(engine, gradius, byPlayer: engine.Opponent);
                engine.NotifyPublic(); // the board refresh every command ends with
                Drain(engine);
                Check("Gradius' Option: the selected Gradius left the field — the Option is destroyed",
                    p.Graveyard.Contains(gradius) && p.Graveyard.Contains(option) && !OnField(p, option),
                    $"gradiusInGy={p.Graveyard.Contains(gradius)} optionInGy={p.Graveyard.Contains(option)}");

                // Only its own procedure: properly summoned before, it still cannot be revived (Monster Reborn).
                var newGradius = PlaceMonster(engine, p, Gradius, 1, BattlePosition.Attack, true);
                engine.NotifyPublic();
                var legal = SpecialSummonLegality.CanBeSpecialSummoned(engine, p, option, null, SummonOrigin.Graveyard);
                var reborn = PutInHand(engine, p, MonsterReborn);
                var rebornCan = engine.CanActivateSpellTrap(p, reborn, fromHand: true);
                if (rebornCan) engine.TryActivateSpellTrap(p, reborn, fromHand: true);
                if (engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Contains(option))
                    engine.TrySelectEffectTarget(option);
                Drain(engine);
                Check("Gradius' Option: no Special Summon other than its procedure (Monster Reborn cannot revive it)",
                    !legal && !OnField(p, option) && OnField(p, newGradius),
                    $"legal={legal} rebornCan={rebornCan} onField={OnField(p, option)}");
            }

            // No face-up Gradius: no procedure.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                PlaceMonster(engine, p, Gradius, 0, BattlePosition.Defense, false);
                var option = PutInHand(engine, p, GradiusOption);
                engine.NotifyPublic();
                Check("Gradius' Option: a face-down Gradius does not allow the procedure",
                    !engine.CanSpecialSummonProcedure(p, option) && !engine.TrySpecialSummonProcedure(p, option) &&
                    p.Hand.Contains(option), "");
            }

            // Two face-up Gradius: the human selects one first (Cancel = no Summon); the Option copies the selected one.
            {
                var engine = Board(db, pDeck, aDeck);
                var p = engine.Player;
                var g1 = PlaceMonster(engine, p, Gradius, 0, BattlePosition.Attack, true);
                var g2 = PlaceMonster(engine, p, Gradius, 1, BattlePosition.Attack, true);
                g2.UntilEndOfTurnAtk += 300;
                var option = PutInHand(engine, p, GradiusOption);
                engine.NotifyPublic();
                var started = engine.TrySpecialSummonProcedure(p, option);
                var asked = engine.IsAwaitingEffectTarget && engine.PendingActivation.LegalTargets.Count == 2;
                var cancelled = asked && engine.CancelEffectTargeting() && !engine.IsAwaitingEffectTarget &&
                                p.Hand.Contains(option);
                var again = cancelled && engine.TrySpecialSummonProcedure(p, option);
                var picked = again && engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(g1);
                Drain(engine);
                engine.NotifyPublic();
                Check("Gradius' Option: with 2 face-up Gradius the human selects one (Cancel first); it copies that one (1200)",
                    started && asked && cancelled && picked && OnField(p, option) && option.CurrentAtk == 1200,
                    $"started={started} asked={asked} cancelled={cancelled} picked={picked} atk={option.CurrentAtk}");

                // The other Gradius leaving does not matter; the selected one does.
                TextEffectRuntime.DestroyCard(engine, g2, byPlayer: engine.Opponent);
                engine.NotifyPublic(); // the board refresh every command ends with
                Drain(engine);
                var stays = OnField(p, option);
                TextEffectRuntime.DestroyCard(engine, g1, byPlayer: engine.Opponent);
                engine.NotifyPublic(); // the board refresh every command ends with
                Drain(engine);
                Check("Gradius' Option: only the selected Gradius' leaving destroys it",
                    stays && !OnField(p, option) && p.Graveyard.Contains(option), $"stays={stays}");
            }
        }
    }
}
