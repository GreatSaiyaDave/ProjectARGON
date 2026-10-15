using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TokensDeclarationsAndPicks batch: Stray Lambs, Scapegoat, Multiplication of Ants, DNA Transplant, Jade Insect Whistle,
    /// A Hero Emerges, Wild Nature's Release, Balloon Lizard. Compile shapes through the real compiler, near-miss texts, and
    /// mini-duels through the engine entry points (TryActivateSpellTrap, TrySelectEffectTarget, TryNormalSummon,
    /// CanFlipSummon, TryAttack, the attack response window, TryEndTurnSafe, FirePhaseTriggers).
    /// Runtime: <see cref="TokensDeclarationsAndPicksRuntime"/>.
    /// </summary>
    public static class TokensDeclarationsAndPicksRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int StrayLambs = 60764581;
        const int Scapegoat = 73915051;
        const int Ants = 22493811;
        const int DnaTransplant = 56769674;
        const int JadeWhistle = 95214051;
        const int HeroEmerges = 21597117;
        const int WildNature = 61166988;
        const int BalloonLizard = 39892082; // Reptile 500/1900

        const int Celtic = 91152256;        // Normal Warrior EARTH 1400/1200
        const int GiantSoldier = 13039848;  // Normal Rock EARTH 1300/2000
        const int SummonedSkull = 70781052; // Normal Fiend DARK 2500/1200
        const int Kumootoko = 56283725;     // Normal Insect 700/1400
        const int NeoBug = 16587243;        // Normal Insect 1800/1700
        const int SilverFang = 90357090;    // Normal Beast 1200/800
        const int BattleOx = 5053103;       // Normal Beast-Warrior 1700/1000
        const int BlackLuster = 5405694;    // Ritual Monster: cannot be Special Summoned from the hand
        const int MonsterReborn = 83764718;
        const int DarkHole = 53129443;
        const int OjamaTrio = 29843091;
        const int LabyrinthWall = 67284908; // Normal Rock 0/3000

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TokensDeclarationsAndPicks: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            StrayLambsDuels(db, pDeck, aDeck, Check);
            ScapegoatDuels(db, pDeck, aDeck, Check);
            AntsDuels(db, pDeck, aDeck, Check);
            DnaTransplantDuels(db, pDeck, aDeck, Check);
            JadeWhistleDuels(db, pDeck, aDeck, Check);
            HeroEmergesDuels(db, pDeck, aDeck, Check);
            WildNatureDuels(db, pDeck, aDeck, Check);
            BalloonLizardDuels(db, pDeck, aDeck, Check);
            OjamaTrioTokenArtVariants(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        // ────────────────── Token hologram artwork variants ─────────────────
        /// <summary>
        /// Ojama Trio (29843091) Special Summons 3 "Ojama Tokens" to its opponent's field
        /// (Beast/LIGHT/Lv2 0/1000, Defense Position; cannot be Tributed for a Tribute
        /// Summon; 300 damage when each is destroyed). The three share one printed name and
        /// one hashed CardId, so they must be told apart for artwork by summon order.
        /// SummonTokens now records that order on <see cref="CardInstance.TokenArtIndex"/>
        /// (set in the summon loop), which WRLDZ.Presentation.TokenArtCatalog maps to
        /// Ojama Yellow (42941100), Green (12482652), Black (79335209). This test pins the
        /// engine half (the variant index); the catalog mapping is presentation-only and is
        /// guarded by Tools/token_art_catalog_check.py and a source listing instead.
        /// </summary>
        static void OjamaTrioTokenArtVariants(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var oppTrio = e.CreateCardInstance(OjamaTrio);
                var clause = CompiledEffectCache.GetOrCompile(oppTrio.Def).ClauseList
                    .FirstOrDefault(c => c.Action == EffectActionKind.SpecialSummonToken);
                bool applied = false, broken = false;
                if (clause != null) TextEffectRuntime.ApplyClause(e, e.Opponent, oppTrio, clause, null, ref applied, ref broken);
                var t = p.MonstersOnField()
                    .Where(m => m != null && m.IsToken && m.Name.Contains("Ojama"))
                    .OrderBy(m => m.TokenArtIndex).ToList();
                Check("Ojama Trio: three Ojama Tokens summoned with distinct art variant indices",
                    reached && applied && t.Count == 3 &&
                    t[0].TokenArtIndex == 0 && t[1].TokenArtIndex == 1 && t[2].TokenArtIndex == 2 &&
                    t.All(x => x.Name == "Ojama Tokens" && x.FaceUp && x.Position == BattlePosition.Defense &&
                               x.CurrentAtk == 0 && x.CurrentDef == 1000 && x.CannotBeTributedForSummon &&
                               x.TokenDestroyedDamage == 300 && x.CardId == t[0].CardId),
                    $"applied={applied} n={t.Count} idx=[{string.Join(",", t.Select(x => x.TokenArtIndex))}] {Tail(e, 3)}");
            }
        }

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            return Clean(e);
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, List<string>> s_logs = new();

        /// <summary>The last <paramref name="n"/> log lines since <see cref="Clean"/> (diagnostics only).</summary>
        static string Tail(DuelEngine e, int n) =>
            s_logs.TryGetValue(e, out var l) ? string.Join(" | ", l.Skip(Math.Max(0, l.Count - n))) : "";

        static DuelEngine Clean(DuelEngine e)
        {
            if (!s_logs.TryGetValue(e, out _))
            {
                var lines = new List<string>();
                s_logs.Add(e, lines);
                e.OnLog += lines.Add;
            }

            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want = null)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want ?? (_ => true));
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

            e.NotifyPublic();
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "TokensDeclarationsAndPicks probe", type = type,
                frameType = type.StartsWith("Spell") || type.StartsWith("Trap") ? "spell" : "effect",
                race = race, attribute = type.StartsWith("Spell") || type.StartsWith("Trap") ? null : "EARTH",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static bool HasAction(CompiledCardProgram p, EffectActionKind a) => Has(p, c => c.Action == a);

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static List<CardInstance> Tokens(DuelistState who, string name) =>
            who.MonstersOnField().Where(m => m != null && m.IsToken && m.Name == name).ToList();

        static int Free(DuelistState who) => who.MonsterZones.Count(z => z != null && z.IsEmpty);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var lambs = Prog(db, StrayLambs);
            Check("Stray Lambs FullyCompiled: 2 Lamb Tokens in Defense, all or none, summon record + oath",
                lambs != null && lambs.FullyCompiled && Implemented(db, StrayLambs) && lambs.ClauseList.Count == 1 &&
                Has(lambs, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.SpecialSummonToken &&
                                c.TokenCount == 2 && c.TokenName == "Lamb Token" && c.TokenRace == "Beast" &&
                                c.TokenAttribute == "EARTH" && c.TokenLevel == 1 && c.TokenAtk == 0 && c.TokenDef == 0 &&
                                c.SummonInDefense && !c.TokenCannotTribute && !c.TokenToOpponent &&
                                c.TokensNeedAllZones && c.ForbidsOtherSummonsThisTurn &&
                                c.ActivationCondition == TokensDeclarationsAndPicksRuntime.TokenSummonLegal),
                Describe(lambs));

            var goat = Prog(db, Scapegoat);
            Check("Scapegoat FullyCompiled: 4 Sheep Tokens in Defense, not Tributable for a Tribute Summon, oath",
                goat != null && goat.FullyCompiled && Implemented(db, Scapegoat) && goat.ClauseList.Count == 1 &&
                Has(goat, c => c.Action == EffectActionKind.SpecialSummonToken && c.TokenCount == 4 &&
                               c.TokenName == "Sheep Token" && c.SummonInDefense && c.TokenCannotTribute &&
                               c.TokensNeedAllZones && c.ForbidsOtherSummonsThisTurn),
                Describe(goat));

            var ants = Prog(db, Ants);
            Check("Multiplication of Ants FullyCompiled: Tribute 1 Insect cost, 2 Army Ant Tokens not Tributable",
                ants != null && ants.FullyCompiled && Implemented(db, Ants) && ants.ClauseList.Count == 1 &&
                Has(ants, c => c.Action == EffectActionKind.SpecialSummonToken && c.TokenCount == 2 &&
                               c.TokenName == "Army Ant Token" && c.TokenRace == "Insect" && c.TokenLevel == 4 &&
                               c.TokenAtk == 500 && c.TokenDef == 1200 && c.TokenCannotTribute &&
                               c.RequiresTributeCount == 1 && c.TributeRaceFilter == "Insect" &&
                               c.TokensNeedAllZones && !c.ForbidsOtherSummonsThisTurn),
                Describe(ants));

            var dna = Prog(db, DnaTransplant);
            Check("DNA Transplant FullyCompiled: Attribute declared on activation (not a target) + continuous change",
                dna != null && dna.FullyCompiled && Implemented(db, DnaTransplant) && dna.ClauseList.Count == 2 &&
                Has(dna, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.DeclareMonsterAttribute &&
                              c.Zone == EffectZoneFilter.DeclaredAttributes && c.RequiresTargetChoice &&
                              c.ChoiceDoesNotTarget && !c.IsPsctTarget) &&
                Has(dna, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                              c.Action == EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute),
                Describe(dna));

            var jade = Prog(db, JadeWhistle);
            Check("Jade Insect Whistle FullyCompiled: the opponent places an Insect from their Deck on top",
                jade != null && jade.FullyCompiled && Implemented(db, JadeWhistle) && jade.ClauseList.Count == 1 &&
                Has(jade, c => c.Action == EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop &&
                               c.RaceFilter == "Insect" && !c.RequiresTargetChoice &&
                               c.ActivationCondition == DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty),
                Describe(jade));

            var hero = Prog(db, HeroEmerges);
            Check("A Hero Emerges FullyCompiled: attack-declaration Trap, random hand card summoned or sent",
                hero != null && hero.FullyCompiled && Implemented(db, HeroEmerges) && hero.ClauseList.Count == 1 &&
                Has(hero, c => c.Timing == EffectTiming.AttackDeclared &&
                               c.Action == EffectActionKind.RandomHandCardSpecialSummonOrSendToGy &&
                               c.ActivationCondition == TokensDeclarationsAndPicksRuntime.HandMonsterCanBeSpecialSummoned),
                Describe(hero));

            var wild = Prog(db, WildNature);
            Check("Wild Nature's Release FullyCompiled: target a face-up Beast / Beast-Warrior; ATK + DEF, End Phase destroy",
                wild != null && wild.FullyCompiled && Implemented(db, WildNature) && wild.ClauseList.Count == 1 &&
                Has(wild, c => c.Action == EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase &&
                               c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetChoice &&
                               c.RequiresTargetFaceUp && c.IsPsctTarget && c.RaceFilter == "Beast|Beast-Warrior"),
                Describe(wild));

            var lizard = Prog(db, BalloonLizard);
            Check("Balloon Lizard FullyCompiled: Standby counter + destroyed burn (counters x 400 to the destroyer)",
                lizard != null && lizard.FullyCompiled && Implemented(db, BalloonLizard) && lizard.ClauseList.Count == 2 &&
                Has(lizard, c => c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.PlaceCounterOnThisCard &&
                                 c.Side == EffectSide.Controller && c.Amount == 1) &&
                Has(lizard, c => c.Timing == EffectTiming.SentFromFieldToGy && c.RequiresDestroyed && !c.IsOptional &&
                                 c.Action == EffectActionKind.InflictCountersTimesAmountToDestroyer && c.Amount == 400),
                Describe(lizard));

            // Near misses: look-alike texts never compile to these shapes.
            var lambsNoLock = CompileText(990101, "Spell Card", "Normal",
                "Special Summon 2 \"Lamb Tokens\" (Beast/EARTH/Level 1/ATK 0/DEF 0) in Defense Position. " +
                "You cannot Summon other monsters the turn you activate this card.");
            Check("Near miss: the oath without \"(but you can Set)\" is not this atom",
                !Has(lambsNoLock, c => c.ForbidsOtherSummonsThisTurn) && !(lambsNoLock?.FullyCompiled ?? false),
                Describe(lambsNoLock));
            var antsOther = CompileText(990102, "Spell Card", "Normal",
                "Tribute 1 Insect-Type Monster on your side of the field. Special Summon 2 \"Army Ant Tokens\" " +
                "(Insect-Type/EARTH/Level 4/ATK 500/DEF 1200) on your side of the field.");
            Check("Near miss: Ants without the Tribute-Summon rider is not this atom",
                !Has(antsOther, c => c.TokensNeedAllZones), Describe(antsOther));
            var dnaType = CompileText(990103, "Trap Card", "Continuous",
                "Activate by declaring 1 monster Attribute. Face-up monsters you control become that Attribute.");
            Check("Near miss: a one-side Attribute change is not DNA Transplant's",
                !HasAction(dnaType, EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute) &&
                !HasAction(dnaType, EffectActionKind.DeclareMonsterAttribute), Describe(dnaType));
            var jadeOwn = CompileText(990104, "Spell Card", "Normal",
                "Select 1 Insect-Type Monster from your Deck. After shuffling the Deck, place the card on top of the Deck.");
            Check("Near miss: selecting from your own Deck is not Jade Insect Whistle",
                !HasAction(jadeOwn, EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop), Describe(jadeOwn));
            var wildNoDestroy = CompileText(990105, "Spell Card", "Normal",
                "Target 1 Beast or Beast-Warrior-Type monster; it gains ATK equal to its current DEF until the End Phase.");
            Check("Near miss: the boost without the End Phase destruction is not Wild Nature's Release",
                !HasAction(wildNoDestroy, EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase),
                Describe(wildNoDestroy));
            var lizardOne = CompileText(990106, "Effect Monster", "Reptile",
                "Put 1 counter on this card during each of your Standby Phases.");
            Check("Near miss: the counter sentence alone is not Balloon Lizard's atom",
                !HasAction(lizardOne, EffectActionKind.PlaceCounterOnThisCard), Describe(lizardOne));
        }

        // ───────────────────────────── Stray Lambs ─────────────────────────────

        static void StrayLambsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Review fix: the oath binds its activator as the summoning player only (Lua EFFECT_CANNOT_SPECIAL_SUMMON,
                // target range (1,0)). The opponent's Ojama Trio still puts its 3 Tokens on the oath player's field; the oath
                // player's own Ojama Trio stays locked.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var lambs = PutInHand(e, p, StrayLambs);
                var myTrio = PutInHand(e, p, OjamaTrio);
                var act = e.TryActivateSpellTrap(p, lambs, fromHand: true);
                Drive(e);
                var ownTrioLegal = e.CanActivateSpellTrap(p, myTrio, fromHand: true);
                var oppTrio = e.CreateCardInstance(OjamaTrio);
                var clause = CompiledEffectCache.GetOrCompile(oppTrio.Def).ClauseList
                    .FirstOrDefault(c => c.Action == EffectActionKind.SpecialSummonToken);
                bool an = false, be = false;
                if (clause != null) TextEffectRuntime.ApplyClause(e, e.Opponent, oppTrio, clause, null, ref an, ref be);
                var ojamas = p.MonstersOnField().Count(m => m != null && m.IsToken && m.Name.Contains("Ojama"));
                Check("Stray Lambs: the oath binds only its activator's Summons — the opponent's Ojama Trio puts 3 Tokens on " +
                      "its field, its own Ojama Trio stays locked",
                    reached && act && Tokens(p, "Lamb Token").Count == 2 && ojamas == 3 && !ownTrioLegal,
                    $"reached={reached} act={act} ojamas={ojamas} ownTrio={ownTrioLegal} free={Free(p)} {Tail(e, 4)}");
            }

            {
                // The summon record counts the summoning player: the opponent's Ojama Tokens on your field do not stop your
                // Stray Lambs (Lua ACTIVITY_SPSUMMON counts the summoner).
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var lambs = PutInHand(e, p, StrayLambs);
                var oppTrio = e.CreateCardInstance(OjamaTrio);
                var clause = CompiledEffectCache.GetOrCompile(oppTrio.Def).ClauseList
                    .FirstOrDefault(c => c.Action == EffectActionKind.SpecialSummonToken);
                bool an = false, be = false;
                if (clause != null) TextEffectRuntime.ApplyClause(e, e.Opponent, oppTrio, clause, null, ref an, ref be);
                var ojamas = p.MonstersOnField().Count(m => m != null && m.IsToken && m.Name.Contains("Ojama"));
                Check("Stray Lambs: the opponent's Ojama Tokens on your field are not your Summon — still activatable",
                    ojamas == 3 && e.CanActivateSpellTrap(p, lambs, fromHand: true), $"ojamas={ojamas} {Tail(e, 3)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var setMon = PlaceMonster(e, p, GiantSoldier, 4, BattlePosition.Defense, false);
                var lambs = PutInHand(e, p, StrayLambs);
                var celtic = PutInHand(e, p, Celtic);
                var reborn = PutInHand(e, p, MonsterReborn);
                p.Graveyard.Add(e.CreateCardInstance(SummonedSkull));
                var canFlipBefore = e.CanFlipSummon(p, setMon);
                var act = e.TryActivateSpellTrap(p, lambs, fromHand: true);
                Drive(e);
                var lambTokens = Tokens(p, "Lamb Token");
                Check("Stray Lambs: 2 Lamb Tokens (0/0) in face-up Defense Position",
                    reached && act && lambTokens.Count == 2 &&
                    lambTokens.All(t => t.FaceUp && t.Position == BattlePosition.Defense && t.CurrentDef == 0) &&
                    p.Graveyard.Any(g => g.CardId == StrayLambs),
                    $"reached={reached} act={act} tokens={lambTokens.Count} {Tail(e, 4)}");
                var summoned = e.TryNormalSummon(p, celtic, asSet: false);
                var rebornLegal = e.CanActivateSpellTrap(p, reborn, fromHand: true);
                var flip = e.CanFlipSummon(p, setMon);
                Check("Stray Lambs: after it, no Normal Summon, no Flip Summon, no Special Summon this turn",
                    !summoned && !rebornLegal && canFlipBefore && !flip && p.Hand.Contains(celtic),
                    $"summoned={summoned} reborn={rebornLegal} flipBefore={canFlipBefore} flip={flip}");
                var set = e.TryNormalSummon(p, celtic, asSet: true);
                Check("Stray Lambs: Setting is still allowed",
                    set && p.TryFindMonster(celtic, out _) && !celtic.FaceUp, $"set={set}");
            }

            {
                // A Normal Summon this turn forbids the activation (Lua s.cost: ACTIVITY_SUMMON == 0).
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var celtic = PutInHand(e, p, Celtic);
                var lambs = PutInHand(e, p, StrayLambs);
                var before = e.CanActivateSpellTrap(p, lambs, fromHand: true);
                var summoned = e.TryNormalSummon(p, celtic, asSet: false);
                Drive(e);
                var after = e.CanActivateSpellTrap(p, lambs, fromHand: true);
                Check("Stray Lambs: not activatable after a Normal Summon by you this turn",
                    before && summoned && !after, $"before={before} summoned={summoned} after={after}");
            }

            {
                // A Set is not a Summon; the opponent's summons do not count; 1 free zone is not enough.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var celtic = PutInHand(e, p, Celtic);
                var lambs = PutInHand(e, p, StrayLambs);
                e.TryNormalSummon(p, celtic, asSet: true);
                Drive(e);
                var afterSet = e.CanActivateSpellTrap(p, lambs, fromHand: true);
                for (var z = 0; z < 5 && Free(p) > 1; z++)
                    if (p.MonsterZones[z].IsEmpty) PlaceMonster(e, p, GiantSoldier, z, BattlePosition.Attack, true);
                var oneZone = Free(p) == 1 && !e.CanActivateSpellTrap(p, lambs, fromHand: true);
                Check("Stray Lambs: a Set is not a Summon; with 1 free Monster Zone it cannot be activated",
                    afterSet && oneZone, $"afterSet={afterSet} free={Free(p)}");
            }
        }

        // ───────────────────────────── Scapegoat ─────────────────────────────

        static void ScapegoatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var goat = PutInHand(e, p, Scapegoat);
                var skull = PutInHand(e, p, SummonedSkull);
                var act = e.TryActivateSpellTrap(p, goat, fromHand: true);
                Drive(e);
                var sheep = Tokens(p, "Sheep Token");
                Check("Scapegoat: 4 Sheep Tokens in face-up Defense Position that cannot be Tributed for a Tribute Summon",
                    reached && act && sheep.Count == 4 && sheep.All(t => t.Position == BattlePosition.Defense) &&
                    sheep.All(t => t.CannotBeTributedForSummon && !TcgRules.CanBeTributedForSummon(p, t)),
                    $"act={act} sheep={sheep.Count}");
                var tribute = e.TryNormalSummon(p, skull, asSet: true);
                Check("Scapegoat: the Sheep Tokens cannot be Tributed to Tribute Set a Level 6",
                    !tribute && p.Hand.Contains(skull) && Tokens(p, "Sheep Token").Count == 4, $"tribute={tribute}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var goat = PutInHand(e, p, Scapegoat);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var withFour = e.CanActivateSpellTrap(p, goat, fromHand: true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                Check("Scapegoat: needs 4 free Monster Zones",
                    withFour && Free(p) == 3 && !e.CanActivateSpellTrap(p, goat, fromHand: true), $"free={Free(p)}");
            }
        }

        // ───────────────────────────── Multiplication of Ants ─────────────────────────────

        static void AntsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Review fix: POS_FACEUP — the summoner chooses each Army Ant Token's face-up position.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, Kumootoko, 0, BattlePosition.Attack, true);
                var ants = PutInHand(e, p, Ants);
                var act = e.TryActivateSpellTrap(p, ants, fromHand: true);
                var want = new Queue<string>(new[] { "Face-up Defense Position", "Face-up Attack Position" });
                var asked = 0;
                for (var i = 0; i < 20; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var legal = Legal(e);
                        var opt = want.Count > 0 ? legal.FirstOrDefault(c => c.Name == want.Peek()) : null;
                        if (opt != null && e.TrySelectEffectTarget(opt))
                        {
                            want.Dequeue();
                            asked++;
                            continue;
                        }

                        var bug = legal.FirstOrDefault(c => c.CardId == Kumootoko);
                        if (bug == null || !e.TrySelectEffectTarget(bug)) e.CancelEffectTargeting();
                        continue;
                    }

                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    break;
                }

                var tokens = Tokens(p, "Army Ant Token");
                Check("Multiplication of Ants: the summoner chooses each Token's face-up position (Defense, then Attack)",
                    reached && act && asked == 2 && tokens.Count == 2 && tokens.All(t => t.FaceUp) &&
                    tokens.Count(t => t.Position == BattlePosition.Defense) == 1 &&
                    tokens.Count(t => t.Position == BattlePosition.Attack) == 1,
                    $"reached={reached} act={act} asked={asked} tokens={tokens.Count} {Tail(e, 4)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var bug = PlaceMonster(e, p, Kumootoko, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var ants = PutInHand(e, p, Ants);
                var act = e.TryActivateSpellTrap(p, ants, fromHand: true);
                var onlyInsect = !e.IsAwaitingEffectTarget || Legal(e).All(c => c.CardId == Kumootoko);
                Drive(e, c => c.CardId == Kumootoko);
                var tokens = Tokens(p, "Army Ant Token");
                Check("Multiplication of Ants: Tributes the Insect (cost), 2 Army Ant Tokens (500/1200)",
                    reached && act && onlyInsect && p.Graveyard.Contains(bug) && p.TryFindMonster(celtic, out _) &&
                    tokens.Count == 2 && tokens.All(t => t.FaceUp && t.CurrentAtk == 500 && t.CurrentDef == 1200 &&
                                                         t.Race == "Insect" && t.Level == 4),
                    $"act={act} onlyInsect={onlyInsect} tokens={tokens.Count} {Tail(e, 4)}");
                Check("Multiplication of Ants: the Tokens cannot be Tributed for a Tribute Summon",
                    tokens.Count == 2 && tokens.All(t => !TcgRules.CanBeTributedForSummon(p, t)), "");
            }

            {
                // 1 free zone: the Tribute frees the second (s.cfilter) — only an Insect in your own Main Monster Zone.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                PlaceMonster(e, p, Kumootoko, 0, BattlePosition.Attack, true);
                for (var z = 1; z < 4; z++) PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
                var ants = PutInHand(e, p, Ants);
                var legal = Free(p) == 1 && e.CanActivateSpellTrap(p, ants, fromHand: true);
                var act = e.TryActivateSpellTrap(p, ants, fromHand: true);
                Drive(e, c => c.CardId == Kumootoko);
                Check("Multiplication of Ants: with 1 free zone the Insect's Tribute frees the other",
                    legal && act && Tokens(p, "Army Ant Token").Count == 2, $"legal={legal} act={act}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Kumootoko, 0, BattlePosition.Attack, true);
                var ants = PutInHand(e, p, Ants);
                Check("Multiplication of Ants: no Insect of yours to Tribute — cannot be activated",
                    !e.CanActivateSpellTrap(p, ants, fromHand: true), "");
            }
        }

        // ───────────────────────────── DNA Transplant ─────────────────────────────

        static void DnaTransplantDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var setMon = PlaceMonster(e, opp, GiantSoldier, 1, BattlePosition.Defense, false);
                var dna = PlaceSetTrap(e, p, DnaTransplant, 0);
                var act = e.TryActivateSpellTrap(p, dna, fromHand: false);
                var offered = Legal(e).Select(t => t.Def?.attribute).ToList();
                var allSeven = offered.Count == 7 && offered.Contains("DIVINE") && offered.Contains("WIND");
                Drive(e, t => t.Def?.attribute == "WATER");
                Check("DNA Transplant: declares 1 of the 7 Attributes; every face-up monster on the field becomes it",
                    reached && act && allSeven && dna.FaceUp && p.TryFindSpellTrap(dna, out _) &&
                    celtic.Attribute == "WATER" && skull.Attribute == "WATER" && setMon.Attribute == "EARTH",
                    $"act={act} offered={string.Join(",", offered)} celtic={celtic.Attribute} skull={skull.Attribute} " +
                    $"set={setMon.Attribute}");
                p.SpellTrapZones[0].Occupant = null;
                p.Graveyard.Add(dna);
                Check("DNA Transplant: gone from the field, the printed Attributes return",
                    celtic.Attribute == "EARTH" && skull.Attribute == "DARK", $"{celtic.Attribute}/{skull.Attribute}");
            }

            {
                // The AI declares on its own activation (auto-pick, both target mirrors).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var dna = PlaceSetTrap(e, opp, DnaTransplant, 0);
                var act = e.TryActivateSpellTrap(opp, dna, fromHand: false);
                Drive(e);
                Check("DNA Transplant: the AI declares an Attribute (its own monster's) and it applies",
                    reached && act && !e.IsAwaitingEffectTarget && celtic.Attribute == "DARK",
                    $"reached={reached} act={act} celtic={celtic.Attribute}");
            }
        }

        // ───────────────────────────── Jade Insect Whistle ─────────────────────────────

        static void JadeWhistleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                opp.Deck.Clear();
                foreach (var id in new[] { Celtic, NeoBug, Celtic, Kumootoko, GiantSoldier, Celtic })
                    opp.Deck.Add(id);
                var whistle = PutInHand(e, p, JadeWhistle);
                var act = e.TryActivateSpellTrap(p, whistle, fromHand: true);
                Drive(e);
                Check("Jade Insect Whistle: the AI opponent puts an Insect from its Deck on top (its weakest)",
                    reached && act && opp.Deck.Count == 6 && opp.Deck[0] == Kumootoko,
                    $"act={act} top={opp.Deck.FirstOrDefault()} n={opp.Deck.Count}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                opp.Deck.Clear();
                foreach (var id in new[] { Celtic, GiantSoldier, SummonedSkull }) opp.Deck.Add(id);
                var whistle = PutInHand(e, p, JadeWhistle);
                var act = e.TryActivateSpellTrap(p, whistle, fromHand: true);
                Drive(e);
                Check("Jade Insect Whistle: no Insect in the opponent's Deck — the Deck is left as it was",
                    act && opp.Deck.Count == 3 && opp.Deck[0] == Celtic && opp.Deck[2] == SummonedSkull,
                    $"act={act} deck={string.Join(",", opp.Deck)}");
                opp.Deck.Clear();
                var whistle2 = PutInHand(e, p, JadeWhistle);
                Check("Jade Insect Whistle: cannot be activated while the opponent has no Deck",
                    !e.CanActivateSpellTrap(p, whistle2, fromHand: true), "");
            }

            {
                // The AI activates it: the human picks from their own Deck.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var opp = e.Opponent;
                p.Deck.Clear();
                foreach (var id in new[] { Celtic, Kumootoko, Celtic, NeoBug, Celtic }) p.Deck.Add(id);
                var whistle = PutInHand(e, opp, JadeWhistle);
                var act = e.TryActivateSpellTrap(opp, whistle, fromHand: true);
                var asked = false;
                var onlyInsects = true;
                for (var i = 0; i < 12; i++)
                {
                    if (e.IsAwaitingEffectTarget && e.PendingActivation?.Controller == p)
                    {
                        asked = true;
                        onlyInsects = Legal(e).Count == 2 && Legal(e).All(c => c.CardId == Kumootoko || c.CardId == NeoBug);
                        var neo = Legal(e).FirstOrDefault(c => c.CardId == NeoBug);
                        if (neo == null || !e.TrySelectEffectTarget(neo)) e.CancelEffectTargeting();
                        continue;
                    }

                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    break;
                }

                Check("Jade Insect Whistle: on the AI's card the human selects an Insect from their own Deck; it goes on top",
                    reached && act && asked && onlyInsects && p.Deck.Count == 5 && p.Deck[0] == NeoBug,
                    $"reached={reached} act={act} asked={asked} only={onlyInsects} top={p.Deck.FirstOrDefault()}");
            }
        }

        // ───────────────────────────── A Hero Emerges ─────────────────────────────

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            return Clean(e);
        }

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

        static void HeroEmergesDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Review fix: POS_FACEUP — the revealed monster can be Special Summoned in face-up Defense Position to block
                // the attack (Labyrinth Wall 0/3000 against Summoned Skull 2500: no damage).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var hero = PlaceSetTrap(e, p, HeroEmerges, 0);
                var wall = PutInHand(e, p, LabyrinthWall);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.ClearAttackFlags();
                e.NotifyPublic();
                var lp = p.LifePoints;
                var declared = e.TryAttack(opp, skull, null);
                var offered = Offers(e, p, hero);
                var act = offered && e.TryActivateSpellTrap(p, hero, fromHand: false);
                var asked = false;
                for (var i = 0; i < 20; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var def = Legal(e).FirstOrDefault(c => c.Name == "Face-up Defense Position");
                        asked |= def != null;
                        if (def == null || !e.TrySelectEffectTarget(def)) e.CancelEffectTargeting();
                        continue;
                    }

                    if (e.IsAwaitingResponse)
                    {
                        e.PassResponse();
                        continue;
                    }

                    break;
                }

                DrainCombat(e);
                Check("A Hero Emerges: its controller chooses the position — Labyrinth Wall in face-up Defense, no damage",
                    reached && declared && act && asked && p.TryFindMonster(wall, out _) && wall.FaceUp &&
                    wall.Position == BattlePosition.Defense && p.LifePoints == lp,
                    $"reached={reached} declared={declared} offered={offered} act={act} asked={asked} " +
                    $"pos={wall.Position} lp={lp}->{p.LifePoints} {Tail(e, 5)}");
            }

            {
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var hero = PlaceSetTrap(e, p, HeroEmerges, 0);
                var giant = PutInHand(e, p, GiantSoldier);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.ClearAttackFlags();
                e.NotifyPublic();
                var declared = e.TryAttack(opp, skull, null);
                var offered = Offers(e, p, hero);
                var act = offered && e.TryActivateSpellTrap(p, hero, fromHand: false);
                Drive(e);
                DrainCombat(e);
                Check("A Hero Emerges: on the opponent's attack the random hand monster is Special Summoned face-up",
                    reached && declared && offered && act && p.TryFindMonster(giant, out _) && giant.FaceUp &&
                    !p.Hand.Contains(giant) && p.Graveyard.Contains(hero),
                    $"reached={reached} declared={declared} offered={offered} act={act} " +
                    $"{Tail(e, 5)}");
            }

            {
                // Only a Ritual Monster in the hand: nothing to Special Summon — not offered.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var hero = PlaceSetTrap(e, p, HeroEmerges, 0);
                PutInHand(e, p, BlackLuster);
                PutInHand(e, p, DarkHole);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                skull.ClearAttackFlags();
                e.NotifyPublic();
                e.TryAttack(opp, skull, null);
                Check("A Hero Emerges: not offered without a hand monster that can be Special Summoned",
                    !Offers(e, p, hero), "");
                Drive(e);
                DrainCombat(e);
            }

            {
                // Resolution: the random card cannot be Special Summoned (a Spell / a Ritual Monster) — sent to the GY.
                var e = OpponentBattle(db, pDeck, aDeck, out _);
                var p = e.Player;
                var hero = PlaceSetTrap(e, p, HeroEmerges, 0);
                var bls = PutInHand(e, p, BlackLuster);
                TokensDeclarationsAndPicksRuntime.ResolveRandomHandSummon(e, p, hero);
                Check("A Hero Emerges: a random card that cannot be Special Summoned is sent to the GY",
                    p.Graveyard.Contains(bls) && !p.Hand.Contains(bls) && p.MonsterCount == 0, "");
                for (var z = 0; z < 5; z++) PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true);
                var giant = PutInHand(e, p, GiantSoldier);
                TokensDeclarationsAndPicksRuntime.ResolveRandomHandSummon(e, p, hero);
                Check("A Hero Emerges: no free Monster Zone at resolution — nothing happens",
                    p.Hand.Contains(giant) && !p.Graveyard.Contains(giant), "");
            }
        }

        // ───────────────────────────── Wild Nature's Release ─────────────────────────────

        static void WildNatureDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var fang = PlaceMonster(e, p, SilverFang, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(e, p, SilverFang, 2, BattlePosition.Defense, false);
                var ox = PlaceMonster(e, opp, BattleOx, 0, BattlePosition.Attack, true);
                var wild = PutInHand(e, p, WildNature);
                var act = e.TryActivateSpellTrap(p, wild, fromHand: true);
                var legal = Legal(e);
                var onlyFaceUpBeasts = legal.Count == 2 && legal.Contains(fang) && legal.Contains(ox);
                Drive(e, t => t == fang);
                Check("Wild Nature's Release: targets a face-up Beast / Beast-Warrior on either field; it gains its DEF in ATK",
                    reached && act && onlyFaceUpBeasts && fang.CurrentAtk == 1200 + 800 && ox.CurrentAtk == 1700,
                    $"act={act} legal={string.Join(",", legal.Select(t => t.Name))} atk={fang.CurrentAtk}");
                e.TryEndTurnSafe(p);
                Drive(e);
                Check("Wild Nature's Release: the monster is destroyed during the End Phase",
                    p.Graveyard.Contains(fang) && !p.TryFindMonster(fang, out _) && opp.TryFindMonster(ox, out _),
                    $"turnPlayer={e.TurnPlayer?.Name} gy={p.Graveyard.Contains(fang)}");
            }

            {
                // A lone legal target resolves with no prompt; a target that is no longer face-up gets nothing.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var fang = PlaceMonster(e, p, SilverFang, 0, BattlePosition.Attack, true);
                var wild = PutInHand(e, p, WildNature);
                e.TryActivateSpellTrap(p, wild, fromHand: true);
                Drive(e, t => t == fang);
                var boosted = fang.CurrentAtk == 2000 && fang.TempDestroyOnEndOfTurn == e.TurnNumber;
                var other = PlaceMonster(e, p, SilverFang, 1, BattlePosition.Defense, false);
                TokensDeclarationsAndPicksRuntime.ApplyDefToAtkThenDestroy(e, wild, other);
                Check("Wild Nature's Release: a lone target resolves; a face-down target at resolution gets nothing",
                    boosted && other.UntilEndOfTurnAtk == 0 && other.TempDestroyOnEndOfTurn != e.TurnNumber,
                    $"atk={fang.CurrentAtk}");
            }

            {
                // A Beast with 0 DEF (a Lamb Token) and a face-down Beast are not legal targets.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var token = e.CreateToken("Lamb Token", "Beast", "EARTH", 1, 0, 0);
                token.Position = BattlePosition.Defense;
                p.MonsterZones[0].Occupant = token;
                PlaceMonster(e, p, SilverFang, 1, BattlePosition.Defense, false);
                PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var wild = PutInHand(e, p, WildNature);
                Check("Wild Nature's Release: no face-up Beast with DEF above 0 — cannot be activated",
                    !e.CanActivateSpellTrap(p, wild, fromHand: true), "");
            }
        }

        // ───────────────────────────── Balloon Lizard ─────────────────────────────

        static void BalloonLizardDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two of its controller's Standby Phases (not the opponent's), then destroyed by battle attacking Summoned
                // Skull: 2 x 400 to the opponent, whose monster destroyed it.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var lizard = PlaceMonster(e, p, BalloonLizard, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                TextEffectRuntime.FirePhaseTriggers(e, p, EffectTiming.StandbyPhase);
                Drive(e);
                TextEffectRuntime.FirePhaseTriggers(e, opp, EffectTiming.StandbyPhase);
                Drive(e);
                var afterOne = lizard.Counters;
                TextEffectRuntime.FirePhaseTriggers(e, p, EffectTiming.StandbyPhase);
                Drive(e);
                Check("Balloon Lizard: 1 counter in each of its controller's Standby Phases only",
                    reached && afterOne == 1 && lizard.Counters == 2, $"afterOne={afterOne} counters={lizard.Counters}");
                var oppLp = opp.LifePoints;
                var battle = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                lizard.ClearAttackFlags();
                var attacked = e.TryAttack(p, lizard, skull);
                DrainCombat(e);
                Drive(e);
                Check("Balloon Lizard: destroyed by battle with 2 counters — 800 damage to the opponent",
                    battle && attacked && p.Graveyard.Contains(lizard) && opp.LifePoints == oppLp - 800,
                    $"battle={battle} attacked={attacked} oppLp={oppLp}->{opp.LifePoints} " +
                    $"{Tail(e, 5)}");
            }

            {
                // Destroyed by its controller's own Dark Hole: the controller takes the damage.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var lizard = PlaceMonster(e, p, BalloonLizard, 0, BattlePosition.Attack, true);
                TextEffectRuntime.FirePhaseTriggers(e, p, EffectTiming.StandbyPhase);
                Drive(e);
                var hole = PutInHand(e, p, DarkHole);
                var pLp = p.LifePoints;
                var oppLp = opp.LifePoints;
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Drive(e);
                Check("Balloon Lizard: destroyed by your own Dark Hole with 1 counter — 400 damage to you",
                    act && p.Graveyard.Contains(lizard) && p.LifePoints == pLp - 400 && opp.LifePoints == oppLp,
                    $"act={act} pLp={pLp}->{p.LifePoints} oppLp={oppLp}->{opp.LifePoints}");
            }

            {
                // No counters: no damage.
                var e = PlayerMain(db, pDeck, aDeck, out _);
                var p = e.Player;
                var opp = e.Opponent;
                var lizard = PlaceMonster(e, p, BalloonLizard, 0, BattlePosition.Attack, true);
                var hole = PutInHand(e, opp, DarkHole);
                var pLp = p.LifePoints;
                var oppLp = opp.LifePoints;
                var stale = e.CreateCardInstance(BalloonLizard);
                stale.Counters = 3; // counters never put by its own effect in this presence do not count
                TokensDeclarationsAndPicksRuntime.InflictCounterBurn(e, stale, Prog(db, BalloonLizard).ClauseList
                    .First(c => c.Action == EffectActionKind.InflictCountersTimesAmountToDestroyer));
                var hand = PutInHand(e, p, DarkHole);
                e.TryActivateSpellTrap(p, hand, fromHand: true);
                Drive(e);
                Check("Balloon Lizard: destroyed with no counters — no damage",
                    p.Graveyard.Contains(lizard) && p.LifePoints == pLp && opp.LifePoints == oppLp && hole != null,
                    $"pLp={pLp}->{p.LifePoints} oppLp={oppLp}->{opp.LifePoints}");
            }
        }
    }
}
