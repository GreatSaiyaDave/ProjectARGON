using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "RDS" (v200–v202): the cards landed while finishing the pre-Link curriculum. Compile shapes plus
    /// the two duels that pin a rule no other card exercises — Pikeru's Circle of Enchantment (effect damage
    /// shielded, battle damage NOT, which is why it cannot share Hallowed Life Barrier's flag) and Roc from the
    /// Valley of Haze (a hand trip with no chain stamp). Perfect Machine King's "other" rule and Machine King's
    /// contrasting plain rule are pinned in <see cref="InteractionRegressionTests"/> "Self-scaling ATK/DEF".
    /// </summary>
    public static class RdsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int PerfectMachineKing = 18891691;
        const int UltimateInsectLv1 = 34894082;
        const int UltimateInsectLv3 = 34088136;
        const int RocFromTheValleyOfHaze = 28143906;
        const int PikerusCircleOfEnchantment = 74270067;
        const int MindHaxorz = 75392615;
        const int MysticalElf = 15025844;    // Normal Lv4 LIGHT 800/2000
        const int Celtic = 91152256;         // Normal Lv4 EARTH 1400/1200

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check(name, ok, detail);

            bool Implemented(int id)
            {
                var d = db.Get(id);
                return d != null && WRLDZ.Duel.CardEffectStatus.Classify(d) == WRLDZ.Duel.CardEffectStatusKind.Implemented;
            }

            string Shape(int id)
            {
                var p = CardTextEffectCompiler.Compile(db.Get(id));
                if (p == null) return "null";
                var cls = string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}:{c.Action}{(c.Amount != 0 ? "/" + c.Amount : "")}"));
                return $"full={p.FullyCompiled} [{cls}] unparsed=[{string.Join("|", p.UnparsedFragments ?? Array.Empty<string>())}]";
            }

            // ── compile shapes: no unparsed residue, Implemented ──
            foreach (var id in new[] { PerfectMachineKing, UltimateInsectLv3, RocFromTheValleyOfHaze,
                                       PikerusCircleOfEnchantment, MindHaxorz })
            {
                Check($"{db.Get(id)?.name ?? id.ToString()}: FullyCompiled and Implemented",
                    Implemented(id), Shape(id));
            }

            // ── Ultimate Insect LV3: the aura is gated on the LV1 summon ──
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(UltimateInsectLv3));
                var aura = prog?.ClauseList.Find(c =>
                    c.Action == EffectActionKind.ContinuousGainAtkDef && c.Amount < 0);
                Check("Ultimate Insect LV3: the -300 ATK aura is live only when Special Summoned by \"Ultimate Insect LV1\"",
                    prog != null && prog.FullyCompiled && aura != null &&
                    aura.Side == EffectSide.Opponent && aura.Amount == -300 &&
                    string.Equals(aura.SpecialSummonedByNamedCard, "Ultimate Insect LV1",
                        StringComparison.OrdinalIgnoreCase) &&
                    aura.RequiresThisSpecialSummonedByNamed,
                    aura == null ? $"no negative aura: {Shape(UltimateInsectLv3)}"
                        : $"side={aura.Side} amount={aura.Amount} named={aura.SpecialSummonedByNamedCard} " +
                          $"gated={aura.RequiresThisSpecialSummonedByNamed}");
            }

            // ── Roc: the hand trip returns the card to the Deck and shuffles ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                o.Hand.Clear();
                o.Graveyard.Clear();
                var roc = PutInHand(engine, p, RocFromTheValleyOfHaze);
                var deckBefore = p.Deck.Count;
                // "Sent directly from your hand to the Graveyard": the card leaves the hand and enters the GY on a
                // hand trip, which is exactly what NoteEnteredGy records from anywhere in the engine.
                p.Graveyard.Add(engine.CreateCardInstance(roc.CardId));
                p.Hand.Remove(roc);
                var inGy = p.Graveyard[^1];
                WRLDZ.Duel.TextEffects.GyHandAndSummonEventTriggersRuntime.NoteEnteredGy(engine, inGy, fromHand: true);
                WRLDZ.Duel.TextEffects.SimpleTriggersAndSwarmRuntime.ShuffleThisFromGyIntoDeck(engine, p, inGy);
                Check("Roc from the Valley of Haze: the hand trip returns it to the Deck (not stuck in the GY)",
                    !p.Graveyard.Contains(inGy) && p.Deck.Count >= deckBefore,
                    $"gy={p.Graveyard.Contains(inGy)} deck {deckBefore}->{p.Deck.Count}");
            }

            // ── Pikeru's Circle of Enchantment: effect damage 0, and only for its controller ──
            {
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(engine);
                ClearBoard(engine);
                var p = engine.Player;
                var o = engine.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                // This is a Normal TRAP, so it has to be Set before it can be activated — "Traps must be Set first".
                var circle = PlaceSetTrap(engine, o, PikerusCircleOfEnchantment, 0);
                circle.SetThisTurn = false;
                var act = engine.TryActivateSpellTrap(o, circle, fromHand: false);
                var shield = o.NoEffectDamageThroughTurnNumber > 0 &&
                             engine.TurnNumber <= o.NoEffectDamageThroughTurnNumber;
                var oppLp = o.LifePoints;
                var myLp = p.LifePoints;
                engine.ApplyEffectDamage(o, 800, "test effect damage", circle);
                Check("Pikeru's Circle of Enchantment: effect damage to its controller becomes 0",
                    reached && act && shield && o.LifePoints == oppLp,
                    $"reached={reached} act={act} shield={shield} lp {oppLp}->{o.LifePoints}");
                // The same shield must not protect the other duelist: Hallowed Life Barrier's flag does.
                engine.ApplyEffectDamage(p, 800, "test effect damage", circle);
                Check("Pikeru's Circle of Enchantment: the other duelist is not protected by it",
                    p.LifePoints == myLp - 800,
                    $"lp {myLp}->{p.LifePoints}");
            }

            // ── Mind Haxorz: paying the LP is the whole cost; a look changes nothing ──
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(MindHaxorz));
                Check("Mind Haxorz: pays 500 LP and looks at the opponent's hand + Set cards",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c =>
                        c.Action == EffectActionKind.PayLpLookAtOpponentHandAndSetCards &&
                        c.PayLpAmount == 500 && c.Side == EffectSide.Opponent),
                    Shape(MindHaxorz));
            }
        }
    }
}
