using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// MfcSimpleSpellsB batch (v174): Emblem of Dragon Destroyer (Deck-or-GY named search) and Hidden
    /// Spellbook (target-2 GY Spells shuffled into the Deck). Compile shapes through the real compiler
    /// plus mini-duels through the engine entry points (TryActivateSpellTrap, TrySelectEffectTarget).
    /// Thunder of Ruler stays fail-closed (see MfcSimpleSpellsBAtoms); its pin lives in
    /// MfcSimpleSpellsRegressionTests.
    /// </summary>
    public static class MfcSimpleSpellsBRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int EmblemOfDragonDestroyer = 6390406; // Normal Spell
        const int HiddenSpellbook = 21840375;        // Normal Trap
        const int BusterBlader = 78193831;           // Effect Monster Lv7 2600/2300
        const int PotOfGreed = 55144522;             // Normal Spell (GY fodder)
        const int PrematureBurial = 70828912;        // Equip Spell (GY fodder)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("MfcSimpleSpellsB: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            EmblemDuels(db, pDeck, aDeck, Check);
            HiddenSpellbookDuels(db, pDeck, aDeck, Check);
        }

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static CardInstance AddToGy(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            who.Graveyard.Add(c);
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }
                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }
                break;
            }
            return picked;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            Drive(e, _ => false);
            DrainCombat(e);
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) =>
            CardTextEffectCompiler.Compile(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var emblem = Prog(db, EmblemOfDragonDestroyer);
            var ec = emblem?.ClauseList.Count == 1 ? emblem.ClauseList[0] : null;
            Check("Emblem of Dragon Destroyer FullyCompiled: one Activate Deck-or-GY named search",
                emblem != null && emblem.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(EmblemOfDragonDestroyer)) == CardEffectStatusKind.Implemented &&
                ec != null && ec.Timing == EffectTiming.Activate &&
                ec.Action == EffectActionKind.AddNamedFromDeckOrGyToHand &&
                ec.Zone == EffectZoneFilter.ControllerDeckOrGyNamed &&
                ec.NamedCard == "Buster Blader" && ec.RequiresTargetChoice && ec.IsPsctTarget &&
                ec.TargetCount == 1 && ec.MakesChainLink &&
                EffectVocabulary.ResolutionOf(ec) == EffectResolutionKind.Search,
                Describe(emblem));

            var book = Prog(db, HiddenSpellbook);
            var bc = book?.ClauseList.Count == 1 ? book.ClauseList[0] : null;
            Check("Hidden Spellbook FullyCompiled: one Activate clause, 2 GY Spell targets shuffled into Deck",
                book != null && book.FullyCompiled &&
                CardEffectStatus.Classify(db.Get(HiddenSpellbook)) == CardEffectStatusKind.Implemented &&
                bc != null && bc.Timing == EffectTiming.Activate &&
                bc.Action == EffectActionKind.ShuffleGySpellsIntoDeck &&
                bc.Zone == EffectZoneFilter.ControllerGySpells &&
                bc.RequiresTargetChoice && bc.IsPsctTarget && bc.TargetCount == 2 && bc.MakesChainLink &&
                EffectVocabulary.ResolutionOf(bc) == EffectResolutionKind.ReturnToDeck,
                Describe(book));
        }

        static void EmblemDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Buster Blader in the Deck: added to the hand (Deck first, per the Lua order).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                p.Deck.Add(BusterBlader);
                var deckBefore = p.Deck.Count;
                var emblem = PutInHand(e, p, EmblemOfDragonDestroyer);
                var act = e.TryActivateSpellTrap(p, emblem, fromHand: true);
                Drive(e, c => c != null && c.CardId == BusterBlader);
                Settle(e);
                Check("Emblem: Buster Blader in the Deck is added to the hand, Deck shrinks, Emblem to the GY",
                    act && p.Hand.Any(c => c != null && c.CardId == BusterBlader) &&
                    !p.Deck.Contains(BusterBlader) && p.Deck.Count == deckBefore - 1 &&
                    p.Graveyard.Contains(emblem),
                    $"act={act} hand={p.Hand.Count} deck={deckBefore}->{p.Deck.Count}");
            }
            {
                // Buster Blader only in the GY: the same copy comes back to the hand.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var gyCopy = AddToGy(e, p, BusterBlader);
                var emblem = PutInHand(e, p, EmblemOfDragonDestroyer);
                var act = e.TryActivateSpellTrap(p, emblem, fromHand: true);
                Drive(e, c => c != null && c.CardId == BusterBlader);
                Settle(e);
                Check("Emblem: with no Deck copy, the GY copy is added to the hand",
                    act && p.Hand.Contains(gyCopy) && !p.Graveyard.Contains(gyCopy) &&
                    p.Graveyard.Contains(emblem),
                    $"act={act} hand={p.Hand.Count} gy={p.Graveyard.Count}");
            }
            {
                // No Buster Blader anywhere: refused, nothing moves (fail-closed activation gate).
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var emblem = PutInHand(e, p, EmblemOfDragonDestroyer);
                var handBefore = p.Hand.Count;
                var deckBefore = p.Deck.Count;
                var can = e.CanActivateSpellTrap(p, emblem, fromHand: true);
                var act = e.TryActivateSpellTrap(p, emblem, fromHand: true);
                Settle(e);
                Check("Emblem: refused with no Buster Blader in Deck or GY",
                    !can && !act && p.Hand.Count == handBefore && p.Deck.Count == deckBefore &&
                    p.Hand.Contains(emblem),
                    $"can={can} act={act} hand={p.Hand.Count} deck={p.Deck.Count}");
            }
        }

        static void HiddenSpellbookDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Two Spells in the GY: both shuffled into the Deck, Deck grows by two.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var s1 = AddToGy(e, p, PotOfGreed);
                var s2 = AddToGy(e, p, PrematureBurial);
                var deckBefore = p.Deck.Count;
                var book = PlaceSetTrap(e, p, HiddenSpellbook, 0);
                var act = e.TryActivateSpellTrap(p, book, fromHand: false);
                // The seated target pick opens after the activation's response window (Wingbeat idiom):
                // pass responses only — never cancel an open pick here.
                for (var i = 0; i < 8 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++)
                    e.PassResponse();
                var pend = e.PendingActivation;
                var offered = e.IsAwaitingEffectTarget && Legal(e).Contains(s1) && Legal(e).Contains(s2);
                var offeredState = $"await={e.IsAwaitingEffectTarget} legal={Legal(e).Count} " +
                                   $"prompt=\"{pend?.Prompt ?? "-"}\" multi={pend?.MultiTarget}";
                var first = offered && e.TrySelectEffectTarget(s1);
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
                var stillAsking = first && e.IsAwaitingEffectTarget;
                var second = stillAsking && e.TrySelectEffectTarget(s2);
                Settle(e);
                Check("Hidden Spellbook: the 2 GY Spell targets are shuffled into the Deck",
                    act && offered && first && stillAsking && second &&
                    !p.Graveyard.Contains(s1) && !p.Graveyard.Contains(s2) &&
                    p.Deck.Count == deckBefore + 2 && p.Deck.Contains(s1.CardId) &&
                    p.Deck.Contains(s2.CardId) && p.Graveyard.Contains(book),
                    $"act={act} offered={offered} first={first} stillAsking={stillAsking} second={second} " +
                    $"deck={deckBefore}->{p.Deck.Count} gy={p.Graveyard.Count} [{offeredState}]");
            }
            {
                // Only one Spell in the GY: refused (needs exactly 2 targets), nothing moves.
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                AddToGy(e, p, PotOfGreed);
                var deckBefore = p.Deck.Count;
                var book = PlaceSetTrap(e, p, HiddenSpellbook, 0);
                var can = e.CanActivateSpellTrap(p, book, fromHand: false);
                var act = e.TryActivateSpellTrap(p, book, fromHand: false);
                Settle(e);
                Check("Hidden Spellbook: refused with only 1 Spell in the GY",
                    !can && !act && p.Deck.Count == deckBefore && p.Graveyard.Count == 1,
                    $"can={can} act={act} deck={p.Deck.Count} gy={p.Graveyard.Count}");
            }
        }
    }
}
