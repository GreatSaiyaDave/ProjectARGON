using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// AttackShieldsAndDamageZero batch: Solar Flare Dragon, Soul-Absorbing Bone Tower, Goblin King, Emissary of the Oasis,
    /// The Sanctuary in the Sky, Arcane Archer of the Forest, Theban Nightmare. Compile shapes through the real compiler,
    /// duels through the engine entry points (TryAttack, SpecialSummonToField, TryActivateSpellTrap, the AI turn, board
    /// refresh), and look-alike sentences that must stay uncompiled. Turn 3 is the Player's, turn 4 the AI's.
    /// Runtime: <see cref="AttackShieldsAndDamageZeroRuntime"/>.
    /// </summary>
    public static class AttackShieldsAndDamageZeroRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SolarFlare = 45985838;        // Pyro Lv4 1500/1000
        const int BoneTower = 63012333;         // Zombie Lv3 400/1500
        const int GoblinKing = 18590133;        // Fiend Lv1 0/0
        const int Emissary = 6103294;           // Spellcaster Lv3 600/400
        const int Sanctuary = 56433456;         // Field Spell
        const int ArcaneArcher = 55001420;      // Warrior Lv3 900/1400
        const int Theban = 51838385;            // Fiend Lv4 1500/500
        const int Orca = 63120904;

        const int Hinotama = 96851799;          // Pyro Normal Lv2 600/500
        const int WittyPhantom = 36304921;      // Fiend Normal Lv4 1400/1300
        const int Terra = 63308047;             // Fiend Normal Lv4 1200/1300
        const int KingFog = 84686841;           // Fiend Normal Lv3 1000/900
        const int ThirteenthGrave = 32864;      // Zombie Normal Lv3 1200/900
        const int Celtic = 91152256;            // Warrior Normal Lv4 1400/1200
        const int AxeRaider = 48305365;         // Warrior Normal Lv4 1700/1150
        const int BasicInsect = 89091579;       // Insect Normal Lv2 500/700
        const int PetitAngel = 38142739;        // Fairy Normal Lv3 600/900
        const int RayTemperature = 85309439;    // Fairy Normal Lv3 1000/1000
        const int ManEater = 93553943;          // Plant Normal Lv2 800/600
        const int Mandragola = 7802006;         // Spellcaster (its name has "Plant")
        const int TrapHole = 4206964;
        const int MonsterReborn = 83764718;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("AttackShieldsAndDamageZero: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            SolarFlareDuels(db, pDeck, aDeck, Check);
            GoblinKingDuels(db, pDeck, aDeck, Check);
            BoneTowerDuels(db, pDeck, aDeck, Check);
            EmissaryDuels(db, pDeck, aDeck, Check);
            SanctuaryDuels(db, pDeck, aDeck, Check);
            ArcaneArcherDuels(db, pDeck, aDeck, Check);
            ThebanDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's Main Phase 1 of turn 3, empty boards and hands.</summary>
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

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase != DuelPhase.Battle) engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        /// <summary>The AI's Battle Phase (turn 4): <paramref name="attacker"/> attacks <paramref name="target"/>.</summary>
        static bool OpponentAttacks(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (!ReachOpponentBattle(engine)) return false;
            engine.Opponent.Hand.Clear();
            var ok = engine.TryAttack(engine.Opponent, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 10 && engine.IsAwaitingResponse; i++) engine.PassResponse();
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

        static CardInstance PlaceField(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            e.NotifyPublic();
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);
        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} side={c.Side} amt={c.Amount} race={c.RaceFilter} " +
                    $"cond={c.WhileCondition}:{c.ConditionName}:{c.ConditionAmount} lv={c.NormalMonsterMaxLevel} " +
                    $"trib={c.TributeRaceFilter}/{c.TributeRaceStrict}")) + "] unparsed=" +
                  string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>());

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Implemented(int id) => CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;
            bool Shield(EffectClause c, string cond, string race, int lv) =>
                c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.CannotBeAttackTarget &&
                !c.ExceptThisCard && !c.AllowsDirectAttackWhileProtected && c.WhileCondition == cond &&
                (race == null || c.ConditionName == race) && c.ConditionAmount == lv;

            var solar = P(SolarFlare);
            Check("Solar Flare Dragon: shield while you control another Pyro + End Phase burn",
                solar != null && solar.FullyCompiled && solar.ClauseList.Count == 2 &&
                solar.ClauseList.Exists(c => Shield(c, AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace, "Pyro", 0)) &&
                solar.ClauseList.Exists(c => c.Timing == EffectTiming.EndPhase &&
                                             c.Action == EffectActionKind.InflictDamageToOpponent && c.Amount == 500) &&
                Implemented(SolarFlare), Describe(solar));

            var tower = P(BoneTower);
            Check("Soul-Absorbing Bone Tower: shield while you control another Zombie + mill 2 on a Zombie Special Summon",
                tower != null && tower.FullyCompiled && tower.ClauseList.Count == 2 &&
                tower.ClauseList.Exists(c => Shield(c, AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace, "Zombie", 0)) &&
                tower.ClauseList.Exists(c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                             c.Action == EffectActionKind.MillOpponentWhenRaceSpecialSummoned &&
                                             c.RaceFilter == "Zombie" && c.Amount == 2 && c.Side == EffectSide.Opponent) &&
                Implemented(BoneTower), Describe(tower));

            var goblin = P(GoblinKing);
            Check("Goblin King: shield while you control another Fiend + ATK/DEF set to face-up Fiends (except this) x 1000",
                goblin != null && goblin.FullyCompiled && goblin.ClauseList.Count == 2 &&
                goblin.ClauseList.Exists(c => Shield(c, AttackShieldsAndDamageZeroRuntime.YouControlAnotherFaceUpRace, "Fiend", 0)) &&
                goblin.ClauseList.Exists(c => c.Action == EffectActionKind.GainSelfAtkPerCount && c.SelfStatIsSet &&
                                              c.Amount == 1000 && c.DefAmount == 1000 && c.RaceFilter == "Fiend" &&
                                              c.CountSource == AttackShieldsAndDamageZeroRuntime.FaceUpRaceOnFieldExceptThis) &&
                Implemented(GoblinKing), Describe(goblin));

            var emissary = P(Emissary);
            Check("Emissary of the Oasis: shield while you control a face-up Lv3- Normal + its controller's battle damage 0",
                emissary != null && emissary.FullyCompiled && emissary.ClauseList.Count == 2 &&
                emissary.ClauseList.Exists(c => Shield(c, AttackShieldsAndDamageZeroRuntime.YouControlFaceUpLowLevelNormal, null, 3)) &&
                emissary.ClauseList.Exists(c => c.Action == EffectActionKind.AvoidBattleDamageFromBattlesInvolving &&
                                                c.Side == EffectSide.Controller && c.NormalMonsterMaxLevel == 3 &&
                                                string.IsNullOrEmpty(c.RaceFilter)) &&
                Implemented(Emissary), Describe(emissary));

            var sanctuary = P(Sanctuary);
            Check("The Sanctuary in the Sky: the controller of a Fairy battler takes no battle damage",
                sanctuary != null && sanctuary.FullyCompiled && sanctuary.ClauseList.Count == 1 &&
                sanctuary.ClauseList[0].Timing == EffectTiming.ContinuousWhileFaceUp &&
                sanctuary.ClauseList[0].Action == EffectActionKind.AvoidBattleDamageFromBattlesInvolving &&
                sanctuary.ClauseList[0].Side == EffectSide.Both && sanctuary.ClauseList[0].RaceFilter == "Fairy" &&
                sanctuary.ClauseList[0].StaysOnField && Implemented(Sanctuary), Describe(sanctuary));

            var archer = P(ArcaneArcher);
            Check("Arcane Archer of the Forest: shield while you control a face-up Plant + Tribute-a-Plant S/T destroy",
                archer != null && archer.FullyCompiled && archer.ClauseList.Count == 2 &&
                archer.ClauseList.Exists(c => Shield(c, AttackShieldsAndDamageZeroRuntime.YouControlFaceUpRace, "Plant", 0)) &&
                archer.ClauseList.Exists(c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                                              c.Zone == EffectZoneFilter.FieldSpellTraps && c.RequiresTargetChoice &&
                                              c.RequiresTributeCount == 1 && c.TributeRaceFilter == "Plant" &&
                                              c.TributeRaceStrict && !c.TributeExceptThis &&
                                              string.IsNullOrEmpty(c.NamedCard)) &&
                Implemented(ArcaneArcher), Describe(archer));

            var orca = P(Orca);
            Check("Orca Mega-Fortress: the named Tribute form is unchanged by the Type form",
                orca != null && orca.FullyCompiled && orca.ClauseList.Count == 2 &&
                orca.ClauseList.All(c => c.TributeExceptThis && !c.TributeRaceStrict &&
                                         string.IsNullOrEmpty(c.TributeRaceFilter) && !string.IsNullOrEmpty(c.NamedCard)),
                Describe(orca));

            var theban = P(Theban);
            Check("Theban Nightmare: +1500 ATK on itself while no cards in hand nor Spell & Trap Zones",
                theban != null && theban.FullyCompiled && theban.ClauseList.Count == 1 &&
                theban.ClauseList[0].Action == EffectActionKind.ContinuousGainAtkDef &&
                theban.ClauseList[0].AffectsThisCardOnly && theban.ClauseList[0].Amount == 1500 &&
                theban.ClauseList[0].DefAmount == 0 &&
                theban.ClauseList[0].WhileCondition == AttackShieldsAndDamageZeroRuntime.NoCardsInHandOrSpellTrapZones &&
                Implemented(Theban), Describe(theban));
        }

        static void NearMisses(Chk Check)
        {
            var near = new (string Desc, EffectActionKind Action)[]
            {
                ("While you control another Pyro-Type monster, this card cannot be attacked directly.",
                    EffectActionKind.CannotBeAttackTarget),
                ("While you control another Pyro-Type monster, this card cannot be attacked, and it cannot attack.",
                    EffectActionKind.CannotBeAttackTarget),
                ("While you control another Pyro-Type monster, this card cannot be destroyed by battle.",
                    EffectActionKind.CannotBeAttackTarget),
                ("While a face-up Level 3 or lower Effect Monster is on your side of the field, your opponent cannot " +
                 "select this card as an attack target.",
                    EffectActionKind.CannotBeAttackTarget),
                ("While this card is face-up on your side of the field, any Battle Damage to the controller of this card " +
                 "from battle involving a Level 3 or lower Effect Monster becomes 0.",
                    EffectActionKind.AvoidBattleDamageFromBattlesInvolving),
                ("Battle damage to the controller of a Fairy monster from a battle involving that monster is halved.",
                    EffectActionKind.AvoidBattleDamageFromBattlesInvolving),
                ("Each time a Zombie-Type monster is Normal Summoned: Send the top 2 cards of your opponent's Deck to the " +
                 "Graveyard.",
                    EffectActionKind.MillOpponentWhenRaceSpecialSummoned),
                ("Each time a Zombie-Type monster(s) is Special Summoned: Send the top 2 cards of your Deck to the Graveyard.",
                    EffectActionKind.MillOpponentWhenRaceSpecialSummoned),
                ("The ATK and DEF of this card are each equal to the number of Fiend-Type monsters on the field x 1000.",
                    EffectActionKind.GainSelfAtkPerCount),
                ("Gains 1500 ATK while you have no cards in your hand.",
                    EffectActionKind.ContinuousGainAtkDef),
                ("By Tributing 1 Plant-Type monster on your side of the field, destroy 1 Spell or Trap Card on the field " +
                 "and inflict 500 damage to your opponent.",
                    EffectActionKind.Destroy),
            };
            var leaked = near.Select((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90917800 + i, name = "Synthetic AttackShieldsAndDamageZero near miss " + i, type = "Effect Monster",
                    race = "Warrior", desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                // A partial match is allowed only while it keeps the card from compiling (the Tribute sentence's
                // trailing rider stays unparsed).
                var bad = p != null && (p.FullyCompiled ||
                                        (n.Action != EffectActionKind.Destroy && p.ClauseList.Exists(c => c.Action == n.Action)));
                return bad ? i + ":" + n.Action : null;
            }).Where(s => s != null).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(",", leaked));
        }

        // ───────────────────────────── Solar Flare Dragon ─────────────────────────────

        static void SolarFlareDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var solar = PlaceMonster(e, p, SolarFlare, 0, BattlePosition.Attack, true);
                var hino = PlaceMonster(e, p, Hinotama, 1, BattlePosition.Attack, true);
                var on = Shielded(e, solar);
                var hinoOpen = !Shielded(e, hino);
                hino.FaceUp = false;
                hino.Position = BattlePosition.Defense;
                var downOpen = !Shielded(e, solar);
                hino.FaceUp = true;
                hino.Position = BattlePosition.Attack;
                var backOn = Shielded(e, solar);
                p.MonsterZones[1].Occupant = null;
                p.Graveyard.Add(hino);
                var goneOpen = !Shielded(e, solar);
                Check("Solar Flare Dragon: protected only while another face-up Pyro is controlled (flips live)",
                    reached && on && hinoOpen && downOpen && backOn && goneOpen,
                    $"on={on} hinoOpen={hinoOpen} down={downOpen} back={backOn} gone={goneOpen}");

                // Negated, its own shield is gone.
                p.MonsterZones[1].Occupant = hino;
                p.Graveyard.Remove(hino);
                solar.IsNegated = true;
                var negOpen = !Shielded(e, solar);
                solar.IsNegated = false;
                Check("Solar Flare Dragon: a negated one can be attacked", negOpen, "");

                // The opponent's attack on it is refused; the LegalIntentService offers no attack on it.
                var axe = PlaceMonster(e, o, AxeRaider, 0, BattlePosition.Attack, true);
                var reachedBattle = ReachOpponentBattle(e);
                o.Hand.Clear();
                var snap = LegalIntentService.Build(e, o);
                var offersSolar = snap.Entries.Any(x => x.Kind == LegalIntentService.LegalKind.Attack && x.Target == solar);
                var offersHino = snap.Entries.Any(x => x.Kind == LegalIntentService.LegalKind.Attack && x.Target == hino);
                var offersDirect = snap.Entries.Any(x => x.Kind == LegalIntentService.LegalKind.DirectAttack);
                var refused = !e.TryAttack(o, axe, solar);
                Check("Solar Flare Dragon: the opponent cannot attack it (TryAttack, LegalIntentService), the other Pyro yes",
                    reachedBattle && !offersSolar && offersHino && !offersDirect && refused && OnField(p, solar),
                    $"battle={reachedBattle} solar={offersSolar} hino={offersHino} direct={offersDirect} refused={refused}");
            }

            {
                // Two Solar Flare Dragons protect each other: the AI can attack neither, nor attack directly, and its turn
                // ends (no soft-lock).
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var a = PlaceMonster(e, p, SolarFlare, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, SolarFlare, 1, BattlePosition.Attack, true);
                var axe = PlaceMonster(e, o, AxeRaider, 0, BattlePosition.Attack, true);
                var ended = e.TryEndTurnSafe(p);
                PassAll(e);
                var aiTurn = e.TurnPlayer == o;
                o.Hand.Clear();
                var lp = p.LifePoints;
                var direct = e.CanAttackDirectly(o, axe);
                var done = RunAiTurn(e, 400);
                Check("Solar Flare Dragon x2: the AI attacks neither and cannot attack directly; its turn ends",
                    reached && ended && aiTurn && !direct && done && p.LifePoints == lp && OnField(p, a) && OnField(p, b) &&
                    e.TurnPlayer == p,
                    $"ended={ended} ai={aiTurn} direct={direct} done={done} lp={p.LifePoints}/{lp} turn={e.TurnPlayer?.Name}");
            }

            {
                // The End Phase burn is the controller's own End Phase only.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, SolarFlare, 0, BattlePosition.Attack, true);
                var oLp = o.LifePoints;
                e.TryEndTurnSafe(p);
                PassAll(e);
                var burned = o.LifePoints == oLp - 500;
                var pLp = p.LifePoints;
                var oLp2 = o.LifePoints;
                var reachedMain = ReachOpponentMain(e);
                o.Hand.Clear();
                e.TryEndTurnSafe(o);
                PassAll(e);
                Check("Solar Flare Dragon: 500 to the opponent in your End Phase, nothing in theirs",
                    reached && burned && reachedMain && p.LifePoints == pLp && o.LifePoints == oLp2,
                    $"burned={burned} o={o.LifePoints}/{oLp2} p={p.LifePoints}/{pLp}");
            }
        }

        // ───────────────────────────── Goblin King ─────────────────────────────

        static void GoblinKingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var king = PlaceMonster(e, p, GoblinKing, 0, BattlePosition.Attack, true);
            e.NotifyPublic();
            var alone = king.CurrentAtk == 0 && king.CurrentDef == 0 && !Shielded(e, king);
            var witty = PlaceMonster(e, p, WittyPhantom, 1, BattlePosition.Attack, true);
            PlaceMonster(e, o, Terra, 0, BattlePosition.Attack, true);
            var fog = PlaceMonster(e, o, KingFog, 1, BattlePosition.Defense, false);
            PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
            e.NotifyPublic();
            var two = king.CurrentAtk == 2000 && king.CurrentDef == 2000 && Shielded(e, king);
            var atkTwo = king.CurrentAtk;
            fog.FaceUp = true;
            e.NotifyPublic();
            var three = king.CurrentAtk == 3000 && king.CurrentDef == 3000;
            witty.FaceUp = false;
            witty.Position = BattlePosition.Defense;
            e.NotifyPublic();
            var flipped = king.CurrentAtk == 2000 && !Shielded(e, king);
            Check("Goblin King: ATK/DEF = face-up Fiends on the field (not itself, either side) x 1000; shielded by another Fiend",
                reached && alone && two && three && flipped,
                $"alone={alone} two={two}({atkTwo}) three={three} flipped={flipped} atk={king.CurrentAtk}");
        }

        // ───────────────────────────── Soul-Absorbing Bone Tower ─────────────────────────────

        static void BoneTowerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var tower = PlaceMonster(e, p, BoneTower, 0, BattlePosition.Defense, true);
                var open = !Shielded(e, tower);
                var deck = o.Deck.Count;
                var gy = o.Graveyard.Count;
                var zombie = e.CreateCardInstance(ThirteenthGrave);
                var summoned = e.SpecialSummonToField(p, zombie, BattlePosition.Attack, true);
                PassAll(e);
                var milled = o.Deck.Count == deck - 2 && o.Graveyard.Count == gy + 2;
                var shield = Shielded(e, tower);
                Check("Soul-Absorbing Bone Tower: a Zombie Special Summoned -> the opponent sends their top 2 to the GY; " +
                      "then shielded by that Zombie",
                    reached && open && summoned && milled && shield,
                    $"open={open} ss={summoned} deck={o.Deck.Count}/{deck} gy={o.Graveyard.Count}/{gy} shield={shield}");

                var deck2 = o.Deck.Count;
                var celtic = e.CreateCardInstance(Celtic);
                e.SpecialSummonToField(p, celtic, BattlePosition.Attack, true);
                PassAll(e);
                var setZombie = e.CreateCardInstance(ThirteenthGrave);
                e.SpecialSummonToField(o, setZombie, BattlePosition.Defense, false);
                PassAll(e);
                var none = o.Deck.Count == deck2;
                var oppZombie = e.CreateCardInstance(ThirteenthGrave);
                e.SpecialSummonToField(o, oppZombie, BattlePosition.Attack, true);
                PassAll(e);
                var byOpp = o.Deck.Count == deck2 - 2;
                Check("Soul-Absorbing Bone Tower: a non-Zombie or a face-down Zombie mills nothing; the opponent's Zombie mills",
                    none && byOpp, $"none={none} byOpp={byOpp} deck={o.Deck.Count}/{deck2}");
            }

            {
                // Its own Special Summon does not trigger it (Lua: not eg:IsContains(e:GetHandler())); a negated one does
                // nothing.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var deck = o.Deck.Count;
                var tower = e.CreateCardInstance(BoneTower);
                var ss = e.SpecialSummonToField(p, tower, BattlePosition.Defense, true);
                PassAll(e);
                var self = o.Deck.Count == deck;
                tower.IsNegated = true;
                e.SpecialSummonToField(p, e.CreateCardInstance(ThirteenthGrave), BattlePosition.Attack, true);
                PassAll(e);
                var negated = o.Deck.Count == deck;
                tower.IsNegated = false;
                Check("Soul-Absorbing Bone Tower: not by its own Special Summon; nothing while negated",
                    reached && ss && self && negated, $"ss={ss} self={self} negated={negated} deck={o.Deck.Count}/{deck}");
            }

            {
                // Monster Reborn's Special Summon happens while its Chain Link resolves: the mill follows the Chain.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, BoneTower, 0, BattlePosition.Defense, true);
                var grave = e.CreateCardInstance(ThirteenthGrave);
                p.Graveyard.Add(grave);
                var reborn = PutInHand(e, p, MonsterReborn);
                var deck = o.Deck.Count;
                var act = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                for (var i = 0; i < 8; i++)
                {
                    if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(grave)) e.TrySelectEffectTarget(grave);
                    else if (e.IsAwaitingResponse) e.PassResponse();
                    else break;
                }

                Check("Soul-Absorbing Bone Tower: Monster Reborn on a Zombie -> the opponent sends 2 once the Chain resolved",
                    reached && act && OnField(p, grave) && o.Deck.Count == deck - 2 && !e.IsResolvingChain,
                    $"act={act} onField={OnField(p, grave)} deck={o.Deck.Count}/{deck}");
            }
        }

        // ───────────────────────────── Emissary of the Oasis ─────────────────────────────

        static void EmissaryDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var emi = PlaceMonster(e, p, Emissary, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var lv4Open = !Shielded(e, emi);
                var grave = PlaceMonster(e, p, ThirteenthGrave, 2, BattlePosition.Attack, true);
                var on = Shielded(e, emi);
                grave.FaceUp = false;
                grave.Position = BattlePosition.Defense;
                var downOpen = !Shielded(e, emi);
                Check("Emissary of the Oasis: shielded only by a face-up Level 3 or lower Normal Monster you control",
                    reached && lv4Open && on && downOpen && !Shielded(e, celtic),
                    $"lv4Open={lv4Open} on={on} downOpen={downOpen}");
            }

            {
                // The opponent's Celtic Guardian (1400) destroys your Thirteenth Grave (1200): you take no damage.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Emissary, 0, BattlePosition.Attack, true);
                var grave = PlaceMonster(e, p, ThirteenthGrave, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var ok = OpponentAttacks(e, celtic, grave);
                Check("Emissary of the Oasis: no battle damage from your Level 3 Normal Monster's battle",
                    reached && ok && InGy(p, grave) && p.LifePoints == lp, $"ok={ok} lp={p.LifePoints}/{lp}");
            }

            {
                // Your Emissary (600) attacks the opponent's Thirteenth Grave (1200): you take no damage (e3).
                // Your Axe Raider (1700) attacks it: the opponent still takes 500.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var emi = PlaceMonster(e, p, Emissary, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var grave = PlaceMonster(e, o, ThirteenthGrave, 0, BattlePosition.Attack, true);
                var grave2 = PlaceMonster(e, o, ThirteenthGrave, 1, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok1 = AttackAndDrain(e, p, emi, grave);
                var noDmg = p.LifePoints == lp && InGy(p, emi);
                var ok2 = AttackAndDrain(e, p, axe, grave2);
                Check("Emissary of the Oasis: no damage to you vs the opponent's Lv3 Normal; the opponent still takes damage",
                    reached && ok1 && noDmg && ok2 && o.LifePoints == oLp - 500 && InGy(o, grave2),
                    $"ok1={ok1} noDmg={noDmg} ok2={ok2} p={p.LifePoints}/{lp} o={o.LifePoints}/{oLp}");
            }

            {
                // Your Basic Insect (500) attacks the opponent's Celtic Guardian (1400): no damage. Face-down Emissary: 900.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var emi = PlaceMonster(e, p, Emissary, 0, BattlePosition.Attack, true);
                var insect = PlaceMonster(e, p, BasicInsect, 1, BattlePosition.Attack, true);
                var insect2 = PlaceMonster(e, p, BasicInsect, 2, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var ok = AttackAndDrain(e, p, insect, celtic);
                var none = p.LifePoints == lp;
                // A face-down Emissary does not protect.
                emi.FaceUp = false;
                emi.Position = BattlePosition.Defense;
                e.NotifyPublic();
                var ok2 = AttackAndDrain(e, p, insect2, celtic);
                Check("Emissary of the Oasis: your Level 2 Normal's battle costs no LP; face-down, it does (900)",
                    reached && ok && none && ok2 && p.LifePoints == lp - 900,
                    $"ok={ok} none={none} ok2={ok2} lp={p.LifePoints}/{lp}");
            }
        }

        // ───────────────────────────── The Sanctuary in the Sky ─────────────────────────────

        static void SanctuaryDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var card = PutInHand(e, p, Sanctuary);
                var act = e.TryActivateSpellTrap(p, card, fromHand: true);
                PassAll(e);
                Check("The Sanctuary in the Sky: activates to the Field Zone and stays face-up",
                    reached && act && ReferenceEquals(p.FieldSpellZone.Occupant, card) && card.FaceUp,
                    $"act={act} field={p.FieldSpellZone.Occupant?.Name}");
            }

            {
                // Your Petit Angel (600) attacked by Celtic Guardian (1400): no damage; it is still destroyed.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var angel = PlaceMonster(e, p, PetitAngel, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var ok = OpponentAttacks(e, celtic, angel);
                Check("The Sanctuary in the Sky: no battle damage to the Fairy's controller (it is still destroyed)",
                    reached && ok && InGy(p, angel) && p.LifePoints == lp, $"ok={ok} lp={p.LifePoints}/{lp}");
            }

            {
                // Your Ray & Temperature (1000) attacks Celtic Guardian (1400): no damage. Your Axe Raider (1700) attacks
                // the opponent's Fairy (1000): the opponent takes none either. Your Celtic Guardian (1400) attacks their Axe
                // Raider (1700): you take 300.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceField(e, p, Sanctuary);
                var ray = PlaceMonster(e, p, RayTemperature, 0, BattlePosition.Attack, true);
                var axe = PlaceMonster(e, p, AxeRaider, 1, BattlePosition.Attack, true);
                var myCeltic = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var oRay = PlaceMonster(e, o, RayTemperature, 1, BattlePosition.Attack, true);
                var oAxe = PlaceMonster(e, o, AxeRaider, 2, BattlePosition.Attack, true);
                var lp = p.LifePoints;
                var oLp = o.LifePoints;
                var ok1 = AttackAndDrain(e, p, ray, celtic);
                var mine = p.LifePoints == lp && InGy(p, ray);
                var ok2 = AttackAndDrain(e, p, axe, oRay);
                var theirs = o.LifePoints == oLp && InGy(o, oRay);
                var ok3 = AttackAndDrain(e, p, myCeltic, oAxe);
                var plain = p.LifePoints == lp - 300;
                Check("The Sanctuary in the Sky: either player's Fairy battles cost its controller nothing; others do",
                    reached && ok1 && mine && ok2 && theirs && ok3 && plain,
                    $"ok1={ok1} mine={mine} ok2={ok2} theirs={theirs} ok3={ok3} p={p.LifePoints}/{lp} o={o.LifePoints}/{oLp}");
            }
        }

        // ───────────────────────────── Arcane Archer of the Forest ─────────────────────────────

        static void ArcaneArcherDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var archer = PlaceMonster(e, p, ArcaneArcher, 0, BattlePosition.Attack, true);
                var open = !Shielded(e, archer);
                var plant = PlaceMonster(e, p, ManEater, 1, BattlePosition.Attack, true);
                var on = Shielded(e, archer);
                plant.FaceUp = false;
                plant.Position = BattlePosition.Defense;
                var downOpen = !Shielded(e, archer);
                Check("Arcane Archer of the Forest: shielded only while you control a face-up Plant",
                    reached && open && on && downOpen, $"open={open} on={on} downOpen={downOpen}");
            }

            {
                // Tribute Man Eater (cost) -> destroy the opponent's Set Trap Hole.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var archer = PlaceMonster(e, p, ArcaneArcher, 0, BattlePosition.Attack, true);
                var plant = PlaceMonster(e, p, ManEater, 1, BattlePosition.Attack, true);
                var hole = PlaceSetTrap(e, o, TrapHole, 0);
                var can = e.CanActivateSpellTrap(p, archer, fromHand: false);
                var act = e.TryActivateSpellTrap(p, archer, fromHand: false);
                for (var i = 0; i < 8; i++)
                {
                    if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(plant)) e.TrySelectEffectTarget(plant);
                    else if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(hole)) e.TrySelectEffectTarget(hole);
                    else if (e.IsAwaitingResponse) e.PassResponse();
                    else break;
                }

                Check("Arcane Archer of the Forest: Tribute a Plant -> destroy 1 Spell/Trap on the field",
                    reached && can && act && InGy(p, plant) && o.Graveyard.Contains(hole) && OnField(p, archer),
                    $"can={can} act={act} plantGy={InGy(p, plant)} holeGy={o.Graveyard.Contains(hole)}");
            }

            {
                // No Plant (Mandragola's name is not its Type), or no Spell/Trap to target: it cannot activate.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var archer = PlaceMonster(e, p, ArcaneArcher, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Mandragola, 1, BattlePosition.Attack, true);
                PlaceSetTrap(e, o, TrapHole, 0);
                var noPlant = !e.CanActivateSpellTrap(p, archer, fromHand: false);
                p.MonsterZones[1].Occupant = null;
                PlaceMonster(e, p, ManEater, 1, BattlePosition.Attack, true);
                o.SpellTrapZones[0].Occupant = null;
                var noTarget = !e.CanActivateSpellTrap(p, archer, fromHand: false);
                Check("Arcane Archer of the Forest: no activation without a Plant to Tribute or a Spell/Trap to target",
                    reached && noPlant && noTarget, $"noPlant={noPlant} noTarget={noTarget}");
            }
        }

        // ───────────────────────────── Theban Nightmare ─────────────────────────────

        static void ThebanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var theban = PlaceMonster(e, p, Theban, 0, BattlePosition.Attack, true);
            PutInHand(e, o, Celtic);
            PlaceSetTrap(e, o, TrapHole, 0);
            e.NotifyPublic();
            var empty = theban.CurrentAtk == 3000;
            var inHand = PutInHand(e, p, Celtic);
            e.NotifyPublic();
            var hand = theban.CurrentAtk == 1500;
            p.Hand.Remove(inHand);
            var set = PlaceSetTrap(e, p, TrapHole, 2);
            e.NotifyPublic();
            var st = theban.CurrentAtk == 1500;
            p.SpellTrapZones[2].Occupant = null;
            PlaceField(e, p, Sanctuary);
            var field = theban.CurrentAtk == 3000 && theban.CurrentDef == 500;
            Check("Theban Nightmare: 3000 ATK with no cards in your hand nor Spell & Trap Zones (the Field Zone and the " +
                  "opponent's cards do not count)",
                reached && empty && hand && st && field && set != null,
                $"empty={empty} hand={hand} st={st} field={field} atk={theban.CurrentAtk}");
        }
    }
}
