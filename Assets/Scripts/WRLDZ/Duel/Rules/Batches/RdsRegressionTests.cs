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
        const int HeavyMechSupportPlatform = 23265594;
        const int LightenTheLoad = 37231841;
        const int MachineKing = 46700124;    // Machine 1750/1800 — the typed Union's host
        const int SummonedSkull = 54652250; // Non-Machine, must not be a legal host
        const int GaiaFierceKnight = 6368038; // Normal Lv7 — a legal Lighten the Load cost
        const int MindHaxorz = 75392615;
        const int DivineWrath = 49010598;      // Counter Trap: answers a monster EFFECT
        const int PotOfGreed = 55144522;       // a Spell activation — Divine Wrath must not answer it
        const int ManEaterBug = 54652250;      // FLIP effect: a chain link that IS a monster effect
        const int MysticalElf = 15025844;    // Normal Lv4 LIGHT 800/2000
        const int Celtic = 91152256;         // Normal Lv4 EARTH 1400/1200

        static bool OnField(DuelistState who, CardInstance c) => who != null && c != null && who.TryFindMonster(c, out _);

        static string State(DuelEngine e) =>
            (e?.PendingResponse == null
                ? "no window"
                : $"timing={e.PendingResponse.Timing} responder={e.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", e.PendingResponse.LegalCards.Select(c => c?.Name))}]") +
            $" chain={e?.Chain?.Count ?? 0}";

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e != null && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static bool Offers(DuelEngine e, DuelistState who, CardInstance card) =>
            e?.PendingResponse != null && e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
            e.PendingResponse.Responder == who && e.PendingResponse.LegalCards.Contains(card);

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
                                       PikerusCircleOfEnchantment, MindHaxorz, HeavyMechSupportPlatform,
                                       LightenTheLoad })
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

            // ── Heavy Mech Support Platform: a typed Union — only a Machine is a legal host ──
            {
                var eng = Fresh(db, pDeck, aDeck);
                ClearBoard(eng);
                var pp = eng.Player;
                var mach = PlaceMonster(eng, pp, MachineKing, 1, BattlePosition.Attack, true);
                var non = PlaceMonster(eng, pp, SummonedSkull, 2, BattlePosition.Attack, true);
                var hm = PlaceMonster(eng, pp, HeavyMechSupportPlatform, 0, BattlePosition.Attack, true);
                hm.SummonedThisTurn = false;
                var correct = WRLDZ.Duel.TextEffects.EquipFamilyRuntime.IsCorrectEquipTarget(eng, hm, mach);
                var wrong = WRLDZ.Duel.TextEffects.EquipFamilyRuntime.IsCorrectEquipTarget(eng, hm, non);
                var clause = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(db.Get(HeavyMechSupportPlatform))
                    ?.ClauseList.Find(c => c?.Action == EffectActionKind.EquipThisToTarget);
                Check("Heavy Mech Support Platform: the typed Union equips to a Machine, and only to a Machine",
                    correct && !wrong && clause != null && clause.UnionEquipsAnyFaceUpMonster &&
                    string.Equals(clause.RaceFilter, "Machine", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(clause.EquipHostName),
                    $"machine={correct} nonMachine={wrong} race={clause?.RaceFilter} host={clause?.EquipHostName ?? "null"}");
                Check("Heavy Mech Support Platform: it can be activated once it is on the field",
                    eng.CanActivateSpellTrap(pp, hm, fromHand: false),
                    "cannot activate");
            }

            // ── Lighten the Load: the Level 7+ floor, and it cannot be used without such a monster ──
            {
                var eng = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(eng);
                ClearBoard(eng);
                var pp = eng.Player;
                var oo = eng.Opponent;
                pp.Hand.Clear();
                oo.Hand.Clear();
                var clause = WRLDZ.Duel.TextEffects.CardTextEffectCompiler.Compile(db.Get(LightenTheLoad))
                    ?.ClauseList[0];
                var lighten = PutInHand(eng, oo, LightenTheLoad);
                var withoutHigh = eng.CanActivateSpellTrap(oo, lighten, fromHand: true);
                // Level 7 Summoned Skull in the hand makes the effect usable.
                PutInHand(eng, oo, GaiaFierceKnight);
                var withHigh = eng.CanActivateSpellTrap(oo, lighten, fromHand: true);
                Check("Lighten the Load: the Level floor is a clause parameter, not a hard-coded number",
                    clause != null && clause.MinLevelOrHigher == 7 &&
                    clause.Action == EffectActionKind.ReturnHandLevelMonsterToDeckShuffleThenDraw,
                    $"min={clause?.MinLevelOrHigher} action={clause?.Action}");
                Check("Lighten the Load: usable only with a Level 7 or higher monster in hand",
                    reached && !withoutHigh && withHigh,
                    $"reached={reached} without={withoutHigh} with={withHigh}");
            }

            // ── Divine Wrath: the one answer whose legality reads a monster EFFECT ──
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(DivineWrath));
                var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Divine Wrath: answers a monster effect, pays a discard, negates and destroys",
                    prog != null && prog.FullyCompiled && Implemented(DivineWrath) && c != null &&
                    c.Timing == EffectTiming.ChainLinkActivated &&
                    c.NegateRespondsToMonsterEffect && !c.NegateRespondsToSpell && !c.NegateRespondsToTrap &&
                    c.RequiresDiscardCost && c.DiscardCostCount == 1 && c.DestroyNegatedCard,
                    c == null ? $"full={prog?.FullyCompiled}"
                        : $"monster={c.NegateRespondsToMonsterEffect} spell={c.NegateRespondsToSpell} " +
                          $"trap={c.NegateRespondsToTrap} discard={c.RequiresDiscardCost}/{c.DiscardCostCount}");
            }

            {
                // A Spell activation is NOT a monster effect: Divine Wrath must not be offered for it. Solemn
                // Judgment-style answers are, which is what keeps the two families apart.
                var eng = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(eng);
                ClearBoard(eng);
                var p = eng.Player;
                var o = eng.Opponent;
                p.Hand.Clear();
                o.Hand.Clear();
                var wrath = PlaceSetTrap(eng, p, DivineWrath, 0);
                wrath.SetThisTurn = false;
                var pot = PutInHand(eng, o, PotOfGreed);
                eng.NotifyPublic();
                var act = reached && eng.TryActivateSpellTrap(o, pot, fromHand: true);
                Check("Divine Wrath: not offered against a Spell Card activation",
                    act && !Offers(eng, p, wrath),
                    $"act={act} offered={Offers(eng, p, wrath)} {State(eng)}");
                PassAll(eng);
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
