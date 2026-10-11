using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// NegationMonsters batch (v161): Freed the Matchless General, Frontier Wiseman, Dark Ruler Ha Des, Twin-Headed Wolf,
    /// Lesser Fiend and Double Snare; Tyrant Dragon's second attack and its Trap target negation sentence (the card itself
    /// stays a Stub: its "unless you Tribute 1 Dragon monster" GY summon cost is not implemented, so its Trap clause does
    /// not run; the sentence is driven on a FullyCompiled synthetic card). Compile shapes, mini-duels through the
    /// real entry points (TryActivateSpellTrap and the Chain, TryAttack and the Damage Step, EndTurn and the next Draw
    /// Phase) and near misses. Runtime: <see cref="NegationMonstersRuntime"/>.
    /// </summary>
    public static class NegationMonstersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Freed = 49681811;             // Warrior Lv5 2300/1700
        const int Wiseman = 38742075;           // Spellcaster Lv3 1600/800
        const int Tyrant = 94568601;            // Dragon Lv8 2900/2500 (Stub)
        const int DoubleSnare = 3682106;
        const int HaDes = 53982768;             // Fiend Lv6 2450/1600
        const int Wolf = 88132637;              // Fiend Lv4 1500/1000
        const int LesserFiend = 16475472;       // Fiend Lv5 2100/1000
        const int FiendSkullDragon = 66235877;  // Fusion: "Negate any Trap effects that target this card …"

        const int RushRecklessly = 70046172;    // Quick-Play: target 1 face-up monster; +700 ATK (recorded Chain Link)
        const int BookOfMoon = 14087893;        // Quick-Play: target 1 face-up monster; face-down Defense
        const int SpellbindingCircle = 18807109; // Continuous Trap: target 1 monster your opponent controls
        const int Jinzo = 77585513;
        const int RoyalDecree = 51452091;
        const int DragonsBead = 92408984;
        const int CallOfTheHaunted = 97077563;  // Continuous Trap: target 1 monster in your GY; Special Summon it

        const int Celtic = 91152256;            // Warrior Lv4 1400/1200
        const int AxeRaider = 48305365;         // Warrior Lv4 1700/1150
        const int SkullServant = 32274490;      // Zombie Lv1 300/200
        const int RyuKishin = 15303296;         // Fiend Lv3 1000/500
        const int ArchfiendSoldier = 49881766;  // Fiend Lv4 1900/1500
        const int Witch = 78010363;             // "If this card is sent from the field to the GY: Add 1 monster …"
        const int MysticTomato = 83011278;      // "When this card is destroyed by battle and sent to the GY: …"
        const int FoxFire = 88753985;           // GY End Phase self-revival when destroyed by battle
        const int ManEaterBug = 54652250;       // FLIP: destroy 1 monster
        const int DarkMagician = 46986414;      // Spellcaster Lv7

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("NegationMonsters: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            TargetNegationDuels(db, pDeck, aDeck, Check);
            FreedDrawDuels(db, pDeck, aDeck, Check);
            TyrantAttackDuels(db, pDeck, aDeck, Check);
            BattleNegationDuels(db, pDeck, aDeck, Check);
            LesserFiendDuels(db, pDeck, aDeck, Check);
            DoubleSnareDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

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
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
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

        /// <summary>Activate <paramref name="card"/> and, when a target prompt opens, pick <paramref name="target"/>.</summary>
        static bool Activate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, CardInstance target)
        {
            var ok = engine.TryActivateSpellTrap(who, card, fromHand);
            if (ok && engine.IsAwaitingEffectTarget && target != null)
                ok = engine.TrySelectEffectTarget(target);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            return ok;
        }

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} race={c.RaceFilter} spell={c.NegateRespondsToSpell} " +
                    $"trap={c.NegateRespondsToTrap} self={c.AffectsThisCardOnly} amt={c.Amount}")) + "]";

        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        /// <summary>
        /// A synthetic Effect Monster that prints only Tyrant Dragon's Trap sentence, so its program is FullyCompiled: no
        /// real card with that sentence was (Tyrant Dragon was a Stub until v163, Fiend Skull Dragon has an uncompiled Flip sentence).
        /// </summary>
        const int TrapWardenId = 90538002;

        static readonly CardDef TrapWardenDef = new()
        {
            id = TrapWardenId, name = "Synthetic Trap warden", type = "Effect Monster", frameType = "effect",
            race = "Dragon", attribute = "DARK", level = 4, atk = 1500, def = 1200,
            desc = "Negate any Trap effects that target this card on the field, and if you do, destroy that Trap."
        };

        static CardInstance Warden(CardInstance c)
        {
            c.CardId = TrapWardenId;
            c.Def = TrapWardenDef;
            return c;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => CardTextEffectCompiler.Compile(db.Get(id));
            bool Is(int id, CardEffectStatusKind k) => CardEffectStatus.Classify(db.Get(id)) == k;

            var freed = P(Freed);
            Check("Freed: FullyCompiled — Spell target negation of this card + Draw Phase search instead of the draw",
                freed != null && freed.FullyCompiled && freed.ClauseList.Count == 2 &&
                freed.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                                             c.Timing == EffectTiming.ContinuousWhileFaceUp && c.NegateRespondsToSpell &&
                                             !c.NegateRespondsToTrap && c.AffectsThisCardOnly && c.DestroyNegatedCard) &&
                freed.ClauseList.Exists(c => c.Action == EffectActionKind.AddFromDeckInsteadOfNormalDraw &&
                                             c.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq && c.Amount == 4 &&
                                             c.RaceFilter == "Warrior" && c.IsOptional) &&
                Is(Freed, CardEffectStatusKind.Implemented),
                Describe(freed));

            var wise = P(Wiseman);
            Check("Frontier Wiseman: Spell effects that target a face-up Warrior you control",
                wise != null && wise.FullyCompiled && wise.ClauseList.Count == 1 &&
                wise.ClauseList[0].Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                wise.ClauseList[0].NegateRespondsToSpell && !wise.ClauseList[0].AffectsThisCardOnly &&
                wise.ClauseList[0].RaceFilter == "Warrior" && wise.ClauseList[0].RequiresTargetFaceUp &&
                Is(Wiseman, CardEffectStatusKind.Implemented),
                Describe(wise));

            var tyrant = P(Tyrant);
            // Rewritten for the SummonPromptsAndCosts batch (v163): the GY summon cost compiles, so the card is Implemented.
            Check("Tyrant Dragon: Trap target negation + conditional second attack + the Tribute GY summon cost (v163)",
                tyrant != null && tyrant.FullyCompiled &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.GySpecialSummonNeedsTributeCost &&
                                              c.TributeRaceFilter == "Dragon" && c.Amount == 1) &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                                              c.NegateRespondsToTrap && !c.NegateRespondsToSpell && c.AffectsThisCardOnly) &&
                tyrant.ClauseList.Exists(c => c.Action == EffectActionKind.ExtraAttacks && c.Amount == 1 &&
                                              c.ActivationCondition == NegationMonstersRuntime.OpponentControlsMonster) &&
                Is(Tyrant, CardEffectStatusKind.Implemented),
                Describe(tyrant) + " status=" + CardEffectStatus.Classify(db.Get(Tyrant)));

            var snare = P(DoubleSnare);
            Check("Double Snare: Activate → Destroy a targeted face-up card that negates Trap effects",
                snare != null && snare.FullyCompiled && snare.ClauseList.Count == 1 &&
                snare.ClauseList[0].Timing == EffectTiming.Activate &&
                snare.ClauseList[0].Action == EffectActionKind.Destroy &&
                snare.ClauseList[0].Zone == EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects &&
                snare.ClauseList[0].RequiresTargetChoice && !snare.ClauseList[0].ChoiceDoesNotTarget &&
                snare.ClauseList[0].IsPsctTarget &&
                snare.ClauseList[0].RequiresTargetFaceUp && Is(DoubleSnare, CardEffectStatusKind.Implemented),
                Describe(snare));

            var hades = P(HaDes);
            Check("Dark Ruler Ha Des: battle negation for Fiends you control (the GY summon restriction is text)",
                hades != null && hades.FullyCompiled && hades.ClauseList.Count == 1 &&
                hades.ClauseList[0].Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                hades.ClauseList[0].RaceFilter == "Fiend" && !hades.ClauseList[0].AffectsThisCardOnly &&
                PsctGrammar.CannotBeSpecialSummonedFromGraveyard(db.Get(HaDes).desc) &&
                Is(HaDes, CardEffectStatusKind.Implemented),
                Describe(hades));

            var wolf = P(Wolf);
            Check("Twin-Headed Wolf: Flip monsters it destroys by battle, while you control another Fiend",
                wolf != null && wolf.FullyCompiled && wolf.ClauseList.Count == 1 &&
                wolf.ClauseList[0].Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                wolf.ClauseList[0].AffectsThisCardOnly && wolf.ClauseList[0].TargetCardKind == "Flip" &&
                wolf.ClauseList[0].CountRaceFilter == "Fiend" && wolf.ClauseList[0].ExceptThisCard &&
                Is(Wolf, CardEffectStatusKind.Implemented),
                Describe(wolf));

            var lesser = P(LesserFiend);
            Check("Lesser Fiend: Archfiend name + banish what it destroys by battle",
                lesser != null && lesser.FullyCompiled && lesser.ClauseList.Count == 2 &&
                lesser.ClauseList.Exists(c => c.Action == EffectActionKind.AlwaysTreatedAsName) &&
                lesser.ClauseList.Exists(c => c.Action == EffectActionKind.BanishMonstersDestroyedByBattleWithThis) &&
                Is(LesserFiend, CardEffectStatusKind.Implemented),
                Describe(lesser));

            // Near misses: look-alike sentences that must not compile to these templates.
            var nearIds = new[]
            {
                1995985,  // Silent Swordsman LV3: "Negate your opponent's Spell effects that target this card."
                42868711, // Gora Turtle of Illusion: "… controlled by your opponent that target this face-up card …"
                39507162, // Blade Knight: "If you control no other monsters, the effects of Flip monsters … are negated."
                73544866, // Guardian Baou: "Negate the effects of monsters destroyed by battle with this card."
            };
            var leaked = nearIds.Where(id =>
            {
                var p = P(id);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c =>
                    c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy ||
                    c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle));
            }).ToList();
            Check("Near misses: look-alike negation sentences stay uncompiled", leaked.Count == 0,
                string.Join(",", leaked));

            // Rod of Silence - Kay'est ("Negate other Spell effects that target the equipped monster …") is compiled whole by
            // DcrEquipSpellsAtoms with an equip-host scope; this batch's monster-scope atom must still not produce it.
            var rod = P(95515060);
            Check("Near miss: Rod of Silence - Kay'est is not this batch's monster-scope target negation",
                rod == null || !rod.ClauseList.Exists(c =>
                    (c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy && !c.AppliesToEquippedHost) ||
                    c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle),
                rod == null ? "null" : string.Join(" | ", rod.ClauseList.Select(c => $"{c.Action}/equipHost={c.AppliesToEquippedHost}")));

            var mismatch = new CardDef
            {
                id = 90538001, name = "Synthetic family mismatch", type = "Effect Monster", race = "Warrior",
                desc = "Negate any Spell effects that target this card, and if you do, destroy that Trap Card."
            };
            var mm = CardTextEffectCompiler.Compile(mismatch);
            Check("Near miss: 'destroy that Trap Card' after negating Spell effects is refused",
                mm == null || (!mm.FullyCompiled &&
                               !mm.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy)),
                Describe(mm));

            var fsd = P(FiendSkullDragon);
            Check("Fiend Skull Dragon: its Trap target negation sentence compiles (Double Snare sees it); its Flip " +
                  "sentence does not, so the partial program never runs; the card stays Structural",
                fsd != null && !fsd.FullyCompiled &&
                fsd.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                                           c.NegateRespondsToTrap && c.AffectsThisCardOnly) &&
                Is(FiendSkullDragon, CardEffectStatusKind.Structural),
                Describe(fsd));

            var warden = CompiledEffectCache.GetOrCompile(TrapWardenDef);
            Check("Synthetic Trap warden (Tyrant Dragon's Trap sentence alone): FullyCompiled",
                warden != null && warden.FullyCompiled && warden.ClauseList.Count == 1 &&
                warden.ClauseList[0].Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                warden.ClauseList[0].NegateRespondsToTrap && warden.ClauseList[0].AffectsThisCardOnly,
                Describe(warden));
        }

        // ───────────────────────────── target negation ─────────────────────────────

        static void TargetNegationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Freed's controller targets it with their own Rush Recklessly (a recorded compiled Chain Link).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var freed = PlaceMonster(engine, p, Freed, 0, BattlePosition.Attack, true);
                var rr = PutInHand(engine, p, RushRecklessly);
                var ok = Activate(engine, p, rr, true, freed);
                Check("Freed: a Spell (its own controller's Rush Recklessly) that targets it is negated and destroyed",
                    reached && ok && InGy(p, rr) && freed.CurrentAtk == 2300 && p.TryFindMonster(freed, out _) &&
                    !engine.IsAwaitingEffectTarget,
                    $"ok={ok} rrGy={InGy(p, rr)} atk={freed.CurrentAtk}");
            }

            {
                // The opponent's Book of Moon (legacy target path) on Freed during the opponent's turn.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var freed = PlaceMonster(engine, p, Freed, 0, BattlePosition.Attack, true);
                EndTurn(engine);
                var oppMain = engine.TurnPlayer == o && engine.InMainPhase;
                o.Hand.Clear();
                var bom = PutInHand(engine, o, BookOfMoon);
                var ok = Activate(engine, o, bom, true, freed);
                Check("Freed: the opponent's Book of Moon that targets it is negated and destroyed — Freed stays face-up",
                    reached && oppMain && ok && InGy(o, bom) && freed.FaceUp && freed.Position == BattlePosition.Attack,
                    $"oppMain={oppMain} ok={ok} bomGy={InGy(o, bom)} faceUp={freed.FaceUp} pos={freed.Position}");
            }

            {
                // Freed negates Spell effects only: a Trap that targets it applies.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var freed = PlaceMonster(engine, o, Freed, 0, BattlePosition.Attack, true);
                var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                var ok = Activate(engine, p, circle, false, freed);
                Check("Freed: a Trap that targets it is not negated (Spellbinding Circle binds it)",
                    reached && ok && !InGy(p, circle) && p.TryFindSpellTrap(circle, out _) && circle.EquippedTo == freed,
                    $"ok={ok} circleGy={InGy(p, circle)} boundTo={circle.EquippedTo?.Name}");
            }

            {
                // Frontier Wiseman: a Spell that targets a face-up Warrior its controller controls.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, Wiseman, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                var bom = PutInHand(engine, p, BookOfMoon);
                var ok = Activate(engine, p, bom, true, celtic);
                Check("Frontier Wiseman: Book of Moon on your face-up Warrior is negated and destroyed",
                    reached && ok && InGy(p, bom) && celtic.FaceUp && celtic.Position == BattlePosition.Attack,
                    $"ok={ok} bomGy={InGy(p, bom)} celticFaceUp={celtic.FaceUp}");

                var bom2 = PutInHand(engine, p, BookOfMoon);
                var ok2 = Activate(engine, p, bom2, true, axe);
                Check("Frontier Wiseman: a Spell that targets the opponent's Warrior applies",
                    ok2 && !axe.FaceUp && axe.Position == BattlePosition.Defense && InGy(p, bom2),
                    $"ok={ok2} axeFaceUp={axe.FaceUp} pos={axe.Position}");
            }

            {
                // Frontier Wiseman protects Warriors only: Book of Moon on itself (a Spellcaster) applies.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var wise = PlaceMonster(engine, p, Wiseman, 0, BattlePosition.Attack, true);
                var bom = PutInHand(engine, p, BookOfMoon);
                var ok = Activate(engine, p, bom, true, wise);
                Check("Frontier Wiseman: a Spell that targets a non-Warrior (itself) applies",
                    reached && ok && !wise.FaceUp && wise.Position == BattlePosition.Defense,
                    $"ok={ok} faceUp={wise.FaceUp}");
            }

            {
                // A face-down Freed applies no effect: the Spell resolves.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Freed, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var rr = PutInHand(engine, p, RushRecklessly);
                var ok = Activate(engine, p, rr, true, celtic);
                Check("Freed: protects only itself — Rush Recklessly on another monster applies",
                    reached && ok && celtic.CurrentAtk == 2100,
                    $"ok={ok} atk={celtic.CurrentAtk}");
            }

            {
                // Tyrant Dragon's Trap sentence on a FullyCompiled program (the synthetic warden): a Trap that targets it is
                // negated and destroyed.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var warden = Warden(PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true));
                var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                var ok = Activate(engine, p, circle, false, warden);
                Check("Trap sentence: a Trap that targets it (Spellbinding Circle) is negated and destroyed",
                    reached && ok && InGy(p, circle) && circle.EquippedTo == null && !warden.Equips.Contains(circle),
                    $"ok={ok} circleGy={InGy(p, circle)} boundTo={circle.EquippedTo?.Name}");
            }

            {
                // Fail closed: Fiend Skull Dragon (Flip sentence uncompiled) is not FullyCompiled, so its compiled Trap
                // clause does not run — Spellbinding Circle binds it, as before this batch. (Tyrant Dragon is FullyCompiled
                // since the SummonPromptsAndCosts batch, v163: SummonPromptsAndCostsRegressionTests checks its Trap clause.)
                foreach (var (id, label) in new[] { (FiendSkullDragon, "Fiend Skull Dragon") })
                {
                    var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    var protector = PlaceMonster(engine, o, id, 0, BattlePosition.Attack, true);
                    var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                    var ok = Activate(engine, p, circle, false, protector);
                    FieldSpellEffects.RefreshBoard(engine);
                    Check($"{label}: not FullyCompiled — a Trap that targets it (Spellbinding Circle) applies",
                        reached && ok && circle.EquippedTo == protector && p.TryFindSpellTrap(circle, out _),
                        $"ok={ok} circleGy={InGy(p, circle)} boundTo={circle.EquippedTo?.Name}");
                }
            }

            {
                // A Set warden is bound (its effect does not apply face-down); flipped face-up, it destroys the Continuous
                // Trap that keeps targeting it (ygopro EFFECT_DISABLE + EFFECT_SELF_DESTROY on that card).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var warden = Warden(PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, false));
                var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                var ok = Activate(engine, p, circle, false, warden);
                var bound = circle.EquippedTo == warden && p.TryFindSpellTrap(circle, out _);
                warden.FaceUp = true;
                warden.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(engine);
                Check("Trap sentence: flipped face-up, it destroys the Continuous Trap that bound it while Set",
                    reached && ok && bound && InGy(p, circle) && o.TryFindMonster(warden, out _),
                    $"ok={ok} bound={bound} circleGy={InGy(p, circle)} wardenOnField={o.TryFindMonster(warden, out _)}");
            }

            {
                // Call of the Haunted revives a monster with the Trap sentence: CotH keeps targeting it on the field, so it
                // is negated and destroyed (EFFECT_DISABLE + EFFECT_SELF_DESTROY). Negated, it leaves without destroying
                // the monster (CotH's Lua checks IsDisabled() on EVENT_LEAVE_FIELD_P).
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var warden = Warden(engine.CreateCardInstance(Celtic));
                p.Graveyard.Add(warden);
                var coth = PlaceSetTrap(engine, p, CallOfTheHaunted, 0);
                var ok = Activate(engine, p, coth, false, warden);
                FieldSpellEffects.RefreshBoard(engine);
                var onField = p.TryFindMonster(warden, out _);
                Check("Trap sentence: Call of the Haunted that revived it is destroyed; the monster stays",
                    reached && ok && onField && InGy(p, coth),
                    $"ok={ok} onField={onField} cothOnField={p.TryFindSpellTrap(coth, out _)} cothGy={InGy(p, coth)}");
            }

            {
                // Fiend Skull Dragon has no SZONE disable / self-destroy effect (c66235877.lua) and its partial program does
                // not run: a Set copy bound by Spellbinding Circle and then flipped face-up stays bound.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var fsd = PlaceMonster(engine, o, FiendSkullDragon, 0, BattlePosition.Defense, false);
                var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                var ok = Activate(engine, p, circle, false, fsd);
                fsd.FaceUp = true;
                fsd.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(engine);
                Check("Fiend Skull Dragon: flipped face-up, the Continuous Trap that bound it while Set stays",
                    reached && ok && circle.EquippedTo == fsd && p.TryFindSpellTrap(circle, out _),
                    $"ok={ok} circleGy={InGy(p, circle)} boundTo={circle.EquippedTo?.Name}");
            }

            {
                // The Trap sentence negates Trap effects only: a Spell that targets it applies.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var warden = Warden(PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true));
                var rr = PutInHand(engine, p, RushRecklessly);
                var ok = Activate(engine, p, rr, true, warden);
                Check("Trap sentence: a Spell that targets it applies (+700 ATK)",
                    reached && ok && warden.CurrentAtk == 2200,
                    $"ok={ok} atk={warden.CurrentAtk}");
            }
        }

        // ───────────────────────────── Freed: search instead of the draw ─────────────────────────────

        static void FreedDrawDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human picks a Warrior from the Deck: it is added and the normal draw does not happen.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Freed, 0, BattlePosition.Attack, true);
                EndTurn(engine);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { SkullServant, Celtic, DarkMagician, AxeRaider, SkullServant });
                var hand0 = p.Hand.Count;
                EndTurn(engine);
                var pending = engine.PendingActivation;
                var held = engine.TurnPlayer == p && engine.Phase == DuelPhase.Draw && pending != null &&
                           p.Hand.Count == hand0;
                var ids = held ? pending.LegalTargets.Select(c => c.CardId).OrderBy(x => x).ToList() : new System.Collections.Generic.List<int>();
                var pick = held ? pending.LegalTargets.FirstOrDefault(c => c.CardId == Celtic) : null;
                var picked = pick != null && engine.TrySelectEffectTarget(pick);
                Check("Freed: your Draw Phase holds on a Deck pick of Level 4 or lower Warriors before the draw",
                    reached && held && ids.SequenceEqual(new[] { Celtic, AxeRaider }.OrderBy(x => x)),
                    $"held={held} turnP={engine.TurnPlayer == p} phase={engine.Phase} ids=[{string.Join(",", ids)}]");
                Check("Freed: the pick is added instead of the normal draw; the turn goes on to Main Phase 1",
                    picked && p.Hand.Count == hand0 + 1 && p.Hand.Last().CardId == Celtic &&
                    p.Deck.Count == 4 && p.Deck[0] == SkullServant && engine.Phase == DuelPhase.Main1 &&
                    !engine.IsAwaitingEffectTarget,
                    $"picked={picked} hand={hand0}->{p.Hand.Count} last={p.Hand.LastOrDefault()?.Name} deck={p.Deck.Count} phase={engine.Phase}");
            }

            {
                // CANCEL: the effect is not activated — the normal draw happens.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Freed, 0, BattlePosition.Attack, true);
                EndTurn(engine);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { SkullServant, Celtic, SkullServant });
                var hand0 = p.Hand.Count;
                EndTurn(engine);
                var held = engine.Phase == DuelPhase.Draw && engine.IsAwaitingEffectTarget;
                engine.CancelEffectTargeting();
                Check("Freed: CANCEL draws normally",
                    reached && held && p.Hand.Count == hand0 + 1 && p.Hand.Last().CardId == SkullServant &&
                    p.Deck.Count == 2 && engine.Phase == DuelPhase.Main1 && !engine.IsAwaitingEffectTarget,
                    $"held={held} hand={hand0}->{p.Hand.Count} last={p.Hand.LastOrDefault()?.Name} phase={engine.Phase}");
            }

            {
                // The AI searches during its own Draw Phase instead of drawing.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                PlaceMonster(engine, o, Freed, 0, BattlePosition.Attack, true);
                o.Deck.Clear();
                o.Deck.AddRange(new[] { SkullServant, Celtic, AxeRaider, SkullServant });
                var hand0 = o.Hand.Count;
                EndTurn(engine);
                Check("Freed (AI): the opponent adds its best Warrior instead of the normal draw",
                    reached && engine.TurnPlayer == o && o.Hand.Count == hand0 + 1 && o.Hand.Last().CardId == AxeRaider &&
                    o.Deck.Count == 3 && o.Deck[0] == SkullServant && !engine.IsAwaitingEffectTarget,
                    $"turnO={engine.TurnPlayer == o} hand={hand0}->{o.Hand.Count} last={o.Hand.LastOrDefault()?.Name} deck={o.Deck.Count}");
            }

            {
                // Face-down Freed, no Warrior in the Deck, a skipped Draw Phase: no offer, normal flow.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Freed, 0, BattlePosition.Defense, false);
                EndTurn(engine);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { SkullServant, Celtic });
                var hand0 = p.Hand.Count;
                EndTurn(engine);
                var faceDown = engine.Phase == DuelPhase.Main1 && !engine.IsAwaitingEffectTarget &&
                               p.Hand.Count == hand0 + 1 && p.Hand.Last().CardId == SkullServant;

                var e2 = PlayerMain1(db, pDeck, aDeck, out var reached2);
                var p2 = e2.Player;
                PlaceMonster(e2, p2, Freed, 0, BattlePosition.Attack, true);
                EndTurn(e2);
                p2.Deck.Clear();
                p2.Deck.AddRange(new[] { SkullServant, DarkMagician });
                EndTurn(e2);
                var noWarrior = e2.Phase == DuelPhase.Main1 && !e2.IsAwaitingEffectTarget &&
                                p2.Hand.Last().CardId == SkullServant;

                var e3 = PlayerMain1(db, pDeck, aDeck, out var reached3);
                var p3 = e3.Player;
                PlaceMonster(e3, p3, Freed, 0, BattlePosition.Attack, true);
                EndTurn(e3);
                p3.Deck.Clear();
                p3.Deck.AddRange(new[] { SkullServant, Celtic });
                p3.SkipNextDrawPhase = true;
                var hand3 = p3.Hand.Count;
                EndTurn(e3);
                var skipped = e3.Phase == DuelPhase.Main1 && !e3.IsAwaitingEffectTarget && p3.Hand.Count == hand3 &&
                              p3.Deck.Count == 2;
                Check("Freed: no offer when face-down, with no Level 4 or lower Warrior in the Deck, or when the draw is skipped",
                    reached && reached2 && reached3 && faceDown && noWarrior && skipped,
                    $"faceDown={faceDown} noWarrior={noWarrior} skipped={skipped}");
            }
        }

        // ───────────────────────────── Tyrant Dragon's second attack ─────────────────────────────

        static void TyrantAttackDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var tyrant = PlaceMonster(engine, p, Tyrant, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, o, SkullServant, 1, BattlePosition.Attack, true);
                var first = AttackAndDrain(engine, p, tyrant, celtic);
                var extraNow = engine.ExtraAttacksAllowed(tyrant);
                var second = AttackAndDrain(engine, p, tyrant, servant);
                var third = engine.CanAttack(p, tyrant);
                Check("Tyrant Dragon: a second attack while the opponent still controls a monster after the first",
                    reached && first && extraNow == 1 && second && InGy(o, celtic) && InGy(o, servant) && !third,
                    $"first={first} extra={extraNow} second={second} celticGy={InGy(o, celtic)} servantGy={InGy(o, servant)} third={third}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var tyrant = PlaceMonster(engine, p, Tyrant, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var first = AttackAndDrain(engine, p, tyrant, celtic);
                var lp = o.LifePoints;
                var direct = engine.TryAttack(p, tyrant, null);
                DrainCombat(engine);
                Check("Tyrant Dragon: no second attack (no direct attack) once the opponent controls no monster",
                    reached && first && InGy(o, celtic) && engine.ExtraAttacksAllowed(tyrant) == 0 && !direct &&
                    o.LifePoints == lp,
                    $"first={first} extra={engine.ExtraAttacksAllowed(tyrant)} direct={direct} lp={lp}->{o.LifePoints}");
            }
        }

        // ───────────────────────────── Ha Des / Twin-Headed Wolf ─────────────────────────────

        static void BattleNegationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Ha Des itself destroys Witch of the Black Forest: Witch's "sent to the GY" search does not happen.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hades = PlaceMonster(engine, p, HaDes, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var deck0 = o.Deck.Count;
                var ok = AttackAndDrain(engine, p, hades, witch);
                Check("Ha Des: a monster destroyed by battle with a Fiend you control has its GY trigger negated",
                    reached && ok && InGy(o, witch) && witch.EffectsNegatedAfterBattle && o.Hand.Count == hand0 &&
                    o.Deck.Count == deck0 && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} gy={InGy(o, witch)} flag={witch.EffectsNegatedAfterBattle} hand={hand0}->{o.Hand.Count} deck={deck0}->{o.Deck.Count}");

                // It leaves the GY: the negation ends (to the hand: board refresh; to the field: field entry).
                o.Graveyard.Remove(witch);
                o.Hand.Add(witch);
                FieldSpellEffects.RefreshBoard(engine);
                var clearedInHand = !witch.EffectsNegatedAfterBattle && !witch.IsNegated;
                Check("Ha Des: the negation ends once the card leaves the GY",
                    clearedInHand, $"flag={witch.EffectsNegatedAfterBattle}");
            }

            {
                // Another Fiend (Archfiend Soldier) destroys Mystic Tomato: no Special Summon from the Deck.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, HaDes, 0, BattlePosition.Defense, true);
                var soldier = PlaceMonster(engine, p, ArchfiendSoldier, 1, BattlePosition.Attack, true);
                var tomato = PlaceMonster(engine, o, MysticTomato, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, soldier, tomato);
                var oppMonsters = o.MonstersOnField().Count();
                Check("Ha Des: another Fiend's battle — Mystic Tomato's 'destroyed by battle' trigger is negated",
                    reached && ok && InGy(o, tomato) && oppMonsters == 0 && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} gy={InGy(o, tomato)} oppMonsters={oppMonsters}");

                // Revived from the GY, it is a new card: not negated.
                o.Graveyard.Remove(tomato);
                engine.NextSpecialSummonFromGraveyard = true;
                var revived = engine.SpecialSummonToField(o, tomato, BattlePosition.Attack, true);
                Check("Ha Des: a negated monster Special Summoned from the GY is no longer negated",
                    revived && !tomato.EffectsNegatedAfterBattle && !tomato.IsNegated,
                    $"revived={revived} flag={tomato.EffectsNegatedAfterBattle}");
            }

            {
                // Fox Fire attacks into a Fiend and is destroyed: its GY End Phase revival is negated while Ha Des is the
                // Fiend it battled (control: another Fiend, no Ha Des).
                foreach (var withHaDes in new[] { true, false })
                {
                    var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    var wall = PlaceMonster(engine, p, withHaDes ? HaDes : ArchfiendSoldier, 0, BattlePosition.Attack, true);
                    var fox = PlaceMonster(engine, o, FoxFire, 0, BattlePosition.Attack, true);
                    var battle = ReachOpponentBattle(engine);
                    var ok = engine.TryAttack(o, fox, wall);
                    DrainCombat(engine);
                    var destroyed = InGy(o, fox);
                    EndTurn(engine);
                    var revived = o.TryFindMonster(fox, out _);
                    Check(withHaDes
                            ? "Ha Des: Fox Fire destroyed by battle with it does not revive in the End Phase"
                            : "Control: Fox Fire destroyed by battle with another Fiend revives in the End Phase",
                        reached && battle && ok && destroyed && (withHaDes ? !revived && InGy(o, fox) : revived),
                        $"battle={battle} ok={ok} destroyed={destroyed} revived={revived}");
                }
            }

            {
                // Near miss: a non-Fiend you control destroys Witch — her search happens.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, HaDes, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 1, BattlePosition.Attack, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var ok = AttackAndDrain(engine, p, axe, witch);
                Check("Ha Des: a battle by a non-Fiend you control negates nothing (Witch searches)",
                    reached && ok && InGy(o, witch) && !witch.EffectsNegatedAfterBattle && o.Hand.Count == hand0 + 1,
                    $"ok={ok} flag={witch.EffectsNegatedAfterBattle} hand={hand0}->{o.Hand.Count}");
            }

            {
                // Twin-Headed Wolf + another Fiend attacks a face-down Man-Eater Bug: its FLIP effect does not activate.
                foreach (var withFiend in new[] { true, false })
                {
                    var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    var wolf = PlaceMonster(engine, p, Wolf, 0, BattlePosition.Attack, true);
                    if (withFiend) PlaceMonster(engine, p, RyuKishin, 1, BattlePosition.Defense, true);
                    var bug = PlaceMonster(engine, o, ManEaterBug, 0, BattlePosition.Defense, false);
                    var ok = AttackAndDrain(engine, p, wolf, bug);
                    for (var i = 0; i < 4 && engine.IsAwaitingEffectTarget; i++)
                        engine.CancelEffectTargeting();
                    DrainCombat(engine);
                    var wolfAlive = p.TryFindMonster(wolf, out _);
                    Check(withFiend
                            ? "Twin-Headed Wolf: with another Fiend, the Flip monster it destroys has its FLIP effect negated"
                            : "Control: without another Fiend, Man-Eater Bug's FLIP effect destroys Twin-Headed Wolf",
                        reached && ok && InGy(o, bug) && (withFiend ? wolfAlive && bug.EffectsNegatedAfterBattle : !wolfAlive),
                        $"ok={ok} bugGy={InGy(o, bug)} wolfAlive={wolfAlive} flag={bug.EffectsNegatedAfterBattle}");
                }
            }

            {
                // Twin-Headed Wolf negates Flip monsters only: Witch searches.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var wolf = PlaceMonster(engine, p, Wolf, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, RyuKishin, 1, BattlePosition.Defense, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var ok = AttackAndDrain(engine, p, wolf, witch);
                Check("Twin-Headed Wolf: a non-Flip monster it destroys is not negated (Witch searches)",
                    reached && ok && InGy(o, witch) && !witch.EffectsNegatedAfterBattle && o.Hand.Count == hand0 + 1,
                    $"ok={ok} flag={witch.EffectsNegatedAfterBattle} hand={hand0}->{o.Hand.Count}");
            }
        }

        // ───────────────────────────── Lesser Fiend ─────────────────────────────

        static void LesserFiendDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lesser = PlaceMonster(engine, p, LesserFiend, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(engine, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var ok = AttackAndDrain(engine, p, lesser, witch);
                Check("Lesser Fiend: the monster it destroys by battle is banished, not sent to the GY (no Witch search)",
                    reached && ok && o.Banished.Contains(witch) && !InGy(o, witch) && o.Hand.Count == hand0,
                    $"ok={ok} banished={o.Banished.Contains(witch)} gy={InGy(o, witch)} hand={hand0}->{o.Hand.Count}");
            }

            {
                // Both destroyed, Lesser Fiend first (it is the attack target): the attacker is still banished.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lesser = PlaceMonster(engine, p, LesserFiend, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(engine, o, AxeRaider, 0, BattlePosition.Attack, true);
                axe.LingeringAtkModifier = 400; // 2100, the same ATK as Lesser Fiend
                var reachedBattle = ReachOpponentBattle(engine);
                var ok = engine.TryAttack(o, axe, lesser);
                DrainCombat(engine);
                Check("Lesser Fiend: destroyed in the same battle, the monster that destroyed it is still banished",
                    reached && reachedBattle && ok && InGy(p, lesser) && o.Banished.Contains(axe) && !InGy(o, axe),
                    $"battle={reachedBattle} ok={ok} lesserGy={InGy(p, lesser)} axeBanished={o.Banished.Contains(axe)}");
            }

            {
                // Dark Ruler Ha Des negates Lesser Fiend at EVENT_BATTLED, before the destruction: its banish redirect (ygopro
                // EFFECT_BATTLE_DESTROY_REDIRECT, read as the monster is destroyed) no longer applies, whoever attacks.
                foreach (var lesserAttacks in new[] { true, false })
                {
                    var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                    var p = engine.Player;
                    var o = engine.Opponent;
                    var lesser = PlaceMonster(engine, p, LesserFiend, 0, BattlePosition.Attack, true);
                    PlaceMonster(engine, o, HaDes, 1, BattlePosition.Defense, true);
                    var soldier = PlaceMonster(engine, o, ArchfiendSoldier, 0, BattlePosition.Attack, true);
                    soldier.LingeringAtkModifier = 200; // 2100, the same ATK as Lesser Fiend
                    var reachedBattle = true;
                    bool ok;
                    if (lesserAttacks)
                        ok = AttackAndDrain(engine, p, lesser, soldier);
                    else
                    {
                        reachedBattle = ReachOpponentBattle(engine);
                        ok = engine.TryAttack(o, soldier, lesser);
                        DrainCombat(engine);
                    }

                    Check(lesserAttacks
                            ? "Lesser Fiend negated by Ha Des (it attacks): the Fiend it destroys goes to the GY"
                            : "Lesser Fiend negated by Ha Des (it is attacked): the Fiend it destroys goes to the GY",
                        reached && reachedBattle && ok && InGy(p, lesser) && InGy(o, soldier) &&
                        !o.Banished.Contains(soldier),
                        $"battle={reachedBattle} ok={ok} lesserGy={InGy(p, lesser)} soldierGy={InGy(o, soldier)} " +
                        $"soldierBanished={o.Banished.Contains(soldier)}");
                }
            }

            {
                // Lesser Fiend attacks a face-down Man-Eater Bug whose FLIP effect destroys it before the Bug's battle
                // destruction: Lesser Fiend is gone when the Bug is destroyed, so the Bug goes to the GY.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var lesser = PlaceMonster(engine, p, LesserFiend, 0, BattlePosition.Attack, true);
                var bug = PlaceMonster(engine, o, ManEaterBug, 0, BattlePosition.Defense, false);
                var ok = AttackAndDrain(engine, p, lesser, bug);
                for (var i = 0; i < 4 && engine.IsAwaitingEffectTarget; i++)
                    engine.CancelEffectTargeting();
                DrainCombat(engine);
                var lesserAlive = p.TryFindMonster(lesser, out _);
                Check("Lesser Fiend destroyed by the Bug's FLIP before the battle destruction: the Bug goes to the GY",
                    reached && ok && !lesserAlive && InGy(o, bug) && !o.Banished.Contains(bug),
                    $"ok={ok} lesserAlive={lesserAlive} bugGy={InGy(o, bug)} bugBanished={o.Banished.Contains(bug)}");
            }

            {
                // Near miss: another monster's battle — destroyed monsters go to the GY.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, p, LesserFiend, 0, BattlePosition.Defense, true);
                var axe = PlaceMonster(engine, p, AxeRaider, 1, BattlePosition.Attack, true);
                var servant = PlaceMonster(engine, o, SkullServant, 0, BattlePosition.Attack, true);
                var ok = AttackAndDrain(engine, p, axe, servant);
                Check("Lesser Fiend: only what it destroys itself is banished",
                    reached && ok && InGy(o, servant) && !o.Banished.Contains(servant),
                    $"ok={ok} gy={InGy(o, servant)}");
            }
        }

        // ───────────────────────────── Double Snare ─────────────────────────────

        static void DoubleSnareDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var jinzo = PlaceMonster(engine, o, Jinzo, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var fsd = PlaceMonster(engine, o, FiendSkullDragon, 2, BattlePosition.Attack, true);
                var decree = PlaceSetTrap(engine, o, RoyalDecree, 0);
                decree.FaceUp = true;
                var bead = PlaceSetTrap(engine, o, DragonsBead, 1);
                bead.FaceUp = true;
                PlaceSetTrap(engine, o, RoyalDecree, 2); // face-down: not a target
                var sorcerer = PlaceMonster(engine, p, 88619463, 0, BattlePosition.Attack, true); // Sorcerer of Dark Magic
                var snare = PutInHand(engine, p, DoubleSnare);
                var can = engine.CanActivateSpellTrap(p, snare, fromHand: true);
                var opened = engine.TryActivateSpellTrap(p, snare, true);
                var legal = engine.PendingActivation?.LegalTargets.Select(c => c.CardId).OrderBy(x => x).ToList() ??
                            new System.Collections.Generic.List<int>();
                var expect = new[] { Jinzo, FiendSkullDragon, RoyalDecree, DragonsBead, 88619463 }.OrderBy(x => x).ToList();
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(jinzo);
                for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                Check("Double Snare: targets face-up cards that negate Trap effects on either field (Jinzo, Royal Decree, " +
                      "The Dragon's Bead, Sorcerer of Dark Magic, Fiend Skull Dragon) — not Celtic, not a Set card",
                    reached && can && opened && legal.SequenceEqual(expect),
                    $"can={can} opened={opened} legal=[{string.Join(",", legal)}]");
                Check("Double Snare: destroys the chosen target",
                    picked && InGy(o, jinzo) && InGy(p, snare) && p.TryFindMonster(sorcerer, out _),
                    $"picked={picked} jinzoGy={InGy(o, jinzo)} snareGy={InGy(p, snare)}");
            }

            {
                // No such face-up card: it cannot be activated.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, Jinzo, 1, BattlePosition.Defense, false);
                PlaceSetTrap(engine, o, RoyalDecree, 0);
                var snare = PutInHand(engine, p, DoubleSnare);
                Check("Double Snare: no face-up card that negates Trap effects → cannot be activated",
                    reached && !engine.CanActivateSpellTrap(p, snare, fromHand: true));
            }

            {
                // v161 cross-group audit: a Jinzo equipped as an Equip Spell (Sword Hunter, BothSideAndMonsterEquip) sits in a
                // Spell & Trap Zone, where its Monster Zone effect does not apply — not a Double Snare target.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var host = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var equipped = PlaceSetTrap(engine, o, Jinzo, 0);
                equipped.FaceUp = true;
                equipped.EquippedTo = host;
                equipped.GrantedEquipHost = host;
                var snare = PutInHand(engine, p, DoubleSnare);
                Check("Double Snare: a Jinzo equipped as an Equip Spell (Sword Hunter) is not a target → cannot be activated",
                    reached && equipped.IsEquipSpellWithGrantedEffectOnly &&
                    !engine.CanActivateSpellTrap(p, snare, fromHand: true));
            }
        }
    }
}
