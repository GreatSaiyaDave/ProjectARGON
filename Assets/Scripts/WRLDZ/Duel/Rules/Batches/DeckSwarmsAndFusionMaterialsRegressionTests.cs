using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// DeckSwarmsAndFusionMaterials batch: Hyena (any number of own-name copies from the Deck), Rescue Cat (2 negated Beasts
    /// destroyed in the End Phase), Sacred Phoenix of Nephthys (next-Standby GY revival, then destroy all Spells/Traps),
    /// Centrifugal Field (the destroyed Fusion's owner revives a listed material) and the Hex-Sealed Fusions (Tribute the
    /// materials, Special Summon the Attribute's Fusion Monster). Compile shapes through the real compiler, near-miss texts,
    /// and mini-duels through the engine entry points (TryAttack, TryActivateSpellTrap, TrySelectEffectTarget /
    /// CancelEffectTargeting, TryEndTurnSafe). Runtime: <see cref="DeckSwarmsAndFusionMaterialsRuntime"/>.
    /// </summary>
    public static class DeckSwarmsAndFusionMaterialsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Hyena = 22873798;          // Beast Lv3 1000/300
        const int AssaultDog = 72714226;     // the same sentence with its own (singular) name; same Lua (IsCode(id), 1..ft)
        const int RescueCat = 14878871;      // Beast Lv4 300/100
        const int Phoenix = 61441708;        // Sacred Phoenix of Nephthys, Winged Beast Lv8 2400/1600
        const int CentrifugalField = 1801154;
        const int HexLight = 15717011;
        const int HexDark = 52101615;
        const int HexEarth = 88696724;

        const int SilverFang = 90357090;     // Normal Beast Lv3 1200/800
        const int MadDog = 79182538;         // Normal Beast Lv4 1900/1400
        const int Kuriboh = 40640057;        // Fiend Lv1
        const int SummonedSkull = 70781052;  // Normal Fiend DARK 2500/1200
        const int BlackSkullDragon = 11901678; // DARK Fusion: "Summoned Skull" + "Red-Eyes Black Dragon"
        const int FlameSwordsman = 45231177; // FIRE Fusion: "Flame Manipulator" + "Masaki the Legendary Swordsman"
        const int FlameManipulator = 34460851;
        const int Masaki = 44287299;
        const int Celtic = 91152256;         // Normal Warrior 1400/1200
        const int DarkHole = 53129443;
        const int Fissure = 66788016;
        const int Mst = 5318639;
        const int VampireLord = 53839837;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DeckSwarmsAndFusionMaterials: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            HyenaDuels(db, pDeck, aDeck, Check);
            RescueCatDuels(db, pDeck, aDeck, Check);
            PhoenixDuels(db, pDeck, aDeck, Check);
            CentrifugalDuels(db, pDeck, aDeck, Check);
            HexSealedDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram Synthetic(CardDatabase db, int likeId, string desc)
        {
            var like = db.Get(likeId);
            return CardTextEffectCompiler.Compile(new CardDef
            {
                id = 0, name = "DeckSwarms near miss", type = like.type, frameType = like.frameType, desc = desc,
                atk = like.atk, def = like.def, level = like.level, race = like.race, attribute = like.attribute
            });
        }

        static bool Has(CompiledCardProgram p, EffectActionKind a) =>
            p != null && p.ClauseList.Exists(c => c != null && c.Action == a);

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}]";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static DuelEngine Clean(DuelEngine engine)
        {
            ClearBoard(engine);
            engine.ClearPendingResponse();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }

            return engine;
        }

        /// <summary>The player's Main Phase 1 on turn 3+; boards, hands and GYs empty.</summary>
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
            return Clean(engine);
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(engine);
            return Clean(engine);
        }

        static DuelEngine OpponentMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(engine);
            return Clean(engine);
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            m.FaceUpStampAtTurnStart = m.FaceUpStamp;
            return m;
        }

        static CardInstance Settled(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack)
        {
            var c = PlaceMonster(e, who, id, zone, pos, true);
            c.FaceUpStampAtTurnStart = c.FaceUpStamp;
            return c;
        }

        static CardInstance FieldSpell(DuelEngine e, DuelistState who, int id)
        {
            var f = e.CreateCardInstance(id);
            f.FaceUp = true;
            who.FieldSpellZone.Occupant = f;
            return f;
        }

        static int CountInDeck(DuelistState who, int id) => who.Deck.Count(x => x == id);

        static List<CardInstance> OnField(DuelistState who, int id) =>
            who.MonstersOnField().Where(m => m != null && m.CardId == id).ToList();

        static void EndTurn(DuelEngine e)
        {
            PassAll(e);
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            PassAll(e);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var hy = Prog(db, Hyena);
            var hc = hy?.ClauseList.Count == 1 ? hy.ClauseList[0] : null;
            Check("Hyena: one optional battle-destroyed GY trigger — any number of own-name copies from the Deck",
                Implemented(db, Hyena) && hy.FullyCompiled && hc != null &&
                hc.Timing == EffectTiming.SentFromFieldToGy &&
                hc.Action == EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck &&
                hc.RequiresThisDestroyedByBattle && hc.IsOptional && hc.FromDeck && hc.NamedCard == "Hyenas" &&
                hc.CheckedAt == ConditionCheckedAt.Activation, Describe(hy));
            var dog = Prog(db, AssaultDog);
            Check("Assault Dog: the same sentence compiles to the same swarm, its own name kept",
                Implemented(db, AssaultDog) && dog.FullyCompiled && dog.ClauseList.Count == 1 &&
                dog.ClauseList[0].Action == EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck &&
                dog.ClauseList[0].NamedCard == "Assault Dog" &&
                DeckSwarmsAndFusionMaterialsRuntime.SwarmName("Hyenas", null) == "Hyenas", Describe(dog));
            var hyHand = Synthetic(db, Hyena,
                "When this card is destroyed by battle and sent to the Graveyard: You can Special Summon any number of " +
                "\"Hyenas\" from your hand.");
            var hyMore = Synthetic(db, Hyena,
                "When this card is destroyed by battle and sent to the Graveyard: You can Special Summon any number of " +
                "\"Hyenas\" from your Deck. Then shuffle your Deck.");
            Check("Hyena near misses (from the hand; an extra sentence) are not this atom",
                !Has(hyHand, EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck) &&
                !(hyMore.FullyCompiled && Has(hyMore, EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck)),
                $"{Describe(hyHand)} | {Describe(hyMore)}");

            var rc = Prog(db, RescueCat);
            var rcc = rc?.ClauseList.Count == 1 ? rc.ClauseList[0] : null;
            Check("Rescue Cat: ignition, send this card (cost), 2 Level 3 or lower Beasts, hard once per turn",
                Implemented(db, RescueCat) && rc.FullyCompiled && rcc != null && rcc.Timing == EffectTiming.Activate &&
                rcc.Action == EffectActionKind.SpecialSummonRaceFromDeckEffectsNegatedDestroyAtEndPhase &&
                rcc.RequiresSendThisToGy && rcc.Amount == 2 && rcc.SummonLevelMax == 3 && rcc.RaceFilter == "Beast" &&
                rcc.OncePerTurn && rcc.OptScope == OncePerTurnScope.PerCardName && rcc.NamedCard == "Rescue Cat" &&
                rcc.ActivationCondition == DeckSwarmsAndFusionMaterialsRuntime.ZonesAndDeckMonstersForPair,
                Describe(rc));
            var rcNoOpt = Synthetic(db, RescueCat,
                "You can send this card to the GY; Special Summon 2 Level 3 or lower Beast monsters from your Deck, but " +
                "their effects are negated, also they are destroyed during the End Phase.");
            var rcNoNeg = Synthetic(db, RescueCat,
                "You can send this card to the GY; Special Summon 2 Level 3 or lower Beast monsters from your Deck. You " +
                "can only use this effect of \"Rescue Cat\" once per turn.");
            Check("Rescue Cat near misses (no once-per-turn sentence; no negation rider) are not this atom",
                !Has(rcNoOpt, EffectActionKind.SpecialSummonRaceFromDeckEffectsNegatedDestroyAtEndPhase) &&
                !Has(rcNoNeg, EffectActionKind.SpecialSummonRaceFromDeckEffectsNegatedDestroyAtEndPhase),
                $"{Describe(rcNoOpt)} | {Describe(rcNoNeg)}");

            var ph = Prog(db, Phoenix);
            var phc = ph?.ClauseList.Count == 1 ? ph.ClauseList[0] : null;
            Check("Sacred Phoenix of Nephthys: one mandatory GY Standby clause (revival, then destroy all Spells/Traps)",
                Implemented(db, Phoenix) && ph.FullyCompiled && phc != null && phc.Timing == EffectTiming.StandbyPhase &&
                phc.Action ==
                EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps &&
                phc.ResolvesFromGy && !phc.IsOptional && !phc.OpponentTurnOnly, Describe(ph));
            var phNoDestroy = Synthetic(db, Phoenix,
                "Once per turn, during your next Standby Phase after this card was destroyed by a card effect and sent " +
                "to the GY: Special Summon this card from the GY.");
            var vl = Prog(db, VampireLord);
            Check("Phoenix near misses: no destroy rider is not this atom; Vampire Lord keeps its own clause",
                !Has(phNoDestroy,
                    EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps) &&
                Has(vl, EffectActionKind.GyRevivalAfterStandbyWhenDestroyedByOpponent) &&
                !Has(vl, EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps),
                $"{Describe(phNoDestroy)} | {Describe(vl)}");

            var cf = Prog(db, CentrifugalField);
            var cfc = cf?.ClauseList.Count == 1 ? cf.ClauseList[0] : null;
            Check("Centrifugal Field: one continuous Field Zone trigger for destroyed Fusion Monsters",
                Implemented(db, CentrifugalField) && cf.FullyCompiled && cfc != null &&
                cfc.Timing == EffectTiming.ContinuousWhileFaceUp &&
                cfc.Action == EffectActionKind.OwnerSpecialSummonsListedMaterialWhenFusionDestroyedByEffect &&
                cfc.MakesChainLink && !cfc.IsOptional, Describe(cf));
            var cfController = Synthetic(db, CentrifugalField,
                "When a Fusion Monster(s) is destroyed by a card effect and sent to the Graveyard: Its controller " +
                "targets 1 Fusion Material Monster in their Graveyard that is listed on that Fusion Monster; that player " +
                "Special Summons that target.");
            Check("Centrifugal Field near miss (\"Its controller\") is not this atom",
                !Has(cfController, EffectActionKind.OwnerSpecialSummonsListedMaterialWhenFusionDestroyedByEffect),
                Describe(cfController));

            foreach (var (id, attr) in new[] { (HexLight, "LIGHT"), (HexDark, "DARK"), (HexEarth, "EARTH") })
            {
                var hx = Prog(db, id);
                var sub = hx?.ClauseList.FirstOrDefault(c => c?.Action == EffectActionKind.FusionSubstitute);
                var ign = hx?.ClauseList.FirstOrDefault(c =>
                    c?.Action == EffectActionKind.TributeMaterialsSpecialSummonCorrespondingAttributeFusion);
                Check($"{db.Get(id)?.name}: substitute + {attr} Tribute-the-materials ignition",
                    Implemented(db, id) && hx.FullyCompiled && hx.ClauseList.Count == 2 && sub != null && ign != null &&
                    ign.Timing == EffectTiming.Activate && ign.AttributeFilter == attr && ign.IsOptional &&
                    ign.ActivationCondition == DeckSwarmsAndFusionMaterialsRuntime.AttributeFusionMaterialsOnField,
                    Describe(hx));
            }

            var hexNoAttr = Synthetic(db, HexDark,
                "You can substitute this card for any 1 Fusion Material Monster, but the other Fusion Material " +
                "Monster(s) must be correct. You can Tribute Fusion Material Monsters on the field, including this " +
                "face-up card; Special Summon 1 corresponding Fusion Monster from your Extra Deck.");
            Check("Hex-Sealed near miss (no Attribute) stays fail-closed",
                !hexNoAttr.FullyCompiled &&
                !Has(hexNoAttr, EffectActionKind.TributeMaterialsSpecialSummonCorrespondingAttributeFusion),
                Describe(hexNoAttr));
        }

        // ───────────────────────────── Hyena ─────────────────────────────

        static (DuelEngine e, bool reached, CardInstance hyena) HyenaLosesBattle(CardDatabase db, DeckFile pDeck,
            DeckFile aDeck, int copies)
        {
            var e = OpponentBattle(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            p.Deck.RemoveAll(x => x == Hyena);
            for (var i = 0; i < copies; i++) p.Deck.Insert(0, Hyena);
            var hyena = Settled(e, p, Hyena, 0);
            var skull = Attacker(e, e.Opponent, SummonedSkull, 0);
            e.NotifyPublic();
            e.TryAttack(e.Opponent, skull, hyena);
            DrainCombat(e);
            return (e, reached, hyena);
        }

        static void HyenaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two copies in the Deck: the human takes both, one tap each.
                var (e, reached, hyena) = HyenaLosesBattle(db, pDeck, aDeck, 2);
                var p = e.Player;
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Card == hyena &&
                            Legal(e).Count == 1 && Legal(e)[0].CardId == Hyena;
                e.SetPendingSpecialSummonPlacement(1, BattlePosition.Defense);
                var first = asked && e.TrySelectEffectTarget(Legal(e)[0]);
                var askedAgain = e.IsAwaitingEffectTarget && Legal(e).Count == 1;
                var second = askedAgain && e.TrySelectEffectTarget(Legal(e)[0]);
                DrainCombat(e);
                var summoned = OnField(p, Hyena);
                Check("Hyena: destroyed by battle, the human Special Summons both Deck copies face-up (one in Defense)",
                    reached && p.Graveyard.Contains(hyena) && asked && first && askedAgain && second &&
                    summoned.Count == 2 && summoned.All(m => m.FaceUp) &&
                    summoned.Count(m => m.Position == BattlePosition.Defense) == 1 && CountInDeck(p, Hyena) == 0,
                    $"reached={reached} asked={asked} first={first} again={askedAgain} second={second} " +
                    $"field={summoned.Count} deck={CountInDeck(p, Hyena)}");
            }

            {
                // CANCEL after one: only that one is Special Summoned; the other copy stays in the Deck.
                var (e, reached, hyena) = HyenaLosesBattle(db, pDeck, aDeck, 2);
                var p = e.Player;
                var first = e.IsAwaitingEffectTarget && e.PendingActivation.Card == hyena &&
                            e.TrySelectEffectTarget(Legal(e)[0]);
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                DrainCombat(e);
                Check("Hyena: any number — CANCEL after the first keeps the other copy in the Deck",
                    reached && first && cancelled && OnField(p, Hyena).Count == 1 && CountInDeck(p, Hyena) == 1,
                    $"reached={reached} first={first} cancel={cancelled} field={OnField(p, Hyena).Count} " +
                    $"deck={CountInDeck(p, Hyena)}");
            }

            {
                // CANCEL at the first pick: the optional trigger Special Summons none.
                var (e, reached, hyena) = HyenaLosesBattle(db, pDeck, aDeck, 2);
                var p = e.Player;
                var declined = e.IsAwaitingEffectTarget && e.PendingActivation.Card == hyena && e.CancelEffectTargeting();
                DrainCombat(e);
                Check("Hyena: CANCEL at the first pick Special Summons none",
                    reached && declined && OnField(p, Hyena).Count == 0 && CountInDeck(p, Hyena) == 2,
                    $"reached={reached} declined={declined} field={OnField(p, Hyena).Count}");
            }

            {
                // Only as many as free Monster Zones: 3 other monsters + 2 zones free, 3 copies in the Deck.
                var (e, reached, hyena) = (default(DuelEngine), false, default(CardInstance));
                e = OpponentBattle(db, pDeck, aDeck, out reached);
                var p = e.Player;
                p.Deck.RemoveAll(x => x == Hyena);
                for (var i = 0; i < 3; i++) p.Deck.Insert(0, Hyena);
                hyena = Settled(e, p, Hyena, 0);
                Settled(e, p, Celtic, 1, BattlePosition.Defense);
                Settled(e, p, Celtic, 2, BattlePosition.Defense);
                Settled(e, p, Celtic, 3, BattlePosition.Defense);
                var skull = Attacker(e, e.Opponent, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(e.Opponent, skull, hyena);
                DrainCombat(e);
                var n = 0;
                for (var i = 0; i < 4 && e.IsAwaitingEffectTarget && Legal(e).Count > 0; i++)
                    if (e.TrySelectEffectTarget(Legal(e)[0])) n++;
                DrainCombat(e);
                Check("Hyena: at most the free Monster Zones (2) — the third copy stays in the Deck",
                    reached && n == 2 && OnField(p, Hyena).Count == 2 && CountInDeck(p, Hyena) == 1,
                    $"reached={reached} picks={n} field={OnField(p, Hyena).Count} deck={CountInDeck(p, Hyena)}");
            }

            {
                // A Hyena not destroyed by battle (Dark Hole) does nothing.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.RemoveAll(x => x == Hyena);
                p.Deck.Insert(0, Hyena);
                var hyena = Settled(e, p, Hyena, 0);
                var hole = PutInHand(e, p, DarkHole);
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("Hyena: destroyed by a card effect (not battle) — no trigger",
                    reached && used && p.Graveyard.Contains(hyena) && !e.IsAwaitingEffectTarget &&
                    OnField(p, Hyena).Count == 0 && CountInDeck(p, Hyena) == 1,
                    $"reached={reached} used={used} pending={e.IsAwaitingEffectTarget}");
            }
        }

        // ───────────────────────────── Rescue Cat ─────────────────────────────

        static void SeedDeck(DuelistState who, params int[] ids)
        {
            foreach (var id in ids) who.Deck.RemoveAll(x => x == id);
            foreach (var id in ids) who.Deck.Insert(0, id);
        }

        static void RescueCatDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                SeedDeck(p, SilverFang, Hyena, MadDog, Kuriboh);
                var cat = Settled(e, p, RescueCat, 0);
                var cat2 = Settled(e, p, RescueCat, 4);
                e.NotifyPublic();
                var can = e.CanActivateSpellTrap(p, cat, fromHand: false);
                var used = e.TryActivateSpellTrap(p, cat, fromHand: false);
                var legal1 = Legal(e);
                var offered = legal1.Count == 2 && legal1.Any(c => c.CardId == SilverFang) &&
                              legal1.Any(c => c.CardId == Hyena);
                e.SetPendingSpecialSummonPlacement(2, BattlePosition.Defense);
                var p1 = offered && e.TrySelectEffectTarget(legal1.First(c => c.CardId == SilverFang));
                var legal2 = Legal(e);
                var p2 = legal2.Count == 1 && legal2[0].CardId == Hyena && e.TrySelectEffectTarget(legal2[0]);
                PassAll(e);
                var fang = OnField(p, SilverFang).FirstOrDefault();
                var hy = OnField(p, Hyena).FirstOrDefault();
                var negated = fang != null && hy != null && fang.IsNegated && hy.IsNegated;
                var marked = fang != null && hy != null && fang.TempDestroyOnEndOfTurn == e.TurnNumber &&
                             hy.TempDestroyOnEndOfTurn == e.TurnNumber;
                var secondBlocked = !e.CanActivateSpellTrap(p, cat2, fromHand: false);
                Check("Rescue Cat: sent as cost; the human picks 2 Level 3 or lower Beasts (not Mad Dog / Kuriboh); " +
                      "negated, destroyed in the End Phase; a second Rescue Cat cannot use it this turn",
                    reached && can && used && p.Graveyard.Contains(cat) && offered && p1 && p2 && fang != null &&
                    hy != null && fang.FaceUp && fang.Position == BattlePosition.Defense && negated && marked &&
                    secondBlocked && CountInDeck(p, MadDog) == 1,
                    $"reached={reached} can={can} used={used} legal1={string.Join(",", legal1.Select(c => c.Name))} " +
                    $"p1={p1} p2={p2} negated={negated} marked={marked} second={secondBlocked}");

                // The End Phase destroys both; in the GY they are no longer negated.
                EndTurn(e);
                var destroyed = p.Graveyard.Contains(fang) && p.Graveyard.Contains(hy);
                Check("Rescue Cat: both monsters are destroyed during the End Phase and lose the negation off the field",
                    destroyed && !fang.IsNegated && !hy.IsNegated,
                    $"destroyed={destroyed} negFang={fang?.IsNegated} negHy={hy?.IsNegated}");
            }

            {
                // The negation lasts only while it stays on the field: returned to the hand, it is not negated.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                SeedDeck(p, SilverFang, Hyena);
                var cat = Settled(e, p, RescueCat, 0);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, cat, fromHand: false);
                for (var i = 0; i < 3 && e.IsAwaitingEffectTarget && Legal(e).Count > 0; i++)
                    e.TrySelectEffectTarget(Legal(e)[0]);
                PassAll(e);
                var fang = OnField(p, SilverFang).FirstOrDefault();
                var wasNeg = fang != null && fang.IsNegated;
                var bounced = fang != null && e.ReturnCardToHand(fang, by: p);
                e.NotifyPublic();
                Check("Rescue Cat: a monster that leaves the field is no longer negated (RESETS_STANDARD)",
                    reached && used && wasNeg && bounced && p.Hand.Contains(fang) && !fang.IsNegated,
                    $"reached={reached} used={used} wasNeg={wasNeg} bounced={bounced} neg={fang?.IsNegated}");
            }

            {
                // Only 1 eligible Beast in the Deck: it cannot be activated.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.Deck.RemoveAll(x => x == Hyena || x == SilverFang);
                SeedDeck(p, SilverFang, MadDog);
                var cat = Settled(e, p, RescueCat, 0);
                e.NotifyPublic();
                Check("Rescue Cat: fewer than 2 Level 3 or lower Beasts in the Deck — cannot be activated",
                    reached && !e.CanActivateSpellTrap(p, cat, fromHand: false) &&
                    !e.TryActivateSpellTrap(p, cat, fromHand: false) && p.TryFindMonster(cat, out _),
                    $"reached={reached}");
            }

            {
                // Zones: 4 other monsters (no free zone) → only its own zone → cannot; 3 others → its zone + 1 → can.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                SeedDeck(p, SilverFang, Hyena);
                var cat = Settled(e, p, RescueCat, 0);
                for (var z = 1; z <= 4; z++) Settled(e, p, Celtic, z, BattlePosition.Defense);
                e.NotifyPublic();
                var full = e.CanActivateSpellTrap(p, cat, fromHand: false);
                p.MonsterZones[4].Occupant = null;
                e.NotifyPublic();
                var oneFree = e.CanActivateSpellTrap(p, cat, fromHand: false);
                Check("Rescue Cat: needs 2 usable zones counting its own (GetMZoneCount(tp,c) > 1)",
                    reached && !full && oneFree, $"reached={reached} full={full} oneFree={oneFree}");
            }
        }

        // ───────────────────────────── Sacred Phoenix of Nephthys ─────────────────────────────

        static void PhoenixDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Destroyed by the player's own Dark Hole in Main Phase 1: Special Summoned in the player's next Standby
                // Phase (not the opponent's), then every Spell/Trap on the field is destroyed.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var phoenix = Settled(e, p, Phoenix, 0);
                var hole = PutInHand(e, p, DarkHole);
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                var inGy = p.Graveyard.Contains(phoenix);
                var mySet = PlaceSetTrap(e, p, Mst, 0);
                EndTurn(e); // the opponent's turn
                var notOnOppTurn = p.Graveyard.Contains(phoenix);
                var oppSet = PlaceSetTrap(e, o, Mst, 1);
                EndTurn(e); // the player's turn: Standby Phase
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == phoenix)
                {
                    e.SetPendingSpecialSummonPlacement(2, BattlePosition.Attack);
                    e.TrySelectEffectTarget(phoenix);
                }

                PassAll(e);
                var revived = p.TryFindMonster(phoenix, out _) && phoenix.FaceUp;
                var swept = !p.TryFindSpellTrap(mySet, out _) && !o.TryFindSpellTrap(oppSet, out _) &&
                            p.Graveyard.Contains(mySet) && o.Graveyard.Contains(oppSet);
                Check("Sacred Phoenix of Nephthys: destroyed by a card effect → revived in your next Standby Phase, then " +
                      "all Spells/Traps on the field are destroyed",
                    reached && used && inGy && notOnOppTurn && revived && swept && e.TurnPlayer == p,
                    $"reached={reached} used={used} gy={inGy} oppTurn={notOnOppTurn} revived={revived} swept={swept} " +
                    $"turn={e.TurnPlayer?.Name} phase={e.Phase}");
            }

            {
                // Destroyed in the opponent's turn by the opponent's Dark Hole: revived in the player's very next Standby.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var phoenix = Settled(e, p, Phoenix, 0);
                var hole = PutInHand(e, o, DarkHole);
                var used = e.TryActivateSpellTrap(o, hole, fromHand: true);
                PassAll(e);
                var inGy = p.Graveyard.Contains(phoenix);
                EndTurn(e);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == phoenix) e.TrySelectEffectTarget(phoenix);
                PassAll(e);
                Check("Sacred Phoenix of Nephthys: destroyed by the opponent's card effect in their turn → revived in your " +
                      "next Standby Phase",
                    reached && used && inGy && p.TryFindMonster(phoenix, out _) && e.TurnPlayer == p,
                    $"reached={reached} used={used} gy={inGy} field={p.TryFindMonster(phoenix, out _)}");
            }

            {
                // Destroyed by battle: no revival.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var phoenix = Settled(e, p, Phoenix, 0, BattlePosition.Defense);
                phoenix.Position = BattlePosition.Defense;
                var attacker = Attacker(e, e.Opponent, BlackSkullDragon, 0);
                e.NotifyPublic();
                e.TryAttack(e.Opponent, attacker, phoenix);
                DrainCombat(e);
                var inGy = p.Graveyard.Contains(phoenix);
                EndTurn(e);
                PassAll(e);
                Check("Sacred Phoenix of Nephthys: destroyed by battle — not revived",
                    reached && inGy && p.Graveyard.Contains(phoenix) && !p.TryFindMonster(phoenix, out _),
                    $"reached={reached} gy={inGy} field={p.TryFindMonster(phoenix, out _)}");
            }

            {
                // The AI's Phoenix: destroyed by the player's Dark Hole, it comes back in the AI's next Standby Phase and
                // destroys the player's Set card.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var phoenix = Settled(e, o, Phoenix, 0);
                var hole = PutInHand(e, p, DarkHole);
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                var mySet = PlaceSetTrap(e, p, Mst, 0);
                mySet.SetThisTurn = true; // Set this turn: it cannot answer the AI's Standby trigger
                e.TryEndTurnSafe(p);
                PassAll(e);
                var revived = o.TryFindMonster(phoenix, out _);
                Check("Sacred Phoenix of Nephthys (AI): revived in its controller's next Standby Phase; the player's Set " +
                      "card is destroyed",
                    reached && used && revived && p.Graveyard.Contains(mySet),
                    $"reached={reached} used={used} revived={revived} set={p.TryFindSpellTrap(mySet, out _)}");
            }
        }

        // ───────────────────────────── Centrifugal Field ─────────────────────────────

        static void CentrifugalDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Flame Swordsman destroyed by the player's Dark Hole: the player targets 1 listed material
                // (2 offered) and Special Summons it.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FieldSpell(e, p, CentrifugalField);
                var fs = Settled(e, p, FlameSwordsman, 0);
                var mani = e.CreateCardInstance(FlameManipulator);
                var masaki = e.CreateCardInstance(Masaki);
                var celticGy = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(mani);
                p.Graveyard.Add(masaki);
                p.Graveyard.Add(celticGy);
                var hole = PutInHand(e, p, DarkHole);
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                var legal = Legal(e);
                var offered = legal.Count == 2 && legal.Contains(mani) && legal.Contains(masaki);
                e.SetPendingSpecialSummonPlacement(1, BattlePosition.Defense);
                var picked = offered && e.TrySelectEffectTarget(masaki);
                PassAll(e);
                Check("Centrifugal Field: a Fusion destroyed by Dark Hole — its owner Special Summons a listed material",
                    reached && used && p.Graveyard.Contains(fs) && offered && picked && p.TryFindMonster(masaki, out _) &&
                    masaki.FaceUp && masaki.Position == BattlePosition.Defense && p.Graveyard.Contains(mani) &&
                    p.Graveyard.Contains(celticGy),
                    $"reached={reached} used={used} legal={string.Join(",", legal.Select(c => c.Name))} picked={picked}");
            }

            {
                // EFFECT_FLAG_EVENT_PLAYER: the player's Field Spell, the AI's Fusion destroyed by the player's Fissure — the
                // AI Special Summons its own listed material.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FieldSpell(e, p, CentrifugalField);
                var fs = Settled(e, o, FlameSwordsman, 0);
                var mani = e.CreateCardInstance(FlameManipulator);
                o.Graveyard.Add(mani);
                var fissure = PutInHand(e, p, Fissure);
                var used = e.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == fissure) e.TrySelectEffectTarget(fs);
                PassAll(e);
                Check("Centrifugal Field: the destroyed Fusion's owner (the AI) activates it and Special Summons for itself",
                    reached && used && o.Graveyard.Contains(fs) && o.TryFindMonster(mani, out _) &&
                     !p.MonstersOnField().Any(), $"reached={reached} used={used} field={o.TryFindMonster(mani, out _)} pend={e.PendingActivation?.Card?.Name}");
            }

            {
                // Lua EFFECT_FLAG_CARD_TARGET (SelectTarget in s.sptg): the owner targets as the trigger activates. The AI
                // owner targets Masaki (higher ATK); the player may respond (a Set MST); Masaki leaves the GY in response
                // (standing in for a GY banish such as D.D. Crow): the effect resolves with no target (spop
                // IsRelateToEffect) and Flame Manipulator is not Summoned in its place.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FieldSpell(e, p, CentrifugalField);
                var fs = Settled(e, o, FlameSwordsman, 0);
                var mani = e.CreateCardInstance(FlameManipulator);
                var masaki = e.CreateCardInstance(Masaki);
                o.Graveyard.Add(mani);
                o.Graveyard.Add(masaki);
                PlaceSetTrap(e, p, Mst, 0);
                var fissure = PutInHand(e, p, Fissure);
                var used = e.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.Card == fissure) e.TrySelectEffectTarget(fs);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                if (window)
                {
                    o.Graveyard.Remove(masaki);
                    o.Banished.Add(masaki);
                    e.PassResponse();
                }

                PassAll(e);
                Check("Centrifugal Field: the target is chosen as it activates — gone in response, nothing is Summoned",
                    reached && used && o.Graveyard.Contains(fs) && window && o.Graveyard.Contains(mani) &&
                    !o.MonstersOnField().Any(),
                    $"reached={reached} used={used} window={window} maniGy={o.Graveyard.Contains(mani)} " +
                    $"field={string.Join(",", o.MonstersOnField().Select(m => m.Name))}");
            }

            {
                // Destroyed by battle: nothing.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FieldSpell(e, p, CentrifugalField);
                var fs = Settled(e, p, FlameSwordsman, 0);
                var mani = e.CreateCardInstance(FlameManipulator);
                p.Graveyard.Add(mani);
                var skull = Attacker(e, e.Opponent, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(e.Opponent, skull, fs);
                DrainCombat(e);
                Check("Centrifugal Field: a Fusion destroyed by battle does not trigger it",
                    reached && p.Graveyard.Contains(fs) && p.Graveyard.Contains(mani) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} fsGy={p.Graveyard.Contains(fs)} pending={e.IsAwaitingEffectTarget}");
            }

            {
                // No listed material in the GY (only an unlisted monster): nothing happens, no prompt.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                FieldSpell(e, p, CentrifugalField);
                var fs = Settled(e, p, FlameSwordsman, 0);
                var celticGy = e.CreateCardInstance(Celtic);
                p.Graveyard.Add(celticGy);
                var hole = PutInHand(e, p, DarkHole);
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                PassAll(e);
                Check("Centrifugal Field: no listed material in the GY — nothing happens",
                    reached && used && p.Graveyard.Contains(fs) && p.Graveyard.Contains(celticGy) &&
                    !e.IsAwaitingEffectTarget && !p.MonstersOnField().Any(), $"reached={reached} used={used}");
            }
        }

        // ───────────────────────────── The Hex-Sealed Fusions ─────────────────────────────

        static void HexSealedDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The Dark: substitutes for "Red-Eyes Black Dragon"; Tributed with Summoned Skull → Black Skull Dragon.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(BlackSkullDragon);
                p.ExtraDeck.Add(FlameSwordsman);
                var hex = Settled(e, p, HexDark, 0);
                var skull = Settled(e, p, SummonedSkull, 1);
                e.NotifyPublic();
                var can = e.CanActivateSpellTrap(p, hex, fromHand: false);
                var used = e.TryActivateSpellTrap(p, hex, fromHand: false);
                var legal = Legal(e);
                var offered = legal.Count == 1 && legal[0].CardId == BlackSkullDragon;
                e.SetPendingSpecialSummonPlacement(3, BattlePosition.Attack);
                var picked = offered && e.TrySelectEffectTarget(legal[0]);
                PassAll(e);
                var bsd = OnField(p, BlackSkullDragon).FirstOrDefault();
                Check("The Dark - Hex-Sealed Fusion: Tribute it + Summoned Skull → Black Skull Dragon (not Flame " +
                      "Swordsman), not a Fusion Summon",
                    reached && can && used && offered && picked && bsd != null && bsd.FaceUp &&
                    p.Graveyard.Contains(hex) && p.Graveyard.Contains(skull) && !p.ExtraDeck.Contains(BlackSkullDragon) &&
                    p.ExtraDeck.Contains(FlameSwordsman) && !bsd.ProcedureCompleted && !e.Chain.HasLinks,
                    $"reached={reached} can={can} used={used} legal={string.Join(",", legal.Select(c => c.Name))} " +
                    $"picked={picked} bsd={bsd != null} chain={e.Chain.Count}");
            }

            foreach (var leaves in new[] { false, true })
            {
                // Lua: s.target Releases the materials (REASON_COST) and stores the code; the opponent may respond to Chain
                // Link 1; s.operation Special Summons the Fusion if it is still in the Extra Deck. The AI activates The
                // Dark; the player (a Set MST) gets the window after the Tributes, before Black Skull Dragon arrives. With
                // the Fusion gone from the Extra Deck in response, the Tributes stay paid and nothing is Summoned.
                var e = OpponentMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                o.ExtraDeck.Clear();
                o.ExtraDeck.Add(BlackSkullDragon);
                var hex = Settled(e, o, HexDark, 0);
                var skull = Settled(e, o, SummonedSkull, 1);
                PlaceSetTrap(e, p, Mst, 0);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(o, hex, fromHand: false);
                var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
                var paid = o.Graveyard.Contains(hex) && o.Graveyard.Contains(skull);
                var notYet = OnField(o, BlackSkullDragon).Count == 0 && o.ExtraDeck.Contains(BlackSkullDragon);
                if (window)
                {
                    if (leaves) o.ExtraDeck.Remove(BlackSkullDragon);
                    e.PassResponse();
                }

                PassAll(e);
                var bsd = OnField(o, BlackSkullDragon).FirstOrDefault();
                var summoned = leaves ? bsd == null : bsd != null && bsd.FaceUp && !o.ExtraDeck.Contains(BlackSkullDragon);
                Check(leaves
                        ? "Hex-Sealed: the Fusion leaves the Extra Deck in response — the Tributes stay paid, nothing is Summoned"
                        : "Hex-Sealed: Tribute (cost) at activation, the opponent may respond, then the Fusion is Summoned",
                    reached && used && window && paid && notYet && summoned && o.Graveyard.Contains(hex) &&
                    !e.Chain.HasLinks,
                    $"reached={reached} used={used} window={window} paid={paid} notYet={notYet} bsd={bsd != null} " +
                    $"chain={e.Chain.Count}");
            }

            {
                // Two Summoned Skulls: the human chooses which one is Tributed.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(BlackSkullDragon);
                var hex = Settled(e, p, HexDark, 0);
                var skull1 = Settled(e, p, SummonedSkull, 1);
                var skull2 = Settled(e, p, SummonedSkull, 2);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, hex, fromHand: false);
                var fusionPick = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(Legal(e)[0]);
                var mats = Legal(e);
                var asked = mats.Count == 2 && mats.Contains(skull1) && mats.Contains(skull2);
                var picked = asked && e.TrySelectEffectTarget(skull2);
                PassAll(e);
                Check("Hex-Sealed: with two possible material sets the human chooses the other material",
                    reached && used && fusionPick && asked && picked && p.Graveyard.Contains(skull2) &&
                    p.TryFindMonster(skull1, out _) && OnField(p, BlackSkullDragon).Count == 1,
                    $"reached={reached} used={used} fusion={fusionPick} mats={mats.Count} picked={picked}");
            }

            {
                // CANCEL at the Fusion pick: nothing is Tributed.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(BlackSkullDragon);
                var hex = Settled(e, p, HexDark, 0);
                var skull = Settled(e, p, SummonedSkull, 1);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, hex, fromHand: false);
                var cancelled = e.IsAwaitingEffectTarget && e.CancelEffectTargeting();
                PassAll(e);
                Check("Hex-Sealed: CANCEL at the Fusion pick Tributes nothing",
                    reached && used && cancelled && p.TryFindMonster(hex, out _) && p.TryFindMonster(skull, out _) &&
                    p.ExtraDeck.Contains(BlackSkullDragon) && !e.Chain.HasLinks,
                    $"reached={reached} used={used} cancel={cancelled} chain={e.Chain.Count}");
            }

            {
                // The Attribute must match: The Light / The Earth cannot summon the DARK Black Skull Dragon.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(BlackSkullDragon);
                var light = Settled(e, p, HexLight, 0);
                var earth = Settled(e, p, HexEarth, 2);
                Settled(e, p, SummonedSkull, 1);
                e.NotifyPublic();
                Check("Hex-Sealed: only a Fusion Monster of its own Attribute (The Light / The Earth: no DARK Fusion)",
                    reached && !e.CanActivateSpellTrap(p, light, fromHand: false) &&
                    !e.CanActivateSpellTrap(p, earth, fromHand: false), $"reached={reached}");
            }

            {
                // The other material must be on the field (not in the hand), and a Fusion with no named materials there
                // is not offered.
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(BlackSkullDragon);
                var hex = Settled(e, p, HexDark, 0);
                PutInHand(e, p, SummonedSkull);
                e.NotifyPublic();
                Check("Hex-Sealed: the other material in the hand does not count (field only)",
                    reached && !e.CanActivateSpellTrap(p, hex, fromHand: false), $"reached={reached}");
            }
        }
    }
}
