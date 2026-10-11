using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Cards that were FullyCompiled from part of their text (fail-closed audit, 2026-09-29):
    /// Assault on GHQ (destroy, then mill 2), Dark Scorpion - Cliff the Trap Remover and Gorg the Strong
    /// ("activate 1 of these effects"). Dark Scorpion - Chick the Yellow and Dark Scorpion - Meanae the Thorn
    /// (ChoiceBullets batch v173) now compile both bullets too: Chick returns a card on the field, or looks at the
    /// top card of the opponent's Deck and puts it on top or bottom; Meanae adds a "Dark Scorpion" card from the
    /// Deck or GY. Dragged Down into the Grave still fails closed (each player's choice from the other's hand
    /// and the discard triggers are not wired). Also guards the two template leaks: a leftover "● …" bullet is no
    /// longer boilerplate, and the PSCT fallback no longer compiles "destroy it, then …" or "both players draw".
    /// </summary>
    public static class PartialCompilesRegressionTests
    {
        const int AssaultOnGhq = 62633180;
        const int Cliff = 6967870;          // Lv3 1200 ATK
        const int Gorg = 48768179;          // Lv5 1800 ATK
        const int Chick = 61587183;
        const int Meanae = 74153887;
        const int CyberRaider = 39978267;
        const int DraggedDown = 16435215;
        const int Celtic = 91152256;        // 1400 / 1200, Normal Monster
        const int DarkMagician = 46986414;  // 2500 / 2100, Normal Monster
        const int PotOfGreed = 55144522;    // a Set Normal Spell nobody can chain
        const int SevenTools = 3819470;     // Seven Tools of the Bandit: a Counter Trap that can answer a Trap

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("PartialCompiles: " + name, ok, detail ?? "");

            void Guard(string name, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    check("PartialCompiles: " + name + " (threw)", false,
                        ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace);
                }
            }

            CompiledCardProgram Prog(int id) => CardTextEffectCompiler.Compile(db.Get(id));

            CompiledCardProgram Synthetic(string type, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 990000001, name = "PartialCompiles Probe", type = type,
                    race = type.StartsWith("Trap") || type.StartsWith("Spell") ? "Normal" : "Warrior",
                    frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                    desc = desc
                });

            Guard("compile shapes", () => CompileShapes(db, Prog, Check));
            Guard("template leaks", () => TemplateLeaks(Synthetic, Check));
            Guard("assault human", () => AssaultHuman(db, pDeck, aDeck, Check));
            Guard("assault target gone", () => AssaultTargetGone(db, pDeck, aDeck, Check));
            Guard("assault chain window", () => AssaultChainWindow(db, pDeck, aDeck, Check));
            Guard("assault ai", () => AssaultAi(db, pDeck, aDeck, Check));
            Guard("cliff human", () => CliffHuman(db, pDeck, aDeck, Check));
            Guard("cliff offers", () => CliffOffers(db, pDeck, aDeck, Check));
            Guard("cliff ai", () => CliffAi(db, pDeck, aDeck, Check));
            Guard("gorg human", () => GorgHuman(db, pDeck, aDeck, Check));
            Guard("gorg owner", () => GorgOwner(db, pDeck, aDeck, Check));
            Guard("gorg ai", () => GorgAi(db, pDeck, aDeck, Check));
        }

        // ───────────────────────────── compile ─────────────────────────────

        static void CompileShapes(CardDatabase db, Func<int, CompiledCardProgram> prog,
            Action<string, bool, string> Check)
        {
            var assault = prog(AssaultOnGhq);
            var a = assault?.ClauseList;
            Check("Assault on GHQ compiles to a targeted Destroy of your own monster, then a 2-card mill",
                assault != null && assault.FullyCompiled && a.Count == 1 &&
                a[0].Timing == EffectTiming.Activate && a[0].Action == EffectActionKind.Destroy &&
                a[0].Zone == EffectZoneFilter.ControllerMonsters && a[0].RequiresTargetChoice &&
                a[0].ThenMillOpponentDeck == 2 &&
                CardEffectStatus.Classify(db.Get(AssaultOnGhq)) == CardEffectStatusKind.Implemented,
                Describe(assault));

            bool IsChoice(CompiledCardProgram p, EffectActionKind first, EffectZoneFilter firstZone, int mill) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 2 &&
                p.ClauseList.All(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.IsOptional) &&
                p.ClauseList[0].Action == first && p.ClauseList[0].Zone == firstZone &&
                p.ClauseList[0].RequiresTargetChoice && p.ClauseList[0].EffectChoiceIndex == 1 &&
                p.ClauseList[1].Action == EffectActionKind.SendFromTopOfDeckToGy &&
                p.ClauseList[1].Side == EffectSide.Opponent && p.ClauseList[1].Amount == mill &&
                !p.ClauseList[1].RequiresTargetChoice && p.ClauseList[1].EffectChoiceIndex == 2;

            bool IsChoiceTwoBullets(CompiledCardProgram p, EffectActionKind a1, EffectZoneFilter z1,
                EffectActionKind a2, EffectZoneFilter z2) =>
                p != null && p.FullyCompiled && p.ClauseList.Count == 2 &&
                p.ClauseList.All(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.IsOptional) &&
                p.ClauseList[0].Action == a1 && p.ClauseList[0].Zone == z1 &&
                p.ClauseList[0].EffectChoiceIndex == 1 &&
                p.ClauseList[1].Action == a2 && p.ClauseList[1].Zone == z2 &&
                p.ClauseList[1].EffectChoiceIndex == 2;

            var cliff = prog(Cliff);
            Check("Cliff compiles both bullets as one choice: destroy a Spell/Trap, or mill 2",
                IsChoice(cliff, EffectActionKind.Destroy, EffectZoneFilter.FieldSpellTraps, 2), Describe(cliff));
            var gorg = prog(Gorg);
            Check("Gorg compiles both bullets as one choice: any opponent's monster to the Deck top, or mill 1",
                IsChoice(gorg, EffectActionKind.PlaceTargetOnTopOfDeck, EffectZoneFilter.OppMonsters, 1),
                Describe(gorg));

            var chick = prog(Chick);
            Check("Chick compiles both bullets as one choice: return a card on the field, or top-or-bottom of the opponent's Deck",
                IsChoiceTwoBullets(chick, EffectActionKind.ReturnToHand, EffectZoneFilter.AnyCardOnField,
                    EffectActionKind.LookAtOpponentTopCardThenTopOrBottom, EffectZoneFilter.None),
                Describe(chick));
            var meanae = prog(Meanae);
            Check("Meanae compiles both bullets as one choice: add a Dark Scorpion card from the Deck or GY to the hand",
                meanae != null && meanae.FullyCompiled && meanae.ClauseList.Count == 2 &&
                meanae.ClauseList.All(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.IsOptional) &&
                meanae.ClauseList[0].Action == EffectActionKind.AddFromDeckToHand &&
                meanae.ClauseList[0].Zone == EffectZoneFilter.ControllerDeckCards &&
                meanae.ClauseList[0].TargetSeriesName == "Dark Scorpion" &&
                meanae.ClauseList[1].Action == EffectActionKind.AddFromGyToHand &&
                meanae.ClauseList[1].Zone == EffectZoneFilter.ControllerGyMonsters &&
                meanae.ClauseList[1].TargetSeriesName == "Dark Scorpion" &&
                CardEffectStatus.Classify(db.Get(Meanae)) == CardEffectStatusKind.Implemented,
                Describe(meanae));
            var cyberRaider = prog(CyberRaider);
            Check("Cyber Raider compiles both bullets as one choice: destroy an Equip Card, or equip it to this card",
                cyberRaider != null && cyberRaider.FullyCompiled && cyberRaider.ClauseList.Count == 2 &&
                cyberRaider.ClauseList.All(c => c.Timing == EffectTiming.ThisCardSummoned && c.IsOptional) &&
                cyberRaider.ClauseList[0].Action == EffectActionKind.Destroy &&
                cyberRaider.ClauseList[0].Zone == EffectZoneFilter.FieldEquipCards &&
                cyberRaider.ClauseList[0].EffectChoiceIndex == 1 &&
                cyberRaider.ClauseList[1].Action == EffectActionKind.EquipTargetToThis &&
                cyberRaider.ClauseList[1].Zone == EffectZoneFilter.FieldEquipCards &&
                cyberRaider.ClauseList[1].EffectChoiceIndex == 2 &&
                CardEffectStatus.Classify(db.Get(CyberRaider)) == CardEffectStatusKind.Implemented,
                Describe(cyberRaider));
            var dragged = prog(DraggedDown);
            // DcrSpellTrapUtility batch: the whole text is one atom now (picks, discards, then both draw), never a bare
            // 'draw 1'.
            Check("Dragged Down into the Grave compiles whole (no bare 'draw 1')",
                dragged != null && dragged.FullyCompiled && dragged.ClauseList.Count == 1 &&
                dragged.ClauseList[0].Action == EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw &&
                !dragged.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.Draw),
                Describe(dragged));
        }

        static void TemplateLeaks(Func<string, string, CompiledCardProgram> synthetic,
            Action<string, bool, string> Check)
        {
            const string Trigger =
                "When this card inflicts Battle Damage to your opponent, you can activate 1 of these effects:\n";
            var unknownBullet = synthetic("Effect Monster",
                Trigger + "● Target 1 Spell/Trap Card on the field; destroy that target.\n● Draw 1 card.");
            Check("a bullet with no compiled template leaves the card uncompiled (no longer boilerplate)",
                unknownBullet != null && !unknownBullet.FullyCompiled, Describe(unknownBullet));

            var twoUntargeted = synthetic("Effect Monster",
                Trigger + "● Send the top card of their Deck to the Graveyard.\n" +
                "● Send the top 2 cards of their Deck to the Graveyard.");
            // BattleOutcomeTriggers batch (v162): two untargeted bullets are a legal choice now (Don Zaloog; EffectChoiceRuntime
            // offers the first, CANCEL the other); two targeted bullets still do not compile.
            Check("two untargeted bullets compile as a 1-of-2 choice (both untargeted, choices 1 and 2)",
                twoUntargeted != null && twoUntargeted.FullyCompiled && twoUntargeted.ClauseList.Count == 2 &&
                twoUntargeted.ClauseList.TrueForAll(c => !c.RequiresTargetChoice && c.IsOptional) &&
                twoUntargeted.ClauseList.Exists(c => c.EffectChoiceIndex == 1) &&
                twoUntargeted.ClauseList.Exists(c => c.EffectChoiceIndex == 2), Describe(twoUntargeted));
            var twoTargeted = synthetic("Effect Monster",
                Trigger + "● Target 1 Spell/Trap Card on the field; destroy that target.\n" +
                "● Target 1 monster your opponent controls; return that target to the top of the Deck.");
            Check("two targeted bullets with different pools do not compile (the picker offers only one pool)",
                twoTargeted != null && !twoTargeted.FullyCompiled, Describe(twoTargeted));
            var twoTargetedSamePool = synthetic("Effect Monster",
                Trigger + "● Target 1 Equip Card on the field; destroy that target.\n" +
                "● Target 1 Equip Card on the field; equip that target to this card.");
            Check("two targeted bullets sharing one pool compile (the picker offers b1's targets, CANCEL b2's)",
                twoTargetedSamePool != null && twoTargetedSamePool.FullyCompiled &&
                twoTargetedSamePool.ClauseList.Count == 2 &&
                twoTargetedSamePool.ClauseList[0].Zone == EffectZoneFilter.FieldEquipCards &&
                twoTargetedSamePool.ClauseList[1].Zone == EffectZoneFilter.FieldEquipCards,
                Describe(twoTargetedSamePool));

            var destroyThen = synthetic("Trap Card",
                "Target 1 monster your opponent controls; destroy it, then draw 1 card.");
            Check("the PSCT fallback no longer compiles 'destroy it, then …' as a lone Destroy",
                destroyThen != null && !destroyThen.FullyCompiled, Describe(destroyThen));

            var bothDraw = synthetic("Spell Card",
                "Both players discard 1 card, then both players draw 1 card.");
            Check("the PSCT fallback no longer compiles 'both players draw 1 card' as 'you draw 1'",
                bothDraw != null && !(bothDraw.FullyCompiled &&
                                      bothDraw.ClauseList.Exists(c => c != null &&
                                                                      c.Action == EffectActionKind.Draw &&
                                                                      c.Side == EffectSide.Controller)),
                Describe(bothDraw));
        }

        // ───────────────────────────── Assault on GHQ ─────────────────────────────

        /// <summary>Only your own monsters are legal; the target is destroyed, then the opponent mills 2.</summary>
        static void AssaultHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, humanVsHuman: false);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachMain(e, p, battle: false);
            ClearAll(e);
            var weak = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var strong = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Defense, false);
            var theirs = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var assault = PlaceSetTrap(e, p, AssaultOnGhq, 0);
            e.NotifyPublic();
            var deck = o.Deck.Count;
            var top2 = o.Deck.Take(2).ToList();
            var gy = o.Graveyard.Count;
            var act = e.TryActivateSpellTrap(p, assault, fromHand: false);
            var legal = e.PendingActivation?.LegalTargets.ToList() ?? new System.Collections.Generic.List<CardInstance>();
            var ownOnly = legal.Contains(weak) && legal.Contains(strong) && !legal.Contains(theirs);
            var sel = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(strong);
            PassAll(e);
            var milled = o.Graveyard.Skip(gy).Select(c => c.CardId).ToList();
            Check("Assault on GHQ: target a monster you control (face-down too); it is destroyed, then the opponent " +
                  "sends the top 2 cards of their Deck to the GY",
                reached && act && ownOnly && sel && p.Graveyard.Contains(strong) && p.TryFindMonster(weak, out _) &&
                o.TryFindMonster(theirs, out _) && o.Deck.Count == deck - 2 && milled.SequenceEqual(top2) &&
                p.Graveyard.Contains(assault),
                $"reached={reached} act={act} legal=[{Names(legal)}] sel={sel} strongGy={p.Graveyard.Contains(strong)} " +
                $"deck {deck}->{o.Deck.Count} milled=[{string.Join(",", milled)}] top2=[{string.Join(",", top2)}]");
        }

        /// <summary>
        /// "Then": the mill needs the destroy. Assault's Chain Link resolves against a target that already left the
        /// field (destroyed in response): nothing is milled. The control link, with its target still there, mills 2.
        /// </summary>
        static void AssaultTargetGone(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var gone in new[] { true, false })
            {
                var e = Start(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p, battle: false);
                ClearAll(e);
                var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var assault = PlaceSetTrap(e, p, AssaultOnGhq, 0);
                assault.FaceUp = true;
                e.NotifyPublic();
                var def = db.Get(AssaultOnGhq);
                var link = e.Chain.AddLink(p, assault, OfficialEffectRegistry.SpeedOf(def),
                    OfficialEffectRegistry.ClassOfActivation(def, isTriggerWindow: false), CardLocation.SpellTrapZone,
                    fromHand: false, wasSet: true, "manual", mine);
                if (gone) e.SendCardToGrave(p, mine);
                var deck = o.Deck.Count;
                e.Chain.StartResolution();
                e.ResolveChainStackForActivation();
                var ok = gone ? o.Deck.Count == deck : o.Deck.Count == deck - 2 && p.Graveyard.Contains(mine);
                Check(gone
                        ? "Assault on GHQ: its target left the field before it resolved, so nothing is milled"
                        : "Assault on GHQ: resolving its Chain Link destroys the target, then mills 2",
                    reached && link != null && ok && !e.Chain.HasLinks,
                    $"reached={reached} link={link != null} deck {deck}->{o.Deck.Count} chain={e.Chain.Describe()}");
            }
        }

        /// <summary>
        /// Seated like any targeted Trap: after the pick the opponent may chain (Seven Tools is offered). The target
        /// leaves the field meanwhile, so Assault destroys nothing and mills nothing.
        /// </summary>
        static void AssaultChainWindow(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, humanVsHuman: true); // a human opponent keeps the window open
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachMain(e, p, battle: false);
            ClearAll(e);
            var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var assault = PlaceSetTrap(e, p, AssaultOnGhq, 0);
            var tools = PlaceSetTrap(e, o, SevenTools, 0);
            e.NotifyPublic();
            var deck = o.Deck.Count;
            var act = e.TryActivateSpellTrap(p, assault, fromHand: false);
            var sel = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(mine);
            var window = e.PendingResponse?.Responder == o && e.PendingResponse.LegalCards.Contains(tools);
            e.SendCardToGrave(p, mine);
            PassAll(e);
            Check("Assault on GHQ: the opponent may chain after the target is picked; a target gone by then means no mill",
                reached && act && sel && window && o.Deck.Count == deck && p.Graveyard.Contains(assault) &&
                !e.Chain.HasLinks && o.TryFindSpellTrap(tools, out _),
                $"reached={reached} act={act} sel={sel} window={window} deck {deck}->{o.Deck.Count} " +
                $"chain={e.Chain.Describe()}");
        }

        /// <summary>The AI activates it: it gives up its weakest monster.</summary>
        static void AssaultAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, humanVsHuman: false);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachMain(e, o, battle: false);
            ClearAll(e);
            var weak = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var strong = PlaceMonster(e, o, DarkMagician, 1, BattlePosition.Attack, true);
            var assault = PlaceSetTrap(e, o, AssaultOnGhq, 0);
            e.NotifyPublic();
            var deck = p.Deck.Count;
            var act = e.TryActivateSpellTrap(o, assault, fromHand: false);
            var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(weak);
            PassAll(e);
            Check("Assault on GHQ (AI): destroys its weakest monster, then the player mills 2",
                reached && act && sel && o.Graveyard.Contains(weak) && o.TryFindMonster(strong, out _) &&
                p.Deck.Count == deck - 2 && !e.IsAwaitingEffectTarget,
                $"reached={reached} act={act} sel={sel} weakGy={o.Graveyard.Contains(weak)} deck {deck}->{p.Deck.Count}");
        }

        // ───────────────────────────── Dark Scorpion - Cliff the Trap Remover ─────────────────────────────

        /// <summary>The human's picker: tap a Spell/Trap (destroy it), tap Cliff (mill 2), or CANCEL (neither).</summary>
        static void CliffHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var mode in new[] { "target", "mill", "cancel" })
            {
                var e = Start(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p, battle: true);
                ClearAll(e);
                var cliff = PlaceMonster(e, p, Cliff, 0, BattlePosition.Attack, true);
                var theirs = PlaceSetTrap(e, o, PotOfGreed, 0);
                var mine = PlaceSetTrap(e, p, PotOfGreed, 0);
                e.NotifyPublic();
                var lp = o.LifePoints;
                var deck = o.Deck.Count;
                var hit = AttackUntilPick(e, p, cliff, null);
                var pending = e.PendingActivation;
                var offered = hit && pending?.EffectChoice != null && pending.Controller == p &&
                              pending.LegalTargets.Count == 3 && pending.LegalTargets.Contains(theirs) &&
                              pending.LegalTargets.Contains(mine) && pending.LegalTargets.Contains(cliff) &&
                              pending.Prompt.Contains("activate 1 effect");
                var prompt = pending?.Prompt;
                var done = mode switch
                {
                    "target" => e.TrySelectEffectTarget(theirs),
                    "mill" => e.TrySelectEffectTarget(cliff),
                    _ => e.CancelEffectTargeting()
                };
                Drain(e);
                var ok = mode switch
                {
                    "target" => o.Graveyard.Contains(theirs) && o.Deck.Count == deck && p.TryFindSpellTrap(mine, out _),
                    "mill" => o.Deck.Count == deck - 2 && o.TryFindSpellTrap(theirs, out _),
                    _ => o.Deck.Count == deck && o.TryFindSpellTrap(theirs, out _) && p.TryFindSpellTrap(mine, out _)
                };
                Check($"Cliff (human, {mode}): the choice is offered once and only that effect applies",
                    reached && offered && done && ok && o.LifePoints == lp - 1200 && !e.IsAwaitingEffectTarget &&
                    !e.HasDeclaredAttack && p.TryFindMonster(cliff, out _),
                    $"reached={reached} hit={hit} offered={offered} done={done} ok={ok} lp {lp}->{o.LifePoints} " +
                    $"deck {deck}->{o.Deck.Count} legal=[{Names(pending?.LegalTargets)}] prompt='{prompt}'");
            }
        }

        /// <summary>A bullet that cannot resolve is not offered; with neither, there is no prompt.</summary>
        static void CliffOffers(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var shape in new[] { "mill only", "target only", "neither" })
            {
                var e = Start(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p, battle: true);
                ClearAll(e);
                var cliff = PlaceMonster(e, p, Cliff, 0, BattlePosition.Attack, true);
                CardInstance theirs = null;
                if (shape == "target only")
                    theirs = PlaceSetTrap(e, o, PotOfGreed, 0);
                if (shape != "mill only")
                    while (o.Deck.Count > 1) o.Deck.RemoveAt(o.Deck.Count - 1); // IsPlayerCanDiscardDeck(1-tp, 2) fails
                e.NotifyPublic();
                var hit = AttackUntilPick(e, p, cliff, null);
                var pending = e.PendingActivation;
                var legal = Names(pending?.LegalTargets);
                var ok = shape switch
                {
                    "mill only" => pending?.EffectChoice != null && pending.LegalTargets.Count == 1 &&
                                   pending.LegalTargets[0] == cliff && pending.Prompt.Contains("send the top 2 cards"),
                    "target only" => pending?.EffectChoice != null && pending.LegalTargets.Count == 1 &&
                                     pending.LegalTargets[0] == theirs,
                    _ => pending == null
                };
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                Drain(e);
                Check($"Cliff offers ({shape})", reached && hit && ok && !e.HasDeclaredAttack,
                    $"reached={reached} hit={hit} legal=[{legal}] prompt='{pending?.Prompt}' deck={o.Deck.Count}");
            }
        }

        /// <summary>The AI destroys the player's Spell/Trap; with only its own on the field, it mills instead.</summary>
        static void CliffAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var playerHasSt in new[] { true, false })
            {
                var e = Start(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, o, battle: true);
                ClearAll(e);
                var cliff = PlaceMonster(e, o, Cliff, 0, BattlePosition.Attack, true);
                var own = PlaceSetTrap(e, o, PotOfGreed, 0);
                var theirs = playerHasSt ? PlaceSetTrap(e, p, PotOfGreed, 0) : null;
                e.NotifyPublic();
                var deck = p.Deck.Count;
                var lp = p.LifePoints;
                var hit = AttackUntilPick(e, o, cliff, null);
                Drain(e);
                var ok = playerHasSt
                    ? p.Graveyard.Contains(theirs) && p.Deck.Count == deck
                    : p.Deck.Count == deck - 2;
                Check($"Cliff (AI, player {(playerHasSt ? "has" : "has no")} Spell/Trap): " +
                      (playerHasSt ? "destroys it" : "mills 2, never its own card"),
                    reached && hit && ok && o.TryFindSpellTrap(own, out _) && p.LifePoints == lp - 1200 &&
                    !e.IsAwaitingEffectTarget,
                    $"reached={reached} hit={hit} ok={ok} deck {deck}->{p.Deck.Count} lp {lp}->{p.LifePoints}");
            }
        }

        // ───────────────────────────── Dark Scorpion - Gorg the Strong ─────────────────────────────

        /// <summary>
        /// Gorg destroys an Attack Position monster by battle: that monster is not a target; a face-down one is.
        /// Tapping it puts it on top of its owner's Deck; tapping Gorg mills 1.
        /// </summary>
        static void GorgHuman(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var mill in new[] { false, true })
            {
                var e = Start(db, pDeck, aDeck, humanVsHuman: false);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p, battle: true);
                ClearAll(e);
                var gorg = PlaceMonster(e, p, Gorg, 0, BattlePosition.Attack, true);
                var beaten = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var setMonster = PlaceMonster(e, o, DarkMagician, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                var deck = o.Deck.Count;
                var lp = o.LifePoints;
                var hit = AttackUntilPick(e, p, gorg, beaten);
                var pending = e.PendingActivation;
                var offered = hit && pending?.EffectChoice != null && pending.LegalTargets.Count == 2 &&
                              pending.LegalTargets.Contains(setMonster) && pending.LegalTargets.Contains(gorg) &&
                              !pending.LegalTargets.Contains(beaten);
                var legal = Names(pending?.LegalTargets);
                var done = e.TrySelectEffectTarget(mill ? gorg : setMonster);
                Drain(e);
                var ok = mill
                    ? o.Deck.Count == deck - 1 && o.TryFindMonster(setMonster, out _)
                    : o.Deck.Count == deck + 1 && o.Deck[0] == DarkMagician && !o.TryFindMonster(setMonster, out _);
                Check($"Gorg (human, {(mill ? "mill 1" : "face-down monster to the Deck top")}): the monster it " +
                      "destroyed is not a target",
                    reached && offered && done && ok && o.Graveyard.Exists(c => c == beaten) &&
                    o.LifePoints == lp - 400 && !e.IsAwaitingEffectTarget && !e.HasDeclaredAttack,
                    $"reached={reached} hit={hit} offered={offered} done={done} ok={ok} legal=[{legal}] " +
                    $"deck {deck}->{o.Deck.Count} beatenGy={o.Graveyard.Contains(beaten)} lp {lp}->{o.LifePoints}");
            }
        }

        /// <summary>"Return that target to the top of the Deck": its owner's Deck, even while the opponent controls it.</summary>
        static void GorgOwner(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, humanVsHuman: false);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachMain(e, p, battle: true);
            ClearAll(e);
            var gorg = PlaceMonster(e, p, Gorg, 0, BattlePosition.Attack, true);
            var beaten = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            // A Dark Magician the player owns, controlled by the opponent (Change of Heart).
            var stolen = PlaceMonster(e, o, DarkMagician, 1, BattlePosition.Attack, true);
            DuelEngine.RecordOwnerOnFieldEntry(stolen, p, o);
            e.NotifyPublic();
            var myDeck = p.Deck.Count;
            var theirDeck = o.Deck.Count;
            var hit = AttackUntilPick(e, p, gorg, beaten);
            var done = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(stolen);
            Drain(e);
            Check("Gorg: a monster the opponent took control of goes on top of its owner's Deck",
                reached && hit && done && !o.TryFindMonster(stolen, out _) && p.Deck.Count == myDeck + 1 &&
                p.Deck[0] == DarkMagician && o.Deck.Count == theirDeck,
                $"reached={reached} hit={hit} done={done} myDeck {myDeck}->{p.Deck.Count} theirDeck {theirDeck}->{o.Deck.Count}");
        }

        /// <summary>The AI returns the player's surviving monster (never the one it destroyed by battle).</summary>
        static void GorgAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck, humanVsHuman: false);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachMain(e, o, battle: true);
            ClearAll(e);
            var gorg = PlaceMonster(e, o, Gorg, 0, BattlePosition.Attack, true);
            var beaten = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var setMonster = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Defense, false);
            e.NotifyPublic();
            var deck = p.Deck.Count;
            var hit = AttackUntilPick(e, o, gorg, beaten);
            Drain(e);
            Check("Gorg (AI): returns the player's other monster to the top of their Deck",
                reached && hit && p.Graveyard.Contains(beaten) && !p.TryFindMonster(setMonster, out _) &&
                p.Deck.Count == deck + 1 && p.Deck[0] == DarkMagician && !e.IsAwaitingEffectTarget,
                $"reached={reached} hit={hit} beatenGy={p.Graveyard.Contains(beaten)} deck {deck}->{p.Deck.Count}");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Start(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman)
        {
            var e = new DuelEngine { HumanVsHuman = humanVsHuman };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        static void ClearAll(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
        }

        /// <summary>End turns until <paramref name="who"/> is in a Main Phase (one that may battle, if asked).</summary>
        static bool ReachMain(DuelEngine e, DuelistState who, bool battle)
        {
            for (var t = 0; t < 8; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (e.TurnPlayer == who && e.InMainPhase && !e.IsAwaitingResponse &&
                    (!battle || e.CanConductBattlePhase))
                    return true;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == who && e.InMainPhase && (!battle || e.CanConductBattlePhase);
        }

        /// <summary>Enter the Battle Phase and attack; stop at the effect prompt (or once the attack resolved).</summary>
        static bool AttackUntilPick(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            var bp = e.TryEnterBattlePhase(who);
            if (e.IsAwaitingResponse) e.PassResponse();
            var declared = bp && e.TryAttack(who, attacker, target);
            for (var i = 0; i < 12 && declared && !e.IsAwaitingEffectTarget; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else break;
            }

            return declared;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static void Drain(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else e.RecoverStuckCombat();
            }
        }

        static string Names(System.Collections.Generic.IEnumerable<CardInstance> cards) =>
            cards == null ? "" : string.Join(", ", cards.Where(c => c != null).Select(c => c.Name));

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "<null>"
                : $"full={prog.FullyCompiled} clauses=[" +
                  string.Join("; ", prog.ClauseList.Where(c => c != null).Select(c =>
                      $"{c.Timing}/{c.Action}/{c.Zone}/{c.Side} amt={c.Amount} tgt={c.RequiresTargetChoice} " +
                      $"choice={c.EffectChoiceIndex} mill={c.ThenMillOpponentDeck}")) +
                  $"] unparsed=[{string.Join(" | ", prog.UnparsedFragments ?? Array.Empty<string>())}]";
    }
}
