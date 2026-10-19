using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvEmptyHandAndAuras batch: Cybernetic Cyclopean, Reborn Zombie, Mechanical Hound, Elemental HERO Wildheart, Gyroid
    /// (and Fortress Warrior, whose other sentence was already compiled), Tyranno Infinity, Batteryman C
    /// (<see cref="CrvEmptyHandAndAurasAtoms"/>, <see cref="CrvEmptyHandAndAurasRuntime"/>). Compile shapes through the real
    /// compiler, mini-duels through the engine entry points (board refreshes, TryAttack in the AI's Battle Phase, the
    /// AttackDeclared window with TryActivateSpellTrap, CanActivateSpellTrap / TryActivateSpellTrap) and look-alike sentences
    /// that must stay off this batch's clauses.
    /// </summary>
    public static class CrvEmptyHandAndAurasRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Cyclopean = 96428622;       // Beast-Warrior Lv4 1400/200
        const int RebornZombie = 23421244;    // Zombie Lv4 1000/1600
        const int MechanicalHound = 22512237; // Machine Lv7 2800/1500
        const int Wildheart = 86188410;       // Warrior Lv4 1500/1600
        const int Gyroid = 18325492;          // Machine Lv3 1000/1000
        const int FortressWarrior = 66288028; // Warrior Lv2 600/1200
        const int TyrannoInfinity = 83235263; // Dinosaur Lv4 ?/0
        const int BatterymanC = 19733961;     // Thunder Lv2 0/0

        const int Celtic = 91152256;          // Warrior 1400/1200
        const int BlueEyes = 89631139;        // Dragon 3000/2500
        const int GiantSoldier = 13039848;    // Rock 1300/2000
        const int KingRex = 94119974;         // Dinosaur 1600/1200
        const int Uraby = 1784619;            // Dinosaur 1500/800
        const int CannonSoldier = 11384280;   // Machine 1400/1300
        const int MirrorForce = 44095762;
        const int PotOfGreed = 55144522;
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int Reinforcements = 17814387;
        const int BannerOfCourage = 10012614;
        const int GravityBind = 85742772;     // Continuous Trap: Level 4 or higher monsters cannot attack
        const int HorusLv6 = 11224103;        // Dragon Lv6 2300/1600, unaffected by Spell effects
        const int Mountain = 50913601;        // Field Spell: Dragon / Winged Beast / Thunder +200
        const int Sogen = 86318356;           // Field Spell: Warrior / Beast-Warrior +200

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("CrvEmptyHandAndAuras: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            CyclopeanDuels(db, pDeck, aDeck, Check);
            RebornZombieDuels(db, pDeck, aDeck, Check);
            HoundDuels(db, pDeck, aDeck, Check);
            WildheartDuels(db, pDeck, aDeck, Check);
            WildheartVsTrapLocks(db, pDeck, aDeck, Check);
            GyroidDuels(db, pDeck, aDeck, Check);
            TyrannoDuels(db, pDeck, aDeck, Check);
            BatterymanCDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Compile(CardDatabase db, int id) =>
            db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;

        static bool Implemented(CardDatabase db, int id) =>
            db.Get(id) != null && CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static CompiledCardProgram CompileText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990212, name = "Near-miss CRV monster", type = "Effect Monster", frameType = "effect",
                race = "Machine", attribute = "EARTH", level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Only(CompiledCardProgram p, Func<EffectClause, bool> shape) =>
            p != null && p.FullyCompiled && p.ClauseList.Count == 1 && shape(p.ClauseList[0]);

        /// <summary>Turn 3, the player's Main Phase 1, with an empty board, hands, GYs and banishments.</summary>
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
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        /// <summary>The AI's next Battle Phase after the current turn (a later turn than <paramref name="after"/>).</summary>
        static bool NextOpponentBattle(DuelEngine engine, int after)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == engine.Opponent && engine.TurnNumber > after); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != engine.Opponent || engine.TurnNumber <= after) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1) engine.TryEnterBattlePhase(engine.Opponent);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Opponent;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 10 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool Attack(DuelEngine e, CardInstance attacker, CardInstance target)
        {
            attacker.ClearAttackFlags();
            var ok = e.TryAttack(e.Opponent, attacker, target);
            DrainCombat(e);
            PassAll(e);
            return ok;
        }

        static CardInstance ToBanished(DuelEngine engine, DuelistState who, int id, bool faceUp)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = faceUp;
            who.Banished.Add(c);
            return c;
        }

        static string Stats(CardInstance c) => c == null ? "null" : $"{c.CurrentAtk}/{c.CurrentDef}";

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var cyc = Compile(db, Cyclopean);
            Check("Cybernetic Cyclopean: +1000 ATK on itself while its controller's hand is empty",
                Implemented(db, Cyclopean) && Only(cyc, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                    c.AffectsThisCardOnly && c.Amount == 1000 && c.DefAmount == 0 &&
                    c.WhileCondition == CrvEmptyHandAndAurasRuntime.NoCardsInHand && !c.MakesChainLink),
                Describe(cyc));

            var rz = Compile(db, RebornZombie);
            Check("Reborn Zombie: the face-up Attack Position battle shield, only while the hand is empty",
                Implemented(db, RebornZombie) && Only(rz, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition &&
                    c.WhileCondition == CrvEmptyHandAndAurasRuntime.NoCardsInHand),
                Describe(rz));

            var hound = Compile(db, MechanicalHound);
            Check("Mechanical Hound: the opponent's Spell Card lock while the hand is empty",
                Implemented(db, MechanicalHound) && Only(hound, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.ContinuousOpponentCannotActivateSpellCards &&
                    c.WhileCondition == CrvEmptyHandAndAurasRuntime.NoCardsInHand && !c.MakesChainLink),
                Describe(hound));

            var wh = Compile(db, Wildheart);
            Check("Elemental HERO Wildheart: unaffected by Trap effects (both players')",
                Implemented(db, Wildheart) && Only(wh, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.UnaffectedByCardEffects &&
                    c.UnaffectedByFilter == "Trap" && c.Side == EffectSide.Both && !c.UnaffectedOnlyByOpponentEffects),
                Describe(wh));

            var gy = Compile(db, Gyroid);
            Check("Gyroid: the once-per-turn battle shield (not the permanent CannotBeDestroyedByBattle)",
                Implemented(db, Gyroid) && Only(gy, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.CannotBeDestroyedByBattleOncePerTurn && c.OncePerTurn),
                Describe(gy));

            var fw = Compile(db, FortressWarrior);
            Check("Fortress Warrior: no battle damage from its battles + the once-per-turn battle shield",
                Implemented(db, FortressWarrior) && fw != null && fw.FullyCompiled && fw.ClauseList.Count == 2 &&
                fw.ClauseList.Exists(c => c.Action == EffectActionKind.PreventControllerBattleDamage &&
                                          c.OnlyBattlesInvolvingThis) &&
                fw.ClauseList.Exists(c => c.Action == EffectActionKind.CannotBeDestroyedByBattleOncePerTurn) &&
                !fw.ClauseList.Exists(c => c.Action == EffectActionKind.CannotBeDestroyedByBattle),
                Describe(fw));

            var ty = Compile(db, TyrannoInfinity);
            Check("Tyranno Infinity: original ATK = your face-up banished Dinosaurs x 1000 (DEF untouched)",
                Implemented(db, TyrannoInfinity) && Only(ty, c =>
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.GainSelfAtkPerCount &&
                    c.Amount == 1000 && c.DefAmount == 0 && c.SelfStatIsSet && c.RaceFilter == "Dinosaur" &&
                    c.CountSource == TlmDerivedStatsAndProceduresRuntime.YourFaceUpBanishedRace),
                Describe(ty));

            var bc = Compile(db, BatterymanC);
            var atk = bc?.ClauseList.FirstOrDefault(c =>
                c.CountSource == CrvEmptyHandAndAurasRuntime.FaceUpNamedYouControlAllInAttack);
            var def = bc?.ClauseList.FirstOrDefault(c =>
                c.CountSource == CrvEmptyHandAndAurasRuntime.FaceUpNamedYouControlAllInDefense);
            Check("Batteryman C: two Machine auras, 500 ATK (all copies in Attack) / 500 DEF (all in Defense) per copy",
                Implemented(db, BatterymanC) && bc.FullyCompiled && bc.ClauseList.Count == 2 && atk != null && def != null &&
                atk.Action == EffectActionKind.ContinuousGainAtkDef && atk.Amount == 500 && atk.DefAmount == 0 &&
                def.Action == EffectActionKind.ContinuousGainAtkDef && def.Amount == 0 && def.DefAmount == 500 &&
                atk.RaceFilter == "Machine" && def.RaceFilter == "Machine" && atk.Side == EffectSide.Controller &&
                atk.ConditionName == "Batteryman C" && string.IsNullOrEmpty(atk.NamedCard) && !atk.AffectsThisCardOnly,
                Describe(bc));
        }

        static void NearMisses(Chk Check)
        {
            var atkDef = CompileText("While you have no cards in your hand, this card gains 1000 ATK and DEF.");
            Check("Near miss: \"gains 1000 ATK and DEF\" while the hand is empty is not Cybernetic Cyclopean's ATK-only gain",
                atkDef == null || !atkDef.ClauseList.Exists(c =>
                    c.WhileCondition == CrvEmptyHandAndAurasRuntime.NoCardsInHand && c.DefAmount == 0 && c.Amount == 1000),
                Describe(atkDef));

            var noPos = CompileText("While you have no cards in your hand, this card cannot be destroyed by battle.");
            Check("Near miss: the empty-hand battle shield without \"in Attack Position\" stays uncompiled",
                noPos == null || !noPos.FullyCompiled, Describe(noPos));

            var traps = CompileText("While you have no cards in your hand, your opponent cannot activate Trap Cards.");
            Check("Near miss: an empty-hand Trap Card lock is not Mechanical Hound's Spell lock",
                traps == null || (!traps.FullyCompiled && !traps.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.ContinuousOpponentCannotActivateSpellCards)),
                Describe(traps));

            var orEffects = CompileText("Once per turn, this card cannot be destroyed by battle or card effects.");
            Check("Near miss: \"by battle or card effects\" once per turn is not Gyroid's battle-only count",
                orEffects == null || (!orEffects.FullyCompiled && !orEffects.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.CannotBeDestroyedByBattleOncePerTurn)),
                Describe(orEffects));

            var plain = CompileText("This card cannot be destroyed by battle.");
            Check("Control: the plain \"cannot be destroyed by battle\" keeps the permanent shield, not the once-per-turn one",
                plain != null && plain.FullyCompiled &&
                plain.ClauseList.Exists(c => c.Action == EffectActionKind.CannotBeDestroyedByBattle) &&
                !plain.ClauseList.Exists(c => c.Action == EffectActionKind.CannotBeDestroyedByBattleOncePerTurn),
                Describe(plain));

            var spellTrap = CompileText("This card is unaffected by Spell and Trap effects.");
            Check("Near miss: \"unaffected by Spell and Trap effects\" is not Wildheart's Trap-only immunity",
                spellTrap == null || !spellTrap.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.UnaffectedByCardEffects && c.UnaffectedByFilter == "Trap"),
                Describe(spellTrap));

            var defIs = CompileText("The original DEF of this card is the number of your banished Dinosaur-Type monsters x 1000.");
            Check("Near miss: an \"original DEF is\" sentence is not Tyranno Infinity's ATK set",
                defIs == null || !defIs.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.GainSelfAtkPerCount && c.Amount == 1000),
                Describe(defIs));

            var otherName = CompileText(
                "If all \"Batteryman C\"(s) you control are in Attack Position, all Machine-Type monsters you control gain " +
                "500 ATK for each \"Batteryman D\" you control.");
            Check("Near miss: Batteryman C's aura counting a different name stays uncompiled",
                otherName == null || (!otherName.FullyCompiled && !otherName.ClauseList.Exists(c =>
                    c.CountSource == CrvEmptyHandAndAurasRuntime.FaceUpNamedYouControlAllInAttack)),
                Describe(otherName));
        }

        // ───────────────────────────── Cybernetic Cyclopean ─────────────────────────────

        static void CyclopeanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var cyc = PlaceMonster(e, p, Cyclopean, 0, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var empty = Stats(cyc);
            var okEmpty = cyc.CurrentAtk == 2400 && cyc.CurrentDef == 200;
            var card = PutInHand(e, p, Celtic);
            FieldSpellEffects.RefreshBoard(e);
            var withHand = Stats(cyc);
            var okHand = cyc.CurrentAtk == 1400;
            p.Hand.Remove(card);

            // The AI's Celtic Guardian (1400) attacks it with the hand empty: Celtic is destroyed, the AI takes 1000.
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var battle = NextOpponentBattle(e, e.TurnNumber);
            p.Hand.Clear();
            o.Hand.Clear();
            e.NotifyPublic();
            var atkInBattle = cyc.CurrentAtk;
            var ok = battle && Attack(e, celtic, cyc);
            Check("Cybernetic Cyclopean: 2400 ATK with an empty hand, 1400 with a card in hand; beats a 1400 attacker",
                reached && okEmpty && okHand && ok && atkInBattle == 2400 && o.Graveyard.Contains(celtic) &&
                OnField(p, cyc) && o.LifePoints == 7000,
                $"empty={empty} withHand={withHand} battle={battle} atk={atkInBattle} celticGy={o.Graveyard.Contains(celtic)} " +
                $"oppLp={o.LifePoints}");
        }

        // ───────────────────────────── Reborn Zombie ─────────────────────────────

        static void RebornZombieDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Empty hand, Attack Position: Celtic (1400) does not destroy it (400 damage); with a card in hand the
                // second Celtic does.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var zombie = PlaceMonster(e, p, RebornZombie, 0, BattlePosition.Attack, true);
                var c1 = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                p.Hand.Clear();
                o.Hand.Clear();
                var ok1 = battle && Attack(e, c1, zombie);
                var survived = OnField(p, zombie);
                var lp1 = p.LifePoints;
                PutInHand(e, p, Celtic);
                var ok2 = battle && Attack(e, c2, zombie);
                Check("Reborn Zombie: survives battle in Attack Position with an empty hand (damage applies); destroyed with " +
                      "a card in hand",
                    reached && ok1 && survived && lp1 == 7600 && ok2 && !OnField(p, zombie) && p.Graveyard.Contains(zombie),
                    $"battle={battle} ok1={ok1} survived={survived} lp1={lp1} ok2={ok2} gy={p.Graveyard.Contains(zombie)}");
            }
            {
                // Empty hand but in Defense Position: Blue-Eyes destroys it.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var zombie = PlaceMonster(e, p, RebornZombie, 0, BattlePosition.Defense, true);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                p.Hand.Clear();
                o.Hand.Clear();
                var ok = battle && Attack(e, be, zombie);
                Check("Reborn Zombie: in Defense Position the empty hand does not shield it",
                    reached && ok && p.Graveyard.Contains(zombie),
                    $"battle={battle} ok={ok} gy={p.Graveyard.Contains(zombie)}");
            }
        }

        // ───────────────────────────── Mechanical Hound ─────────────────────────────

        static void HoundDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var hound = PlaceMonster(e, p, MechanicalHound, 0, BattlePosition.Attack, true);
            var pot = PutInHand(e, o, PotOfGreed);
            var setMst = PlaceSetTrap(e, o, Mst, 0);
            var setTrap = PlaceSetTrap(e, o, Reinforcements, 1);
            var banner = PlaceSetTrap(e, o, BannerOfCourage, 2);
            banner.FaceUp = true;
            var ownPot = PutInHand(e, p, PotOfGreed);
            p.Hand.Remove(ownPot);
            e.NotifyPublic();
            var handLocked = !e.CanActivateSpellTrap(o, pot, fromHand: true);
            var tried = e.TryActivateSpellTrap(o, pot, fromHand: true);
            var stillInHand = o.Hand.Contains(pot);
            var setLocked = !e.CanActivateSpellTrap(o, setMst, fromHand: false);
            var trapFree = !ContinuousNegationRuntime.ActivationLocked(e, o, setTrap, out _);
            var faceUpSpellFree = !ContinuousNegationRuntime.ActivationLocked(e, o, banner, out _);
            Check("Mechanical Hound: with its controller's hand empty the opponent cannot activate Spell Cards (hand or " +
                  "Set); a Set Trap and a face-up Spell's effects stay legal",
                reached && handLocked && !tried && stillInHand && setLocked && trapFree && faceUpSpellFree,
                $"reached={reached} handLocked={handLocked} tried={tried} inHand={stillInHand} setLocked={setLocked} " +
                $"trapFree={trapFree} faceUpSpellFree={faceUpSpellFree}");

            var celtic = PutInHand(e, p, Celtic);
            e.NotifyPublic();
            var handBefore = o.Hand.Count;
            var act = e.CanActivateSpellTrap(o, pot, fromHand: true) && e.TryActivateSpellTrap(o, pot, fromHand: true);
            PassAll(e);
            var drew = o.Hand.Count == handBefore - 1 + 2 && o.Graveyard.Contains(pot);
            p.Hand.Remove(celtic);
            var pot2 = PutInHand(e, o, PotOfGreed);
            hound.FaceUp = false;
            hound.Position = BattlePosition.Defense;
            e.NotifyPublic();
            var faceDownFree = e.CanActivateSpellTrap(o, pot2, fromHand: true);
            Check("Mechanical Hound: a card in its controller's hand lifts the lock (Pot of Greed resolves); a face-down " +
                  "Hound does not lock",
                reached && act && drew && faceDownFree,
                $"act={act} drew={drew} hand={o.Hand.Count} faceDownFree={faceDownFree}");

            // The Hound's controller is not locked by its own Hound.
            var e2 = PlayerMain1(db, pDeck, aDeck, out var reached2);
            PlaceMonster(e2, e2.Player, MechanicalHound, 0, BattlePosition.Attack, true);
            var mine = PlaceSetTrap(e2, e2.Player, Mst, 0);
            PlaceSetTrap(e2, e2.Opponent, Reinforcements, 0);
            e2.NotifyPublic();
            Check("Mechanical Hound: its controller (hand empty) can still activate its own Spell Cards",
                reached2 && !CrvEmptyHandAndAurasRuntime.ActivationLocked(e2, e2.Player, mine, out _) &&
                e2.CanActivateSpellTrap(e2.Player, mine, fromHand: false),
                $"reached={reached2}");
        }

        // ───────────────────────────── Elemental HERO Wildheart ─────────────────────────────

        static void WildheartDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's Celtic attacks; the player answers with Mirror Force: Celtic is destroyed, Wildheart is not.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var hero = PlaceMonster(e, o, Wildheart, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var wall = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
                var mirror = PlaceSetTrap(e, p, MirrorForce, 0);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                p.Hand.Clear();
                o.Hand.Clear();
                celtic.ClearAttackFlags();
                var declared = battle && e.TryAttack(o, celtic, wall);
                var window = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared && e.PendingResponse.Responder == p;
                var act = window && e.TryActivateSpellTrap(p, mirror, fromHand: false);
                DrainCombat(e);
                PassAll(e);
                Check("Elemental HERO Wildheart: Mirror Force destroys the other Attack Position monster, not Wildheart",
                    reached && declared && act && o.Graveyard.Contains(celtic) && OnField(o, hero) &&
                    p.Graveyard.Contains(mirror),
                    $"battle={battle} declared={declared} window={window} act={act} celticGy={o.Graveyard.Contains(celtic)} " +
                    $"heroOnField={OnField(o, hero)} mirrorGy={p.Graveyard.Contains(mirror)}");
            }
            {
                // Spells still affect it (Dark Hole), and its own controller's Trap does not.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var hero = PlaceMonster(e, p, Wildheart, 0, BattlePosition.Attack, true);
                var ownTrap = PlaceSetTrap(e, p, Reinforcements, 0);
                var oppTrap = PlaceSetTrap(e, o, MirrorForce, 0);
                var unaffectedOwn = ContinuousProtections.IsUnaffectedBy(e, hero, ownTrap);
                var unaffectedOpp = ContinuousProtections.IsUnaffectedBy(e, hero, oppTrap);
                p.SpellTrapZones[0].Occupant = null;
                o.SpellTrapZones[0].Occupant = null;
                var hole = PutInHand(e, p, DarkHole);
                var act = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("Elemental HERO Wildheart: unaffected by its controller's and the opponent's Traps; Dark Hole destroys it",
                    reached && unaffectedOwn && unaffectedOpp && act && p.Graveyard.Contains(hero),
                    $"own={unaffectedOwn} opp={unaffectedOpp} act={act} gy={p.Graveyard.Contains(hero)}");
            }
        }

        static void WildheartVsTrapLocks(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's face-up Gravity Bind: the Level 4 Celtic cannot attack, the Level 4 Wildheart (unaffected by Trap
                // effects) can (Lua: Gravity Bind is an EFFECT_CANNOT_ATTACK field effect, which immunity ignores).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var hero = PlaceMonster(e, p, Wildheart, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var bind = PlaceSetTrap(e, o, GravityBind, 0);
                bind.FaceUp = true;
                hero.ClearAttackFlags();
                celtic.ClearAttackFlags();
                var battle = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                DrainCombat(e);
                var heroCan = e.CanAttack(p, hero);
                var celticCan = e.CanAttack(p, celtic);
                Check("Elemental HERO Wildheart: Gravity Bind does not stop it attacking; it still stops the other Level 4",
                    reached && battle && e.Phase == DuelPhase.Battle && heroCan && !celticCan,
                    $"battle={battle} phase={e.Phase} heroCan={heroCan} celticCan={celticCan}");
            }
            {
                // The aura path asks the same immunity question. No Continuous Trap in the corpus compiles to a field ATK
                // aura yet, so the shared guard is pinned with the Spell-immune shape: Horus the Black Flame Dragon LV6 gets
                // nothing from Mountain (field effects respect EFFECT_IMMUNE_EFFECT), Blue-Eyes still gets +200, and
                // Wildheart (immune to Traps only) still gets Sogen's +200.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var horus = PlaceMonster(e, p, HorusLv6, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, BlueEyes, 1, BattlePosition.Attack, true);
                var hero = PlaceMonster(e, p, Wildheart, 2, BattlePosition.Attack, true);
                var mountain = e.CreateCardInstance(Mountain);
                mountain.FaceUp = true;
                p.FieldSpellZone.Occupant = mountain;
                var sogen = e.CreateCardInstance(Sogen);
                sogen.FaceUp = true;
                o.FieldSpellZone.Occupant = sogen;
                FieldSpellEffects.RefreshBoard(e);
                Check("Field auras skip a monster unaffected by their source: Horus LV6 vs Mountain; Wildheart still takes Sogen",
                    reached && horus.CurrentAtk == 2300 && bewd.CurrentAtk == 3200 && hero.CurrentAtk == 1700,
                    $"horus={horus.CurrentAtk} bewd={bewd.CurrentAtk} hero={hero.CurrentAtk}");
            }
        }

        // ───────────────────────────── Gyroid / Fortress Warrior ─────────────────────────────

        static void GyroidDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two attacks in one turn: the first does not destroy it (damage applies), the second does.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gyroid = PlaceMonster(e, p, Gyroid, 0, BattlePosition.Attack, true);
                var c1 = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                o.Hand.Clear();
                var ok1 = battle && Attack(e, c1, gyroid);
                var survived = OnField(p, gyroid);
                var lp1 = p.LifePoints;
                var ok2 = battle && Attack(e, c2, gyroid);
                Check("Gyroid: the first battle destruction this turn is ignored (400 damage taken), the second is not",
                    reached && ok1 && survived && lp1 == 7600 && ok2 && p.Graveyard.Contains(gyroid) && p.LifePoints == 7200,
                    $"battle={battle} ok1={ok1} survived={survived} lp1={lp1} ok2={ok2} gy={p.Graveyard.Contains(gyroid)} " +
                    $"lp={p.LifePoints}");
            }
            {
                // The count is fresh on the AI's next turn.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var gyroid = PlaceMonster(e, p, Gyroid, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var battle1 = NextOpponentBattle(e, e.TurnNumber);
                o.Hand.Clear();
                var ok1 = battle1 && Attack(e, celtic, gyroid);
                var after1 = OnField(p, gyroid);
                var battle2 = NextOpponentBattle(e, e.TurnNumber);
                o.Hand.Clear();
                var ok2 = battle2 && Attack(e, celtic, gyroid);
                var after2 = OnField(p, gyroid);
                Check("Gyroid: the count resets each turn (survives one attack on each of two AI turns)",
                    reached && ok1 && after1 && ok2 && after2 && p.LifePoints == 7200,
                    $"b1={battle1} ok1={ok1} after1={after1} b2={battle2} ok2={ok2} after2={after2} lp={p.LifePoints}");
            }
            {
                // Fortress Warrior: no battle damage from its battles, and the once-per-turn shield.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var fw = PlaceMonster(e, p, FortressWarrior, 0, BattlePosition.Attack, true);
                var c1 = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                o.Hand.Clear();
                var ok1 = battle && Attack(e, c1, fw);
                var survived = OnField(p, fw);
                var ok2 = battle && Attack(e, c2, fw);
                Check("Fortress Warrior: survives the first battle this turn, destroyed by the second, no battle damage",
                    reached && ok1 && survived && ok2 && p.Graveyard.Contains(fw) && p.LifePoints == 8000,
                    $"battle={battle} ok1={ok1} survived={survived} ok2={ok2} gy={p.Graveyard.Contains(fw)} lp={p.LifePoints}");
            }
        }

        // ───────────────────────────── Tyranno Infinity ─────────────────────────────

        static void TyrannoDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var tyranno = PlaceMonster(e, p, TyrannoInfinity, 0, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var none = Stats(tyranno);
            var okNone = tyranno.CurrentAtk == 0 && tyranno.CurrentDef == 0;
            ToBanished(e, p, KingRex, true);
            ToBanished(e, p, Uraby, true);
            ToBanished(e, p, Uraby, false);        // face-down: not counted
            ToBanished(e, p, GiantSoldier, true);  // a Rock: not counted
            ToBanished(e, o, KingRex, true);       // the opponent's banishment: not counted
            FieldSpellEffects.RefreshBoard(e);
            var two = Stats(tyranno);
            var okTwo = tyranno.CurrentAtk == 2000 && tyranno.CurrentDef == 0 && BattleMechanics.AttackValue(tyranno) == 2000;
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var battle = NextOpponentBattle(e, e.TurnNumber);
            o.Hand.Clear();
            var ok = battle && Attack(e, celtic, tyranno);
            Check("Tyranno Infinity: 0 ATK with nothing banished; 2 face-up banished Dinosaurs → 2000; beats a 1400 attacker",
                reached && okNone && okTwo && ok && o.Graveyard.Contains(celtic) && OnField(p, tyranno) && o.LifePoints == 7400,
                $"none={none} two={two} battle={battle} ok={ok} celticGy={o.Graveyard.Contains(celtic)} oppLp={o.LifePoints}");
        }

        // ───────────────────────────── Batteryman C ─────────────────────────────

        static void BatterymanCDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var b1 = PlaceMonster(e, p, BatterymanC, 0, BattlePosition.Attack, true);
                var machine = PlaceMonster(e, p, CannonSoldier, 1, BattlePosition.Attack, true);
                var oppMachine = PlaceMonster(e, o, CannonSoldier, 0, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var one = Stats(machine);
                var okOne = machine.CurrentAtk == 1900 && machine.CurrentDef == 1300;

                var b2 = PlaceMonster(e, p, BatterymanC, 2, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                // Each copy's aura: 500 x 2 copies → 2 x 1000. Batteryman C (Thunder) and the AI's Machine get nothing.
                var two = $"{Stats(machine)} b1={Stats(b1)} opp={Stats(oppMachine)}";
                var okTwo = machine.CurrentAtk == 3400 && machine.CurrentDef == 1300 && b1.CurrentAtk == 0 &&
                            b2.CurrentAtk == 0 && oppMachine.CurrentAtk == 1400;

                var b3 = PlaceMonster(e, p, BatterymanC, 3, BattlePosition.Defense, false);
                FieldSpellEffects.RefreshBoard(e);
                var okSet = machine.CurrentAtk == 3400;

                b2.Position = BattlePosition.Defense;
                FieldSpellEffects.RefreshBoard(e);
                var mixed = Stats(machine);
                var okMixed = machine.CurrentAtk == 1400 && machine.CurrentDef == 1300;

                b1.Position = BattlePosition.Defense;
                FieldSpellEffects.RefreshBoard(e);
                var defense = Stats(machine);
                var okDefense = machine.CurrentAtk == 1400 && machine.CurrentDef == 3300 && b3.CurrentDef == 0;

                Check("Batteryman C: 1 copy → Machines +500 ATK; 2 in Attack → +2000 ATK (itself and the AI's Machine " +
                      "untouched); a Set copy ignored; mixed → nothing; 2 in Defense → +2000 DEF",
                    reached && okOne && okTwo && okSet && okMixed && okDefense,
                    $"one={one} two={two} set={okSet} mixed={mixed} defense={defense}");
            }
            {
                // Two copies in Attack: the AI's Blue-Eyes (3000) attacks the boosted Cannon Soldier (3400) and is destroyed.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, BatterymanC, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, BatterymanC, 1, BattlePosition.Attack, true);
                var machine = PlaceMonster(e, p, CannonSoldier, 2, BattlePosition.Attack, true);
                var be = PlaceMonster(e, o, BlueEyes, 0, BattlePosition.Attack, true);
                var battle = NextOpponentBattle(e, e.TurnNumber);
                o.Hand.Clear();
                e.NotifyPublic();
                var atk = machine.CurrentAtk;
                var ok = battle && Attack(e, be, machine);
                Check("Batteryman C: the boosted Machine (3400) destroys an attacking Blue-Eyes",
                    reached && ok && atk == 3400 && o.Graveyard.Contains(be) && OnField(p, machine) && o.LifePoints == 7600,
                    $"battle={battle} ok={ok} atk={atk} beGy={o.Graveyard.Contains(be)} oppLp={o.LifePoints}");
            }
        }
    }
}
