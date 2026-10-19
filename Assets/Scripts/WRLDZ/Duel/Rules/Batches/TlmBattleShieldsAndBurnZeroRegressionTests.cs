using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TlmBattleShieldsAndBurnZero batch: Monk Fighter, Hieracosphinx, Grave Ohja, Des Wombat and Kozaky's Self-Destruct
    /// Button. Compile shapes, near misses, and mini-duels through the engine entry points (TryAttack, LegalIntentService,
    /// TryFlipSummon, TryActivateSpellTrap with Hinotama / MST / Heavy Storm, the AI's Barrel Behind the Door and turn).
    /// Runtime: <see cref="TlmBattleShieldsAndBurnZeroRuntime"/>.
    /// </summary>
    public static class TlmBattleShieldsAndBurnZeroRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int MonkFighter = 3810071;        // Rock Lv3 1300/1000
        const int Hieracosphinx = 82260502;     // Rock Lv6 2400/1200
        const int GraveOhja = 40937767;         // Rock Lv4 1600/1500
        const int DesWombat = 9637706;          // Beast Lv3 1600/300
        const int Kozaky = 21908319;            // Normal Trap (cannot be activated)

        const int Celtic = 91152256;            // Warrior Normal Lv4 1400/1200
        const int AxeRaider = 48305365;         // Warrior Normal Lv4 1700/1150
        const int DarkBlade = 11321183;         // Warrior Normal Lv4 1800/1500
        const int KingFog = 84686841;           // Fiend Normal Lv3 1000/900
        const int BasicInsect = 89091579;       // Insect Normal Lv2 500/700
        const int Hinotama = 46130346;          // Normal Spell: 500 damage to the opponent
        const int Mst = 5318639;
        const int HeavyStorm = 19613556;
        const int Barrel = 78783370;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("TlmBattleShieldsAndBurnZero: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(Check);
            MonkFighterDuels(db, pDeck, aDeck, Check);
            HieracosphinxDuels(db, pDeck, aDeck, Check);
            GraveOhjaDuels(db, pDeck, aDeck, Check);
            DesWombatDuels(db, pDeck, aDeck, Check);
            KozakyDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, System.Collections.Generic.List<string>>
            s_logs = new();

        static DuelEngine Watch(DuelEngine e)
        {
            var lines = s_logs.GetValue(e, _ => new System.Collections.Generic.List<string>());
            e.OnLog += lines.Add;
            return e;
        }

        static string State(DuelEngine e)
        {
            var lines = s_logs.TryGetValue(e, out var l) ? l : new System.Collections.Generic.List<string>();
            return $"pLP={e.Player.LifePoints} oLP={e.Opponent.LifePoints} chain={e.Chain.Count} " +
                   $"resp={e.PendingResponse?.Timing} pend={(e.PendingActivation != null)} " +
                   $"log={string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 8)))}";
        }

        /// <summary>The Player's Main Phase 1 of turn 3+, empty boards and hands.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
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
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>The AI's Main Phase (turn 2+), empty boards and hands.</summary>
        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Watch(Fresh(db, pDeck, aDeck));
            reached = ReachOpponentMain(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.ClearPendingResponse();
            return engine;
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
            engine.NotifyPublic();
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The AI's Battle Phase: <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static bool Shielded(DuelEngine e, CardInstance m)
        {
            e.NotifyPublic();
            return ContinuousProtections.CannotBeAttackTarget(e, m);
        }

        static CardInstance FaceDown(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        static CardInstance FaceUp(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static CardInstance FaceUpTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        static bool Offers(DuelEngine e, DuelistState who, LegalIntentService.LegalKind kind, CardInstance target) =>
            LegalIntentService.Build(e, who).Entries.Any(x => x.Kind == kind && (target == null || x.Target == target));

        static bool DestroyWithMst(DuelEngine e, DuelistState who, CardInstance target)
        {
            var mst = PutInHand(e, who, Mst);
            var act = e.TryActivateSpellTrap(who, mst, fromHand: true);
            if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(target))
                e.TrySelectEffectTarget(target);
            Settle(e);
            return act;
        }

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} clauses=[{string.Join("; ", prog.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram Compile(int id, string name, string type, string frame, string desc) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id,
                name = name,
                type = type,
                frameType = frame,
                race = "Rock",
                attribute = "EARTH",
                level = 4,
                atk = 1500,
                def = 1500,
                desc = desc
            });

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(MonkFighter));
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Monk Fighter: one continuous 'no battle damage to its controller from its own battles' clause",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.PreventControllerBattleDamage && c.OnlyBattlesInvolvingThis &&
                    !c.MakesChainLink && CardEffectStatus.Classify(db.Get(MonkFighter)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Hieracosphinx));
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Hieracosphinx: one continuous face-down Defense attack-target shield (not Feint Plan's lock)",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.OpponentCannotSelectYourFaceDownDefenseMonstersAsAttackTarget &&
                    !prog.ClauseList.Exists(x => x.Action == EffectActionKind.CannotAttackFaceDownMonstersThisTurn) &&
                    CardEffectStatus.Classify(db.Get(Hieracosphinx)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(GraveOhja));
                var shield = prog?.ClauseList.Find(x => x.Action == EffectActionKind.CannotBeAttackTarget);
                var burn = prog?.ClauseList.Find(x =>
                    x.Action == EffectActionKind.InflictDamageToOpponentEachTimeYourMonsterFlipSummoned);
                Check("Grave Ohja: a conditional self shield (face-down Defense monster) and a 300 Flip Summon burn",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 2 && shield != null && burn != null &&
                    shield.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    shield.WhileCondition == TlmBattleShieldsAndBurnZeroRuntime.YouControlFaceDownDefense &&
                    !shield.ExceptThisCard && !shield.AllowsDirectAttackWhileProtected &&
                    burn.Timing == EffectTiming.ContinuousWhileFaceUp && burn.Amount == 300 &&
                    CardEffectStatus.Classify(db.Get(GraveOhja)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(DesWombat));
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Des Wombat: one continuous 'effect damage to you becomes 0' clause",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.EffectDamageToControllerBecomesZero &&
                    CardEffectStatus.Classify(db.Get(DesWombat)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Kozaky));
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Kozaky's Self-Destruct Button: one mandatory 'destroyed while Set' GY trigger, 1000 to the destroyer, " +
                      "no Activate clause",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.SentFromFieldToGy &&
                    c.Action == EffectActionKind.InflictDamageToPlayerWhoDestroyedThisSetCard && c.Amount == 1000 &&
                    c.RequiresDestroyedWhileFaceDown && !c.IsOptional && !prog.HasTiming(EffectTiming.Activate) &&
                    CardEffectStatus.Classify(db.Get(Kozaky)) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
        }

        static void NearMisses(Chk Check)
        {
            var misses = new (string what, string type, string frame, string desc)[]
            {
                ("Monk Fighter: another monster's battles", "Effect Monster", "effect",
                    "Battle Damage to this card's controller from a battle involving a Rock monster becomes 0."),
                ("Hieracosphinx: face-up Defense", "Effect Monster", "effect",
                    "Your opponent cannot select a face-up Defense Position monster as an attack target."),
                ("Grave Ohja: face-up Defense condition", "Effect Monster", "effect",
                    "While there is a face-up Defense Position monster(s) on your side of the field, no player can select " +
                    "this card as an attack target."),
                ("Grave Ohja: Normal Summon burn", "Effect Monster", "effect",
                    "Each time a monster on your side of the field is Normal Summoned, inflict 300 damage to your opponent."),
                ("Des Wombat: your opponent's damage", "Effect Monster", "effect",
                    "Any damage to your opponent from a card effect becomes 0."),
                ("Kozaky's: not 'this Set card'", "Trap Card", "trap",
                    "Inflict 1000 damage to the player who destroyed this card while it was face-up."),
            };
            for (var i = 0; i < misses.Length; i++)
            {
                var (what, type, frame, desc) = misses[i];
                var prog = Compile(990211000 + i, "Near miss " + what, type, frame, desc);
                Check("near miss stays not FullyCompiled — " + what,
                    prog == null || !prog.FullyCompiled || prog.ClauseList.Count == 0, Describe(prog));
            }
        }

        // ───────────────────────────── Monk Fighter ─────────────────────────────

        static void MonkFighterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacked by Axe Raider (1700 vs 1300): Monk Fighter is destroyed, its controller takes nothing.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var monk = FaceUp(e, p, MonkFighter, 0);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var lp = p.LifePoints;
                var ok = OpponentAttacks(e, axe, monk);
                Check("Monk Fighter: attacked by a stronger monster — destroyed, no battle damage to its controller",
                    reached && ok && p.Graveyard.Contains(monk) && p.LifePoints == lp, $"ok={ok} {State(e)}");
            }
            {
                // Its controller's other monster battles: the damage is taken (only battles involving this card).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, MonkFighter, 0);
                var celtic = FaceUp(e, p, Celtic, 1);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var lp = p.LifePoints;
                var ok = OpponentAttacks(e, axe, celtic);
                Check("Monk Fighter: another monster of yours battling — you take the 300",
                    reached && ok && p.Graveyard.Contains(celtic) && p.LifePoints == lp - 300, $"ok={ok} {State(e)}");
            }
            {
                // Monk Fighter attacks Dark Blade (1800): no damage to you. A second one attacks King Fog (1000): the
                // opponent still takes 300.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var monk = FaceUp(e, p, MonkFighter, 0);
                var monk2 = FaceUp(e, p, MonkFighter, 1);
                var blade = FaceUp(e, o, DarkBlade, 0);
                var fog = FaceUp(e, o, KingFog, 1);
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok1 = AttackAndDrain(e, p, monk, blade);
                var none = p.LifePoints == lp && p.Graveyard.Contains(monk);
                var ok2 = AttackAndDrain(e, p, monk2, fog);
                Check("Monk Fighter: attacking a stronger monster costs no LP; the opponent still takes damage from it",
                    reached && ok1 && none && ok2 && o.LifePoints == oLp - 300 && o.Graveyard.Contains(fog),
                    $"ok1={ok1} none={none} ok2={ok2} {State(e)}");
            }
            {
                // Negated or face-down, it does not apply.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var monk = FaceUp(e, p, MonkFighter, 0);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var live = FieldSpellEffects.ControllerAvoidsBattleDamage(e, p, axe, monk);
                var oppNot = !FieldSpellEffects.ControllerAvoidsBattleDamage(e, o, axe, monk);
                monk.IsNegated = true;
                var negated = !FieldSpellEffects.ControllerAvoidsBattleDamage(e, p, axe, monk);
                monk.IsNegated = false;
                monk.FaceUp = false;
                var down = !FieldSpellEffects.ControllerAvoidsBattleDamage(e, p, axe, monk);
                Check("Monk Fighter: only its controller, only while face-up and not negated",
                    reached && live && oppNot && negated && down,
                    $"live={live} oppNot={oppNot} negated={negated} down={down}");
            }
        }

        // ───────────────────────────── Hieracosphinx ─────────────────────────────

        static void HieracosphinxDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var hiera = FaceUp(e, p, Hieracosphinx, 0);
                var set = FaceDown(e, p, Celtic, 1);
                var insect = FaceUp(e, p, BasicInsect, 2);
                var oSet = FaceDown(e, o, KingFog, 0);
                var on = Shielded(e, set) && !Shielded(e, insect) && !Shielded(e, hiera);
                var oppOpen = !Shielded(e, oSet);
                hiera.IsNegated = true;
                var negOpen = !Shielded(e, set);
                hiera.IsNegated = false;
                hiera.FaceUp = false;
                hiera.Position = BattlePosition.Defense;
                var downOpen = !Shielded(e, set) && !Shielded(e, hiera);
                Check("Hieracosphinx: its controller's face-down Defense monsters only, while face-up and not negated",
                    reached && on && oppOpen && negOpen && downOpen,
                    $"on={on} oppOpen={oppOpen} neg={negOpen} down={downOpen}");
            }
            {
                // The AI cannot attack the Set monster (TryAttack, LegalIntentService), can attack the face-up ones, and
                // cannot attack directly.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var hiera = FaceUp(e, p, Hieracosphinx, 0);
                var set = FaceDown(e, p, Celtic, 1);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var offersSet = Offers(e, o, LegalIntentService.LegalKind.Attack, set);
                var offersHiera = Offers(e, o, LegalIntentService.LegalKind.Attack, hiera);
                var offersDirect = Offers(e, o, LegalIntentService.LegalKind.DirectAttack, null);
                var direct = e.CanAttackDirectly(o, axe);
                var refused = !e.TryAttack(o, axe, set);
                Check("Hieracosphinx: the opponent cannot attack the Set monster nor attack directly; Hieracosphinx yes",
                    reached && battle && !offersSet && offersHiera && !offersDirect && !direct && refused && OnField(p, set),
                    $"battle={battle} set={offersSet} hiera={offersHiera} direct={offersDirect}/{direct} refused={refused}");
            }
            {
                // The AI's turn with only a weaker attacker: no attack on the Set monster, the turn ends (no soft-lock).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, Hieracosphinx, 0);
                var set = FaceDown(e, p, BasicInsect, 1);
                FaceUp(e, o, AxeRaider, 0);
                var ended = e.TryEndTurnSafe(p);
                Settle(e);
                var aiTurn = e.TurnPlayer == o;
                o.Hand.Clear();
                var lp = p.LifePoints;
                var done = RunAiTurn(e, 400);
                Check("Hieracosphinx: the AI leaves the Set monster alone and its turn ends",
                    reached && ended && aiTurn && done && OnField(p, set) && !set.FaceUp && p.LifePoints == lp &&
                    e.TurnPlayer == p,
                    $"ended={ended} ai={aiTurn} done={done} {State(e)}");
            }
        }

        // ───────────────────────────── Grave Ohja ─────────────────────────────

        static void GraveOhjaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var ohja = FaceUp(e, p, GraveOhja, 0);
                var alone = !Shielded(e, ohja);
                var set = FaceDown(e, p, Celtic, 1);
                var on = Shielded(e, ohja);
                set.FaceUp = true;
                set.Position = BattlePosition.Attack;
                var upOpen = !Shielded(e, ohja);
                set.FaceUp = false;
                set.Position = BattlePosition.Defense;
                ohja.IsNegated = true;
                var negOpen = !Shielded(e, ohja);
                ohja.IsNegated = false;
                Check("Grave Ohja: cannot be attacked only while you control a face-down Defense Position monster",
                    reached && alone && on && upOpen && negOpen && !Shielded(e, set),
                    $"alone={alone} on={on} upOpen={upOpen} neg={negOpen}");
            }
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ohja = FaceUp(e, p, GraveOhja, 0);
                var set = FaceDown(e, p, Celtic, 1);
                var axe = FaceUp(e, o, AxeRaider, 0);
                var battle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var offersOhja = Offers(e, o, LegalIntentService.LegalKind.Attack, ohja);
                var offersSet = Offers(e, o, LegalIntentService.LegalKind.Attack, set);
                var direct = e.CanAttackDirectly(o, axe);
                var refused = !e.TryAttack(o, axe, ohja);
                Check("Grave Ohja: the opponent must attack the Set monster (not Grave Ohja, not directly)",
                    reached && battle && !offersOhja && offersSet && !direct && refused && OnField(p, ohja),
                    $"battle={battle} ohja={offersOhja} set={offersSet} direct={direct} refused={refused}");
            }
            {
                // Two Flip Summons by its controller: 300 each to the opponent.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, GraveOhja, 0);
                var a = FaceDown(e, p, Celtic, 1);
                var b = FaceDown(e, p, KingFog, 2);
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                var f1 = e.TryFlipSummon(p, a);
                Settle(e);
                var once = o.LifePoints == oLp - 300;
                var f2 = e.TryFlipSummon(p, b);
                Settle(e);
                Check("Grave Ohja: each Flip Summon of your monster inflicts 300 to the opponent",
                    reached && f1 && once && f2 && o.LifePoints == oLp - 600 && p.LifePoints == pLp && !e.Chain.HasLinks,
                    $"f1={f1} once={once} f2={f2} {State(e)}");
            }
            {
                // Grave Ohja's own Flip Summon, and the opponent's Flip Summon: no damage.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ohja = FaceDown(e, p, GraveOhja, 0);
                var oLp = o.LifePoints;
                var self = e.TryFlipSummon(p, ohja);
                Settle(e);
                var selfNone = o.LifePoints == oLp && ohja.FaceUp;
                var e2 = OpponentMain(db, pDeck, aDeck, out var reached2);
                FaceUp(e2, e2.Player, GraveOhja, 0);
                var oSet = FaceDown(e2, e2.Opponent, Celtic, 0);
                var lp2 = e2.Player.LifePoints;
                var olp2 = e2.Opponent.LifePoints;
                var oppFlip = e2.TryFlipSummon(e2.Opponent, oSet);
                Settle(e2);
                Check("Grave Ohja: not for its own Flip Summon, nor the opponent's",
                    reached && reached2 && self && selfNone && oppFlip && e2.Player.LifePoints == lp2 &&
                    e2.Opponent.LifePoints == olp2,
                    $"self={self} selfNone={selfNone} oppFlip={oppFlip} {State(e)} || {State(e2)}");
            }
            {
                // The AI's Set Barrel Behind the Door answers the trigger link: the 300 goes to the player.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, GraveOhja, 0);
                var set = FaceDown(e, p, Celtic, 1);
                var barrel = PlaceSetTrap(e, o, Barrel, 0);
                var oLp = o.LifePoints;
                var pLp = p.LifePoints;
                var flip = e.TryFlipSummon(p, set);
                Settle(e);
                Check("Grave Ohja: its trigger is a Chain Link that would damage the opponent — Barrel reflects the 300",
                    reached && flip && o.Graveyard.Contains(barrel) && p.LifePoints == pLp - 300 && o.LifePoints == oLp &&
                    !e.Chain.HasLinks,
                    $"flip={flip} barrelGy={o.Graveyard.Contains(barrel)} {State(e)}");
            }
            {
                // Negated: no trigger.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var ohja = FaceUp(e, p, GraveOhja, 0);
                var set = FaceDown(e, p, Celtic, 1);
                ohja.IsNegated = true;
                var oLp = o.LifePoints;
                TlmBattleShieldsAndBurnZeroRuntime.OnFlipSummoned(e, p, set);
                e.NotifyPublic();
                Settle(e);
                Check("Grave Ohja: a negated one does not trigger", reached && o.LifePoints == oLp, State(e));
            }
        }

        // ───────────────────────────── Des Wombat ─────────────────────────────

        static void DesWombatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's face-up Des Wombat: the player's Hinotama does nothing; battle damage still applies.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wombat = FaceUp(e, o, DesWombat, 0);
                var axe = FaceUp(e, p, AxeRaider, 0);
                var oLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, PutInHand(e, p, Hinotama), fromHand: true);
                Settle(e);
                var zero = o.LifePoints == oLp;
                var hit = AttackAndDrain(e, p, axe, wombat);
                Check("Des Wombat: effect damage to its controller becomes 0; battle damage is still taken (100)",
                    reached && act && zero && hit && o.LifePoints == oLp - 100 && o.Graveyard.Contains(wombat),
                    $"act={act} zero={zero} hit={hit} {State(e)}");
            }
            {
                // Face-down, it does not apply; the player's own Des Wombat does not stop damage to the opponent.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceDown(e, o, DesWombat, 0);
                FaceUp(e, p, DesWombat, 0);
                var oLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, PutInHand(e, p, Hinotama), fromHand: true);
                Settle(e);
                Check("Des Wombat: face-down it does not apply; it only covers its controller",
                    reached && act && o.LifePoints == oLp - 500, $"act={act} {State(e)}");
            }
            {
                // Grave Ohja's burn into the opponent's Des Wombat: 0.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, GraveOhja, 0);
                var set = FaceDown(e, p, Celtic, 1);
                FaceUp(e, o, DesWombat, 0);
                var oLp = o.LifePoints;
                var flip = e.TryFlipSummon(p, set);
                Settle(e);
                Check("Des Wombat: Grave Ohja's 300 becomes 0", reached && flip && o.LifePoints == oLp, State(e));
            }
        }

        // ───────────────────────────── Kozaky's Self-Destruct Button ─────────────────────────────

        static void KozakyDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI's Set Kozaky's, destroyed by the player's MST: the player takes 1000.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var koz = PlaceSetTrap(e, o, Kozaky, 2);
                var cant = !e.CanActivateSpellTrap(o, koz, fromHand: false);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = DestroyWithMst(e, p, koz);
                Check("Kozaky's Self-Destruct Button: cannot be activated; destroyed while Set by the player's MST — " +
                      "the player takes 1000",
                    reached && cant && act && o.Graveyard.Contains(koz) && p.LifePoints == pLp - 1000 &&
                    o.LifePoints == oLp && !e.Chain.HasLinks,
                    $"cant={cant} act={act} {State(e)}");
            }
            {
                // The player's own Heavy Storm destroys both players' Set copies: the player takes 2000.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceSetTrap(e, p, Kozaky, 0);
                var theirs = PlaceSetTrap(e, o, Kozaky, 0);
                var cant = !e.CanActivateSpellTrap(p, mine, fromHand: false);
                var pLp = p.LifePoints;
                var oLp = o.LifePoints;
                var act = e.TryActivateSpellTrap(p, PutInHand(e, p, HeavyStorm), fromHand: true);
                Settle(e);
                Check("Kozaky's Self-Destruct Button: the destroyer takes the damage, its own controller too (2000)",
                    reached && cant && act && p.Graveyard.Contains(mine) && o.Graveyard.Contains(theirs) &&
                    p.LifePoints == pLp - 2000 && o.LifePoints == oLp,
                    $"cant={cant} act={act} {State(e)}");
            }
            {
                // Destroyed face-up, or sent to the GY without being destroyed: no damage.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var up = FaceUpTrap(e, o, Kozaky, 0);
                var sent = PlaceSetTrap(e, o, Kozaky, 1);
                var pLp = p.LifePoints;
                var act = DestroyWithMst(e, p, up);
                e.SendCardToGrave(o, sent);
                Settle(e);
                Check("Kozaky's Self-Destruct Button: destroyed face-up, or sent (not destroyed) while Set — no damage",
                    reached && act && o.Graveyard.Contains(up) && o.Graveyard.Contains(sent) && p.LifePoints == pLp,
                    $"act={act} {State(e)}");
            }
            {
                // The destroyer controls Des Wombat: 0.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUp(e, p, DesWombat, 0);
                var koz = PlaceSetTrap(e, o, Kozaky, 2);
                var pLp = p.LifePoints;
                var act = DestroyWithMst(e, p, koz);
                Check("Kozaky's Self-Destruct Button vs a destroyer with Des Wombat: 0 damage",
                    reached && act && o.Graveyard.Contains(koz) && p.LifePoints == pLp, $"act={act} {State(e)}");
            }
        }
    }
}
