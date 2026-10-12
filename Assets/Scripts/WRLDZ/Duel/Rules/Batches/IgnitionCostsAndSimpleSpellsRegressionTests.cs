using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// IgnitionCostsAndSimpleSpells batch: 3-Hump Lacooda, Avatar of The Pot, Lady Ninja Yae, Sand Gambler, Mystik Wok, Solar
    /// Ray, Special Hurricane. Compile shapes through the real compiler, near misses that must not take these shapes, and
    /// mini-duels through the engine entry points (TryActivateSpellTrap, TrySelectEffectTarget, CancelEffectTargeting, the
    /// queued coins) on both the player's and the AI's side. Runtime: <see cref="IgnitionCostsAndSimpleSpellsRuntime"/>.
    /// </summary>
    public static class IgnitionCostsAndSimpleSpellsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Lacooda = 86988864;
        const int AvatarOfThePot = 99284890;
        const int LadyNinjaYae = 82005435;
        const int SandGambler = 50593156;
        const int MystikWok = 80161395;
        const int SolarRay = 44472639;
        const int SpecialHurricane = 42598242;

        const int Celtic = 91152256;          // Normal Warrior EARTH Lv4 1400/1200
        const int GiantSoldier = 13039848;    // Normal Rock EARTH Lv3 1300/2000
        const int Kuriboh = 40640057;         // DARK Lv1 300/200
        const int HarpieLady = 76812113;      // Normal WIND Lv4 1300/1400
        const int Bewd = 89631139;            // Normal LIGHT Lv8 3000/2500
        const int MysticalElf = 15025844;     // Normal LIGHT Lv4 800/2000
        const int OjamaYellow = 42941100;     // Normal LIGHT Lv2 0/1000
        const int PotOfGreed = 55144522;
        const int Yami = 59197169;            // Field Spell
        const int Swords = 72302403;          // Normal Spell

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("IgnitionCostsAndSimpleSpells: " + name, ok, detail ?? "");

            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "near misses", () => NearMisses(db, Check));
            Guard(Check, "3-Hump Lacooda", () => LacoodaDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Avatar of The Pot", () => AvatarDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Lady Ninja Yae", () => YaeDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Sand Gambler", () => SandGamblerDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Mystik Wok", () => MystikWokDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Solar Ray", () => SolarRayDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Mystik Wok windows", () => MystikWokWindows(db, pDeck, aDeck, Check));
            Guard(Check, "Special Hurricane", () => SpecialHurricaneDuels(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk check, string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                check(label + ": threw", false, ex.ToString());
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}";

        static EffectClause Only(CompiledCardProgram p) => p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram Synthetic(CardDatabase db, int likeId, string desc)
        {
            var like = db.Get(likeId);
            return CardTextEffectCompiler.Compile(new CardDef
            {
                id = 0,
                name = "IgnitionCostsAndSimpleSpells near miss",
                type = like.type,
                frameType = like.frameType,
                desc = desc,
                atk = like.atk,
                def = like.def,
                level = like.level,
                race = like.race,
                attribute = like.attribute
            });
        }

        /// <summary>
        /// The player's Main Phase 1 (turn 3+) or the AI's Main Phase; boards, hands and GYs empty, 8000 LP, 10-card Decks.
        /// </summary>
        static DuelEngine MainOf(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, out DuelistState p,
            out DuelistState o, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            if (player)
            {
                for (var t = 0; t < 10 &&
                                !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                                  engine.Phase == DuelPhase.Main1); t++)
                {
                    if (engine.IsAwaitingResponse) engine.PassResponse();
                    engine.RecoverStuckCombat();
                    engine.TryEndTurnSafe(engine.TurnPlayer);
                }

                if (engine.IsAwaitingResponse) engine.PassResponse();
                reached = engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1;
            }
            else
            {
                reached = ReachOpponentMain(engine);
            }

            ClearBoard(engine);
            p = engine.Player;
            o = engine.Opponent;
            foreach (var who in new[] { p, o })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
                who.Deck.Clear();
                for (var i = 0; i < 10; i++) who.Deck.Add(Celtic);
            }

            return engine;
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static List<string> Capture(DuelEngine engine)
        {
            var lines = new List<string>();
            engine.OnLog += s => lines.Add(s);
            return lines;
        }

        static CardInstance FaceUp(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack) =>
            PlaceMonster(e, who, id, zone, pos, true);

        static CardInstance Token(DuelEngine e, DuelistState who, int zone, int atk, int def)
        {
            var t = e.CreateToken("Test Token", "Beast", "EARTH", 1, atk, def);
            t.FaceUp = true;
            t.Position = BattlePosition.Attack;
            who.MonsterZones[zone].Occupant = t;
            return t;
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "null";
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.TryFindMonster(c, out _)) return tag + ".monster";
                if (who.TryFindSpellTrap(c, out _)) return tag + ".st";
                if (who.FieldSpellZone?.Occupant == c) return tag + ".field";
                if (who.Hand.Contains(c)) return tag + ".hand";
                if (who.Graveyard.Contains(c)) return tag + ".gy";
            }

            return "gone";
        }

        static bool Discarded(CardInstance c) => c != null && (c.GyEntryReason & GyEntryReason.Discard) != 0;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var la = Prog(db, Lacooda);
            var lc = Only(la);
            Check("3-Hump Lacooda: Implemented — ignition, 3 face-up copies, Tribute 2 face-up copies, draw 3",
                Implemented(db, Lacooda) && la.FullyCompiled && lc != null && lc.Timing == EffectTiming.Activate &&
                lc.Action == EffectActionKind.Draw && lc.Side == EffectSide.Controller && lc.Amount == 3 &&
                lc.RequiresTributeCount == 2 && lc.TributeFaceUpOnly && lc.TributeNamedCard == "3-Hump Lacooda" &&
                lc.ActivationCondition == IgnitionCostsAndSimpleSpellsRuntime.FaceUpNamedYouControlAtLeast &&
                lc.ConditionAmount == 3 && lc.ConditionName == "3-Hump Lacooda" && !lc.OncePerTurn &&
                !lc.RequiresTargetChoice && EffectVocabulary.CostOf(lc) == EffectCostKind.Tribute,
                Describe(la));

            var av = Prog(db, AvatarOfThePot);
            var ac = Only(av);
            Check("Avatar of The Pot: Implemented — ignition, SEND 1 \"Pot of Greed\" from the hand (not a discard), draw 3",
                Implemented(db, AvatarOfThePot) && av.FullyCompiled && ac != null && ac.Timing == EffectTiming.Activate &&
                ac.Action == EffectActionKind.Draw && ac.Amount == 3 && ac.RequiresDiscardCost && ac.DiscardCostIsSend &&
                IgnitionCostsAndSimpleSpellsRuntime.NamedHandCardName(ac.DiscardCostAttribute) == "Pot of Greed" &&
                ac.ActivationCondition == DrawThenDiscardRuntime.CanDrawAmount &&
                EffectVocabulary.CostOf(ac) == EffectCostKind.SendToGy,
                Describe(av));

            var ya = Prog(db, LadyNinjaYae);
            var yc = Only(ya);
            Check("Lady Ninja Yae: Implemented — ignition, discard 1 WIND monster, every opponent Spell/Trap to the hand",
                Implemented(db, LadyNinjaYae) && ya.FullyCompiled && yc != null && yc.Timing == EffectTiming.Activate &&
                yc.Action == EffectActionKind.ReturnToHand && yc.Side == EffectSide.Opponent &&
                yc.Zone == EffectZoneFilter.FieldSpellTraps && !yc.RequiresTargetChoice && yc.RequiresDiscardCost &&
                !yc.DiscardCostIsSend && yc.DiscardCostAttribute == "WIND" &&
                yc.ActivationCondition == IgnitionCostsAndSimpleSpellsRuntime.OpponentControlsSpellTrap &&
                EffectVocabulary.CostOf(yc) == EffectCostKind.Discard,
                Describe(ya));

            var sg = Prog(db, SandGambler);
            var sc = Only(sg);
            Check("Sand Gambler: Implemented — once-per-turn ignition, 3 coins, all Heads / all Tails destroys",
                Implemented(db, SandGambler) && sg.FullyCompiled && sc != null && sc.Timing == EffectTiming.Activate &&
                sc.Action == EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours &&
                sc.CoinCount == 3 && sc.OncePerTurn && !sc.RequiresTargetChoice &&
                EffectVocabulary.ResolutionOf(sc) == EffectResolutionKind.Randomize,
                Describe(sg));

            var mw = Prog(db, MystikWok);
            var mc = Only(mw);
            Check("Mystik Wok: Implemented — Tribute 1 monster (cost), gain its ATK or DEF",
                Implemented(db, MystikWok) && mw.FullyCompiled && mc != null && mc.Timing == EffectTiming.Activate &&
                mc.Action == EffectActionKind.GainLpEqualToTributedAtkOrDef && mc.RequiresTributeCount == 1 &&
                !mc.RequiresTargetChoice && EffectVocabulary.ResolutionOf(mc) == EffectResolutionKind.GainLp &&
                EffectVocabulary.CostOf(mc) == EffectCostKind.Tribute,
                Describe(mw));

            var sr = Prog(db, SolarRay);
            var rc = Only(sr);
            Check("Solar Ray: Implemented — 600 x face-up LIGHT monsters you control, needs 1+",
                Implemented(db, SolarRay) && sr.FullyCompiled && rc != null && rc.Timing == EffectTiming.Activate &&
                rc.Action == EffectActionKind.InflictDamageToOpponent && rc.Amount == 600 &&
                rc.CountSource == IgnitionCostsAndSimpleSpellsRuntime.YourFaceUpMonstersOfConditionAttribute &&
                rc.ActivationCondition == IgnitionCostsAndSimpleSpellsRuntime.YouControlFaceUpAttribute &&
                rc.ConditionName == "LIGHT" && rc.ConditionAmount == 1 && !rc.RequiresTargetChoice,
                Describe(sr));

            var sh = Prog(db, SpecialHurricane);
            var hc = Only(sh);
            Check("Special Hurricane: Implemented — discard 1 other card, destroy every Special Summoned monster",
                Implemented(db, SpecialHurricane) && sh.FullyCompiled && hc != null && hc.Timing == EffectTiming.Activate &&
                hc.Action == EffectActionKind.DestroySpecialSummonedMonsters && hc.Side == EffectSide.Both &&
                hc.RequiresDiscardCost && hc.DiscardCostAttribute == "*" && !hc.DiscardCostRandom &&
                !hc.RequiresTargetChoice &&
                hc.ActivationCondition == RuleLocksRuntime.SpecialSummonedMonsterOnField,
                Describe(sh));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            bool NotShape(CompiledCardProgram p, Func<EffectClause, bool> shape) =>
                p != null && !(p.FullyCompiled && p.ClauseList.Exists(c => c != null && shape(c)));

            var moreTributes = Synthetic(db, Lacooda,
                "If there are 3 face-up \"3-Hump Lacooda\" cards on your side of the field, Tribute 4 of them to draw 3 cards.");
            var lacoodaRider = Synthetic(db, Lacooda,
                "If there are 3 face-up \"3-Hump Lacooda\" cards on your side of the field, Tribute 2 of them to draw 3 " +
                "cards. Then discard 1 card.");
            Check("Near miss: a Lacooda text Tributing more copies than it needs, or with a rider, is not compiled",
                NotShape(moreTributes, c => c.RequiresTributeCount > 0 && c.TributeFaceUpOnly) &&
                NotShape(lacoodaRider, c => c.RequiresTributeCount == 2 && c.TributeFaceUpOnly),
                Describe(moreTributes) + " / " + Describe(lacoodaRider));

            var avatarRider = Synthetic(db, AvatarOfThePot,
                "By sending 1 \"Pot of Greed\" from your hand to the Graveyard, draw 3 cards from your Deck. " +
                "You cannot Special Summon this turn.");
            Check("Near miss: Avatar of The Pot's sentence with a rider is not compiled",
                NotShape(avatarRider, c => c.DiscardCostIsSend && c.Action == EffectActionKind.Draw),
                Describe(avatarRider));

            var yaeField = Synthetic(db, LadyNinjaYae,
                "By discarding 1 WIND monster from your hand to the Graveyard, return all Spell and Trap Cards on the " +
                "field to the owner's hand.");
            Check("Near miss: \"all Spell and Trap Cards on the field\" does not take Lady Ninja Yae's opponent-only shape",
                NotShape(yaeField, c => c.Action == EffectActionKind.ReturnToHand && c.Side == EffectSide.Opponent &&
                                        c.DiscardCostAttribute == "WIND"),
                Describe(yaeField));

            var gamblerHalf = Synthetic(db, SandGambler,
                "Toss a coin 3 times. If all 3 results are Heads, destroy all monsters on your opponent's side of the " +
                "field. You can only activate this effect once per turn, during your Main Phase.");
            var gamblerMixed = Synthetic(db, SandGambler,
                "Toss a coin 3 times. If all 2 results are Heads, destroy all monsters on your opponent's side of the " +
                "field. If all 2 results are Tails, destroy all monsters on your side of the field. You can only " +
                "activate this effect once per turn, during your Main Phase.");
            Check("Near miss: a Sand Gambler text without the Tails sentence, or with a different coin count, is not compiled",
                NotShape(gamblerHalf, c => c.Action == EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours) &&
                NotShape(gamblerMixed, c => c.Action == EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours),
                Describe(gamblerHalf) + " / " + Describe(gamblerMixed));

            var wokRider = Synthetic(db, MystikWok,
                "Tribute 1 monster. Select either its ATK or DEF and gain that many Life Points. Then draw 1 card.");
            Check("Near miss: Mystik Wok's text with a rider is not compiled",
                NotShape(wokRider, c => c.Action == EffectActionKind.GainLpEqualToTributedAtkOrDef),
                Describe(wokRider));

            var rayField = Synthetic(db, SolarRay,
                "Inflict 600 points of damage to your opponent's Life Points for each face-up LIGHT monster on the field.");
            var rayDark = Synthetic(db, SolarRay,
                "Inflict 600 points of damage to your opponent's Life Points for each face-up DARK monster on your side " +
                "of the field.");
            var darkClause = Only(rayDark);
            Check("Near miss: \"on the field\" is not Solar Ray's own-side count; a DARK twin counts DARK",
                NotShape(rayField, c => c.CountSource == IgnitionCostsAndSimpleSpellsRuntime.YourFaceUpMonstersOfConditionAttribute) &&
                darkClause != null && rayDark.FullyCompiled && darkClause.ConditionName == "DARK",
                Describe(rayField) + " / " + Describe(rayDark));

            var hurricaneOpp = Synthetic(db, SpecialHurricane,
                "Discard 1 card from your hand. Destroy all Special Summoned monsters your opponent controls.");
            Check("Near miss: Special Hurricane's sentence with \"your opponent controls\" does not take its shape",
                NotShape(hurricaneOpp, c => c.Action == EffectActionKind.DestroySpecialSummonedMonsters),
                Describe(hurricaneOpp));
        }

        // ───────────────────────────── 3-Hump Lacooda ─────────────────────────────

        static void LacoodaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human Tributes 2 of the 3 face-up copies (the activating copy may be one); draw 3. A face-down copy is
                // not offered.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var l1 = FaceUp(e, p, Lacooda, 0);
                var l2 = FaceUp(e, p, Lacooda, 1);
                var l3 = FaceUp(e, p, Lacooda, 2, BattlePosition.Defense);
                var down = PlaceMonster(e, p, Lacooda, 3, BattlePosition.Defense, false);
                var can = e.CanActivateSpellTrap(p, l1, fromHand: false);
                var act = can && e.TryActivateSpellTrap(p, l1, fromHand: false);
                var legal1 = Legal(e);
                var kind = e.PendingActivation?.TargetKind;
                var pick1 = legal1.Contains(l1) && e.TrySelectEffectTarget(l1);
                var legal2 = Legal(e);
                var pick2 = legal2.Contains(l2) && e.TrySelectEffectTarget(l2);
                Settle(e);
                Check("3-Hump Lacooda: the human Tributes 2 face-up copies (itself included), draws 3; Set copy not offered",
                    reached && can && act && kind == EffectTargetKind.TributeMonsterYouControl && legal1.Count == 3 &&
                    !legal1.Contains(down) && pick1 && pick2 && !legal2.Contains(down) && p.Graveyard.Contains(l1) &&
                    p.Graveyard.Contains(l2) && p.TryFindMonster(l3, out _) && p.TryFindMonster(down, out _) &&
                    p.Hand.Count == 3 && p.Deck.Count == 7 && e.PendingActivation == null,
                    $"can={can} act={act} kind={kind} legal1={legal1.Count} legal2={legal2.Count} p1={pick1} p2={pick2} " +
                    $"l1={Where(e, l1)} l2={Where(e, l2)} hand={p.Hand.Count} deck={p.Deck.Count}");
            }

            {
                // 2 face-up + 1 face-down: refused. 3 face-up but only 2 cards in the Deck: refused (IsPlayerCanDraw 3).
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var l1 = FaceUp(e, p, Lacooda, 0);
                FaceUp(e, p, Lacooda, 1);
                var l3 = PlaceMonster(e, p, Lacooda, 2, BattlePosition.Defense, false);
                var twoUp = e.CanActivateSpellTrap(p, l1, fromHand: false);
                l3.FaceUp = true;
                var threeUp = e.CanActivateSpellTrap(p, l1, fromHand: false);
                p.Deck.RemoveRange(0, p.Deck.Count - 2);
                var shortDeck = e.CanActivateSpellTrap(p, l1, fromHand: false);
                Check("3-Hump Lacooda: needs 3 face-up copies and 3 cards to draw",
                    reached && !twoUp && threeUp && !shortDeck,
                    $"twoUp={twoUp} threeUp={threeUp} shortDeck={shortDeck}");
            }

            {
                // Cancel at the Tribute pick: nothing Tributed, nothing drawn, no Chain Link left.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var l1 = FaceUp(e, p, Lacooda, 0);
                FaceUp(e, p, Lacooda, 1);
                FaceUp(e, p, Lacooda, 2);
                var act = e.TryActivateSpellTrap(p, l1, fromHand: false) && e.IsAwaitingEffectTarget;
                var cancelled = e.CancelEffectTargeting();
                Settle(e);
                Check("3-Hump Lacooda: Cancel at the Tribute pick → nothing Tributed or drawn",
                    reached && act && cancelled && p.MonsterCount == 3 && p.Hand.Count == 0 && p.Deck.Count == 10 &&
                    !e.Chain.HasLinks && e.PendingActivation == null &&
                    e.CanActivateSpellTrap(p, l1, fromHand: false),
                    $"act={act} cancel={cancelled} mons={p.MonsterCount} hand={p.Hand.Count} chain={e.Chain.HasLinks}");
            }

            {
                // The AI: Tributes the 2 other copies, keeps the activating one, draws 3.
                var e = MainOf(db, pDeck, aDeck, false, out _, out var o, out var reached);
                var l1 = FaceUp(e, o, Lacooda, 0);
                var l2 = FaceUp(e, o, Lacooda, 1);
                var l3 = FaceUp(e, o, Lacooda, 2);
                var act = e.CanActivateSpellTrap(o, l1, fromHand: false) && e.TryActivateSpellTrap(o, l1, fromHand: false);
                Settle(e);
                Check("3-Hump Lacooda (AI): Tributes the 2 other copies, draws 3",
                    reached && act && o.TryFindMonster(l1, out _) && o.Graveyard.Contains(l2) && o.Graveyard.Contains(l3) &&
                    o.Hand.Count == 3 && e.PendingActivation == null,
                    $"act={act} l1={Where(e, l1)} l2={Where(e, l2)} l3={Where(e, l3)} hand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── Avatar of The Pot ─────────────────────────────

        static void AvatarDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human: only "Pot of Greed" is offered; it is sent (not discarded); draw 3.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var av = FaceUp(e, p, AvatarOfThePot, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                var other = PutInHand(e, p, Celtic);
                var can = e.CanActivateSpellTrap(p, av, fromHand: false);
                var act = can && e.TryActivateSpellTrap(p, av, fromHand: false);
                var legal = Legal(e);
                var prompt = e.PendingActivation?.Prompt ?? "";
                var picked = legal.Count == 1 && legal[0] == pot && e.TrySelectEffectTarget(pot);
                Settle(e);
                Check("Avatar of The Pot: the human sends \"Pot of Greed\" (no discard), draws 3",
                    reached && can && act && picked && prompt.Contains("Pot of Greed") && p.Graveyard.Contains(pot) &&
                    !Discarded(pot) && p.Hand.Contains(other) && p.Hand.Count == 4 && p.Deck.Count == 7,
                    $"can={can} act={act} legal={legal.Count} prompt='{prompt}' picked={picked} pot={Where(e, pot)} " +
                    $"reason={pot.GyEntryReason} hand={p.Hand.Count}");
            }

            {
                // No "Pot of Greed" in hand, or a Deck of 2: refused.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var av = FaceUp(e, p, AvatarOfThePot, 0);
                PutInHand(e, p, Celtic);
                PutInHand(e, p, Swords);
                var noPot = e.CanActivateSpellTrap(p, av, fromHand: false);
                PutInHand(e, p, PotOfGreed);
                var withPot = e.CanActivateSpellTrap(p, av, fromHand: false);
                p.Deck.RemoveRange(0, p.Deck.Count - 2);
                var shortDeck = e.CanActivateSpellTrap(p, av, fromHand: false);
                Check("Avatar of The Pot: needs \"Pot of Greed\" in hand and 3 cards to draw",
                    reached && !noPot && withPot && !shortDeck, $"noPot={noPot} withPot={withPot} short={shortDeck}");
            }

            {
                // Cancel at the send pick: "Pot of Greed" stays in the hand, nothing drawn, no Chain Link left.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var av = FaceUp(e, p, AvatarOfThePot, 0);
                var pot = PutInHand(e, p, PotOfGreed);
                PutInHand(e, p, Celtic);
                var act = e.TryActivateSpellTrap(p, av, fromHand: false) && e.IsAwaitingEffectTarget;
                var cancelled = e.CancelEffectTargeting();
                Settle(e);
                Check("Avatar of The Pot: Cancel at the send pick → nothing sent or drawn",
                    reached && act && cancelled && p.Hand.Contains(pot) && p.Hand.Count == 2 && p.Deck.Count == 10 &&
                    !e.Chain.HasLinks && e.PendingActivation == null && e.CanActivateSpellTrap(p, av, fromHand: false),
                    $"act={act} cancel={cancelled} pot={Where(e, pot)} hand={p.Hand.Count} deck={p.Deck.Count} " +
                    $"chain={e.Chain.HasLinks}");
            }

            {
                // The AI sends its "Pot of Greed" and draws 3.
                var e = MainOf(db, pDeck, aDeck, false, out _, out var o, out var reached);
                var log = Capture(e);
                var av = FaceUp(e, o, AvatarOfThePot, 0);
                var pot = PutInHand(e, o, PotOfGreed);
                var act = e.CanActivateSpellTrap(o, av, fromHand: false) && e.TryActivateSpellTrap(o, av, fromHand: false);
                Settle(e);
                Check("Avatar of The Pot (AI): sends \"Pot of Greed\" (no discard), draws 3",
                    reached && act && o.Graveyard.Contains(pot) && !Discarded(pot) && o.Hand.Count == 3 &&
                    log.Exists(l => l.Contains("Cost: send Pot of Greed")),
                    $"act={act} pot={Where(e, pot)} reason={pot.GyEntryReason} hand={o.Hand.Count}");
            }
        }

        // ───────────────────────────── Lady Ninja Yae ─────────────────────────────

        static void YaeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human discards the WIND monster (the DARK one is not offered); every Spell/Trap the opponent controls
                // (Set card and Field Spell) returns to the hand; the player's own Set card stays.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var yae = FaceUp(e, p, LadyNinjaYae, 0);
                var harpie = PutInHand(e, p, HarpieLady);
                var kuri = PutInHand(e, p, Kuriboh);
                var mySet = PlaceSetTrap(e, p, PotOfGreed, 0);
                var oppSet = PlaceSetTrap(e, o, PotOfGreed, 1);
                var yami = e.CreateCardInstance(Yami);
                yami.FaceUp = true;
                o.FieldSpellZone.Occupant = yami;
                var can = e.CanActivateSpellTrap(p, yae, fromHand: false);
                var act = can && e.TryActivateSpellTrap(p, yae, fromHand: false);
                var legal = Legal(e);
                var picked = legal.Count == 1 && legal[0] == harpie && e.TrySelectEffectTarget(harpie);
                Settle(e);
                Check("Lady Ninja Yae: the human discards the WIND monster; the opponent's Set card and Field Spell return",
                    reached && can && act && picked && p.Graveyard.Contains(harpie) && Discarded(harpie) &&
                    p.Hand.Contains(kuri) && o.Hand.Contains(oppSet) && o.Hand.Contains(yami) &&
                    o.FieldSpellZone.Occupant == null && p.TryFindSpellTrap(mySet, out _) && p.TryFindMonster(yae, out _),
                    $"can={can} act={act} legal={legal.Count} picked={picked} harpie={Where(e, harpie)} " +
                    $"oppSet={Where(e, oppSet)} yami={Where(e, yami)} mySet={Where(e, mySet)}");
            }

            {
                // No WIND monster in hand, or no opponent Spell/Trap: refused.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var yae = FaceUp(e, p, LadyNinjaYae, 0);
                PutInHand(e, p, Kuriboh);
                PlaceSetTrap(e, o, PotOfGreed, 0);
                var noWind = e.CanActivateSpellTrap(p, yae, fromHand: false);
                PutInHand(e, p, HarpieLady);
                var ok = e.CanActivateSpellTrap(p, yae, fromHand: false);
                o.SpellTrapZones[0].Occupant = null;
                var noTarget = e.CanActivateSpellTrap(p, yae, fromHand: false);
                Check("Lady Ninja Yae: needs a WIND monster in hand and an opponent Spell/Trap",
                    reached && !noWind && ok && !noTarget, $"noWind={noWind} ok={ok} noTarget={noTarget}");
            }

            {
                // Cancel at the discard pick: the WIND monster stays in the hand, the opponent's Set card stays.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var yae = FaceUp(e, p, LadyNinjaYae, 0);
                var harpie = PutInHand(e, p, HarpieLady);
                var oppSet = PlaceSetTrap(e, o, PotOfGreed, 1);
                var act = e.TryActivateSpellTrap(p, yae, fromHand: false) && e.IsAwaitingEffectTarget;
                var cancelled = e.CancelEffectTargeting();
                Settle(e);
                Check("Lady Ninja Yae: Cancel at the discard pick → nothing discarded or returned",
                    reached && act && cancelled && p.Hand.Contains(harpie) && o.TryFindSpellTrap(oppSet, out _) &&
                    !e.Chain.HasLinks && e.PendingActivation == null,
                    $"act={act} cancel={cancelled} harpie={Where(e, harpie)} oppSet={Where(e, oppSet)} chain={e.Chain.HasLinks}");
            }

            {
                // The AI discards its WIND monster and returns the player's Spell/Trap Cards.
                var e = MainOf(db, pDeck, aDeck, false, out var p, out var o, out var reached);
                var yae = FaceUp(e, o, LadyNinjaYae, 0);
                var harpie = PutInHand(e, o, HarpieLady);
                var pSet = PlaceSetTrap(e, p, PotOfGreed, 2);
                var act = e.CanActivateSpellTrap(o, yae, fromHand: false) && e.TryActivateSpellTrap(o, yae, fromHand: false);
                Settle(e);
                Check("Lady Ninja Yae (AI): discards its WIND monster; the player's Set card returns to the hand",
                    reached && act && o.Graveyard.Contains(harpie) && p.Hand.Contains(pSet) && e.PendingActivation == null,
                    $"act={act} harpie={Where(e, harpie)} pSet={Where(e, pSet)}");
            }
        }

        // ───────────────────────────── Sand Gambler ─────────────────────────────

        static void SandGamblerDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 3 Heads: every monster the opponent controls (face-down too) is destroyed; once per turn.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var sg = FaceUp(e, p, SandGambler, 0);
                var mine = FaceUp(e, p, Celtic, 1);
                var o1 = FaceUp(e, o, Celtic, 0);
                var o2 = PlaceMonster(e, o, GiantSoldier, 1, BattlePosition.Defense, false);
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(true);
                var act = e.CanActivateSpellTrap(p, sg, fromHand: false) && e.TryActivateSpellTrap(p, sg, fromHand: false);
                Settle(e);
                var again = e.CanActivateSpellTrap(p, sg, fromHand: false);
                Check("Sand Gambler: 3 Heads destroys every opponent monster; once per turn",
                    reached && act && o.Graveyard.Contains(o1) && o.Graveyard.Contains(o2) && p.TryFindMonster(sg, out _) &&
                    p.TryFindMonster(mine, out _) && !again,
                    $"act={act} o1={Where(e, o1)} o2={Where(e, o2)} sg={Where(e, sg)} again={again}");
            }

            {
                // 3 Tails: every monster you control is destroyed, Sand Gambler included.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var sg = FaceUp(e, p, SandGambler, 0);
                var mine = FaceUp(e, p, Celtic, 1);
                var o1 = FaceUp(e, o, Celtic, 0);
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(false);
                var act = e.TryActivateSpellTrap(p, sg, fromHand: false);
                Settle(e);
                Check("Sand Gambler: 3 Tails destroys every monster you control, itself included",
                    reached && act && p.Graveyard.Contains(sg) && p.Graveyard.Contains(mine) && o.TryFindMonster(o1, out _),
                    $"act={act} sg={Where(e, sg)} mine={Where(e, mine)} o1={Where(e, o1)}");
            }

            {
                // Mixed results: nothing is destroyed.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var sg = FaceUp(e, p, SandGambler, 0);
                var o1 = FaceUp(e, o, Celtic, 0);
                e.Rng.QueueCoin(true);
                e.Rng.QueueCoin(false);
                e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(p, sg, fromHand: false);
                Settle(e);
                Check("Sand Gambler: 2 Heads + 1 Tails does nothing",
                    reached && act && p.TryFindMonster(sg, out _) && o.TryFindMonster(o1, out _) &&
                    p.Graveyard.Count == 0 && o.Graveyard.Count == 0,
                    $"act={act} sg={Where(e, sg)} o1={Where(e, o1)}");
            }

            {
                // The AI gambles only while the player controls a monster; 3 Heads destroys the player's monsters.
                var e = MainOf(db, pDeck, aDeck, false, out var p, out var o, out var reached);
                var sg = FaceUp(e, o, SandGambler, 0);
                var skipEmpty = IgnitionCostsAndSimpleSpellsRuntime.AiSkipsIgnition(e, o, sg);
                var pm = FaceUp(e, p, Celtic, 0);
                var skipWithTarget = IgnitionCostsAndSimpleSpellsRuntime.AiSkipsIgnition(e, o, sg);
                for (var i = 0; i < 3; i++) e.Rng.QueueCoin(true);
                var act = e.TryActivateSpellTrap(o, sg, fromHand: false);
                Settle(e);
                Check("Sand Gambler (AI): skipped with no player monster; 3 Heads destroys the player's monster",
                    reached && skipEmpty && !skipWithTarget && act && p.Graveyard.Contains(pm) && o.TryFindMonster(sg, out _),
                    $"skipEmpty={skipEmpty} skipWith={skipWithTarget} act={act} pm={Where(e, pm)}");
            }
        }

        // ───────────────────────────── Mystik Wok ─────────────────────────────

        static void MystikWokDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human Tributes Giant Soldier (1300/2000), CANCELs the ATK tap → gains its DEF when the link resolves.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                FaceUp(e, p, Celtic, 0);
                var giant = FaceUp(e, p, GiantSoldier, 1, BattlePosition.Defense);
                var wok = PutInHand(e, p, MystikWok);
                var can = e.CanActivateSpellTrap(p, wok, fromHand: true);
                var act = can && e.TryActivateSpellTrap(p, wok, fromHand: true);
                var kind1 = e.PendingActivation?.TargetKind;
                var legal1 = Legal(e);
                var tributed = legal1.Count == 2 && e.TrySelectEffectTarget(giant);
                var kind2 = e.PendingActivation?.TargetKind;
                var legal2 = Legal(e);
                var lpAtChoice = p.LifePoints;
                var chose = legal2.Count == 1 && legal2[0] == giant && e.CancelEffectTargeting();
                Settle(e);
                Check("Mystik Wok: the human Tributes, chooses DEF on activation (CANCEL), gains 2000 at resolution",
                    reached && can && act && kind1 == EffectTargetKind.TributeMonsterYouControl && tributed &&
                    kind2 == EffectTargetKind.MonsterInEitherGy && lpAtChoice == 8000 && chose &&
                    p.LifePoints == 10000 && p.Graveyard.Contains(giant) && p.Graveyard.Contains(wok) &&
                    e.PendingActivation == null && !e.Chain.HasLinks,
                    $"can={can} act={act} k1={kind1} l1={legal1.Count} trib={tributed} k2={kind2} l2={legal2.Count} " +
                    $"chose={chose} lp={p.LifePoints} wok={Where(e, wok)}");
            }

            {
                // The human taps the Tributed monster → its ATK.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var giant = FaceUp(e, p, GiantSoldier, 0);
                var wok = PutInHand(e, p, MystikWok);
                var act = e.TryActivateSpellTrap(p, wok, fromHand: true);
                var tributed = Legal(e).Contains(giant) && e.TrySelectEffectTarget(giant);
                var tapped = Legal(e).Contains(giant) && e.TrySelectEffectTarget(giant);
                Settle(e);
                Check("Mystik Wok: tapping the Tributed monster gains its ATK (1300)",
                    reached && act && tributed && tapped && p.LifePoints == 9300,
                    $"act={act} trib={tributed} tapped={tapped} lp={p.LifePoints}");
            }

            {
                // ATK 0: no ATK/DEF prompt, the DEF is gained. A 0/0 monster cannot be Tributed: refused.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var zero = Token(e, p, 0, 0, 0);
                var wok = PutInHand(e, p, MystikWok);
                var noFodder = e.CanActivateSpellTrap(p, wok, fromHand: true);
                var ojama = FaceUp(e, p, OjamaYellow, 1);
                var act = e.CanActivateSpellTrap(p, wok, fromHand: true) && e.TryActivateSpellTrap(p, wok, fromHand: true);
                var legal = Legal(e);
                var tributed = legal.Count == 1 && legal[0] == ojama && e.TrySelectEffectTarget(ojama);
                var noChoice = !e.IsAwaitingEffectTarget;
                Settle(e);
                Check("Mystik Wok: a 0/0 monster is not offered (alone: refused); ATK 0 → its DEF with no choice",
                    reached && !noFodder && act && tributed && noChoice && p.LifePoints == 9000 &&
                    p.TryFindMonster(zero, out _),
                    $"noFodder={noFodder} act={act} legal={legal.Count} trib={tributed} noChoice={noChoice} lp={p.LifePoints}");
            }

            {
                // Cancel at the Tribute pick: nothing Tributed, the card stays in the hand.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var celtic = FaceUp(e, p, Celtic, 0);
                var wok = PutInHand(e, p, MystikWok);
                var act = e.TryActivateSpellTrap(p, wok, fromHand: true) && e.IsAwaitingEffectTarget;
                var cancelled = e.CancelEffectTargeting();
                Settle(e);
                Check("Mystik Wok: Cancel at the Tribute pick → nothing Tributed, the card stays in the hand",
                    reached && act && cancelled && p.TryFindMonster(celtic, out _) && p.Hand.Contains(wok) &&
                    p.LifePoints == 8000 && !e.Chain.HasLinks && e.PendingActivation == null,
                    $"act={act} cancel={cancelled} celtic={Where(e, celtic)} wok={Where(e, wok)} lp={p.LifePoints}");
            }

            {
                // The AI: Tributes and takes the higher stat (Mystical Elf 800/2000 → 2000).
                var e = MainOf(db, pDeck, aDeck, false, out _, out var o, out var reached);
                var elf = FaceUp(e, o, MysticalElf, 0);
                var wok = PutInHand(e, o, MystikWok);
                var act = e.CanActivateSpellTrap(o, wok, fromHand: true) && e.TryActivateSpellTrap(o, wok, fromHand: true);
                Settle(e);
                Check("Mystik Wok (AI): Tributes, gains the higher of ATK / DEF (2000)",
                    reached && act && o.Graveyard.Contains(elf) && o.LifePoints == 10000 && e.PendingActivation == null,
                    $"act={act} elf={Where(e, elf)} lp={o.LifePoints}");
            }

            {
                // A Tributed Token leaves no card to tap: the higher value is taken (500/700 → 700).
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var tok = Token(e, p, 0, 500, 700);
                var wok = PutInHand(e, p, MystikWok);
                var act = e.TryActivateSpellTrap(p, wok, fromHand: true);
                var tributed = Legal(e).Contains(tok) && e.TrySelectEffectTarget(tok);
                Settle(e);
                Check("Mystik Wok: a Tributed Token (no card left to tap) gains the higher value",
                    reached && act && tributed && p.LifePoints == 8700 && e.PendingActivation == null,
                    $"act={act} trib={tributed} lp={p.LifePoints}");
            }
        }

        static void MystikWokWindows(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Set copy in its own Main Phase: Tribute, tap → ATK.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out _, out var reached);
                var elf = FaceUp(e, p, MysticalElf, 0);
                var wok = PlaceSetTrap(e, p, MystikWok, 0);
                var act = e.CanActivateSpellTrap(p, wok, fromHand: false) && e.TryActivateSpellTrap(p, wok, fromHand: false);
                var tributed = Legal(e).Contains(elf) && e.TrySelectEffectTarget(elf);
                var tapped = Legal(e).Contains(elf) && e.TrySelectEffectTarget(elf);
                Settle(e);
                Check("Mystik Wok: a Set copy in your Main Phase Tributes and gains the tapped ATK (800)",
                    reached && act && tributed && tapped && p.LifePoints == 8800 && p.Graveyard.Contains(wok),
                    $"act={act} trib={tributed} tapped={tapped} lp={p.LifePoints} wok={Where(e, wok)}");
            }

            {
                // The opponent's attack window: a seat there pays no Tribute, so the Set copy is not offered (fail closed).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var giant = FaceUp(e, p, GiantSoldier, 0, BattlePosition.Defense);
                var wok = PlaceSetTrap(e, p, MystikWok, 0);
                // A Set Waboku keeps the attack window open (a legal answer of its own).
                PlaceSetTrap(e, p, SpellTrapEffects.Waboku, 1);
                var atk = FaceUp(e, o, Bewd, 0);
                var bp = e.TryEnterBattlePhase(o);
                e.TryAttack(o, atk, giant);
                var window = e.IsAwaitingResponse;
                var can = e.CanActivateSpellTrap(p, wok, fromHand: false);
                var tried = e.TryActivateSpellTrap(p, wok, fromHand: false);
                DrainCombat(e);
                Check("Mystik Wok: not offered in the opponent's attack window (no Tribute there) — stays Set, no LP",
                    reached && bp && window && !can && !tried && p.TryFindSpellTrap(wok, out _) && !wok.FaceUp &&
                    p.LifePoints == 8000 && !p.Graveyard.Contains(wok),
                    $"reached={reached} bp={bp} window={window} can={can} tried={tried} wok={Where(e, wok)} " +
                    $"giant={Where(e, giant)} lp={p.LifePoints}");
            }

            {
                // The AI's Set copy when the player attacks: not activated there; the battle finishes (no soft-lock).
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var oElf = FaceUp(e, o, MysticalElf, 0, BattlePosition.Defense);
                var wok = PlaceSetTrap(e, o, MystikWok, 0);
                var atk = FaceUp(e, p, Bewd, 0);
                var bp = e.TryEnterBattlePhase(p);
                e.TryAttack(p, atk, oElf);
                DrainCombat(e);
                Check("Mystik Wok (AI): its Set copy is not activated in the attack window; the battle finishes",
                    reached && bp && o.Graveyard.Contains(oElf) && o.TryFindSpellTrap(wok, out _) && !wok.FaceUp &&
                    o.LifePoints == 8000 && e.PendingActivation == null,
                    $"bp={bp} elf={Where(e, oElf)} wok={Where(e, wok)} lp={o.LifePoints}");
            }
        }

        // ───────────────────────────── Solar Ray ─────────────────────────────

        static void SolarRayDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 2 face-up LIGHT (a face-down LIGHT and a face-up EARTH do not count) → 1200.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                FaceUp(e, p, Bewd, 0);
                FaceUp(e, p, MysticalElf, 1, BattlePosition.Defense);
                PlaceMonster(e, p, MysticalElf, 2, BattlePosition.Defense, false);
                FaceUp(e, p, Celtic, 3);
                FaceUp(e, o, MysticalElf, 0); // the opponent's LIGHT does not count
                var ray = PlaceSetTrap(e, p, SolarRay, 0);
                var can = e.CanActivateSpellTrap(p, ray, fromHand: false);
                var act = can && e.TryActivateSpellTrap(p, ray, fromHand: false);
                Settle(e);
                Check("Solar Ray: 600 x 2 face-up LIGHT monsters you control = 1200",
                    reached && can && act && o.LifePoints == 6800 && p.Graveyard.Contains(ray),
                    $"can={can} act={act} lp={o.LifePoints} ray={Where(e, ray)}");
            }

            {
                // No face-up LIGHT monster you control: refused.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Defense, false);
                FaceUp(e, o, Bewd, 0);
                var ray = PlaceSetTrap(e, p, SolarRay, 0);
                var refused = !e.CanActivateSpellTrap(p, ray, fromHand: false);
                Check("Solar Ray: no face-up LIGHT monster you control → refused",
                    reached && refused, $"refused={refused}");
            }

            {
                // The AI's Solar Ray burns the player.
                var e = MainOf(db, pDeck, aDeck, false, out var p, out var o, out var reached);
                FaceUp(e, o, Bewd, 0);
                var ray = PlaceSetTrap(e, o, SolarRay, 0);
                var act = e.CanActivateSpellTrap(o, ray, fromHand: false) && e.TryActivateSpellTrap(o, ray, fromHand: false);
                Settle(e);
                Check("Solar Ray (AI): 600 x 1 face-up LIGHT monster = 600 to the player",
                    reached && act && p.LifePoints == 7400, $"act={act} lp={p.LifePoints}");
            }
        }

        // ───────────────────────────── Special Hurricane ─────────────────────────────

        static void SpecialHurricaneDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The human discards the other hand card (not Special Hurricane itself); every Special Summoned monster on the
                // field (both sides) is destroyed, the Normal Summoned one stays.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var mySs = FaceUp(e, p, Celtic, 0);
                mySs.WasSpecialSummoned = true;
                var oppSs = FaceUp(e, o, Bewd, 0);
                oppSs.WasSpecialSummoned = true;
                var oppNs = FaceUp(e, o, GiantSoldier, 1);
                var hurricane = PutInHand(e, p, SpecialHurricane);
                var fodder = PutInHand(e, p, Kuriboh);
                var can = e.CanActivateSpellTrap(p, hurricane, fromHand: true);
                var act = can && e.TryActivateSpellTrap(p, hurricane, fromHand: true);
                var legal = Legal(e);
                var picked = legal.Count == 1 && legal[0] == fodder && e.TrySelectEffectTarget(fodder);
                Settle(e);
                Check("Special Hurricane: discard 1 other card; both Special Summoned monsters destroyed, not the other",
                    reached && can && act && picked && p.Graveyard.Contains(fodder) && Discarded(fodder) &&
                    p.Graveyard.Contains(mySs) && o.Graveyard.Contains(oppSs) && o.TryFindMonster(oppNs, out _) &&
                    p.Graveyard.Contains(hurricane),
                    $"can={can} act={act} legal={legal.Count} picked={picked} mySs={Where(e, mySs)} oppSs={Where(e, oppSs)} " +
                    $"oppNs={Where(e, oppNs)} hurricane={Where(e, hurricane)}");
            }

            {
                // Only itself in hand, or no Special Summoned monster: refused.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var ss = FaceUp(e, o, Bewd, 0);
                ss.WasSpecialSummoned = true;
                var hurricane = PutInHand(e, p, SpecialHurricane);
                var alone = e.CanActivateSpellTrap(p, hurricane, fromHand: true);
                PutInHand(e, p, Kuriboh);
                var ok = e.CanActivateSpellTrap(p, hurricane, fromHand: true);
                ss.WasSpecialSummoned = false;
                var noSs = e.CanActivateSpellTrap(p, hurricane, fromHand: true);
                Check("Special Hurricane: needs another card to discard and a Special Summoned monster",
                    reached && !alone && ok && !noSs, $"alone={alone} ok={ok} noSs={noSs}");
            }

            {
                // Cancel at the discard pick: nothing discarded, the card stays in the hand.
                var e = MainOf(db, pDeck, aDeck, true, out var p, out var o, out var reached);
                var ss = FaceUp(e, o, Bewd, 0);
                ss.WasSpecialSummoned = true;
                var hurricane = PutInHand(e, p, SpecialHurricane);
                var fodder = PutInHand(e, p, Kuriboh);
                var act = e.TryActivateSpellTrap(p, hurricane, fromHand: true) && e.IsAwaitingEffectTarget;
                var cancelled = e.CancelEffectTargeting();
                Settle(e);
                Check("Special Hurricane: Cancel at the discard pick → nothing discarded or destroyed",
                    reached && act && cancelled && p.Hand.Contains(hurricane) && p.Hand.Contains(fodder) &&
                    o.TryFindMonster(ss, out _) && !e.Chain.HasLinks && e.PendingActivation == null,
                    $"act={act} cancel={cancelled} hurricane={Where(e, hurricane)} fodder={Where(e, fodder)} ss={Where(e, ss)}");
            }

            {
                // The AI discards and destroys the player's Special Summoned monster.
                var e = MainOf(db, pDeck, aDeck, false, out var p, out var o, out var reached);
                var ss = FaceUp(e, p, Bewd, 0);
                ss.WasSpecialSummoned = true;
                var hurricane = PutInHand(e, o, SpecialHurricane);
                var fodder = PutInHand(e, o, Kuriboh);
                var act = e.CanActivateSpellTrap(o, hurricane, fromHand: true) &&
                          e.TryActivateSpellTrap(o, hurricane, fromHand: true);
                Settle(e);
                Check("Special Hurricane (AI): discards, destroys the player's Special Summoned monster",
                    reached && act && o.Graveyard.Contains(fodder) && p.Graveyard.Contains(ss) && e.PendingActivation == null,
                    $"act={act} fodder={Where(e, fodder)} ss={Where(e, ss)}");
            }
        }
    }
}
