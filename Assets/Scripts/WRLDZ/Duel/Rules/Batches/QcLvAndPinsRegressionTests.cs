using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// QC LvAndPins (2026-10-18): the review of three RDS-era cards another agent compiled on main without review
    /// (4d0c17c4b, 004b84868, cc5a4dd9d), whose older "stays leftover" pins had gone red.
    /// <list type="bullet">
    /// <item><b>Dekoichi the Battlechanted Locomotive</b> — kept: FLIP, draw 1 + 1 per face-up "Bokoichi the Freightening
    /// Car" its controller controls, counted as the effect resolves. Face-up Bokoichi cards in the Spell &amp; Trap Zone
    /// count too (Lua s.filter over LOCATION_ONFIELD); face-down ones and the opponent's do not.</item>
    /// <item><b>Mystic Swordsman LV6</b> — failed closed: its atom is gone, so its top-of-Deck rider stays leftover and it
    /// battles like a vanilla monster (no invented start-of-Damage-Step destroy, never a forced Deck placement).</item>
    /// <item><b>Silent Swordsman LV3</b> — failed closed on its first sentence (the opponent's Spell negation never
    /// applied), and its Standby sentence compiles once: the generic PhaseTriggerTemplates LV clause no longer doubles an
    /// atom that already owns the sentence.</item>
    /// </list>
    /// Duels go through the engine entry points (TryFlipSummon, TryEnterBattlePhase, TryAttack, the response windows).
    /// </summary>
    public static class QcLvAndPinsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Dekoichi = 87621407;        // Flip, Machine DARK Lv4 1400/1000
        const int Bokoichi = 8715625;         // Normal Machine DARK Lv2 500/500
        const int MysticSwordsmanLv6 = 60482781;
        const int SilentSwordsmanLv3 = 1995985;
        const int GiantSoldier = 13039848;    // Normal Rock EARTH 1300/2000
        const int Celtic = 91152256;          // Normal Warrior EARTH 1400/1200
        const int DarkMimicLv1 = 74713516;
        const int HorusLv4 = 75830094;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("QcLvAndPins: " + name, ok, detail ?? "");

            DekoichiShape(db, Check);
            DekoichiDuels(db, pDeck, aDeck, Check);
            MysticSwordsmanLv6Closed(db, pDeck, aDeck, Check);
            SilentSwordsmanLv3Shape(db, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CardEffectStatusKind Status(CardDatabase db, int id) => CardEffectStatus.Classify(db.Get(id));

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} [" + string.Join("; ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} side={c.Side} named={c.NamedCard} own={c.OnlyDuringOwnStandby} " +
                    $"cond={c.ActivationCondition}")) + "] unparsed=[" +
                  string.Join("|", p.UnparsedFragments ?? Array.Empty<string>()) + "]";

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        /// <summary>Pick the first legal card at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = e.PendingActivation?.LegalTargets?.FirstOrDefault();
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

        static void SetDeck(DuelistState who, int id, int n)
        {
            who.Deck.Clear();
            for (var i = 0; i < n; i++) who.Deck.Add(id);
        }

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone,
            BattlePosition pos = BattlePosition.Attack) => PlaceMonster(e, who, id, zone, pos, true);

        static CardInstance Down(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Defense, false);

        // ───────────────────────────── Dekoichi ─────────────────────────────

        static void DekoichiShape(CardDatabase db, Chk Check)
        {
            var p = Prog(db, Dekoichi);
            var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
            Check("Dekoichi: FLIP draw 1 + 1 per face-up \"Bokoichi the Freightening Car\" you control (Implemented)",
                p != null && p.FullyCompiled && Status(db, Dekoichi) == CardEffectStatusKind.Implemented && c != null &&
                c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.DrawOnePlusPerFaceUpNamed &&
                c.Side == EffectSide.Controller && c.NamedCard == "Bokoichi the Freightening Car" && c.Amount == 0 &&
                c.MakesChainLink && !c.IsOptional && !c.RequiresTargetChoice,
                Describe(p));
        }

        static void DekoichiDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // 2 face-up Bokoichi of its controller count; a face-down one and the opponent's face-up one do not.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                SetDeck(p, Celtic, 10);
                var deko = Down(e, p, Dekoichi, 0);
                Up(e, p, Bokoichi, 1);
                Up(e, p, Bokoichi, 2, BattlePosition.Defense);
                Down(e, p, Bokoichi, 3);
                Up(e, o, Bokoichi, 0);
                e.NotifyPublic();
                var hand = p.Hand.Count;
                var deck = p.Deck.Count;
                var flipped = reached && e.TryFlipSummon(p, deko);
                Drive(e);
                Check("Dekoichi: Flip Summoned with 2 face-up Bokoichi (+1 face-down, +1 opponent's) draws 3",
                    flipped && deko.FaceUp && p.Hand.Count == hand + 3 && p.Deck.Count == deck - 3,
                    $"reached={reached} flipped={flipped} hand {hand}->{p.Hand.Count} deck {deck}->{p.Deck.Count}");
            }

            {
                // No Bokoichi: the printed "Draw 1 card" alone.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                SetDeck(p, Celtic, 10);
                var deko = Down(e, p, Dekoichi, 0);
                Up(e, p, Celtic, 1);
                e.NotifyPublic();
                var hand = p.Hand.Count;
                var flipped = reached && e.TryFlipSummon(p, deko);
                Drive(e);
                Check("Dekoichi: Flip Summoned with no Bokoichi draws exactly 1",
                    flipped && p.Hand.Count == hand + 1,
                    $"reached={reached} flipped={flipped} hand {hand}->{p.Hand.Count}");
            }

            {
                // A face-up Bokoichi equipped as an Equip Card (Spell & Trap Zone) is a face-up "Bokoichi" you control.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                SetDeck(p, Celtic, 10);
                var deko = Down(e, p, Dekoichi, 0);
                var host = Up(e, p, Celtic, 1);
                Up(e, p, Bokoichi, 2);
                var eq = e.CreateCardInstance(Bokoichi);
                eq.FaceUp = true;
                eq.EquippedTo = host;
                host.Equips.Add(eq);
                p.SpellTrapZones[0].Occupant = eq;
                e.NotifyPublic();
                var hand = p.Hand.Count;
                var flipped = reached && e.TryFlipSummon(p, deko);
                Drive(e);
                Check("Dekoichi: a face-up Bokoichi in the Spell & Trap Zone counts (1 + monster + equip = 3)",
                    flipped && p.TryFindSpellTrap(eq, out _) && p.Hand.Count == hand + 3,
                    $"reached={reached} flipped={flipped} eqOnField={p.TryFindSpellTrap(eq, out _)} " +
                    $"hand {hand}->{p.Hand.Count}");
            }
        }

        // ───────────────────────────── Mystic Swordsman LV6 ─────────────────────────────

        static void MysticSwordsmanLv6Closed(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // RdsRedoBattle batch (2026-10-18): the rider is compiled again, with the "you can" choice and the owner check
            // (RdsRedoBattleRuntime.DestroyAttackedFaceDownMayPlaceOnDeckTop) and a DestroyMonster deck-top branch that
            // keeps the card. These checks now pin the Cancel answer: the monster goes to the GY, the Deck is unchanged.
            var prog = Prog(db, MysticSwordsmanLv6);
            Check("Mystic Swordsman LV6: the top-of-Deck rider is its own clause (never a forced Deck placement)",
                prog != null && prog.FullyCompiled && Status(db, MysticSwordsmanLv6) == CardEffectStatusKind.Implemented &&
                prog.ClauseList.Count == 1 &&
                prog.ClauseList[0].Action == EffectActionKind.DestroyAttackedFaceDownDefenseReturnToOwnerDeckTopOrGy,
                Describe(prog));

            {
                // The player declines the placement (Cancel): the face-down target, destroyed at the start of the Damage
                // Step without being flipped, goes to the GY; the opponent's Deck is unchanged.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lv6 = Up(e, p, MysticSwordsmanLv6, 0);
                var soldier = Down(e, o, GiantSoldier, 0);
                e.NotifyPublic();
                var oppDeck = o.Deck.Count;
                if (e.Phase != DuelPhase.Battle) e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var attacked = reached && e.TryAttack(p, lv6, soldier);
                for (var k = 0; k < 4 && e.IsAwaitingResponse; k++) e.PassResponse();
                var asked = e.IsAwaitingEffectTarget;
                if (asked) e.CancelEffectTargeting();
                DrainCombat(e);
                Check("Mystic Swordsman LV6 (Cancel): its face-down target is destroyed into the GY, " +
                      "not placed on top of the Deck",
                    attacked && asked && o.Graveyard.Contains(soldier) && o.Deck.Count == oppDeck,
                    $"reached={reached} attacked={attacked} asked={asked} gy={o.Graveyard.Contains(soldier)} " +
                    $"faceUp={soldier.FaceUp} oppDeck {oppDeck}->{o.Deck.Count}");
            }
        }

        // ───────────────────────────── Silent Swordsman LV3 ─────────────────────────────

        static void SilentSwordsmanLv3Shape(CardDatabase db, Chk Check)
        {
            var p = Prog(db, SilentSwordsmanLv3);
            var standby = p?.ClauseList.Where(c => c.Timing == EffectTiming.StandbyPhase).ToList() ?? new List<EffectClause>();
            var s0 = standby.Count == 1 ? standby[0] : null;
            Check("Silent Swordsman LV3: one Standby LV5 clause (own Standby, not the turn it was Summoned/flipped)",
                p != null && s0 != null && s0.Action == EffectActionKind.SpecialSummonNamed &&
                s0.NamedCard == "Silent Swordsman LV5" && s0.OnlyDuringOwnStandby && s0.RequiresSendThisToGy &&
                s0.ActivationCondition == LvChainRuntime.NotTurnSummoned && s0.FromHand && s0.FromDeck && s0.IsOptional,
                Describe(p));
            // RdsRedoBattle batch (2026-10-18): the negation is compiled as a negate-only marker
            // (RdsRedoBattleRuntime.TryNegateSpellTargetingLink), never Freed's negate-and-destroy.
            Check("Silent Swordsman LV3: the opponent's-Spell negation is the negate-only marker (not Freed's destroy)",
                p != null && p.FullyCompiled && Status(db, SilentSwordsmanLv3) == CardEffectStatusKind.Implemented &&
                p.ClauseList.Count(c => c.Action == EffectActionKind.NegateOpponentSpellEffectsThatTargetThis) == 1 &&
                !p.ClauseList.Exists(c => c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy),
                Describe(p));

            // The Standby sentence alone (the atom's whole span): one clause, FullyCompiled — no generic duplicate.
            var lone = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990208301, name = "QcLvAndPins LV probe", type = "Effect Monster", frameType = "effect",
                race = "Warrior", attribute = "LIGHT", level = 3, atk = 1000, def = 1000,
                desc = "During your Standby Phase: You can send this face-up card to the Graveyard; Special Summon 1 " +
                       "\"Silent Swordsman LV5\" from your hand or Deck. You cannot activate this effect the turn this " +
                       "card is Summoned or flipped face-up."
            });
            Check("LV progression sentence owned by an atom compiles once (the generic LV template stays out)",
                lone != null && lone.FullyCompiled && lone.ClauseList.Count == 1 &&
                lone.ClauseList[0].ActivationCondition == LvChainRuntime.NotTurnSummoned &&
                lone.ClauseList[0].OnlyDuringOwnStandby,
                Describe(lone));

            // Where no atom owns it, the generic LV template still compiles the sentence, once.
            foreach (var (id, timing, named) in new[]
                     {
                         (DarkMimicLv1, EffectTiming.StandbyPhase, "Dark Mimic LV3"),
                         (HorusLv4, EffectTiming.EndPhase, "Horus the Black Flame Dragon LV6"),
                     })
            {
                var q = Prog(db, id);
                var lv = q?.ClauseList.Where(c => c.Timing == timing && c.Action == EffectActionKind.SpecialSummonNamed)
                    .ToList() ?? new List<EffectClause>();
                Check($"{db.Get(id)?.name ?? id.ToString()}: the generic LV clause is still compiled exactly once",
                    q != null && q.FullyCompiled && lv.Count == 1 && lv[0].NamedCard == named && lv[0].RequiresSendThisToGy,
                    Describe(q));
            }
        }
    }
}
