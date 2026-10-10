using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// FlipMassEffects batch (v142 curriculum): Cyber Jar (destroy all, reveal 5, summon the Level 4 or lower
    /// monsters face-up Attack or face-down Defense, the rest to the hand), Morphing Jar #2 (shuffle all monsters
    /// into the Deck, excavate as many monsters, summon the Level 4 or lower ones face-down, the rest to the GY),
    /// Jigen Bakudan (after it is flipped: Tribute it at your next Standby Phase, destroy all your monsters, burn
    /// half their ATK) and Spear Cretin (sent to the GY after being flipped: each player revives 1 monster from
    /// their GY face-up Attack or face-down Defense). Compile shapes, mini-duels through the real engine entry
    /// points (TryFlipSummon, TryAttack, turn changes, DestroyMonsterPublic, TrySelectEffectTarget with the UI's
    /// Special Summon placement, CancelEffectTargeting) and fail-closed near misses.
    /// Parasite Paracide is implemented by the PerCopyDeck batch (v163); the guard at the end asserts its shape.
    /// Runtime: <see cref="FlipMassEffectsRuntime"/>.
    /// </summary>
    public static class FlipMassEffectsRegressionTests
    {
        const int CyberJar = 34124316;
        const int MorphingJar2 = 79106360;
        const int JigenBakudan = 90020065;
        const int SpearCretin = 58551308;
        const int ParasiteParacide = 27911549;
        const int MorphingJar = 33508719;
        const int DarkHole = 53129443;

        const int Celtic = 91152256;       // Level 4 Normal 1400/1200
        const int Kuriboh = 40640057;      // Level 1 Effect 300/200
        const int GiantSoldier = 13039848; // Level 3 Normal 1300/2000
        const int MysticalElf = 15025844;  // Level 4 Normal 800/2000
        const int Bewd = 89631139;         // Level 8
        const int ManEaterBug = 54652250;  // Level 2 Flip
        const int YataGarasu = 3078576;    // Level 2 Spirit: "This card cannot be Special Summoned."
        const int FlameSwordsman = 45231177; // Fusion
        const int Sangan = 26202165;
        const int PotOfGreed = 55144522;
        const int MirrorForce = 44095762;
        const int MaskOfRestrict = 29549364;
        const int Relinquished = 64631466;    // Level 1 Ritual Monster
        const int Fenrir = 218704;            // Level 4 Nomi: "can only be Special Summoned by removing from play …"
        const int RareMetalDragon = 25236056; // Level 4: "This card cannot be Normal Summoned or Set." only
        const int MysticSwordsmanLv4 = 74591968; // Level 4: "Cannot be Normal Summoned (but can be Normal Set)."
        const int MonsterReborn = 83764718;
        const int ChangeOfHeart = 4031928;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            CyberJarDuels(db, pDeck, aDeck, Check);
            CyberJarBattleAndTriggers(db, pDeck, aDeck, Check);
            CyberJarSimpleAiTurn(db, pDeck, aDeck, Check);
            MorphingJar2Duels(db, pDeck, aDeck, Check);
            MorphingJar2Choice(db, pDeck, aDeck, Check);
            JigenDuels(db, pDeck, aDeck, Check);
            SpearCretinDuels(db, pDeck, aDeck, Check);
            SpearCretinBattle(db, pDeck, aDeck, Check);
            SimultaneousSends(db, pDeck, aDeck, Check);
            FaceDownSummonsCannotFlipThisTurn(db, pDeck, aDeck, Check);
            WaitingAndDisplacedChoices(db, pDeck, aDeck, Check);
            SpearCretinProperSummonTargets(db, pDeck, aDeck, Check);
            DeferredGuard(db, Check);
        }

        delegate void Chk(string name, bool ok, string detail = "");

        static string Left(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} n={prog.ClauseList.Count} unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static string Ids(IEnumerable<CardInstance> cards) =>
            cards == null ? "none" : string.Join(",", cards.Where(c => c != null).Select(c => c.Name));

        /// <summary>A clean board, empty hands / GYs, and the given Decks (index 0 = top).</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck, int[] playerDeck, int[] oppDeck)
        {
            var engine = Fresh(db, pDeck, aDeck);
            PrepareBoard(engine, playerDeck, oppDeck);
            return engine;
        }

        static void PrepareBoard(DuelEngine engine, int[] playerDeck, int[] oppDeck)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
                who.Deck.Clear();
            }

            engine.Player.Deck.AddRange(playerDeck ?? Array.Empty<int>());
            engine.Opponent.Deck.AddRange(oppDeck ?? Array.Empty<int>());
        }

        static int[] Fill(int id, int n) => Enumerable.Repeat(id, n).ToArray();

        static int[] Concat(params int[][] parts) => parts.SelectMany(p => p).ToArray();

        static CardInstance OnField(DuelistState who, int id) =>
            who.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == id);

        static bool FaceDownDefense(CardInstance m) =>
            m != null && !m.FaceUp && m.Position == BattlePosition.Defense;

        static bool FaceUpAttack(CardInstance m) =>
            m != null && m.FaceUp && m.Position == BattlePosition.Attack;

        static int Count(IEnumerable<CardInstance> cards, int id) => cards.Count(c => c != null && c.CardId == id);

        static CardInstance Offered(DuelEngine engine, int id) =>
            engine.PendingActivation?.LegalTargets?.FirstOrDefault(c => c != null && c.CardId == id);

        /// <summary>The UI's "Summon DEF / Summon ATK" + zone tap: placement, then the target (DuelUI.CommitSsPlacement).</summary>
        static bool PickWith(DuelEngine engine, CardInstance target, BattlePosition? pos, int zone = -1)
        {
            if (target == null) return false;
            if (pos != null || zone >= 0) engine.SetPendingSpecialSummonPlacement(zone, pos);
            return engine.TrySelectEffectTarget(target);
        }

        // ── Compile shapes ──

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var jar = CardTextEffectCompiler.Compile(db.Get(CyberJar));
            Check("FlipMassEffects: Cyber Jar FullyCompiled — one Flip CyberJarStyle clause (reveal 5, Level 4 or lower, ATK / face-down DEF)",
                jar != null && jar.FullyCompiled && jar.ClauseList.Count == 1 &&
                jar.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                           c.Action == EffectActionKind.CyberJarStyle && c.Amount == 5 &&
                                           c.SummonLevelMax == 4 && c.SummonFaceUpAttackOrFaceDownDefense &&
                                           !c.RequiresTargetChoice),
                Left(jar));
            Check("FlipMassEffects: Cyber Jar has no stray Activate clause (Dark Hole branch leaves its sentence alone)",
                jar != null && !jar.HasTiming(EffectTiming.Activate), Left(jar));

            var mj2 = CardTextEffectCompiler.Compile(db.Get(MorphingJar2));
            Check("FlipMassEffects: Morphing Jar #2 FullyCompiled — one Flip shuffle/excavate clause, Level 4 or lower face-down Defense",
                mj2 != null && mj2.FullyCompiled && mj2.ClauseList.Count == 1 &&
                mj2.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Flip &&
                                           c.Action == EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon &&
                                           c.SummonLevelMax == 4 && c.SummonFaceDown && c.SummonInDefense &&
                                           !c.SummonFaceUpAttackOrFaceDownDefense),
                Left(mj2));

            var jigen = CardTextEffectCompiler.Compile(db.Get(JigenBakudan));
            var jigenOk = jigen != null && jigen.FullyCompiled && jigen.ClauseList.Count == 2;
            if (jigenOk)
            {
                var f = jigen.ClauseList[0];
                var s = jigen.ClauseList[1];
                // Jigen's FLIP is an activated Flip effect (Lua EFFECT_TYPE_FLIP): it makes a Chain Link.
                jigenOk = f != null && f.Timing == EffectTiming.Flip && f.Action == EffectActionKind.RecordThisCardFlipped &&
                          f.MakesChainLink &&
                          s != null && s.Timing == EffectTiming.StandbyPhase &&
                          s.Action == EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk &&
                          s.RequiresTributeThis && !s.OpponentTurnOnly && !s.EachStandbyPhase && !s.IsOptional;
            }

            Check("FlipMassEffects: Jigen Bakudan FullyCompiled — Flip record, then mandatory Standby tribute/destroy/burn",
                jigenOk, Left(jigen));

            var cretin = CardTextEffectCompiler.Compile(db.Get(SpearCretin));
            var cretinOk = cretin != null && cretin.FullyCompiled && cretin.ClauseList.Count == 2;
            if (cretinOk)
            {
                var f = cretin.ClauseList[0];
                var g = cretin.ClauseList[1];
                // Spear Cretin's flip marker is a continuous EVENT_FLIP effect (Lua e1), not an activation.
                cretinOk = f != null && f.Timing == EffectTiming.Flip && f.Action == EffectActionKind.RecordThisCardFlipped &&
                           !f.MakesChainLink &&
                           g != null && g.Timing == EffectTiming.SentFromFieldToGy &&
                           g.Action == EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy &&
                           g.Amount == 1 && g.IsPsctTarget && g.SummonToTargetsOwner &&
                           g.SummonFaceUpAttackOrFaceDownDefense && !g.RequiresTargetChoice && !g.IsOptional;
            }

            Check("FlipMassEffects: Spear Cretin FullyCompiled — Flip record, then each player's own-GY revival (ATK / face-down DEF)",
                cretinOk, Left(cretin));

            Check("FlipMassEffects: all four are Implemented",
                new[] { CyberJar, MorphingJar2, JigenBakudan, SpearCretin }.All(id =>
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented));
            Check("FlipMassEffects: vocabulary — marker None, Standby tribute Destroy, mass summons SpecialSummon",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.RecordThisCardFlipped }) ==
                EffectResolutionKind.None &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk }) == EffectResolutionKind.Destroy &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy }) == EffectResolutionKind.SpecialSummon &&
                EffectVocabulary.ResolutionOf(new EffectClause
                    { Action = EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon }) == EffectResolutionKind.SpecialSummon);
            Check("FlipMassEffects: the four are not registry-hardcoded Flip scripts (Cyber Jar runs its compiled clause)",
                !OfficialEffectRegistry.HasActivatableScript(CyberJar) &&
                !OfficialEffectRegistry.HasActivatableScript(MorphingJar2) &&
                !OfficialEffectRegistry.HasActivatableScript(JigenBakudan) &&
                !OfficialEffectRegistry.HasActivatableScript(SpearCretin));

            // Look-alikes that must keep their own shapes.
            var hole = CardTextEffectCompiler.Compile(db.Get(DarkHole));
            Check("FlipMassEffects: Dark Hole still Activate Destroy all monsters (both sides)",
                hole != null && hole.FullyCompiled &&
                hole.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                                            c.Action == EffectActionKind.Destroy && c.Side == EffectSide.Both &&
                                            c.Zone == EffectZoneFilter.FieldMonsters),
                Left(hole));
            var mj1 = CardTextEffectCompiler.Compile(db.Get(MorphingJar));
            Check("FlipMassEffects: Morphing Jar (#1) still Flip BothPlayersDiscardAndRedraw 5, not a #2 shuffle",
                mj1 != null && mj1.FullyCompiled &&
                mj1.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.BothPlayersDiscardAndRedraw &&
                                           c.Amount == 5) &&
                !mj1.ClauseList.Exists(c => c != null &&
                                            (c.Action == EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon ||
                                             c.Action == EffectActionKind.CyberJarStyle)),
                Left(mj1));
        }

        // ── Near misses: look-alike sentences must not compile to these shapes ──

        static CardDef Synthetic(CardDef like, string desc) =>
            new CardDef
            {
                id = 0,
                name = "FlipMassEffects near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            };

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var jar = db.Get(CyberJar);
            var mj2 = db.Get(MorphingJar2);
            var jigen = db.Get(JigenBakudan);
            var cretin = db.Get(SpearCretin);

            bool NotShape(CardDef like, string desc, EffectActionKind action)
            {
                var prog = CardTextEffectCompiler.Compile(Synthetic(like, desc));
                return prog == null || !(prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action));
            }

            const string jarText =
                "FLIP: Destroy all monsters on the field, then both players reveal the top 5 cards from their Decks, " +
                "then Special Summon all revealed Level 4 or lower monsters in face-up Attack Position or face-down " +
                "Defense Position, also add any remaining cards to their hand. (If either player has less than 5 cards " +
                "in their Deck, reveal as many as possible.)";
            Check("FlipMassEffects near miss: the exact Cyber Jar text compiles (control)",
                !NotShape(jar, jarText, EffectActionKind.CyberJarStyle));
            Check("FlipMassEffects near miss: 'face-up Attack Position' only is not Cyber Jar",
                NotShape(jar, jarText.Replace("in face-up Attack Position or face-down Defense Position",
                    "in face-up Attack Position"), EffectActionKind.CyberJarStyle));
            Check("FlipMassEffects near miss: remaining cards to the Graveyard is not Cyber Jar",
                NotShape(jar, jarText.Replace("also add any remaining cards to their hand",
                    "also send any remaining cards to the Graveyard"), EffectActionKind.CyberJarStyle));
            Check("FlipMassEffects near miss: a reminder with a different count is not Cyber Jar",
                NotShape(jar, jarText.Replace("less than 5 cards", "less than 3 cards"), EffectActionKind.CyberJarStyle));
            Check("FlipMassEffects near miss: 'your opponent' reveals only is not Cyber Jar",
                NotShape(jar, jarText.Replace("both players reveal the top 5 cards from their Decks",
                    "your opponent reveals the top 5 cards from their Deck"), EffectActionKind.CyberJarStyle));

            const string mj2Text =
                "FLIP: Shuffle all monsters on the field into the Deck. Then, each player excavates cards from the top " +
                "of their Deck, until they excavate the same number of monsters they shuffled into their Main Deck. " +
                "Special Summon all excavated Level 4 or lower monsters in face-down Defense Position, also send the " +
                "remaining cards to the Graveyard.";
            Check("FlipMassEffects near miss: the exact Morphing Jar #2 text compiles (control)",
                !NotShape(mj2, mj2Text, EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon));
            Check("FlipMassEffects near miss: excavated monsters face-up is not Morphing Jar #2",
                NotShape(mj2, mj2Text.Replace("in face-down Defense Position", "in face-up Attack Position"),
                    EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon));
            Check("FlipMassEffects near miss: 'monsters your opponent controls' is not Morphing Jar #2",
                NotShape(mj2, mj2Text.Replace("Shuffle all monsters on the field", "Shuffle all monsters your opponent controls"),
                    EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon));
            Check("FlipMassEffects near miss: remaining cards to the hand is not Morphing Jar #2",
                NotShape(mj2, mj2Text.Replace("also send the remaining cards to the Graveyard",
                    "also add the remaining cards to their hand"), EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon));

            const string jigenText =
                "FLIP: After this card is flipped, Tribute it during your Standby Phase to destroy all monsters you " +
                "control and inflict damage equal to half of the total ATK of the destroyed cards (excluding this " +
                "monster) to your opponent.";
            Check("FlipMassEffects near miss: the exact Jigen Bakudan text compiles (control)",
                !NotShape(jigen, jigenText, EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk));
            Check("FlipMassEffects near miss: 'monsters your opponent controls' is not Jigen Bakudan",
                NotShape(jigen, jigenText.Replace("destroy all monsters you control", "destroy all monsters your opponent controls"),
                    EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk));
            Check("FlipMassEffects near miss: full total ATK is not Jigen Bakudan (half)",
                NotShape(jigen, jigenText.Replace("equal to half of the total ATK", "equal to the total ATK"),
                    EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk));
            Check("FlipMassEffects near miss: 'your opponent's Standby Phase' is not Jigen Bakudan",
                NotShape(jigen, jigenText.Replace("during your Standby Phase", "during your opponent's Standby Phase"),
                    EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk));

            const string cretinText =
                "FLIP: When this card is sent to the Graveyard after being flipped, each player selects 1 monster from " +
                "their Graveyard, and Special Summons it in face-up Attack Position or face-down Defense Position.";
            Check("FlipMassEffects near miss: the exact Spear Cretin text compiles (control)",
                !NotShape(cretin, cretinText, EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy));
            Check("FlipMassEffects near miss: without 'after being flipped' is not Spear Cretin",
                NotShape(cretin, cretinText.Replace("FLIP: When this card is sent to the Graveyard after being flipped",
                    "When this card is sent to the Graveyard"), EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy));
            Check("FlipMassEffects near miss: 'you select 1 monster' (one player) is not Spear Cretin",
                NotShape(cretin, cretinText.Replace("each player selects 1 monster from their Graveyard, and Special Summons it",
                    "select 1 monster from your Graveyard, and Special Summon it"), EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy));
            Check("FlipMassEffects near miss: face-up Defense is not Spear Cretin (face-up Attack or face-down Defense)",
                NotShape(cretin, cretinText.Replace("face-down Defense Position", "face-up Defense Position"),
                    EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy));
        }

        // ── Cyber Jar ──

        static void CyberJarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player flips it in their Main Phase: everything is destroyed, the turn player (you) and then the
            // opponent reveal 5; you choose the position of each of your Level 4-or-lower monsters (Summon DEF = set
            // face-down, otherwise face-up Attack); the AI decides at once; every other revealed card goes to the hand.
            {
                var engine = Setup(db, pDeck, aDeck,
                    new[] { Celtic, PotOfGreed, Bewd, GiantSoldier, MirrorForce, Kuriboh },
                    new[] { MysticalElf, Kuriboh, PotOfGreed, YataGarasu, ManEaterBug, Celtic });
                var p = engine.Player;
                var opp = engine.Opponent;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var mine = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, opp, MysticalElf, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(engine, opp, Bewd, 1, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, jar);
                var pending = engine.PendingActivation;
                var offered = pending?.LegalTargets?.ToList();
                Check("Cyber Jar: Flip destroys every monster on the field (itself included)",
                    flipped && p.Graveyard.Contains(jar) && p.Graveyard.Contains(mine) && opp.Graveyard.Contains(elf) &&
                    opp.Graveyard.Contains(bewd),
                    $"flipped={flipped} pGy={Ids(p.Graveyard)} oGy={Ids(opp.Graveyard)}");
                Check("Cyber Jar: you choose for your revealed Level 4-or-lower monsters only (Celtic, Giant Soldier)",
                    pending != null && FlipMassEffectsRuntime.IsMassSummonPick(pending) &&
                    pending.Controller == p && offered != null && offered.Count == 2 &&
                    offered.Exists(c => c.CardId == Celtic) && offered.Exists(c => c.CardId == GiantSoldier) &&
                    SpellTrapEffects.PendingTargetIsSpecialSummon(pending),
                    $"pending={pending != null} offered={Ids(offered)}");
                Check("Cyber Jar: nothing is summoned or added before your choice (the Summons are simultaneous)",
                    p.MonsterCount == 0 && opp.MonsterCount == 0 && p.Hand.Count == 0 && opp.Hand.Count == 0 &&
                    p.Deck.Count == 1 && opp.Deck.Count == 1,
                    $"pM={p.MonsterCount} oM={opp.MonsterCount} pH={p.Hand.Count} oH={opp.Hand.Count}");

                var first = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense, 3);
                var stillOpen = engine.IsAwaitingEffectTarget && FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                var second = PickWith(engine, Offered(engine, Celtic), null);
                DrainCombat(engine);
                var soldier = OnField(p, GiantSoldier);
                var celtic = OnField(p, Celtic);
                Check("Cyber Jar: Summon DEF sets the monster face-down Defense in the chosen zone; the other is face-up Attack",
                    first && stillOpen && second && !engine.IsAwaitingEffectTarget &&
                    FaceDownDefense(soldier) && p.MonsterZones[3].Occupant == soldier && FaceUpAttack(celtic),
                    $"first={first} open={stillOpen} second={second} soldier={soldier?.FaceUp}/{soldier?.Position} celtic={celtic?.FaceUp}/{celtic?.Position}");
                Check("Cyber Jar: your other revealed cards (Spell, Level 8, Trap) go to your hand",
                    p.Hand.Count == 3 && Count(p.Hand, PotOfGreed) == 1 && Count(p.Hand, Bewd) == 1 &&
                    Count(p.Hand, MirrorForce) == 1 && p.Deck.Count == 1 && p.Deck[0] == Kuriboh,
                    $"hand={Ids(p.Hand)} deck={p.Deck.Count}");
                Check("Cyber Jar: the AI summons Mystical Elf and Man-Eater Bug face-down, Kuriboh face-up Attack",
                    opp.MonsterCount == 3 && FaceDownDefense(OnField(opp, MysticalElf)) &&
                    FaceDownDefense(OnField(opp, ManEaterBug)) && FaceUpAttack(OnField(opp, Kuriboh)),
                    $"opp={Ids(opp.MonstersOnField())}");
                Check("Cyber Jar: the AI's Spell and the Yata-Garasu that cannot be Special Summoned go to its hand",
                    opp.Hand.Count == 2 && Count(opp.Hand, PotOfGreed) == 1 && Count(opp.Hand, YataGarasu) == 1 &&
                    opp.Deck.Count == 1,
                    $"oppHand={Ids(opp.Hand)}");
            }

            // CANCEL: the Summons are mandatory — the rest of your monsters are summoned face-up Attack.
            {
                var engine = Setup(db, pDeck, aDeck,
                    new[] { Celtic, GiantSoldier, Kuriboh, PotOfGreed, PotOfGreed },
                    Fill(PotOfGreed, 6));
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                var offered = engine.PendingActivation?.LegalTargets?.Count ?? 0;
                var one = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense);
                var cancelled = engine.CancelEffectTargeting();
                DrainCombat(engine);
                Check("Cyber Jar: CANCEL after one choice summons the other revealed monsters face-up Attack",
                    flipped && offered == 3 && one && cancelled && !engine.IsAwaitingEffectTarget &&
                    FaceDownDefense(OnField(p, GiantSoldier)) && FaceUpAttack(OnField(p, Celtic)) &&
                    FaceUpAttack(OnField(p, Kuriboh)) && Count(p.Hand, PotOfGreed) == 2 &&
                    engine.Opponent.Hand.Count == 5 && engine.Opponent.MonsterCount == 0,
                    $"offered={offered} one={one} cancel={cancelled} field={Ids(p.MonstersOnField())} hand={Ids(p.Hand)}");
            }

            // What "can be Special Summoned" means for a revealed card: a Ritual Monster (Relinquished) and a Nomi
            // (Fenrir) were never properly summoned — to the hand; "cannot be Normal Summoned or Set" alone (Rare Metal
            // Dragon) and a conditional Normal Summon lock (Mystic Swordsman LV4) can be Special Summoned.
            {
                var engine = Setup(db, pDeck, aDeck,
                    new[] { Relinquished, Fenrir, RareMetalDragon, MysticSwordsmanLv4, Celtic },
                    Fill(PotOfGreed, 5));
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                var offered = engine.PendingActivation?.LegalTargets?.ToList();
                var cancelled = engine.CancelEffectTargeting();
                DrainCombat(engine);
                Check("Cyber Jar: Ritual Monster and Nomi revealed go to the hand; Rare Metal Dragon / Mystic Swordsman LV4 are summoned",
                    flipped && offered != null && offered.Count == 3 && cancelled &&
                    FaceUpAttack(OnField(p, RareMetalDragon)) && FaceUpAttack(OnField(p, MysticSwordsmanLv4)) &&
                    FaceUpAttack(OnField(p, Celtic)) && Count(p.Hand, Relinquished) == 1 && Count(p.Hand, Fenrir) == 1 &&
                    p.Hand.Count == 2,
                    $"offered={Ids(offered)} field={Ids(p.MonstersOnField())} hand={Ids(p.Hand)}");
            }

            // Fewer than 5 cards: reveal as many as possible; an empty Deck reveals nothing.
            {
                var engine = Setup(db, pDeck, aDeck, new[] { PotOfGreed, MirrorForce }, Array.Empty<int>());
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                Check("Cyber Jar: a 2-card Deck reveals both (no monster: straight to the hand), an empty Deck reveals nothing",
                    flipped && !engine.IsAwaitingEffectTarget && p.Hand.Count == 2 && p.Deck.Count == 0 &&
                    engine.Opponent.Hand.Count == 0 && p.MonsterCount == 0 && !engine.GameOver,
                    $"hand={Ids(p.Hand)} over={engine.GameOver}");
            }

            // The AI flips its own Cyber Jar in its turn: it reveals first; you still choose for your monsters.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Cyber Jar AI setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, new[] { MysticalElf, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed },
                        new[] { Celtic, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed });
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var jar = PlaceMonster(engine, opp, CyberJar, 0, BattlePosition.Defense, false);
                    var flipped = engine.TryFlipSummon(opp, jar);
                    var pending = engine.PendingActivation;
                    var mine = pending?.LegalTargets?.ToList();
                    var aiDoneFirst = opp.MonsterCount == 0; // everything waits for the simultaneous Summon
                    var picked = PickWith(engine, Offered(engine, MysticalElf), BattlePosition.Defense);
                    DrainCombat(engine);
                    Check("Cyber Jar (AI's): you choose for your revealed monster; both sides are summoned together",
                        flipped && pending != null && pending.Controller == p && mine != null && mine.Count == 1 &&
                        aiDoneFirst && picked && FaceDownDefense(OnField(p, MysticalElf)) &&
                        FaceUpAttack(OnField(opp, Celtic)) && Count(p.Hand, PotOfGreed) == 4 &&
                        Count(opp.Hand, PotOfGreed) == 4 && opp.Graveyard.Contains(jar),
                        $"flipped={flipped} mine={Ids(mine)} picked={picked} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
                }
            }
        }

        /// <summary>
        /// The real AI turn (SimpleAi coroutine): it Flip Summons its own face-down Cyber Jar; your pick for your
        /// revealed monsters stays yours (the AI does not answer it) and the AI waits until you have chosen.
        /// </summary>
        static void CyberJarSimpleAiTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = Fresh(db, pDeck, aDeck);
            if (!ReachOpponentMain(engine))
            {
                Check("Cyber Jar SimpleAi setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                return;
            }

            PrepareBoard(engine, new[] { Celtic, GiantSoldier, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed },
                Fill(PotOfGreed, 8));
            var p = engine.Player;
            var opp = engine.Opponent;
            var jar = PlaceMonster(engine, opp, CyberJar, 0, BattlePosition.Defense, false);
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(engine, null);
                var running = true;
                for (var i = 0; i < 80 && running && !FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation); i++)
                {
                    DuelPresentationPacer.Clear();
                    running = it.MoveNext();
                }

                var opened = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation) &&
                             engine.PendingActivation.Controller == p;
                for (var i = 0; i < 10 && running; i++)
                {
                    DuelPresentationPacer.Clear();
                    running = it.MoveNext();
                }

                var untouched = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation) &&
                                engine.PendingActivation.LegalTargets.Count == 2 && p.MonsterCount == 0 &&
                                engine.TurnPlayer == opp;
                var a = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense);
                var b = PickWith(engine, Offered(engine, Celtic), null);
                for (var i = 0; i < 300 && running; i++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (engine.PendingResponse != null && engine.PendingResponse.Responder == p) engine.PassResponse();
                    if (engine.IsAwaitingEffectTarget && engine.PendingActivation?.Controller == p)
                        engine.CancelEffectTargeting();
                    running = it.MoveNext();
                }

                Check("Cyber Jar (SimpleAi turn): the AI flips it, your pick stays yours and the AI waits for it",
                    opp.Graveyard.Contains(jar) && opened && untouched && a && b &&
                    FaceDownDefense(OnField(p, GiantSoldier)) && FaceUpAttack(OnField(p, Celtic)) && !running,
                    $"opened={opened} untouched={untouched} a={a} b={b} p={Ids(p.MonstersOnField())} running={running} turn={engine.TurnNumber}");
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static void CyberJarBattleAndTriggers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Flipped by the AI's attack: its effect resolves after damage calculation (the battle waits for your
            // choice), destroys the attacker too, and the battle then ends with nothing left to destroy.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Cyber Jar battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, new[] { GiantSoldier, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed },
                        Fill(PotOfGreed, 5));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                    var attacker = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                    attacker.ClearAttackFlags();
                    var atk = engine.TryAttack(opp, attacker, jar);
                    DrainCombat(engine);
                    var waiting = engine.IsAwaitingEffectTarget && FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                    var picked = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense);
                    DrainCombat(engine);
                    Check("Cyber Jar: flipped by an attack — destroys the attacker, you set your revealed monster, the battle ends cleanly",
                        atk && waiting && picked && !engine.IsAwaitingEffectTarget && !engine.HasDeclaredAttack &&
                        opp.Graveyard.Contains(attacker) && p.Graveyard.Contains(jar) &&
                        Count(p.Graveyard, CyberJar) == 1 && Count(opp.Graveyard, Celtic) == 1 &&
                        FaceDownDefense(OnField(p, GiantSoldier)) && Count(opp.Hand, PotOfGreed) == 5,
                        $"atk={atk} waiting={waiting} picked={picked} pGy={Ids(p.Graveyard)} oGy={Ids(opp.Graveyard)} p={Ids(p.MonstersOnField())}");
                }
            }

            // "Sent to the GY" triggers of the destroyed monsters wait until Cyber Jar has resolved: the AI's Sangan
            // searches after the reveal (the revealed Celtic is summoned; Sangan then finds the Kuriboh under it).
            {
                var engine = Setup(db, pDeck, aDeck,
                    Fill(PotOfGreed, 5),
                    new[] { Celtic, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed, Kuriboh });
                var p = engine.Player;
                var opp = engine.Opponent;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var sangan = PlaceMonster(engine, opp, Sangan, 0, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                Check("Cyber Jar: the destroyed AI Sangan searches only after the reveal (Celtic summoned, Kuriboh added)",
                    flipped && !engine.IsAwaitingEffectTarget && opp.Graveyard.Contains(sangan) &&
                    FaceUpAttack(OnField(opp, Celtic)) && Count(opp.Hand, Kuriboh) == 1 &&
                    Count(opp.Hand, PotOfGreed) == 4 && opp.Deck.Count == 0,
                    $"oppField={Ids(opp.MonstersOnField())} oppHand={Ids(opp.Hand)} oppDeck={opp.Deck.Count}");
            }

            // Your own Sangan: Cyber Jar's choice comes first; Sangan's search opens after the Summons.
            {
                var engine = Setup(db, pDeck, aDeck,
                    new[] { Celtic, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed, Kuriboh },
                    Fill(PotOfGreed, 5));
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var sangan = PlaceMonster(engine, p, Sangan, 1, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, jar);
                var jarPick = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                var picked = PickWith(engine, Offered(engine, Celtic), BattlePosition.Attack);
                var sanganPending = engine.PendingActivation;
                var sanganOffers = sanganPending?.LegalTargets?.ToList();
                var searched = sanganOffers != null && sanganOffers.Count > 0 &&
                               engine.TrySelectEffectTarget(sanganOffers.First(c => c.CardId == Kuriboh));
                DrainCombat(engine);
                Check("Cyber Jar: your Sangan's search waits for Cyber Jar's Summons, then finds the Kuriboh",
                    flipped && jarPick && picked && sanganPending != null &&
                    !FlipMassEffectsRuntime.IsMassSummonPick(sanganPending) && searched &&
                    p.Graveyard.Contains(sangan) && FaceUpAttack(OnField(p, Celtic)) && Count(p.Hand, Kuriboh) == 1,
                    $"jarPick={jarPick} picked={picked} sanganOffers={Ids(sanganOffers)} searched={searched} hand={Ids(p.Hand)}");
            }

            // Two held triggers of yours: your flipped Spear Cretin's GY selection opens first, and your Sangan's
            // search waits for it (it does not replace the open choice); then both resolve.
            {
                var engine = Setup(db, pDeck, aDeck,
                    Concat(Fill(PotOfGreed, 5), new[] { Kuriboh }),
                    Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var cretin = PlaceMonster(engine, p, SpearCretin, 1, BattlePosition.Defense, false);
                var cretinFlipped = engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                var sangan = PlaceMonster(engine, p, Sangan, 2, BattlePosition.Attack, true);
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                var cretinPending = engine.PendingActivation;
                var cretinOffers = cretinPending?.LegalTargets?.ToList();
                var cretinIsMassPick = FlipMassEffectsRuntime.IsMassSummonPick(cretinPending);
                var revived = PickWith(engine, Offered(engine, CyberJar), BattlePosition.Defense);
                var sanganPending = engine.PendingActivation;
                var searched = sanganPending?.LegalTargets != null &&
                               !FlipMassEffectsRuntime.IsMassSummonPick(sanganPending) &&
                               engine.TrySelectEffectTarget(sanganPending.LegalTargets.FirstOrDefault(c => c.CardId == Kuriboh));
                DrainCombat(engine);
                Check("Cyber Jar: your held Spear Cretin choice opens first; your held Sangan search waits for it, then both resolve",
                    cretinFlipped && flipped && cretinIsMassPick &&
                    cretinOffers != null && cretinOffers.Count == 2 && cretinOffers.Exists(c => c.CardId == CyberJar) &&
                    cretinOffers.Exists(c => c.CardId == Sangan) && revived && searched &&
                    !engine.IsAwaitingEffectTarget && FaceDownDefense(OnField(p, CyberJar)) &&
                    p.Graveyard.Contains(sangan) && p.Graveyard.Contains(cretin) && Count(p.Hand, Kuriboh) == 1 &&
                    Count(p.Hand, PotOfGreed) == 5 && opp.MonsterCount == 0,
                    $"mass={cretinIsMassPick} offers={Ids(cretinOffers)} revived={revived} searched={searched} p={Ids(p.MonstersOnField())} gy={Ids(p.Graveyard)} hand={Ids(p.Hand)}");
            }
        }

        // ── Morphing Jar #2 ──

        static void MorphingJar2Duels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Your Flip: every monster goes back to its owner's Deck (Fusion to the Extra Deck, Tokens vanish), then
            // each player excavates until as many monsters as they shuffled into their Main Deck and sets them.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 3), Fill(PotOfGreed, 2));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jar = PlaceMonster(engine, p, MorphingJar2, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, Kuriboh, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, opp, FlameSwordsman, 1, BattlePosition.Attack, true);
                var token = engine.CreateToken("Sheep Token", "Beast", "EARTH", 1, 0, 0);
                opp.MonsterZones[2].Occupant = token;
                var extraBefore = opp.ExtraDeck.Count(id => id == FlameSwordsman);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                var pField = p.MonstersOnField().ToList();
                var oField = opp.MonstersOnField().ToList();
                Check("Morphing Jar #2: no choice needed (enough zones) — nothing waits on a pick",
                    flipped && !engine.IsAwaitingEffectTarget);
                Check("Morphing Jar #2: you shuffled 2 (itself + Celtic) and set both back face-down Defense",
                    pField.Count == 2 && pField.All(FaceDownDefense) && Count(pField, MorphingJar2) == 1 &&
                    Count(pField, Celtic) == 1 && !pField.Contains(jar),
                    $"p={Ids(pField)}");
                Check("Morphing Jar #2: your excavated Spells go to the GY, the rest stay in the Deck",
                    Count(p.Graveyard, PotOfGreed) + p.Deck.Count == 3 && p.Deck.All(id => id == PotOfGreed),
                    $"gy={Ids(p.Graveyard)} deck={p.Deck.Count}");
                Check("Morphing Jar #2: the AI counted only Kuriboh (Fusion to the Extra Deck, Token gone) and sets it",
                    oField.Count == 1 && FaceDownDefense(oField[0]) && oField[0].CardId == Kuriboh &&
                    opp.ExtraDeck.Count(id => id == FlameSwordsman) == extraBefore + 1 &&
                    Count(opp.Graveyard, PotOfGreed) + opp.Deck.Count == 2,
                    $"o={Ids(oField)} extra={opp.ExtraDeck.Count(id => id == FlameSwordsman)} oGy={Ids(opp.Graveyard)} oDeck={opp.Deck.Count}");
            }

            // A Level 5+ monster excavated counts toward the number but is sent to the GY.
            {
                var engine = Setup(db, pDeck, aDeck, Array.Empty<int>(), Array.Empty<int>());
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, MorphingJar2, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, Bewd, 1, BattlePosition.Attack, true);
                PlaceMonster(engine, p, Relinquished, 2, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                Check("Morphing Jar #2: Blue-Eyes (Level 8) and Relinquished (Ritual, not properly summoned from the Deck) are excavated to the GY",
                    flipped && !engine.IsAwaitingEffectTarget && p.MonsterCount == 1 &&
                    FaceDownDefense(OnField(p, MorphingJar2)) && Count(p.Graveyard, Bewd) == 1 &&
                    Count(p.Graveyard, Relinquished) == 1 && p.Deck.Count == 0,
                    $"p={Ids(p.MonstersOnField())} gy={Ids(p.Graveyard)}");
            }

            // Flipped by an attack that destroys it: it stays for the battle (not shuffled), the attacker is shuffled.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Morphing Jar #2 battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, new[] { PotOfGreed }, Fill(PotOfGreed, 2));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var jar = PlaceMonster(engine, p, MorphingJar2, 0, BattlePosition.Defense, false);
                    PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Attack, true);
                    var attacker = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
                    attacker.ClearAttackFlags();
                    var atk = engine.TryAttack(opp, attacker, jar);
                    DrainCombat(engine);
                    Check("Morphing Jar #2: destroyed by the attack that flipped it — the attacker and your Giant Soldier are re-set",
                        atk && !engine.IsAwaitingEffectTarget && !engine.HasDeclaredAttack && p.Graveyard.Contains(jar) &&
                        !opp.TryFindMonster(attacker, out _) && FaceDownDefense(OnField(opp, Celtic)) &&
                        FaceDownDefense(OnField(p, GiantSoldier)) && p.MonsterCount == 1 && opp.MonsterCount == 1,
                        $"atk={atk} pGy={Ids(p.Graveyard)} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
                }
            }
        }

        static void MorphingJar2Choice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // You own 6 shuffled monsters (5 of yours + 1 the AI took) but have 5 zones: you choose 5; the 6th goes
            // to the GY. The pick is face-down only, so it asks no position.
            for (var variant = 0; variant < 2; variant++)
            {
                var engine = Setup(db, pDeck, aDeck, Array.Empty<int>(), Fill(PotOfGreed, 3));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jar = PlaceMonster(engine, p, MorphingJar2, 0, BattlePosition.Defense, false);
                for (var z = 1; z < p.MonsterZones.Length; z++)
                    PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);
                var taken = PlaceMonster(engine, opp, Kuriboh, 0, BattlePosition.Attack, true);
                taken.ControlOwner = p;
                var flipped = engine.TryFlipSummon(p, jar);
                var pending = engine.PendingActivation;
                var offered = pending?.LegalTargets?.ToList();
                var asksPosition = pending != null && SpellTrapEffects.PendingTargetIsSpecialSummon(pending);
                var setup = flipped && pending != null && FlipMassEffectsRuntime.IsMassSummonPick(pending) &&
                            offered != null && offered.Count == 6 && pending.TargetPicksRemaining == 5 &&
                            !asksPosition && opp.MonsterCount == 0;
                if (variant == 0)
                {
                    var kuri = Offered(engine, Kuriboh);
                    var ok = PickWith(engine, kuri, BattlePosition.Attack); // position ignored: face-down only
                    for (var i = 0; i < 4 && engine.IsAwaitingEffectTarget; i++)
                        ok &= PickWith(engine, Offered(engine, Celtic), null);
                    DrainCombat(engine);
                    Check("Morphing Jar #2: 6 monsters for 5 zones — you choose 5 (Kuriboh + 4 Celtic), one Celtic to the GY",
                        setup && ok && !engine.IsAwaitingEffectTarget && p.MonsterCount == 5 &&
                        p.MonstersOnField().All(FaceDownDefense) && Count(p.MonstersOnField(), Kuriboh) == 1 &&
                        Count(p.MonstersOnField(), Celtic) == 4 && Count(p.Graveyard, MorphingJar2) == 1 &&
                        p.Deck.Count == 0,
                        $"setup={setup} offered={Ids(offered)} ask={asksPosition} ok={ok} p={Ids(p.MonstersOnField())} gy={Ids(p.Graveyard)}");
                }
                else
                {
                    var ok = PickWith(engine, Offered(engine, MorphingJar2), null);
                    var cancelled = engine.CancelEffectTargeting();
                    DrainCombat(engine);
                    Check("Morphing Jar #2: CANCEL after one choice takes the rest in excavation order (5 set, 1 to the GY)",
                        setup && ok && cancelled && !engine.IsAwaitingEffectTarget && p.MonsterCount == 5 &&
                        p.MonstersOnField().All(FaceDownDefense) && Count(p.MonstersOnField(), MorphingJar2) == 1 &&
                        p.Graveyard.Count == 1 && p.Deck.Count == 0,
                        $"setup={setup} ok={ok} cancel={cancelled} p={Ids(p.MonstersOnField())} gy={Ids(p.Graveyard)}");
                }
            }
        }

        // ── Jigen Bakudan ──

        /// <summary>End the current turn (the AI's turn is not played: turns only advance).</summary>
        static void PassTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        static void JigenDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Flipped in your Main Phase: nothing at the AI's Standby Phase; at your next one it is Tributed, your
            // monsters are destroyed and the AI takes half their ATK (1400 + 1300 → 1350).
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jigen = PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var soldier = PlaceMonster(engine, p, GiantSoldier, 2, BattlePosition.Defense, false);
                var enemy = PlaceMonster(engine, opp, MysticalElf, 0, BattlePosition.Attack, true);
                var lp = opp.LifePoints;
                var myTurn = engine.TurnNumber;
                var flipped = engine.TryFlipSummon(p, jigen);
                DrainCombat(engine);
                PassTurn(engine);
                var afterAiStandby = engine.TurnPlayer == opp && p.TryFindMonster(jigen, out _) &&
                                     opp.LifePoints == lp && p.MonsterCount == 3;
                PassTurn(engine);
                Check("Jigen Bakudan: nothing during the AI's Standby Phase",
                    flipped && afterAiStandby, $"flipped={flipped} turn={engine.TurnNumber}");
                Check("Jigen Bakudan: your next Standby Phase — Tributed, your monsters destroyed, 1350 damage to the AI",
                    engine.TurnPlayer == p && engine.TurnNumber == myTurn + 2 && p.Graveyard.Contains(jigen) &&
                    p.Graveyard.Contains(celtic) && p.Graveyard.Contains(soldier) && p.MonsterCount == 0 &&
                    opp.TryFindMonster(enemy, out _) && opp.LifePoints == lp - 1350,
                    $"turn={engine.TurnNumber} pGy={Ids(p.Graveyard)} p={Ids(p.MonstersOnField())} lp={opp.LifePoints}/{lp}");
                PassTurn(engine);
                PassTurn(engine);
                Check("Jigen Bakudan: once only — nothing at the following Standby Phase",
                    opp.LifePoints == lp - 1350 && !engine.GameOver, $"lp={opp.LifePoints}");
            }

            // Flipped by the AI's attack (it survives): it fires at your very next Standby Phase.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("Jigen Bakudan battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var jigen = PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Defense, false);
                    var soldier = PlaceMonster(engine, p, GiantSoldier, 1, BattlePosition.Attack, true);
                    var attacker = PlaceMonster(engine, opp, MysticalElf, 0, BattlePosition.Attack, true); // 800 < DEF 1000
                    attacker.ClearAttackFlags();
                    var atk = engine.TryAttack(opp, attacker, jigen);
                    DrainCombat(engine);
                    var survived = jigen.FaceUp && p.TryFindMonster(jigen, out _);
                    var lp = opp.LifePoints;
                    PassTurn(engine);
                    Check("Jigen Bakudan: flipped in the AI's Battle Phase — fires at your next Standby Phase (650 damage)",
                        atk && survived && engine.TurnPlayer == p && p.Graveyard.Contains(jigen) &&
                        p.Graveyard.Contains(soldier) && opp.LifePoints == lp - 650,
                        $"atk={atk} survived={survived} pGy={Ids(p.Graveyard)} lp={opp.LifePoints}/{lp}");
                }
            }

            // Never flipped (placed face-up): no effect. Flipped then turned face-down: no effect.
            for (var variant = 0; variant < 2; variant++)
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jigen = variant == 0
                    ? PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Attack, true)
                    : PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var lp = opp.LifePoints;
                if (variant == 1)
                {
                    engine.TryFlipSummon(p, jigen);
                    DrainCombat(engine);
                    jigen.FaceUp = false; // Book of Moon: turned face-down after the flip
                    jigen.Position = BattlePosition.Defense;
                }

                PassTurn(engine);
                PassTurn(engine);
                Check(variant == 0
                        ? "Jigen Bakudan: Summoned face-up (never flipped) — nothing at your Standby Phase"
                        : "Jigen Bakudan: turned face-down after the flip — the effect is gone",
                    engine.TurnPlayer == p && p.TryFindMonster(jigen, out _) && p.MonsterCount == 2 &&
                    opp.LifePoints == lp,
                    $"variant={variant} p={Ids(p.MonstersOnField())} lp={opp.LifePoints}/{lp}");
            }

            // Mask of Restrict: it cannot be Tributed, so the effect is not activated — and it does not come back.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jigen = PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Defense, false);
                PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var mask = PlaceSetTrap(engine, opp, MaskOfRestrict, 0);
                mask.FaceUp = true;
                var lp = opp.LifePoints;
                engine.TryFlipSummon(p, jigen);
                DrainCombat(engine);
                PassTurn(engine);
                PassTurn(engine);
                var blocked = engine.TurnPlayer == p && p.TryFindMonster(jigen, out _) && p.MonsterCount == 2 &&
                              opp.LifePoints == lp;
                opp.SpellTrapZones[0].Occupant = null;
                opp.Graveyard.Add(mask);
                PassTurn(engine);
                PassTurn(engine);
                Check("Jigen Bakudan: under Mask of Restrict it is not Tributed, and the expired effect never returns",
                    blocked && engine.TurnPlayer == p && p.TryFindMonster(jigen, out _) && p.MonsterCount == 2 &&
                    opp.LifePoints == lp,
                    $"blocked={blocked} p={Ids(p.MonstersOnField())} lp={opp.LifePoints}/{lp}");
            }
        }

        // ── Spear Cretin ──

        static void SpearCretinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Flipped, then destroyed: you select from your GY (not Spear Cretin itself, not a Fusion, not a monster
            // that cannot be Special Summoned) and set it; the AI revives its Kuriboh face-up Attack.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                p.Graveyard.Add(engine.CreateCardInstance(GiantSoldier));
                p.Graveyard.Add(engine.CreateCardInstance(FlameSwordsman));
                p.Graveyard.Add(engine.CreateCardInstance(YataGarasu));
                p.Graveyard.Add(engine.CreateCardInstance(Fenrir));
                opp.Graveyard.Add(engine.CreateCardInstance(Kuriboh));
                var flipped = engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                var quietAfterFlip = !engine.IsAwaitingEffectTarget && p.TryFindMonster(cretin, out _);
                engine.DestroyMonsterPublic(p, cretin, null);
                var pending = engine.PendingActivation;
                var offered = pending?.LegalTargets?.ToList();
                Check("Spear Cretin: the flip itself does nothing; destroyed → you select from your own GY (not itself, a Fusion, Yata-Garasu or Nomi Fenrir)",
                    flipped && quietAfterFlip && pending != null && FlipMassEffectsRuntime.IsMassSummonPick(pending) &&
                    pending.Controller == p && SpellTrapEffects.PendingTargetIsSpecialSummon(pending) &&
                    offered != null && offered.Count == 1 && offered[0].CardId == GiantSoldier,
                    $"flipped={flipped} quiet={quietAfterFlip} offered={Ids(offered)}");
                var picked = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense, 2);
                DrainCombat(engine);
                var soldier = OnField(p, GiantSoldier);
                Check("Spear Cretin: Summon DEF sets your target face-down; the AI's Kuriboh comes back face-up Attack",
                    picked && !engine.IsAwaitingEffectTarget && FaceDownDefense(soldier) &&
                    p.MonsterZones[2].Occupant == soldier && FaceUpAttack(OnField(opp, Kuriboh)) &&
                    p.Graveyard.Contains(cretin) && Count(p.Graveyard, FlameSwordsman) == 1 &&
                    Count(p.Graveyard, YataGarasu) == 1 && Count(p.Graveyard, Fenrir) == 1 && opp.Graveyard.Count == 0,
                    $"picked={picked} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())} pGy={Ids(p.Graveyard)}");
            }

            // Never flipped / flipped then turned face-down: destroyed → nothing.
            for (var variant = 0; variant < 2; variant++)
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var cretin = variant == 0
                    ? PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Attack, true)
                    : PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                opp.Graveyard.Add(engine.CreateCardInstance(Kuriboh));
                if (variant == 1)
                {
                    engine.TryFlipSummon(p, cretin);
                    DrainCombat(engine);
                    cretin.FaceUp = false; // Book of Moon
                    cretin.Position = BattlePosition.Defense;
                }

                engine.DestroyMonsterPublic(p, cretin, null);
                DrainCombat(engine);
                Check(variant == 0
                        ? "Spear Cretin: Summoned face-up (never flipped) and destroyed — nothing is Special Summoned"
                        : "Spear Cretin: turned face-down after the flip, then destroyed — nothing is Special Summoned",
                    !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(cretin) && p.MonsterCount == 0 &&
                    opp.MonsterCount == 0,
                    $"variant={variant} await={engine.IsAwaitingEffectTarget} p={p.MonsterCount} o={opp.MonsterCount}");
            }

            // CANCEL: the selection is mandatory — your highest-ATK monster, face-up Attack.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                p.Graveyard.Add(engine.CreateCardInstance(Kuriboh));
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                engine.DestroyMonsterPublic(p, cretin, null);
                var waiting = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                var cancelled = engine.CancelEffectTargeting();
                DrainCombat(engine);
                Check("Spear Cretin: CANCEL selects your highest-ATK monster (Celtic) in face-up Attack Position",
                    waiting && cancelled && !engine.IsAwaitingEffectTarget && FaceUpAttack(OnField(p, Celtic)) &&
                    Count(p.Graveyard, Kuriboh) == 1,
                    $"waiting={waiting} cancel={cancelled} p={Ids(p.MonstersOnField())}");
            }

            // No free Monster Zone for you: your Spear Cretin, flipped while the AI controls it, goes to your GY while
            // your field is full — you get no selection; the AI (its zone freed) sets its Mystical Elf.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Spear Cretin full-field setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var cretin = PlaceMonster(engine, opp, SpearCretin, 0, BattlePosition.Defense, false);
                    cretin.ControlOwner = p;
                    for (var z = 0; z < p.MonsterZones.Length; z++)
                        PlaceMonster(engine, p, Celtic, z, BattlePosition.Attack, true);
                    p.Graveyard.Add(engine.CreateCardInstance(GiantSoldier));
                    opp.Graveyard.Add(engine.CreateCardInstance(MysticalElf));
                    var flipped = engine.TryFlipSummon(opp, cretin);
                    DrainCombat(engine);
                    engine.DestroyMonsterPublic(opp, cretin, null);
                    DrainCombat(engine);
                    Check("Spear Cretin: in your GY with no free zone for you — no selection for you; the AI sets Mystical Elf",
                        flipped && !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(cretin) &&
                        p.MonsterCount == 5 && Count(p.Graveyard, GiantSoldier) == 1 &&
                        FaceDownDefense(OnField(opp, MysticalElf)),
                        $"flipped={flipped} await={engine.IsAwaitingEffectTarget} pGy={Ids(p.Graveyard)} o={Ids(opp.MonstersOnField())}");
                }
            }

            // The AI's Spear Cretin: the AI selects first (at once), then you select from your GY.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                    Check("Spear Cretin AI setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var cretin = PlaceMonster(engine, opp, SpearCretin, 0, BattlePosition.Defense, false);
                    opp.Graveyard.Add(engine.CreateCardInstance(MysticalElf));
                    p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                    var flipped = engine.TryFlipSummon(opp, cretin);
                    DrainCombat(engine);
                    engine.DestroyMonsterPublic(opp, cretin, null);
                    var pending = engine.PendingActivation;
                    var aiWaits = opp.MonsterCount == 0;
                    var picked = PickWith(engine, Offered(engine, Celtic), BattlePosition.Attack);
                    DrainCombat(engine);
                    Check("Spear Cretin (AI's): you select from your GY; both targets are summoned together (Elf set face-down)",
                        flipped && pending != null && pending.Controller == p && aiWaits && picked &&
                        FaceUpAttack(OnField(p, Celtic)) && FaceDownDefense(OnField(opp, MysticalElf)) &&
                        opp.Graveyard.Contains(cretin),
                        $"flipped={flipped} pending={pending != null} picked={picked} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
                }
            }
        }

        static void SpearCretinBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Attacked while face-down: flipped, destroyed by battle → the trigger; you set your monster, the AI
            // revives its own.
            var engine = Fresh(db, pDeck, aDeck);
            if (!ReachOpponentBattle(engine))
            {
                Check("Spear Cretin battle setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                return;
            }

            PrepareBoard(engine, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
            var p = engine.Player;
            var opp = engine.Opponent;
            var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
            var attacker = PlaceMonster(engine, opp, Celtic, 0, BattlePosition.Attack, true);
            attacker.ClearAttackFlags();
            p.Graveyard.Add(engine.CreateCardInstance(GiantSoldier));
            opp.Graveyard.Add(engine.CreateCardInstance(Kuriboh));
            var atk = engine.TryAttack(opp, attacker, cretin);
            DrainCombat(engine);
            var waiting = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
            var picked = PickWith(engine, Offered(engine, GiantSoldier), BattlePosition.Defense);
            DrainCombat(engine);
            Check("Spear Cretin: flipped and destroyed by an attack — you set Giant Soldier, the AI revives Kuriboh",
                atk && waiting && picked && !engine.IsAwaitingEffectTarget && p.Graveyard.Contains(cretin) &&
                FaceDownDefense(OnField(p, GiantSoldier)) && FaceUpAttack(OnField(opp, Kuriboh)) &&
                opp.TryFindMonster(attacker, out _),
                $"atk={atk} waiting={waiting} picked={picked} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
        }

        // ── Review fixes: simultaneous sends, face-down Summons, waiting / displaced choices, proper Summons ──

        /// <summary>Every card either player owns, wherever it is (a card that leaves every zone is lost).</summary>
        static int Total(DuelistState w) =>
            w.Deck.Count + w.Hand.Count + w.Graveyard.Count + w.Banished.Count + w.MonsterCount +
            w.SpellTrapsOnField().Count();

        static void SimultaneousSends(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Dark Hole destroys your flipped Spear Cretin, your Celtic Guardian and the AI's 5 Mystical Elves at the
            // same time: the trigger comes after Dark Hole — you may select Celtic, the AI one of its Elves. Both zone
            // orders (Spear Cretin destroyed before / after Celtic).
            for (var order = 0; order < 2; order++)
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var opp = engine.Opponent;
                var cretin = PlaceMonster(engine, p, SpearCretin, order == 0 ? 0 : 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, order == 0 ? 1 : 0, BattlePosition.Attack, true);
                for (var z = 0; z < 5; z++) PlaceMonster(engine, opp, MysticalElf, z, BattlePosition.Attack, true);
                var flipped = engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                var hole = PutInHand(engine, p, DarkHole);
                var act = engine.TryActivateSpellTrap(p, hole, fromHand: true);
                DrainCombat(engine);
                var pending = engine.PendingActivation;
                var offered = pending?.LegalTargets?.ToList();
                var celticOffered = offered != null && offered.Contains(celtic) &&
                                    FlipMassEffectsRuntime.IsMassSummonPick(pending) && pending.Controller == p;
                var picked = PickWith(engine, celtic, BattlePosition.Attack);
                DrainCombat(engine);
                Check($"FlipMassEffects: Dark Hole + flipped Spear Cretin — Celtic (destroyed by the same Dark Hole) is offered (order {order})",
                    flipped && act && celticOffered && picked && FaceUpAttack(OnField(p, Celtic)),
                    $"flipped={flipped} act={act} offered={Ids(offered)} picked={picked} p={Ids(p.MonstersOnField())} pGy={Ids(p.Graveyard)}");
                Check($"FlipMassEffects: Dark Hole + flipped Spear Cretin — the AI revives one of its destroyed Elves (order {order})",
                    opp.MonsterCount == 1 && FaceDownDefense(OnField(opp, MysticalElf)) && Count(opp.Graveyard, MysticalElf) == 4,
                    $"o={Ids(opp.MonstersOnField())} oGy={Ids(opp.Graveyard)}");
            }

            // Jigen Bakudan destroys your flipped Spear Cretin and Celtic at your next Standby Phase: the trigger
            // comes after Jigen's effect (and its damage) — Celtic is offered.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jigen = PlaceMonster(engine, p, JigenBakudan, 0, BattlePosition.Defense, false);
                var cretin = PlaceMonster(engine, p, SpearCretin, 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var lp = opp.LifePoints;
                var f1 = engine.TryFlipSummon(p, jigen);
                DrainCombat(engine);
                cretin.SetThisTurn = false;
                var f2 = engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                PassTurn(engine);
                PassTurn(engine);
                var pending = engine.PendingActivation;
                var offered = pending?.LegalTargets?.ToList();
                var ok = offered != null && offered.Contains(celtic) && offered.Contains(jigen) &&
                         opp.LifePoints == lp - (1400 + 500) / 2;
                var picked = PickWith(engine, celtic, BattlePosition.Attack);
                DrainCombat(engine);
                Check("FlipMassEffects: Jigen Bakudan destroys flipped Spear Cretin + Celtic — after the damage, Celtic and the Tributed Jigen are offered",
                    f1 && f2 && ok && picked && FaceUpAttack(OnField(p, Celtic)),
                    $"f1={f1} f2={f2} offered={Ids(offered)} lp={opp.LifePoints}/{lp} p={Ids(p.MonstersOnField())} pGy={Ids(p.Graveyard)}");
            }

            // Both battling monsters destroyed (500 vs 500): the AI's attacker is in its GY when the trigger resolves,
            // so the AI revives it; you select from yours.
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(engine))
                    Check("FlipMassEffects mutual-destruction setup: reach the opponent's Battle Phase", false, $"phase={engine.Phase}");
                else
                {
                    PrepareBoard(engine, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                    var p = engine.Player;
                    var opp = engine.Opponent;
                    var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Attack, true);
                    MonsterEffects.OnFlipSummoned(engine, p, cretin);
                    var recorded = FlipMassEffectsRuntime.WasFlipped(cretin);
                    var soldier = engine.CreateCardInstance(GiantSoldier);
                    p.Graveyard.Add(soldier);
                    var attacker = PlaceMonster(engine, opp, SpearCretin, 0, BattlePosition.Attack, true);
                    attacker.ClearAttackFlags();
                    var atk = engine.TryAttack(opp, attacker, cretin);
                    DrainCombat(engine);
                    var offered = engine.PendingActivation?.LegalTargets?.ToList();
                    var picked = PickWith(engine, soldier, BattlePosition.Defense);
                    DrainCombat(engine);
                    Check("FlipMassEffects: both battling monsters destroyed — the AI revives its attacker, you set Giant Soldier",
                        recorded && atk && picked && opp.TryFindMonster(attacker, out _) &&
                        FaceDownDefense(OnField(p, GiantSoldier)) && p.Graveyard.Contains(cretin),
                        $"rec={recorded} atk={atk} offered={Ids(offered)} o={Ids(opp.MonstersOnField())} oGy={Ids(opp.Graveyard)} p={Ids(p.MonstersOnField())}");
                }
            }
        }

        static void FaceDownSummonsCannotFlipThisTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Morphing Jar #2 in your Main Phase: the face-down monster it Special Summons cannot be Flip Summoned
            // this turn (no second resolution in the same Main Phase), but it can next turn.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(Celtic, 10), Fill(PotOfGreed, 10));
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, MorphingJar2, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                var set = p.MonstersOnField().FirstOrDefault(m => m != null && !m.FaceUp);
                var canFlip = set != null && engine.CanFlipSummon(p, set);
                var didFlip = set != null && engine.TryFlipSummon(p, set);
                PassTurn(engine);
                PassTurn(engine);
                var nextTurn = set != null && engine.TurnPlayer == p && engine.CanFlipSummon(p, set);
                Check("FlipMassEffects: a monster Morphing Jar #2 sets cannot be Flip Summoned the same turn (it can next turn)",
                    flipped && set != null && !canFlip && !didFlip && nextTurn,
                    $"flipped={flipped} set={set?.Name} canFlip={canFlip} didFlip={didFlip} next={nextTurn}");
            }

            // Cyber Jar: a revealed Man-Eater Bug you set face-down cannot be Flip Summoned this turn.
            {
                var engine = Setup(db, pDeck, aDeck, Concat(new[] { ManEaterBug }, Fill(PotOfGreed, 5)),
                    Fill(PotOfGreed, 6));
                var p = engine.Player;
                var jar = PlaceMonster(engine, p, CyberJar, 0, BattlePosition.Defense, false);
                var flipped = engine.TryFlipSummon(p, jar);
                DrainCombat(engine);
                var picked = PickWith(engine, Offered(engine, ManEaterBug), BattlePosition.Defense);
                DrainCombat(engine);
                var bug = OnField(p, ManEaterBug);
                Check("FlipMassEffects: Cyber Jar — a Man-Eater Bug set face-down by it cannot be Flip Summoned this turn",
                    flipped && picked && FaceDownDefense(bug) && !engine.CanFlipSummon(p, bug) && !engine.TryFlipSummon(p, bug),
                    $"flipped={flipped} picked={picked} bug={bug?.Name} setThisTurn={bug?.SetThisTurn}");
            }

            // Spear Cretin on your turn: the monster you set from your GY cannot be Flip Summoned this turn.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                var bug = engine.CreateCardInstance(ManEaterBug);
                p.Graveyard.Add(bug);
                engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                engine.DestroyMonsterPublic(p, cretin, null);
                var picked = PickWith(engine, bug, BattlePosition.Defense);
                DrainCombat(engine);
                Check("FlipMassEffects: Spear Cretin — a monster set from your GY on your turn cannot be Flip Summoned that turn",
                    picked && FaceDownDefense(bug) && p.TryFindMonster(bug, out _) && !engine.CanFlipSummon(p, bug),
                    $"picked={picked} p={Ids(p.MonstersOnField())} setThisTurn={bug.SetThisTurn}");
            }
        }

        static void WaitingAndDisplacedChoices(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Two flipped Spear Cretins destroyed by one Dark Hole: the second trigger waits for your first choice
            // (it is never decided for you), and it never offers the monster the first one summoned.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var c1 = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                var c2 = PlaceMonster(engine, p, SpearCretin, 1, BattlePosition.Defense, false);
                engine.TryFlipSummon(p, c1);
                DrainCombat(engine);
                c2.SetThisTurn = false;
                var f2 = engine.TryFlipSummon(p, c2);
                DrainCombat(engine);
                var celtic = engine.CreateCardInstance(Celtic);
                var kuriboh = engine.CreateCardInstance(Kuriboh);
                p.Graveyard.Add(celtic);
                p.Graveyard.Add(kuriboh);
                PlaceMonster(engine, opp, MysticalElf, 0, BattlePosition.Attack, true);
                var hole = PutInHand(engine, p, DarkHole);
                var act = engine.TryActivateSpellTrap(p, hole, fromHand: true);
                DrainCombat(engine);
                var first = engine.PendingActivation;
                var firstMass = FlipMassEffectsRuntime.IsMassSummonPick(first);
                var firstOffered = first?.LegalTargets?.ToList();
                var nothingYet = p.MonsterCount == 0;
                var a = PickWith(engine, celtic, BattlePosition.Attack);
                DrainCombat(engine);
                var second = engine.PendingActivation;
                var secondMass = FlipMassEffectsRuntime.IsMassSummonPick(second);
                var secondOffered = second?.LegalTargets?.ToList();
                var b = PickWith(engine, kuriboh, BattlePosition.Attack);
                DrainCombat(engine);
                Check("FlipMassEffects: two flipped Spear Cretins + Dark Hole — the second choice waits for the first and is yours",
                    f2 && act && firstMass && nothingYet &&
                    firstOffered != null && firstOffered.Contains(celtic) && firstOffered.Contains(kuriboh) &&
                    secondMass && second != first && second.Controller == p &&
                    secondOffered != null && !secondOffered.Contains(celtic) && secondOffered.Contains(kuriboh) &&
                    a && b && FaceUpAttack(OnField(p, Celtic)) && FaceUpAttack(OnField(p, Kuriboh)) &&
                    Count(p.MonstersOnField(), Celtic) == 1 && opp.MonsterCount == 1,
                    $"f2={f2} act={act} first={Ids(firstOffered)} second={Ids(secondOffered)} a={a} b={b} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
            }

            // Two flipped Spear Cretins Tributed for Blue-Eyes White Dragon: after the Summon, one choice at a time.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var c1 = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                var c2 = PlaceMonster(engine, p, SpearCretin, 1, BattlePosition.Defense, false);
                engine.TryFlipSummon(p, c1);
                DrainCombat(engine);
                c2.SetThisTurn = false;
                engine.TryFlipSummon(p, c2);
                DrainCombat(engine);
                p.NormalSummonUsed = false;
                var celtic = engine.CreateCardInstance(Celtic);
                p.Graveyard.Add(celtic);
                var elf = engine.CreateCardInstance(MysticalElf);
                opp.Graveyard.Add(elf);
                var skull = PutInHand(engine, p, Bewd);
                engine.PendingTributes.Clear();
                engine.PendingTributes.Add(c1);
                engine.PendingTributes.Add(c2);
                var log = new List<string>();
                engine.OnLog += m => log.Add(m);
                var summoned = engine.TryNormalSummon(p, skull, asSet: false);
                DrainCombat(engine);
                var first = engine.PendingActivation;
                var firstMass = FlipMassEffectsRuntime.IsMassSummonPick(first);
                var a = PickWith(engine, celtic, BattlePosition.Attack);
                DrainCombat(engine);
                var second = engine.PendingActivation;
                var secondMass = FlipMassEffectsRuntime.IsMassSummonPick(second);
                var secondOffered = second?.LegalTargets?.ToList();
                var b = PickWith(engine, secondOffered?.FirstOrDefault(), BattlePosition.Defense);
                DrainCombat(engine);
                Check("FlipMassEffects: two flipped Spear Cretins Tributed together — two choices in turn, the Elf revived once",
                    summoned && p.TryFindMonster(skull, out _) && firstMass && a && secondMass && second != first &&
                    secondOffered != null && secondOffered.Count == 1 && !secondOffered.Contains(celtic) && b &&
                    FaceUpAttack(OnField(p, Celtic)) && p.MonsterCount == 3 && opp.TryFindMonster(elf, out _) &&
                    !engine.IsAwaitingEffectTarget,
                    $"summoned={summoned} a={a} second={Ids(secondOffered)} b={b} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())} log={string.Join(" | ", log)}");
            }

            // The AI acts while your Cyber Jar choice is open (engine calls: Monster Reborn / Dark Hole / Change of
            // Heart). Nothing is lost: a replaced choice is made automatically first.
            foreach (var spellId in new[] { MonsterReborn, DarkHole, ChangeOfHeart })
            {
                var engine = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentMain(engine))
                {
                    Check("FlipMassEffects displaced-choice setup: reach the opponent's Main Phase", false, $"phase={engine.Phase}");
                    continue;
                }

                PrepareBoard(engine, new[] { Celtic, GiantSoldier, PotOfGreed, PotOfGreed, PotOfGreed, PotOfGreed },
                    Fill(PotOfGreed, 8));
                var p = engine.Player;
                var opp = engine.Opponent;
                var jar = PlaceMonster(engine, opp, CyberJar, 0, BattlePosition.Defense, false);
                opp.Graveyard.Add(engine.CreateCardInstance(MysticalElf));
                var before = Total(p) + Total(opp);
                var flipped = engine.TryFlipSummon(opp, jar);
                var pick = engine.PendingActivation;
                var opened = FlipMassEffectsRuntime.IsMassSummonPick(pick) && pick.Controller == p;
                PlaceMonster(engine, p, Kuriboh, 4, BattlePosition.Attack, true);
                var spell = PutInHand(engine, opp, spellId);
                before += 2; // Kuriboh and the Spell
                var act = engine.CanActivateSpellTrap(opp, spell, true) && engine.TryActivateSpellTrap(opp, spell, true);
                for (var i = 0; i < 12; i++)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    else if (engine.IsAwaitingEffectTarget)
                    {
                        if (FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation))
                            engine.CancelEffectTargeting();
                        else
                        {
                            var t = engine.PendingActivation.LegalTargets.FirstOrDefault();
                            if (t == null || !engine.TrySelectEffectTarget(t)) engine.CancelEffectTargeting();
                        }
                    }
                    else break;
                }

                DrainCombat(engine);
                var after = Total(p) + Total(opp);
                var revealedSummoned = Count(p.MonstersOnField(), Celtic) + Count(p.Graveyard, Celtic) +
                                       Count(p.Hand, Celtic) + Count(opp.MonstersOnField(), Celtic) == 1 &&
                                       Count(p.MonstersOnField(), GiantSoldier) + Count(p.Graveyard, GiantSoldier) +
                                       Count(opp.MonstersOnField(), GiantSoldier) == 1;
                Check($"FlipMassEffects: the AI's {spell.Name} while your Cyber Jar choice is open — no card is lost",
                    flipped && opened && after == before && revealedSummoned && !engine.IsAwaitingEffectTarget,
                    $"act={act} before={before} after={after} p={Ids(p.MonstersOnField())} pGy={Ids(p.Graveyard)} pHand={Ids(p.Hand)} o={Ids(opp.MonstersOnField())}");
            }

            // Something clears your open Spear Cretin choice from outside: the Summons still happen.
            {
                var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
                var p = engine.Player;
                var opp = engine.Opponent;
                var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
                p.Graveyard.Add(engine.CreateCardInstance(Celtic));
                opp.Graveyard.Add(engine.CreateCardInstance(Kuriboh));
                engine.TryFlipSummon(p, cretin);
                DrainCombat(engine);
                engine.DestroyMonsterPublic(p, cretin, null);
                var opened = FlipMassEffectsRuntime.IsMassSummonPick(engine.PendingActivation);
                engine.ClearPendingActivation();
                DrainCombat(engine);
                Check("FlipMassEffects: an open Spear Cretin choice cleared from outside is made automatically (both Summons happen)",
                    opened && !engine.IsAwaitingEffectTarget && FaceUpAttack(OnField(p, Celtic)) &&
                    OnField(opp, Kuriboh) != null,
                    $"opened={opened} p={Ids(p.MonstersOnField())} o={Ids(opp.MonstersOnField())}");
            }
        }

        static void SpearCretinProperSummonTargets(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Relinquished discarded (never Ritual Summoned) is not a legal target; one that was Ritual Summoned and
            // then destroyed is, and so is a Fusion Summoned Flame Swordsman (Lua IsCanBeSpecialSummoned, revive limit).
            var engine = Setup(db, pDeck, aDeck, Fill(PotOfGreed, 5), Fill(PotOfGreed, 5));
            var p = engine.Player;
            var cretin = PlaceMonster(engine, p, SpearCretin, 0, BattlePosition.Defense, false);
            var discarded = engine.CreateCardInstance(Relinquished);
            p.Graveyard.Add(discarded);
            var ritual = engine.CreateCardInstance(Relinquished);
            var fusion = engine.CreateCardInstance(FlameSwordsman);
            var r = engine.SpecialSummonToField(p, ritual, BattlePosition.Attack, true, SummonKind.RitualSummon);
            var f = engine.SpecialSummonToField(p, fusion, BattlePosition.Attack, true, SummonKind.FusionSummon);
            DrainCombat(engine);
            engine.DestroyMonsterPublic(p, ritual, null);
            engine.DestroyMonsterPublic(p, fusion, null);
            DrainCombat(engine);
            var flipped = engine.TryFlipSummon(p, cretin);
            DrainCombat(engine);
            engine.DestroyMonsterPublic(p, cretin, null);
            var offered = engine.PendingActivation?.LegalTargets?.ToList();
            Check("FlipMassEffects: Spear Cretin — a never-summoned Ritual Monster is not offered; Ritual / Fusion Summoned ones are",
                r && f && flipped && offered != null && !offered.Contains(discarded) && offered.Contains(ritual) &&
                offered.Contains(fusion) && p.Graveyard.Contains(discarded),
                $"r={r} f={f} flipped={flipped} offered={Ids(offered)} pGy={Ids(p.Graveyard)}");
            if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
        }

        // ── Deferred ──

        static void DeferredGuard(CardDatabase db, Chk Check)
        {
            // Parasite Paracide was deferred here (Decks held card ids only). PerCopyDeck batch (v163): DeckPile keeps
            // the copy's owner and face-up flag, so it compiles to its FLIP and its "when drawn" trigger
            // (PerCopyDeckRegressionTests drive it).
            var paracide = db.Get(ParasiteParacide);
            var prog = CardTextEffectCompiler.Compile(paracide);
            Check("FlipMassEffects: Parasite Paracide compiles to its FLIP into the opponent's Deck and its draw trigger",
                CardEffectStatus.Classify(paracide) == CardEffectStatusKind.Implemented && prog != null &&
                prog.FullyCompiled && prog.ClauseList.Count == 2 &&
                prog.ClauseList[0].Timing == EffectTiming.Flip &&
                prog.ClauseList[0].Action == EffectActionKind.PutThisFaceUpInOpponentDeck &&
                prog.ClauseList[1].Timing == EffectTiming.DrawnFaceUpFromDeck &&
                prog.ClauseList[1].Action == EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace,
                Left(prog));
        }
    }
}
