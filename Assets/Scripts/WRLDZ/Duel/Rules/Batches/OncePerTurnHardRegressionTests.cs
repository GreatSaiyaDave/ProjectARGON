using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "OncePerTurnHard" (v211 QC): "You can only use this effect of "X" once per turn." (ygopro
    /// <c>SetCountLimit(1, id)</c>: per player, every copy of the name, per turn) is compiled to
    /// <see cref="EffectClause.OncePerTurn"/> + <see cref="OncePerTurnScope.PerCardName"/> and enforced by
    /// <see cref="OncePerTurnHardRuntime"/>, or the card fails closed:
    /// <list type="bullet">
    /// <item>Sangan c26202165 / Witch of the Black Forest c78010363: TRIGGER_F EVENT_TO_GRAVE from the field, CountLimit(1,id).</item>
    /// <item>Guerilla Kite c54629413: TRIGGER_F EVENT_TO_GRAVE from the field, 500 to the opponent, CountLimit(1,id).</item>
    /// <item>Satellarknight Alsahm c65056481: TRIGGER_O on Normal / Flip / Special Summon success, 1000, CountLimit(1,id).</item>
    /// <item>RAM Clouder c9190563: IGNITION, cost Release 1, target 1 Cyberse in your GY; Special Summon it.</item>
    /// <item>Golden Dragon Summoner c28565527: IGNITION, cost Release 1, target 1 monster on the field; to the hand.</item>
    /// <item>Garbage Ogre c18698739: IGNITION in the hand, Cost.SelfToGrave; search 1 "Garbage Lord".</item>
    /// <item>Second Coin Toss c36562627: player flag (tp, id) — flag set on its compiled clause.</item>
    /// </list>
    /// Fail closed: Rose Lover (GY ignition), Valerifawn, Lightning Dragon Ruler, Xyz-Raypierce, Fairy Cheer Girl, Herald of
    /// Pure Light, Battlecruiser Dianthus.
    /// </summary>
    public static class OncePerTurnHardRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Sangan = 26202165;
        const int Witch = 78010363;
        const int SecondCoinToss = 36562627;
        const int FairyCheerGirl = 51960178;
        const int GarbageOgre = 18698739;
        const int GarbageLord = 44682448;
        const int GuerillaKite = 54629413;
        const int RamClouder = 9190563;
        const int RoseLover = 89252157;
        const int GoldenDragonSummoner = 28565527;
        const int HeraldOfPureLight = 1249315;
        const int LightningRuler = 89185742;
        const int Alsahm = 65056481;
        const int Valerifawn = 24096499;
        const int XyzRaypierce = 86559484;
        const int Dianthus = 40424929;
        const int Kuriboh = 40640057;
        const int Celtic = 91152256;
        const int DarkHole = 53129443;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("OncePerTurnHard: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            FailClosed(db, Check);
            NearMisses(db, Check);
            CorpusGuard(db, Check);

            SanganTwiceInOneTurn(db, pDeck, aDeck, Check);
            SanganPairByDarkHole(db, pDeck, aDeck, Check);
            GuerillaKiteTwice(db, pDeck, aDeck, Check);
            AlsahmTwice(db, pDeck, aDeck, Check);
            RamClouderTwice(db, pDeck, aDeck, Check);
            GoldenDragonSummonerTwiceAndCancel(db, pDeck, aDeck, Check);
            GarbageOgreTwice(db, pDeck, aDeck, Check);

            // Review fixes.
            GoldenDragonSummonerNeedsATargetAfterTheTribute(db, pDeck, aDeck, Check);
            NewDuelOnTheSameEngineStartsClean(db, pDeck, aDeck, Check);
            SummonTriggerWithNoTargetKeepsItsCount(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id)
        {
            var def = db.Get(id);
            return def != null ? CardTextEffectCompiler.Compile(def) : null;
        }

        static bool HardOpt(EffectClause c) => c != null && c.OncePerTurn && c.OptScope == OncePerTurnScope.PerCardName;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "(null)"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    c == null ? "null" : $"{c.Timing}/{c.Action}/{c.Zone} opt1={c.OncePerTurn}/{c.OptScope}")) +
                  " unparsed=[" + string.Join(" / ", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        static CompiledCardProgram Synthetic(string name, string type, string desc, string race = "Fiend") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000 + Math.Abs((name + desc).GetHashCode() % 9000),
                name = name,
                type = type,
                frameType = "effect",
                desc = desc,
                race = race,
                attribute = "DARK",
                level = 3,
                atk = 1000,
                def = 600
            });

        /// <summary>Answer every prompt (preferred legal card first, else the first one) and pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> prefer = null)
        {
            for (var i = 0; i < 40; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var legal = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                    var pick = (prefer != null ? legal.FirstOrDefault(prefer) : null) ?? legal.FirstOrDefault();
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
        }

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
            return e;
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static int CountIn(List<CardInstance> pile, int id) => pile.Count(c => c != null && c.CardId == id);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, name, timing, action) in new[]
                     {
                         (Sangan, "Sangan", EffectTiming.SentFromFieldToGy, EffectActionKind.AddFromDeckToHand),
                         (Witch, "Witch of the Black Forest", EffectTiming.SentFromFieldToGy, EffectActionKind.AddFromDeckToHand),
                         (GuerillaKite, "Guerilla Kite", EffectTiming.SentFromFieldToGy, EffectActionKind.InflictDamageToOpponent),
                         (Alsahm, "Satellarknight Alsahm", EffectTiming.ThisCardSummoned, EffectActionKind.InflictDamageToOpponent),
                         (RamClouder, "RAM Clouder", EffectTiming.Activate, EffectActionKind.SpecialSummonFromGy),
                         (GoldenDragonSummoner, "Golden Dragon Summoner", EffectTiming.Activate, EffectActionKind.ReturnToHand),
                         (GarbageOgre, "Garbage Ogre", EffectTiming.Activate, EffectActionKind.AddNamedFromDeckToHand),
                         (SecondCoinToss, "Second Coin Toss", EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.RedoOwnCoinTossOncePerTurn),
                     })
            {
                var p = Prog(db, id);
                var def = db.Get(id);
                Check($"{name}: one {timing}/{action} clause with the hard once per turn by name; Implemented",
                    p != null && p.FullyCompiled && p.ClauseList.Count == 1 && p.ClauseList[0].Timing == timing &&
                    p.ClauseList[0].Action == action && HardOpt(p.ClauseList[0]) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    Describe(p));
            }

            var sangan = Prog(db, Sangan)?.ClauseList.FirstOrDefault();
            var witch = Prog(db, Witch)?.ClauseList.FirstOrDefault();
            Check("Sangan / Witch keep their search shape (1500 ATK / DEF cap, name lock, mandatory)",
                sangan != null && witch != null && sangan.Amount == 1500 && sangan.AmountIsAtkMax && witch.AmountIsDefMax &&
                sangan.LocksAddedCardNameThisTurn && witch.LocksAddedCardNameThisTurn && !sangan.IsOptional && !witch.IsOptional,
                $"{Describe(Prog(db, Sangan))} / {Describe(Prog(db, Witch))}");
            var kite = Prog(db, GuerillaKite)?.ClauseList.FirstOrDefault();
            Check("Guerilla Kite: mandatory 500", kite != null && kite.Amount == 500 && !kite.IsOptional, Describe(Prog(db, GuerillaKite)));
            var alsahm = Prog(db, Alsahm)?.ClauseList.FirstOrDefault();
            Check("Satellarknight Alsahm: optional 1000 on any Summon (no Normal/Special-only gate)",
                alsahm != null && alsahm.Amount == 1000 && alsahm.IsOptional && !alsahm.RequiresThisSpecialSummoned &&
                !alsahm.RequiresThisNormalSummoned && !alsahm.RequiresNormalOrFlipSummon && !alsahm.RequiresThisFlipSummoned,
                Describe(Prog(db, Alsahm)));
            var ram = Prog(db, RamClouder)?.ClauseList.FirstOrDefault();
            Check("RAM Clouder: Tribute 1 cost, targets 1 Cyberse in your GY",
                ram != null && ram.RequiresTributeCount == 1 && ram.Zone == EffectZoneFilter.ControllerGyMonsters &&
                ram.RaceFilter == "Cyberse" && ram.RequiresTargetChoice && ram.IsPsctTarget && ram.IsOptional,
                Describe(Prog(db, RamClouder)));
            var gds = Prog(db, GoldenDragonSummoner)?.ClauseList.FirstOrDefault();
            Check("Golden Dragon Summoner: Tribute 1 cost, targets 1 monster on the field",
                gds != null && gds.RequiresTributeCount == 1 && gds.Zone == EffectZoneFilter.FieldAnyMonster &&
                gds.RequiresTargetChoice && gds.IsPsctTarget, Describe(Prog(db, GoldenDragonSummoner)));
            var ogre = Prog(db, GarbageOgre)?.ClauseList.FirstOrDefault();
            Check("Garbage Ogre: hand ignition, sends itself (not a discard), searches \"Garbage Lord\"",
                ogre != null && ogre.ActivatesFromHand && ogre.RequiresDiscardSelf && ogre.DiscardCostIsSend &&
                ogre.NamedCard == "Garbage Lord" && ogre.FromDeck && ogre.Amount == 1 &&
                EffectVocabulary.CostOf(ogre) == EffectCostKind.SendToGy,
                Describe(Prog(db, GarbageOgre)));
        }

        static void FailClosed(CardDatabase db, Chk Check)
        {
            foreach (var (id, name, why) in new[]
                     {
                         (RoseLover, "Rose Lover", "GY ignition with a banish-self cost and a Trap-immunity rider"),
                         (Valerifawn, "Valerifawn", "Level 2 or lower Beast target filter, except-name, position choice"),
                         (LightningRuler, "Lightning, Dragon Ruler of Drafts", "second discard and the cannot-attack rider"),
                         (XyzRaypierce, "Xyz-Raypierce", "banish Dragon + Wyrm cost; \"each effect\""),
                         (FairyCheerGirl, "Fairy Cheer Girl", "detach cost"),
                         (HeraldOfPureLight, "Herald of Pure Light", "detach cost, then-shuffle rider"),
                         (Dianthus, "Battlecruiser Dianthus", "detach cost, 200 for each card in hand"),
                     })
            {
                var p = Prog(db, id);
                var def = db.Get(id);
                Check($"{name}: fails closed ({why})",
                    p != null && !p.FullyCompiled && CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    Describe(p));
            }
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            const string trig = "If this card is sent from the field to the GY: Inflict 500 damage to your opponent.";
            var ok = Synthetic("Opt Probe", "Effect Monster", trig + " You can only use this effect of \"Opt Probe\" once per turn.");
            Check("near miss control: a whole-sentence trigger + its own-name once per turn compiles with the flag",
                ok.FullyCompiled && ok.ClauseList.Count == 1 && HardOpt(ok.ClauseList[0]), Describe(ok));

            var other = Synthetic("Opt Probe", "Effect Monster", trig + " You can only use this effect of \"Other Card\" once per turn.");
            Check("another card's name in the once-per-turn sentence fails closed", !other.FullyCompiled, Describe(other));

            var each = Synthetic("Opt Probe", "Effect Monster", trig + " You can only use each effect of \"Opt Probe\" once per turn.");
            Check("\"each effect\" (one count per effect) fails closed", !each.FullyCompiled, Describe(each));

            var none = Synthetic("Opt Probe", "Effect Monster", trig);
            Check("no once-per-turn sentence: no flag", none.FullyCompiled && !none.ClauseList.Exists(HardOpt), Describe(none));

            // A generic PSCT ignition (cost ; resolution) never carries a hard once per turn: it is not a whole-sentence compile.
            var ign = Synthetic("Opt Probe", "Effect Monster",
                "You can discard 1 card; draw 1 card. You can only use this effect of \"Opt Probe\" once per turn.");
            Check("a generic PSCT ignition with the sentence fails closed", !ign.FullyCompiled, Describe(ign));

            // RAM Clouder's atom is whole: a summon position it does not know is not dropped.
            var ramPos = Synthetic("Opt Probe", "Effect Monster",
                "You can Tribute 1 monster, then target 1 Cyberse monster in your GY; Special Summon it in Defense Position. " +
                "You can only use this effect of \"Opt Probe\" once per turn.", "Cyberse");
            Check("RAM Clouder look-alike with a position rider fails closed", !ramPos.FullyCompiled, Describe(ramPos));

            // Garbage Ogre's atom names the search exactly; a Spell search look-alike ("add 1 Spell") does not match it.
            var ogreAny = Synthetic("Opt Probe", "Effect Monster",
                "You can send this card from your hand to the GY; add 1 Spell from your Deck to your hand. " +
                "You can only use this effect of \"Opt Probe\" once per turn.");
            Check("Garbage Ogre look-alike with an unnamed search fails closed", !ogreAny.FullyCompiled, Describe(ogreAny));
        }

        /// <summary>
        /// No FullyCompiled program whose text says "You can only use this / the / each effect of "X" once per turn" may lack
        /// the name-scoped once-per-turn flag (2026-10-18: 15 such programs ran with no limit).
        /// </summary>
        static void CorpusGuard(CardDatabase db, Chk Check)
        {
            var rx = new Regex(@"You can only use (?:this|the|each) effect of [""“”][^""“”]+[""“”] once per turn",
                RegexOptions.IgnoreCase);
            var scanned = 0;
            var checkedPrograms = 0;
            var bad = new List<string>();
            foreach (var def in db.GetAllCards())
            {
                if (def == null) continue;
                var text = OfficialCardAuthority.OfficialText(def) ?? "";
                if (!rx.IsMatch(text)) continue;
                scanned++;
                var p = CardTextEffectCompiler.Compile(def);
                // A program with no clause runs nothing (an Extra Deck monster kept structural): no limit to carry.
                if (p == null || !p.FullyCompiled || p.ClauseList.Count == 0) continue;
                checkedPrograms++;
                if (!p.ClauseList.Exists(HardOpt)) bad.Add($"{def.name} ({def.id})");
            }

            Check($"corpus: every FullyCompiled hard once-per-turn text carries OncePerTurn + PerCardName ({scanned} texts, " +
                  $"{checkedPrograms} programs with clauses)",
                scanned > 10 && checkedPrograms >= 8 && bad.Count == 0, string.Join(", ", bad));
        }

        // ───────────────────────────── mini-duels ─────────────────────────────

        static void SanganTwiceInOneTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var a = Up(e, p, Sangan, 0);
            var b = Up(e, p, Sangan, 1);
            var w = Up(e, p, Witch, 2);
            var oa = Up(e, o, Sangan, 0);
            for (var i = 0; i < 4; i++)
            {
                p.Deck.Insert(0, Kuriboh);
                o.Deck.Insert(0, Kuriboh);
            }

            e.SendCardToGrave(p, a);
            Drive(e, c => c.CardId == Kuriboh);
            var first = CountIn(p.Hand, Kuriboh);
            e.SendCardToGrave(p, b);
            Drive(e, c => c.CardId == Kuriboh);
            var second = CountIn(p.Hand, Kuriboh);
            Check("Sangan: the first one sent searches, a second Sangan sent the same turn does not",
                first == 1 && second == 1 && p.Graveyard.Contains(b) && !e.IsAwaitingEffectTarget,
                $"first={first} second={second} hand={string.Join(",", p.Hand.Select(c => c.Name))}");

            // The same copy back on the field and sent again the same turn: a new trip to the GY, the count is still used.
            e.NextSpecialSummonFromGraveyard = true;
            var revived = e.SpecialSummonToField(p, a, BattlePosition.Attack, true);
            Drive(e);
            var onField = p.MonstersOnField().Contains(a);
            e.SendCardToGrave(p, a);
            Drive(e, c => c.CardId == Kuriboh);
            Check("Sangan: the copy that searched, Special Summoned back and sent again the same turn, does not search",
                revived && onField && p.Graveyard.Contains(a) && CountIn(p.Hand, Kuriboh) == 1,
                $"revived={revived} onField={onField} kuriboh={CountIn(p.Hand, Kuriboh)}");

            // Another name keeps its own count; the opponent keeps theirs.
            e.SendCardToGrave(p, w);
            Drive(e, c => c.CardId == Kuriboh);
            var afterWitch = CountIn(p.Hand, Kuriboh);
            var oppBefore = o.Hand.Count;
            e.SendCardToGrave(o, oa);
            Drive(e, c => c.CardId == Kuriboh);
            Check("Sangan's count is per name and per player: Witch and the opponent's Sangan still search this turn",
                afterWitch == 2 && o.Hand.Count == oppBefore + 1 && CountIn(o.Hand, Kuriboh) >= 1,
                $"afterWitch={afterWitch} opp {oppBefore}->{o.Hand.Count}");

            // A new turn: the count is back.
            var reached = ReachOpponentMain(e);
            var c3 = Up(e, p, Sangan, 3);
            var before = CountIn(p.Hand, Kuriboh);
            e.SendCardToGrave(p, c3);
            Drive(e, c => c.CardId == Kuriboh);
            Check("Sangan: next turn a Sangan searches again",
                reached && CountIn(p.Hand, Kuriboh) == before + 1,
                $"reached={reached} turn={e.TurnNumber} {before}->{CountIn(p.Hand, Kuriboh)}");
        }

        static void SanganPairByDarkHole(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            Up(e, p, Sangan, 0);
            Up(e, p, Sangan, 1);
            for (var i = 0; i < 4; i++) p.Deck.Insert(0, Kuriboh);
            var dh = PutInHand(e, p, DarkHole);
            var prompts = 0;
            e.TryActivateSpellTrap(p, dh, fromHand: true);
            for (var i = 0; i < 40; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    prompts++;
                    var l = e.PendingActivation.LegalTargets.ToList();
                    var pick = l.FirstOrDefault(c => c.CardId == Kuriboh) ?? l.FirstOrDefault();
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

            Check("Dark Hole destroys two Sangans: one search (the second has no trigger left)",
                CountIn(p.Hand, Kuriboh) == 1 && CountIn(p.Graveyard, Sangan) == 2,
                $"kuriboh={CountIn(p.Hand, Kuriboh)} prompts={prompts} gy={string.Join(",", p.Graveyard.Select(c => c.Name))}");
        }

        static void GuerillaKiteTwice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var a = Up(e, p, GuerillaKite, 0);
            var b = Up(e, p, GuerillaKite, 1);
            var lp0 = o.LifePoints;
            e.SendCardToGrave(p, a);
            Drive(e);
            var lp1 = o.LifePoints;
            e.SendCardToGrave(p, b);
            Drive(e);
            Check("Guerilla Kite: 500 for the first one sent, nothing for a second one the same turn",
                lp0 - lp1 == 500 && lp1 == o.LifePoints, $"{lp0}->{lp1}->{o.LifePoints}");
        }

        static void AlsahmTwice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var a = PutInHand(e, p, Alsahm);
            var b = PutInHand(e, p, Alsahm);
            var lp0 = o.LifePoints;
            var summoned = e.TryNormalSummon(p, a, asSet: false);
            Drive(e);
            var lp1 = o.LifePoints;
            e.MarkSpecialSummonFromHand(b);
            var special = e.SpecialSummonToField(p, b, BattlePosition.Attack, true);
            Drive(e);
            Check("Satellarknight Alsahm: 1000 on its Normal Summon, none for a second one Special Summoned the same turn",
                summoned && special && lp0 - lp1 == 1000 && o.LifePoints == lp1,
                $"summoned={summoned} special={special} {lp0}->{lp1}->{o.LifePoints}");
        }

        static void RamClouderTwice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var a = Up(e, p, RamClouder, 0);
            var b = Up(e, p, RamClouder, 1);
            var fodder = Up(e, p, Celtic, 2);
            Up(e, p, Celtic, 3);
            var gy1 = e.CreateCardInstance(RamClouder);
            var gy2 = e.CreateCardInstance(RamClouder);
            p.Graveyard.Add(gy1);
            p.Graveyard.Add(gy2);
            var could = e.CanActivateSpellTrap(p, a, fromHand: false);
            var ok = e.TryActivateSpellTrap(p, a, fromHand: false);
            Drive(e, c => c == fodder || c == gy1);
            var summoned = p.MonstersOnField().Contains(gy1) || p.MonstersOnField().Contains(gy2);
            var bBlocked = !e.CanActivateSpellTrap(p, b, fromHand: false);
            Check("RAM Clouder: Tribute 1, revive a Cyberse; a second RAM Clouder cannot use it the same turn",
                could && ok && summoned && p.Graveyard.Contains(fodder) && bBlocked,
                $"could={could} ok={ok} summoned={summoned} fodderGy={p.Graveyard.Contains(fodder)} bBlocked={bBlocked} " +
                $"field={string.Join(",", p.MonstersOnField().Select(c => c.Name))}");
        }

        static void GoldenDragonSummonerTwiceAndCancel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var a = Up(e, p, GoldenDragonSummoner, 0);
                var b = Up(e, p, GoldenDragonSummoner, 1);
                var fodder = Up(e, p, Celtic, 2);
                var victim = Up(e, o, Celtic, 0);
                var ok = e.TryActivateSpellTrap(p, a, fromHand: false);
                Drive(e, c => c == fodder || c == victim);
                var bounced = o.Hand.Contains(victim);
                var bBlocked = !e.CanActivateSpellTrap(p, b, fromHand: false);
                Check("Golden Dragon Summoner: Tribute 1, return 1 monster; a second copy cannot use it the same turn",
                    ok && bounced && p.Graveyard.Contains(fodder) && bBlocked,
                    $"ok={ok} bounced={bounced} fodderGy={p.Graveyard.Contains(fodder)} bBlocked={bBlocked}");
            }

            {
                // Cancel at the first pick undoes the activation: the count is not used.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var a = Up(e, p, GoldenDragonSummoner, 0);
                var b = Up(e, p, GoldenDragonSummoner, 1);
                Up(e, p, Celtic, 2);
                Up(e, o, Celtic, 0);
                var ok = e.TryActivateSpellTrap(p, a, fromHand: false);
                var prompted = e.IsAwaitingEffectTarget;
                if (prompted) e.CancelEffectTargeting();
                var aStill = p.MonstersOnField().Contains(a);
                var bFree = e.CanActivateSpellTrap(p, b, fromHand: false);
                Check("Golden Dragon Summoner: an activation cancelled at its first pick does not use the once per turn",
                    ok && prompted && !e.IsAwaitingEffectTarget && aStill && bFree,
                    $"ok={ok} prompted={prompted} aStill={aStill} bFree={bFree}");
            }
        }

        static void GarbageOgreTwice(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var a = PutInHand(e, p, GarbageOgre);
            var b = PutInHand(e, p, GarbageOgre);
            var onField = Up(e, p, GarbageOgre, 0);
            p.Deck.Insert(0, GarbageLord);
            p.Deck.Insert(3, GarbageLord);
            var fieldBlocked = !e.CanActivateSpellTrap(p, onField, fromHand: false);
            var ok = e.TryActivateSpellTrap(p, a, fromHand: true);
            Drive(e, c => c.CardId == GarbageLord);
            var lords = CountIn(p.Hand, GarbageLord);
            var bBlocked = !e.CanActivateSpellTrap(p, b, fromHand: true);
            Check("Garbage Ogre: sent from the hand, adds \"Garbage Lord\"; a second Ogre cannot use it the same turn; " +
                  "not from the field",
                ok && lords == 1 && p.Graveyard.Contains(a) && !p.Hand.Contains(a) && bBlocked && fieldBlocked,
                $"ok={ok} lords={lords} aGy={p.Graveyard.Contains(a)} bBlocked={bBlocked} fieldBlocked={fieldBlocked}");
        }

        // ───────────────────────────── review fixes ─────────────────────────────

        /// <summary>
        /// c28565527 s.cfilter: the Tribute must leave a monster to target (IsExistingTarget excluding the released card).
        /// </summary>
        static void GoldenDragonSummonerNeedsATargetAfterTheTribute(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var a = Up(e, p, GoldenDragonSummoner, 0);
                var could = e.CanActivateSpellTrap(p, a, fromHand: false);
                Check("Golden Dragon Summoner alone on the field cannot activate (its Tribute would leave nothing to return)",
                    !could && p.MonstersOnField().Contains(a), $"could={could}");
            }

            {
                // Alone on your field with an opponent's monster: it Tributes itself and returns that monster.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var a = Up(e, p, GoldenDragonSummoner, 0);
                var victim = Up(e, o, Celtic, 0);
                var could = e.CanActivateSpellTrap(p, a, fromHand: false);
                var ok = e.TryActivateSpellTrap(p, a, fromHand: false);
                var offer = e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();
                Drive(e, c => c == a || c == victim);
                Check("Golden Dragon Summoner + 1 opponent's monster: Tributes itself, returns that monster",
                    could && ok && p.Graveyard.Contains(a) && o.Hand.Contains(victim),
                    $"could={could} ok={ok} offer={string.Join(",", offer.Select(c => c.Name))} " +
                    $"aGy={p.Graveyard.Contains(a)} bounced={o.Hand.Contains(victim)}");
            }
        }

        /// <summary>A second StartDuel on the same engine has new players: the first duel's uses do not carry over.</summary>
        static void NewDuelOnTheSameEngineStartsClean(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var a = Up(e, e.Player, RamClouder, 0);
            Up(e, e.Player, Celtic, 1);
            e.Player.Graveyard.Add(e.CreateCardInstance(RamClouder));
            var ok = e.TryActivateSpellTrap(e.Player, a, fromHand: false);
            Drive(e, c => c.CardId == Celtic || c.CardId == RamClouder);
            var turn1 = e.TurnNumber;

            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            ClearBoard(e);
            e.Player.Hand.Clear();
            var b = Up(e, e.Player, RamClouder, 0);
            Up(e, e.Player, Celtic, 1);
            e.Player.Graveyard.Add(e.CreateCardInstance(RamClouder));
            var free = e.CanActivateSpellTrap(e.Player, b, fromHand: false);
            Check("a new duel on the same engine (same turn number) does not inherit the last duel's once per turn",
                ok && turn1 == e.TurnNumber && free, $"ok={ok} turns={turn1}/{e.TurnNumber} free={free}");
        }

        /// <summary>
        /// A hard once-per-turn targeted "If this card is Normal Summoned" trigger with no legal target does not activate,
        /// so a second copy Summoned later that turn (with a target) still can. Synthetic card: no card of this batch has
        /// the shape (Alsahm has no target), so the flag is set on the test card's own program.
        /// </summary>
        static void SummonTriggerWithNoTargetKeepsItsCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var def = new CardDef
            {
                id = 998877,
                name = "OPT Review Summoner",
                type = "Effect Monster",
                frameType = "effect",
                desc = "If this card is Normal Summoned: You can target 1 monster your opponent controls; destroy it.",
                race = "Fiend",
                attribute = "DARK",
                level = 3,
                atk = 1000,
                def = 600
            };
            var prog = CompiledEffectCache.GetOrCompile(def);
            var clause = prog?.ClausesFor(EffectTiming.ThisCardSummoned)?.FirstOrDefault();
            if (clause != null)
            {
                clause.OncePerTurn = true;
                clause.OptScope = OncePerTurnScope.PerCardName;
            }

            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            CardInstance Copy(int zone)
            {
                var c = Up(e, p, Celtic, zone);
                c.CardId = def.id;
                c.Def = def;
                return c;
            }

            var a = Copy(0);
            var first = TextEffectRuntime.TryResolveThisCardSummoned(e, p, a);
            Drive(e);
            var victim = Up(e, o, Celtic, 0);
            var b = Copy(1);
            var second = TextEffectRuntime.TryResolveThisCardSummoned(e, p, b);
            Drive(e, c => c == victim);
            Check("a hard once-per-turn targeted Summon trigger with no target does not use the count; a second copy destroys",
                prog != null && prog.FullyCompiled && clause != null && clause.RequiresTargetChoice &&
                second && o.Graveyard.Contains(victim),
                $"prog={Describe(prog)} first={first} second={second} victimGy={o.Graveyard.Contains(victim)}");
        }
    }
}
