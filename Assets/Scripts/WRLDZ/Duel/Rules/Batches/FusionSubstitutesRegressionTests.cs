using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Coverage family F2 (v180, FusionSubstitutes): "You can substitute this card for any 1 Fusion Material Monster."
    /// (<see cref="FusionSubstitutesAtoms"/>, <see cref="FusionSubstitutesRuntime"/>). Compile shapes, the substitute
    /// satisfying exactly one named material (from the hand or the field), the rule that the other material(s) must be
    /// correct, and the fail-closed pins for the three Hex-Sealed cards whose second sentence is not compiled.
    /// </summary>
    public static class FusionSubstitutesRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Polymerization = 24094653;
        const int FlameSwordsman = 45231177;   // "Flame Manipulator" + "Masaki the Legendary Swordsman"
        const int FlameManipulator = 34460851; // Normal
        const int Masaki = 44287299;           // Normal

        const int MysticalSheep = 30451366;
        const int Versago = 50259460;
        const int GoddessThirdEye = 53493204;
        const int Beastking = 99426834;
        const int HexSealedLight = 15717011;
        const int HexSealedDark = 52101615;
        const int HexSealedEarth = 88696724;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("FusionSubstitutes: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            SubstituteDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, label) in new[]
                     {
                         (MysticalSheep, "Mystical Sheep #1"), (Versago, "Versago the Destroyer"),
                         (GoddessThirdEye, "Goddess with the Third Eye"), (Beastking, "Beastking of the Swamps")
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                Check($"{label} compiles to a FusionSubstitute ability",
                    implemented && prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0] != null && prog.ClauseList[0].Action == EffectActionKind.FusionSubstitute,
                    $"implemented={implemented} FC={prog?.FullyCompiled} n={prog?.ClauseList.Count}");
            }

            // The three Hex-Sealed cards carry the substitute ability but their second sentence is not compiled yet, so
            // they must stay fail-closed (not FullyCompiled) and must not be usable as substitutes.
            foreach (var (id, label) in new[]
                     {
                         (HexSealedLight, "The Light - Hex-Sealed Fusion"), (HexSealedDark, "The Dark - Hex-Sealed Fusion"),
                         (HexSealedEarth, "The Earth - Hex-Sealed Fusion")
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{label} still fails closed (its tribute sentence is not compiled)",
                    prog != null && !prog.FullyCompiled &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    $"FC={prog?.FullyCompiled} classify={CardEffectStatus.Classify(def)}");
            }
        }

        // ───────────────────────────── fusion duels ─────────────────────────────

        static void SubstituteDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // 1. The substitute, in hand, stands in for the missing second named material.
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                var flame = PutInHand(e, p, FlameManipulator);
                var sheep = PutInHand(e, p, MysticalSheep);
                var poly = PutInHand(e, p, Polymerization);
                Check("Polymerization is legal: Flame Manipulator + Mystical Sheep #1 substitutes for Masaki",
                    e.CanActivateSpellTrap(p, poly, fromHand: true));
                var ok = e.TryActivateSpellTrap(p, poly, fromHand: true);
                Check("the substitute fuses into Flame Swordsman; both materials go to the GY",
                    ok && p.MonstersOnField().Any(m => m != null && m.CardId == FlameSwordsman) &&
                    p.Graveyard.Contains(flame) && p.Graveyard.Contains(sheep) &&
                    !p.ExtraDeck.Contains(FlameSwordsman),
                    $"ok={ok} field={string.Join(",", p.MonstersOnField().Select(m => m.ToString()))}");
            }

            // 2. The other material may itself be the substitute (Masaki present, Sheep stands in for Flame Manipulator).
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                PutInHand(e, p, Masaki);
                PutInHand(e, p, MysticalSheep);
                var poly = PutInHand(e, p, Polymerization);
                Check("the substitute may stand in for either named material",
                    e.TryActivateSpellTrap(p, poly, fromHand: true) &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == FlameSwordsman));
            }

            // 3. The substitute works from the field as well.
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                PlaceMonster(e, p, FlameManipulator, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, MysticalSheep, 1, BattlePosition.Attack, true);
                var poly = PutInHand(e, p, Polymerization);
                Check("the substitute works from the field too",
                    e.TryActivateSpellTrap(p, poly, fromHand: true) &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == FlameSwordsman));
            }

            // 4. Only ONE material may be substituted: two substitutes cannot cover both names.
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                PutInHand(e, p, MysticalSheep);
                PutInHand(e, p, Versago);
                var poly = PutInHand(e, p, Polymerization);
                Check("two substitutes cannot cover both named materials (only one substitution allowed)",
                    !e.CanActivateSpellTrap(p, poly, fromHand: true) &&
                    !e.TryActivateSpellTrap(p, poly, fromHand: true));
            }

            // 5. A substitute carrier whose own text is not fully compiled (Hex-Sealed) does not count.
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                PutInHand(e, p, FlameManipulator);
                PutInHand(e, p, HexSealedLight);
                var poly = PutInHand(e, p, Polymerization);
                Check("an uncompiled substitute carrier (Hex-Sealed) cannot substitute",
                    !e.CanActivateSpellTrap(p, poly, fromHand: true));
            }

            // 6. Regression: the printed named materials still fuse with no substitute involved.
            {
                var e = Fresh(db, pDeck, aDeck);
                var p = e.Player;
                ClearBoard(e);
                p.Hand.Clear();
                p.Graveyard.Clear();
                p.ExtraDeck.Clear();
                p.ExtraDeck.Add(FlameSwordsman);
                PutInHand(e, p, FlameManipulator);
                PutInHand(e, p, Masaki);
                var poly = PutInHand(e, p, Polymerization);
                Check("the printed materials still fuse without a substitute",
                    e.TryActivateSpellTrap(p, poly, fromHand: true) &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == FlameSwordsman));
            }
        }
    }
}
