using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SpellCounter duel QC (v164). <c>SpellCounterRegressionTests</c> locks the compiled clause shape;
    /// this drives the same four cards through a real duel so we know the counters actually land, the
    /// costs are actually charged, the targets are actually offered and the ATK actually moves.
    ///
    /// "Implemented" on a compile-shape test only means the text was understood. These checks are the
    /// difference between a card that compiles and a card that plays. Note the entry point: a face-up
    /// monster's Activate effect goes through <see cref="DuelEngine.TryActivateSpellTrap"/>, not through
    /// <c>FaceUpQuickEffectsRuntime</c>, which is Spell/Trap-only by construction.
    /// </summary>
    public static class SpellCounterDuelRegressionTests
    {
        const int Marionette = 8034697;     // +200 ATK per counter; 2 -> destroy a monster
        const int Rml = 70791313;           // 1 per Spell, max 3; 3 -> draw 1
        const int SkilledDark = 73752131;  // 3 -> tribute self, SS Dark Magician from hand
        const int SkilledWhite = 46363422;  // same sentence, Buster Blader, long "Graveyard" spelling
        const int PotOfGreed = 55144522;    // any implemented Spell: the resolve trigger
        const int DarkMagician = 46986414;
        const int BusterBlader = 78193831;
        const int GeminiElf = 69140098;    // a plain 1900 ATK body, owned by the opponent in the target check

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("SpellCounterDuel: " + n, ok, d ?? "");

            CounterLandsOnSpellResolve(db, pDeck, aDeck, Check);
            RmlMaxAndCost(db, pDeck, aDeck, Check);
            MarionetteAtk(db, pDeck, aDeck, Check);
            MarionetteDestroysTarget(db, pDeck, aDeck, Check);
            MarionetteMayTargetOpponent(db, pDeck, aDeck, Check);
            SpecialistTributeAndSummon(db, pDeck, aDeck, Check);
            OpponentSpellsCount(db, pDeck, aDeck, Check);
            NegatedCardTakesNoCounter(db, pDeck, aDeck, Check);
            FaceDownTakesNoCounter(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
            return e;
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        /// <summary>Activate and resolve a plain Spell from <paramref name="who"/>'s hand.</summary>
        static void FireSpell(DuelEngine e, DuelistState who)
        {
            var s = PutInHand(e, who, PotOfGreed);
            e.TryActivateSpellTrap(who, s, fromHand: true);
            Settle(e);
        }

        /// <summary>
        /// Tap a face-up monster to use its Activate effect, answering a single target pick with
        /// <paramref name="target"/> when one is offered. False means the engine refused the activation.
        /// </summary>
        static bool TapAndActivate(DuelEngine e, DuelistState who, CardInstance card, CardInstance target = null)
        {
            var accepted = e.TryActivateSpellTrap(who, card, fromHand: false);
            Settle(e);
            if (accepted && e.IsAwaitingEffectTarget)
            {
                if (target != null && e.IsLegalEffectTarget(target)) e.TrySelectEffectTarget(target);
                else e.CancelEffectTargeting();
                Settle(e);
            }
            return accepted;
        }

        /// <summary>Set counters through the engine's recompute pass so continuous ATK actually re-applies.</summary>
        static void SetCounters(DuelEngine e, CardInstance card, int n)
        {
            card.Counters = n;
            FieldSpellEffects.RefreshBoard(e);
        }

        static int Atk(CardInstance c) => c?.CurrentAtk ?? 0;

        /// <summary>The engine exposes zones, not a card-side "am I in the GY" flag.</summary>
        static bool InGy(DuelEngine e, CardInstance c) =>
            c != null && (e.Player.Graveyard.Contains(c) || e.Opponent.Graveyard.Contains(c));

        static string Board(DuelistState who) =>
            string.Join("|", who.MonsterZones.Where(z => z.Occupant != null).Select(z => z.Occupant.Name));

        // ───────────────────────────── checks ─────────────────────────────

        /// <summary>"Each time a Spell Card is activated ... when that Spell resolves" — one counter per
        /// resolve, on your own and the opponent's counter cards alike.</summary>
        static void CounterLandsOnSpellResolve(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mine = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);
            var theirs = PlaceMonster(e, e.Opponent, Rml, 0, BattlePosition.Attack, true);

            Check("no counter before any Spell resolves",
                mine.Counters == 0 && theirs.Counters == 0, $"{mine.Counters}/{theirs.Counters}");

            FireSpell(e, e.Player);
            Check("one Spell resolved -> one counter on each counter card",
                mine.Counters == 1 && theirs.Counters == 1, $"{mine.Counters}/{theirs.Counters}");
        }

        /// <summary>Royal Magical Library prints "(max. 3)": the cap holds and the 3-counter cost is real.</summary>
        static void RmlMaxAndCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var rml = PlaceMonster(e, e.Player, Rml, 0, BattlePosition.Attack, true);
            for (var i = 0; i < 5; i++) FireSpell(e, e.Player);
            Check("RML honours its printed (max. 3) over five resolves",
                rml.Counters == 3, $"counters={rml.Counters}");

            // Two counters cannot pay a 3-counter cost.
            var e2 = Setup(db, pDeck, aDeck);
            var rml2 = PlaceMonster(e2, e2.Player, Rml, 0, BattlePosition.Attack, true);
            SetCounters(e2, rml2, 2);
            Check("RML refuses its effect at 2 of 3 counters",
                !TapAndActivate(e2, e2.Player, rml2), $"counters={rml2.Counters}");

            var e3 = Setup(db, pDeck, aDeck);
            var rml3 = PlaceMonster(e3, e3.Player, Rml, 0, BattlePosition.Attack, true);
            SetCounters(e3, rml3, 3);
            var handBefore = e3.Player.Hand.Count;
            var used = TapAndActivate(e3, e3.Player, rml3);
            Check("RML spends 3 counters to draw 1",
                used && rml3.Counters == 0 && e3.Player.Hand.Count == handBefore + 1,
                $"used={used} counters={rml3.Counters} hand={handBefore}->{e3.Player.Hand.Count}");
        }

        /// <summary>Marionette: 200 ATK for each Spell Counter on it.</summary>
        static void MarionetteAtk(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mm = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);

            SetCounters(e, mm, 0);
            var atk0 = Atk(mm);
            SetCounters(e, mm, 1);
            var atk1 = Atk(mm);
            SetCounters(e, mm, 3);
            var atk3 = Atk(mm);
            Check("Marionette gains 200 ATK per Spell Counter",
                atk1 == atk0 + 200 && atk3 == atk0 + 600, $"0={atk0} 1={atk1} 3={atk3}");

            // The gain must also follow a real resolve, not only a manual counter write.
            var e2 = Setup(db, pDeck, aDeck);
            var real = PlaceMonster(e2, e2.Player, Marionette, 0, BattlePosition.Attack, true);
            var before = Atk(real);
            FireSpell(e2, e2.Player);
            Check("the ATK gain follows a real Spell resolve",
                real.Counters == 1 && Atk(real) == before + 200,
                $"counters={real.Counters} atk {before}->{Atk(real)}");
        }

        /// <summary>2 counters buy a monster destruction, and only with 2.</summary>
        static void MarionetteDestroysTarget(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mm = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);
            var victim = PlaceMonster(e, e.Opponent, GeminiElf, 0, BattlePosition.Attack, true);

            SetCounters(e, mm, 1);
            Check("Marionette refuses at 1 of 2 counters",
                !TapAndActivate(e, e.Player, mm, victim), $"counters={mm.Counters}");

            SetCounters(e, mm, 2);
            var offered = false;
            var used = e.TryActivateSpellTrap(e.Player, mm, fromHand: false);
            Settle(e);
            offered = e.IsAwaitingEffectTarget;
            Check("Marionette offers a target at 2 counters", used && offered,
                $"used={used} awaitingTarget={offered}");
            if (offered) e.TrySelectEffectTarget(victim);
            Settle(e);
            Check("Marionette destroys the chosen monster and pays 2 counters",
                mm.Counters == 0 && InGy(e, victim), $"counters={mm.Counters} victimInGy={InGy(e, victim)}");
        }

        /// <summary>"target 1 monster on the field" is not restricted to your own monsters.</summary>
        static void MarionetteMayTargetOpponent(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mm = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);
            var foe = PlaceMonster(e, e.Opponent, GeminiElf, 0, BattlePosition.Attack, true);
            SetCounters(e, mm, 2);

            var used = e.TryActivateSpellTrap(e.Player, mm, fromHand: false);
            Settle(e);
            var offered = e.IsAwaitingEffectTarget;
            var legal = offered && e.IsLegalEffectTarget(foe);
            Check("Marionette may target an opponent's monster", legal,
                $"used={used} awaiting={offered} legal={legal}");
            if (offered && legal) e.TrySelectEffectTarget(foe);
            Settle(e);
            Check("the opponent's monster is destroyed", InGy(e, foe), $"inGy={InGy(e, foe)}");
        }

        /// <summary>The Specialists pay "tribute this card with 3 Spell Counters": the card leaves the
        /// field as its own cost, and the named monster comes from the hand.</summary>
        static void SpecialistTributeAndSummon(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var sdm = PlaceMonster(e, e.Player, SkilledDark, 0, BattlePosition.Attack, true);
            PutInHand(e, e.Player, DarkMagician);

            SetCounters(e, sdm, 2);
            Check("Skilled Dark Magician refuses at 2 of 3 counters",
                !TapAndActivate(e, e.Player, sdm), $"counters={sdm.Counters}");

            SetCounters(e, sdm, 3);
            var used = TapAndActivate(e, e.Player, sdm);
            Check("Skilled Dark Magician tributes itself as the cost",
                used && InGy(e, sdm), $"used={used} inGy={InGy(e, sdm)} board={Board(e.Player)}");
            Check("Skilled Dark Magician Special Summons Dark Magician from hand",
                e.Player.MonsterZones.Any(z => z.Occupant?.Def?.name == "Dark Magician"),
                $"board={Board(e.Player)}");

            // The White variant shares the sentence; the long "Graveyard" spelling must resolve too.
            var e2 = Setup(db, pDeck, aDeck);
            var swm = PlaceMonster(e2, e2.Player, SkilledWhite, 0, BattlePosition.Attack, true);
            PutInHand(e2, e2.Player, BusterBlader);
            SetCounters(e2, swm, 3);
            var used2 = TapAndActivate(e2, e2.Player, swm);
            Check("Skilled White Magician Special Summons Buster Blader",
                used2 && e2.Player.MonsterZones.Any(z => z.Occupant?.Def?.name == "Buster Blader"),
                $"used={used2} inGy={InGy(e2, swm)} board={Board(e2.Player)}");
        }

        /// <summary>The text says "each time a Spell Card is activated" — the opponent's Spells count.</summary>
        static void OpponentSpellsCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mine = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);
            var atk0 = Atk(mine);
            FieldSpellEffects.RefreshBoard(e);
            atk0 = Atk(mine);

            // Hand the turn over: a Spell can only be activated by its own controller.
            EndTurnTo(e, e.Opponent);
            FireSpell(e, e.Opponent);

            Check("an opponent Spell counts toward my counter and my ATK",
                mine.Counters == 1 && Atk(mine) == atk0 + 200,
                $"turn={(e.TurnPlayer == e.Opponent ? "O" : "P")} counters={mine.Counters} atk {atk0}->{Atk(mine)}");
        }

        static void NegatedCardTakesNoCounter(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mm = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, true);
            mm.IsNegated = true;
            FireSpell(e, e.Player);
            Check("a negated counter card takes no counter", mm.Counters == 0, $"counters={mm.Counters}");
        }

        /// <summary>Counters on a face-down card are hidden information, so the engine must not place
        /// them where the player could read them off the arena hologram.</summary>
        static void FaceDownTakesNoCounter(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var mm = PlaceMonster(e, e.Player, Marionette, 0, BattlePosition.Attack, false);
            FireSpell(e, e.Player);
            Check("a face-down counter card is skipped (no visible-information leak)",
                mm.Counters == 0, $"counters={mm.Counters} faceUp={mm.FaceUp}");
        }

        /// <summary>Advance to <paramref name="target"/>'s Main Phase 1, keeping the board as-is.</summary>
        static void EndTurnTo(DuelEngine e, DuelistState target)
        {
            for (var i = 0; i < 12 && e.TurnPlayer != target; i++)
            {
                Settle(e);
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }
            if (e.IsAwaitingResponse) e.PassResponse();
        }
    }
}