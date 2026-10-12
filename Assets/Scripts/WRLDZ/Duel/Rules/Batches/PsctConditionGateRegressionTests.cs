using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Engine fix "PsctConditionGate": the generic PSCT path refuses a sentence whose condition before the colon it
    /// cannot compile, instead of dropping the condition and compiling the rest (<see cref="PsctConditionGate"/>).
    /// Every card below used to be FullyCompiled with its condition silently gone; it now fails closed. Also:
    /// the unconditional direct-attack template no longer takes a sentence with an "if" rider (Drillago), and
    /// Gear Golem's "during your Main Phase 1" is compiled and enforced instead of dropped.
    /// </summary>
    public static class PsctConditionGateRegressionTests
    {
        /// <summary>Cards whose only compiled sentence had its condition dropped (see the class summary).</summary>
        static readonly (int id, string why)[] FailClosed =
        {
            (79853073, "Cipher Soldier: \"If this card battles a Warrior-Type monster, during damage calculation\""),
            // Coffin Seller (65830223) left this list in v162: the EffectDamageEvents atom compiles its whole sentence, condition
            // included (DroppedConditionsFailClosed checks that below).
            (57046845, "Gearfried the Swordmaster: \"Each time an Equip Card(s) is equipped to this card\""),
            // Michizure (37580756) left this list in v159: the BattleWindows atom compiles its whole sentence, condition
            // included (DroppedConditionsFailClosed checks that below).
            // Rope of Life (93382620) left this list in v162: the BattleDestroyedGyRecovery atom compiles its whole
            // sentence, condition included (DroppedConditionsFailClosed checks that below).
            (10035717, "Weapon Change: \"Once during each of your Standby Phases\""),
            // Z-Metal Tank and Heavy Mech Support Platform are Union monsters whose whole text is compiled by the
            // Union whole-text atom (v167); their "Once per turn, you can either" condition is no longer tested
            // through the generic PSCT path and are now Implemented.
            // Cliff, Gorg, Chick and Meanae are compiled whole (both choices) by the PartialCompiles atoms (v144/v173);
            // their generic first choice must not stand alone.
            // Drillago (99050989) left this list in v177: the EndOfDamageStepRiders atom compiles its whole sentence, condition
            // included (DroppedConditionsFailClosed checks that below).
        };

        const int GearGolem = 30190809;
        const int AlligatorsSwordDragon = 3366982;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("PsctConditionGate: " + name, ok, detail ?? "");

            GateUnit(Check);
            DroppedConditionsFailClosed(db, Check);
            DirectAttackRiders(db, Check);
            GearGolemMainPhase1(db, pDeck, aDeck, Check);
        }

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Condition gate probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : "effect",
                race = type.StartsWith("Trap") ? "Normal" : "Warrior", attribute = "EARTH", level = 4, desc = text
            });

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}]";

        static void GateUnit(Action<string, bool, string> Check)
        {
            foreach (var cond in new[]
                     {
                         "", "FLIP", "Once per turn", "When this card is Flip Summoned", "If this card is Tribute Summoned",
                         "When this card is destroyed by battle and sent to the Graveyard",
                         "If this card inflicts battle damage to your opponent", "If you control \"Dark Magician\"",
                         "If you control a face-up \"Dark Magician Girl\"", "Once per turn, during your Main Phase 1",
                         "During each of your End Phases", "When an opponent's monster declares an attack"
                     })
                Check($"recognized: \"{cond}\"", PsctConditionGate.IsRecognized(cond), "");

            foreach (var cond in new[]
                     {
                         "Once per turn, during the End Phase, if this card is currently banished, and was banished this turn",
                         "During the End Phase, if this card was Normal or Special Summoned this turn",
                         "Each time a monster(s) is sent to your opponent's GY",
                         "If this card battles a Warrior-Type monster, during damage calculation",
                         "When a monster(s) is destroyed by battle and sent to your GY",
                         "Once per turn, you can either",
                         "When this card inflicts Battle Damage to your opponent, you can activate 1 of these effects",
                         "If you control \"Dark Magician\", and your opponent controls no monsters"
                     })
                Check($"refused: \"{cond}\"", !PsctConditionGate.IsRecognized(cond), "");

            // Near miss through the whole compiler: a recognized trigger with an extra "if" rider is not compiled.
            var rider = CompileText(990021, "Effect Monster",
                "When this card is Flip Summoned, if you control no other monsters: Draw 1 card.");
            Check("a Flip Summon draw with an extra \"if\" rider fails closed",
                rider == null || !rider.FullyCompiled, Describe(rider));
            var plain = CompileText(990022, "Effect Monster", "When this card is Flip Summoned: Draw 1 card.");
            Check("the same draw without the rider still compiles",
                plain != null && plain.FullyCompiled && plain.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.Draw && c.RequiresThisFlipSummoned),
                Describe(plain));
        }

        static void DroppedConditionsFailClosed(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var (id, why) in FailClosed)
            {
                if (!db.TryGet(id, out var def)) continue;
                var prog = CardTextEffectCompiler.Compile(def);
                var status = CardEffectStatus.Classify(def);
                Check($"fails closed — {why}",
                    (prog == null || !prog.FullyCompiled) && status != CardEffectStatusKind.Implemented,
                    $"{status} {Describe(prog)}");
            }

            // Coffin Seller: "Each time a monster(s) is sent to your opponent's GY" is compiled whole by the EffectDamageEvents atom
            // (v162): its condition is the clause's own trigger action, never dropped into a bare burn.
            if (db.TryGet(65830223, out var coffinSeller))
            {
                var prog = CardTextEffectCompiler.Compile(coffinSeller);
                Check("Coffin Seller: its condition is compiled (EffectDamageEvents), not dropped",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy &&
                    !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.InflictDamageToOpponent),
                    Describe(prog));
            }

            // Michizure: "When a monster is sent from the field to your GY, even during the Damage Step" is compiled whole by
            // the BattleWindows atom (v159); its condition is the clause's ActivationCondition, never dropped.
            if (db.TryGet(37580756, out var michizure))
            {
                var prog = CardTextEffectCompiler.Compile(michizure);
                Check("Michizure: its condition is compiled (BattleWindows), not dropped",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count > 0 &&
                    prog.ClauseList.TrueForAll(c => c != null &&
                                                    c.ActivationCondition == BattleWindowsRuntime.MonsterSentFromFieldToYourGy),
                    Describe(prog));
            }

            // Rope of Life: "When a monster(s) is destroyed by battle and sent to your GY" is compiled whole by the
            // BattleDestroyedGyRecovery atom (v162); its condition is the clause's ActivationCondition, never dropped.
            if (db.TryGet(93382620, out var rope))
            {
                var prog = CardTextEffectCompiler.Compile(rope);
                Check("Rope of Life: its condition is compiled (BattleDestroyedGyRecovery), not dropped",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].ActivationCondition ==
                    BattleDestroyedGyRecoveryRuntime.MonsterDestroyedByBattleSentToYourGy,
                    Describe(prog));
            }

            // Drillago: "If the only cards your opponent controls are face-up monsters with 1600 or more ATK" is compiled whole
            // by the EndOfDamageStepRiders atom (v177): the condition is the clause's own action and Amount, never an
            // unconditional direct attack.
            if (db.TryGet(99050989, out var drillago))
            {
                var prog = CardTextEffectCompiler.Compile(drillago);
                Check("Drillago: its condition is compiled (EndOfDamageStepRiders), not dropped",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action ==
                    EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast &&
                    prog.ClauseList[0].Amount == 1600 &&
                    !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.CanAttackDirectly),
                    Describe(prog));
            }
        }

        static void DirectAttackRiders(CardDatabase db, Action<string, bool, string> Check)
        {
            if (db.TryGet(AlligatorsSwordDragon, out var asd))
            {
                var prog = CardTextEffectCompiler.Compile(asd);
                Check("Alligator's Sword Dragon: \"… directly if the only face-up monsters they control are …\" " +
                      "is not an unconditional direct attack",
                    prog == null || !prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.CanAttackDirectly),
                    Describe(prog));
            }

            var plain = CompileText(990023, "Effect Monster", "This card can attack your opponent directly.");
            Check("\"This card can attack your opponent directly.\" still compiles to CanAttackDirectly",
                plain != null && plain.FullyCompiled &&
                plain.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.CanAttackDirectly),
                Describe(plain));
        }

        /// <summary>"Once per turn, during your Main Phase 1: You can pay 800 Life Points; …" — not in Main Phase 2.</summary>
        static void GearGolemMainPhase1(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var def = db.Get(GearGolem);
            var prog = CardTextEffectCompiler.Compile(def);
            Check("Gear Golem: FullyCompiled ignition limited to Main Phase 1",
                prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.GrantDirectAttackThisTurn &&
                                            c.OnlyDuringMainPhase1 && c.OncePerTurn && c.PayLpAmount == 800),
                Describe(prog));

            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            var p = e.Player;
            p.LifePoints = 8000;
            var golem = PlaceMonster(e, p, GearGolem, 2, BattlePosition.Attack, true);
            var main1 = e.Phase == DuelPhase.Main1 && e.CanActivateSpellTrap(p, golem, fromHand: false);
            var inMain2 = e.TryEnterMainPhase2(p);
            var main2 = e.CanActivateSpellTrap(p, golem, fromHand: false);
            Check("Gear Golem: can activate in Main Phase 1, not in Main Phase 2",
                main1 && inMain2 && e.Phase == DuelPhase.Main2 && !main2 && p.LifePoints == 8000,
                $"main1={main1} inMain2={inMain2} phase={e.Phase} main2={main2} LP={p.LifePoints}");
        }
    }
}
