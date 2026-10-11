using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ArchfiendRiders batch: Shadowknight / Terrorking / Vilepawn / Infernalqueen / Desrook Archfiend's last sentences,
    /// Checkmate, Pandemonium and Pandemonium Watchbear. Compile shapes through the real compiler, near-miss wordings, and
    /// mini-duels through the engine entry points (TryNormalSummon, TryFlipSummon, TryAttack, TryActivateSpellTrap,
    /// TrySelectEffectTarget, response windows, FirePhaseTriggers). Turn 1 is the Player's, turn 2 the AI's. Runtime:
    /// <see cref="ArchfiendRidersRuntime"/>.
    /// </summary>
    public static class ArchfiendRidersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int Shadowknight = 9603356;    // Lv4 2000/1600, pays 900
        const int Terrorking = 35975813;     // Lv4 2000/1500, pays 800
        const int Vilepawn = 73219648;       // Lv2 1200/200, pays 500
        const int Desrook = 72192100;        // Lv3 1100/1800, pays 500
        const int Infernalqueen = 8581705;   // Lv4 900/1500, pays 500
        const int Checkmate = 69313735;      // Normal Spell
        const int Pandemonium = 94585852;    // Field Spell
        const int Watchbear = 75375465;      // Beast 1300/1800

        const int Celtic = 91152256;         // Warrior 1400/1200 (Normal)
        const int BlueEyes = 89631139;       // Dragon 3000/2500 (Normal)
        const int Sangan = 26202165;         // Fiend 1000/600, "sent from the field to the GY" search
        const int DarkHole = 53129443;       // Normal Spell: destroy all monsters
        const int Mst = 5318639;             // Quick-Play Spell: destroy 1 Spell/Trap
        const int Fissure = 66788016;        // Normal Spell: destroy the opponent's lowest-ATK face-up monster

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ArchfiendRiders: " + name, ok, detail ?? "");
            Guard(Check, "compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "Shadowknight", () => ShadowknightDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Terrorking", () => TerrorkingDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Vilepawn", () => VilepawnDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Infernalqueen", () => InfernalqueenDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Checkmate", () => CheckmateDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Pandemonium", () => PandemoniumDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Watchbear", () => WatchbearDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Desrook", () => DesrookDuels(db, pDeck, aDeck, Check));
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static void Guard(Chk check, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { check(name + " threw no exception", false, ex.ToString()); }
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

        static CardInstance Up(DuelEngine e, DuelistState who, int id, int zone) =>
            PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);

        static CardInstance PlaceField(DuelEngine e, DuelistState who, int id)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
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

        /// <summary>Pass every response window until <paramref name="who"/>'s window of that timing is open.</summary>
        static bool WaitForWindow(DuelEngine e, DuelistState who, ResponseTiming timing)
        {
            for (var i = 0; i < 12; i++)
            {
                var pr = e.PendingResponse;
                if (pr != null && pr.Responder == who && pr.Timing == timing) return true;
                if (e.IsAwaitingEffectTarget || !e.IsAwaitingResponse) return false;
                e.PassResponse();
            }

            return false;
        }

        static bool Offers(DuelEngine e, CardInstance card) =>
            e.PendingResponse?.LegalCards != null && e.PendingResponse.LegalCards.Contains(card);

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static CompiledCardProgram CompileText(int id, string type, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "ArchfiendRiders probe", type = type,
                frameType = type.StartsWith("Spell") || type.StartsWith("Trap") ? "spell" : "effect",
                race = type.StartsWith("Spell") ? "Normal" : type.StartsWith("Trap") ? "Normal" : "Fiend",
                attribute = type.StartsWith("Spell") || type.StartsWith("Trap") ? null : "DARK",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            bool Full(int id, CompiledCardProgram p) => p != null && p.FullyCompiled && Implemented(db, id);

            var sk = Prog(db, Shadowknight);
            Check("Shadowknight FullyCompiled: maintenance, die protection, halved battle damage marker",
                Full(Shadowknight, sk) &&
                Has(sk, c => c.Action == EffectActionKind.HalveBattleDamageThisInflictsToOpponent &&
                             c.Timing == EffectTiming.ContinuousWhileFaceUp) &&
                Has(sk, c => c.Action == EffectActionKind.StandbyMaintenancePayLp && c.PayLpAmount == 900),
                Describe(sk));

            var tk = Prog(db, Terrorking);
            Check("Terrorking FullyCompiled: \"Archfiend\" summon lock + battle negation of Effect Monsters",
                Full(Terrorking, tk) &&
                Has(tk, c => c.Action == EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries &&
                             c.NamedCard == "Archfiend" && !c.MakesChainLink) &&
                Has(tk, c => c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                             c.AffectsThisCardOnly && c.TargetCardKind == "Effect"),
                Describe(tk));
            Check("Terrorking's conditional lock is not a printed Normal Summon/Set lock (it can be Set)",
                !PsctGrammar.BlocksNormalSummonOrSet(db.Get(Terrorking)?.desc) &&
                PsctGrammar.BlocksNormalSummonOrSet("Cannot be Normal Summoned/Set. Must first be Special Summoned."),
                db.Get(Terrorking)?.desc);

            var vp = Prog(db, Vilepawn);
            Check("Vilepawn FullyCompiled: attack lock on face-up \"Archfiend\" monsters except \"Vilepawn Archfiend\"",
                Full(Vilepawn, vp) &&
                Has(vp, c => c.Action == EffectActionKind.CannotBeAttackTarget && c.ExceptThisCard &&
                             c.NamedCard == "Archfiend" && c.NamedCardIsSeries &&
                             c.ExceptNamedCard == "Vilepawn Archfiend"),
                Describe(vp));

            var iq = Prog(db, Infernalqueen);
            Check("Infernalqueen FullyCompiled: every Standby Phase, target 1 \"Archfiend\", +1000 ATK until the End Phase",
                Full(Infernalqueen, iq) &&
                Has(iq, c => c.Timing == EffectTiming.StandbyPhase &&
                             c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn && c.Amount == 1000 &&
                             c.EachStandbyPhase && c.RequiresTargetChoice && c.TargetSeriesName == "Archfiend" &&
                             EffectVocabulary.ResolutionOf(c) == EffectResolutionKind.ModifyStats),
                Describe(iq));

            var dr = Prog(db, Desrook);
            Check("Desrook FullyCompiled: hand Quick Effect for a destroyed \"Terrorking Archfiend\"",
                Full(Desrook, dr) &&
                Has(dr, c => c.Timing == EffectTiming.HandQuickEffectWhenNamedDestroyed &&
                             c.Action == EffectActionKind.SpecialSummonDestroyedNamedFromGyBySendingThisFromHand &&
                             c.NamedCard == "Terrorking Archfiend" && c.IsOptional),
                Describe(dr));

            var cm = Prog(db, Checkmate);
            var cc = cm?.ClauseList.Count == 1 ? cm.ClauseList[0] : null;
            Check("Checkmate FullyCompiled: Tribute 1 \"Archfiend\" (cost), target a face-up \"Terrorking Archfiend\"",
                Full(Checkmate, cm) && cc != null && cc.Timing == EffectTiming.Activate &&
                cc.Action == EffectActionKind.TargetCanAttackDirectlyThisTurn && cc.RequiresTributeCount == 1 &&
                cc.TributeSeriesName == "Archfiend" && cc.NamedCard == "Terrorking Archfiend" &&
                cc.RequiresTargetChoice && cc.RequiresTargetFaceUp && cc.Zone == EffectZoneFilter.ControllerMonsters,
                Describe(cm));

            var pd = Prog(db, Pandemonium);
            Check("Pandemonium FullyCompiled: the Standby LP waiver and the search marker",
                Full(Pandemonium, pd) && pd.ClauseList.Count == 2 &&
                Has(pd, c => c.Action == EffectActionKind.WaiveSeriesMonsterStandbyLpCosts && c.NamedCard == "Archfiend") &&
                Has(pd, c => c.Action == EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle &&
                             c.NamedCard == "Archfiend" && c.IsOptional),
                Describe(pd));

            var wb = Prog(db, Watchbear);
            Check("Pandemonium Watchbear FullyCompiled: \"Pandemonium\" protected from the opponent's card effects",
                Full(Watchbear, wb) &&
                Has(wb, c => c.Action == EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects &&
                             c.NamedCard == "Pandemonium"),
                Describe(wb));

            // Near-misses: look-alike sentences stay out of this batch's clauses.
            var doubled = CompileText(90176001, "Effect Monster",
                "The Battle Damage this card inflicts to your opponent is doubled.");
            Check("Near-miss: \"is doubled\" is not the halving marker",
                !Has(doubled, c => c.Action == EffectActionKind.HalveBattleDamageThisInflictsToOpponent), Describe(doubled));

            var nsOnly = CompileText(90176002, "Effect Monster",
                "This card cannot be Normal Summoned unless you have an Archfiend Monster Card on your side of the field.");
            Check("Near-miss: a Normal-Summon-only lock (no Flip Summon) is not Terrorking's clause",
                !Has(nsOnly, c => c.Action == EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries),
                Describe(nsOnly));

            var noExcept = CompileText(90176003, "Effect Monster",
                "As long as this card remains on the field, your opponent cannot attack any Archfiend Monster Card on " +
                "your side of the field.");
            Check("Near-miss: the attack lock without the name exception is not Vilepawn's clause",
                !Has(noExcept, c => c.Action == EffectActionKind.CannotBeAttackTarget && c.ExceptNamedCard != null),
                Describe(noExcept));

            var ownStandby = CompileText(90176004, "Effect Monster",
                "As long as this card remains on the field, during your Standby Phase, increase the ATK of 1 Archfiend " +
                "Monster Card by 1000 points until the End Phase.");
            Check("Near-miss: \"during your Standby Phase\" is not Infernalqueen's every-Standby boost",
                !Has(ownStandby, c => c.Timing == EffectTiming.StandbyPhase && c.TargetSeriesName == "Archfiend"),
                Describe(ownStandby));

            var noTribute = CompileText(90176005, "Spell Card",
                "During this turn, 1 \"Terrorking Archfiend\" on your side of the field can attack your opponent's " +
                "Life Points directly.");
            Check("Near-miss: the direct attack without the Tribute is not Checkmate's clause",
                !Has(noTribute, c => c.TributeSeriesName != null), Describe(noTribute));

            var byBattle = CompileText(90176006, "Spell Card",
                "Each time a player's \"Archfiend\" monster(s) is destroyed by battle and sent to the Graveyard, that " +
                "player can add 1 \"Archfiend\" monster from their Deck to their hand that is a lower Level than the " +
                "destroyed card.");
            Check("Near-miss: a by-battle search is not Pandemonium's marker",
                !Has(byBattle, c => c.Action == EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle),
                Describe(byBattle));

            var anyEffect = CompileText(90176007, "Effect Monster",
                "As long as this card remains face-up on your side of the field, \"Pandemonium\" on your side of the " +
                "field is not destroyed by card effects.");
            Check("Near-miss: protection from every card effect is not Watchbear's opponent-only clause",
                !Has(anyEffect, c => c.Action == EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects),
                Describe(anyEffect));

            var notDestroyed = CompileText(90176008, "Effect Monster",
                "When a \"Terrorking Archfiend\" on your side of the field is sent to the Graveyard, you can send this " +
                "card from your hand to the Graveyard to Special Summon the \"Terrorking Archfiend\".");
            Check("Near-miss: \"sent to the Graveyard\" without \"destroyed\" is not Desrook's clause",
                !Has(notDestroyed, c => c.Timing == EffectTiming.HandQuickEffectWhenNamedDestroyed),
                Describe(notDestroyed));
        }

        // ───────────────────────────── Shadowknight Archfiend ─────────────────────────────

        static void ShadowknightDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var sk = Up(e, e.Opponent, Shadowknight, 2);
                var reached = ReachOpponentBattle(e);
                var lp = e.Player.LifePoints;
                var attacked = reached && e.TryAttack(e.Opponent, sk, null);
                DrainCombat(e);
                Check("Shadowknight: its direct attack inflicts half its ATK (2000 → 1000)",
                    reached && attacked && lp - e.Player.LifePoints == 1000,
                    $"reached={reached} attacked={attacked} lp {lp}→{e.Player.LifePoints}");
            }

            {
                // Battle damage its controller takes is not halved (aux.ChangeBattleDamage(1, …): the opponent only).
                var e = Setup(db, pDeck, aDeck);
                var sk = Up(e, e.Opponent, Shadowknight, 2);
                var be = Up(e, e.Player, BlueEyes, 2);
                var reached = ReachOpponentBattle(e);
                var lp = e.Opponent.LifePoints;
                var attacked = reached && e.TryAttack(e.Opponent, sk, be);
                DrainCombat(e);
                Check("Shadowknight: attacking a stronger monster, its controller takes the full 1000",
                    reached && attacked && lp - e.Opponent.LifePoints == 1000 && e.Opponent.Graveyard.Contains(sk),
                    $"reached={reached} attacked={attacked} lp {lp}→{e.Opponent.LifePoints}");
            }
        }

        // ───────────────────────────── Terrorking Archfiend ─────────────────────────────

        static void TerrorkingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                Up(e, e.Player, Celtic, 0);
                var tk = PutInHand(e, e.Player, Terrorking);
                var summoned = e.TryNormalSummon(e.Player, tk, asSet: false);
                var set = !summoned && e.TryNormalSummon(e.Player, tk, asSet: true);
                Check("Terrorking: no Normal Summon without a face-up \"Archfiend\" (a Warrior does not count); it can be Set",
                    !summoned && set && !tk.FaceUp && OnField(e.Player, tk), $"summoned={summoned} set={set}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                Up(e, e.Player, Vilepawn, 0);
                var tk = PutInHand(e, e.Player, Terrorking);
                var summoned = e.TryNormalSummon(e.Player, tk, asSet: false);
                Check("Terrorking: Normal Summoned while you control a face-up Vilepawn Archfiend",
                    summoned && tk.FaceUp && OnField(e.Player, tk), $"summoned={summoned}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var setTk = PlaceMonster(e, e.Player, Terrorking, 0, BattlePosition.Defense, false);
                var downVp = PlaceMonster(e, e.Player, Vilepawn, 1, BattlePosition.Defense, false);
                var blocked = !e.TryFlipSummon(e.Player, setTk);
                downVp.FaceUp = true;
                downVp.Position = BattlePosition.Attack;
                var flipped = e.TryFlipSummon(e.Player, setTk);
                Check("Terrorking: no Flip Summon with only a face-down \"Archfiend\"; Flip Summoned once one is face-up",
                    blocked && flipped && setTk.FaceUp, $"blocked={blocked} flipped={flipped}");
            }

            {
                // EVENT_BATTLED: the Effect Monster Terrorking destroys has its effects negated (Sangan's search).
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Opponent, Terrorking, 2);
                Up(e, e.Opponent, Vilepawn, 1);
                var sangan = Up(e, e.Player, Sangan, 2);
                var reached = ReachOpponentBattle(e);
                var hand = e.Player.Hand.Count;
                var attacked = reached && e.TryAttack(e.Opponent, tk, sangan);
                DrainCombat(e);
                Check("Terrorking: Sangan destroyed by it in battle has its effect negated (no search)",
                    reached && attacked && e.Player.Graveyard.Contains(sangan) && sangan.EffectsNegatedAfterBattle &&
                    !e.IsAwaitingEffectTarget && e.Player.Hand.Count == hand,
                    $"reached={reached} attacked={attacked} negated={sangan.EffectsNegatedAfterBattle} " +
                    $"pick={e.IsAwaitingEffectTarget} hand {hand}→{e.Player.Hand.Count}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Opponent, Terrorking, 2);
                Up(e, e.Opponent, Vilepawn, 1);
                var celtic = Up(e, e.Player, Celtic, 2);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(e.Opponent, tk, celtic);
                DrainCombat(e);
                Check("Terrorking: a Normal Monster it destroys is not marked negated (Effect Monsters only)",
                    reached && attacked && e.Player.Graveyard.Contains(celtic) && !celtic.EffectsNegatedAfterBattle,
                    $"reached={reached} attacked={attacked}");
            }
        }

        // ───────────────────────────── Vilepawn Archfiend ─────────────────────────────

        static void VilepawnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var vp = Up(e, e.Player, Vilepawn, 0);
                var tk = Up(e, e.Player, Terrorking, 1);
                var celtic = Up(e, e.Player, Celtic, 2);
                var be = Up(e, e.Opponent, BlueEyes, 2);
                var reached = ReachOpponentBattle(e);
                var tkLocked = ContinuousProtections.CannotBeAttackTarget(e, tk);
                var celticOpen = !ContinuousProtections.CannotBeAttackTarget(e, celtic);
                var onTk = reached && e.TryAttack(e.Opponent, be, tk);
                var onVp = reached && !onTk && e.TryAttack(e.Opponent, be, vp);
                DrainCombat(e);
                var tkOpenAfter = !ContinuousProtections.CannotBeAttackTarget(e, tk);
                Check("Vilepawn: the opponent cannot attack Terrorking; Vilepawn and a Warrior stay open; gone, the lock ends",
                    reached && tkLocked && celticOpen && !onTk && onVp && e.Player.Graveyard.Contains(vp) && tkOpenAfter,
                    $"reached={reached} tkLocked={tkLocked} celticOpen={celticOpen} onTk={onTk} onVp={onVp} " +
                    $"after={tkOpenAfter}");
            }

            {
                // Two Vilepawns: each excepts every card named "Vilepawn Archfiend"; a face-down Archfiend is attackable.
                var e = Setup(db, pDeck, aDeck);
                var vp1 = Up(e, e.Player, Vilepawn, 0);
                var vp2 = Up(e, e.Player, Vilepawn, 1);
                var downTk = PlaceMonster(e, e.Player, Terrorking, 2, BattlePosition.Defense, false);
                var open1 = !ContinuousProtections.CannotBeAttackTarget(e, vp1);
                var open2 = !ContinuousProtections.CannotBeAttackTarget(e, vp2);
                var openDown = !ContinuousProtections.CannotBeAttackTarget(e, downTk);
                Check("Vilepawn: another \"Vilepawn Archfiend\" and a face-down Archfiend can be attacked",
                    open1 && open2 && openDown, $"vp1={open1} vp2={open2} faceDown={openDown}");
            }
        }

        // ───────────────────────────── Infernalqueen Archfiend ─────────────────────────────

        static void InfernalqueenDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Standby: the player targets Vilepawn among two face-up Archfiends (a Warrior is not offered).
                var e = Setup(db, pDeck, aDeck);
                var iq = Up(e, e.Player, Infernalqueen, 0);
                var vp = Up(e, e.Player, Vilepawn, 1);
                var celtic = Up(e, e.Player, Celtic, 2);
                TextEffectRuntime.FirePhaseTriggers(e, e.Player, EffectTiming.StandbyPhase);
                var legal = Legal(e);
                var offered = e.IsAwaitingEffectTarget && legal.Contains(iq) && legal.Contains(vp) && !legal.Contains(celtic);
                var picked = offered && e.TrySelectEffectTarget(vp);
                Check("Infernalqueen: its controller targets 1 face-up Archfiend (+1000 ATK: Vilepawn 1200 → 2200)",
                    offered && picked && vp.CurrentAtk == 2200 && iq.CurrentAtk == 900 && celtic.CurrentAtk == 1400,
                    $"offered={offered} picked={picked} vp={vp.CurrentAtk} iq={iq.CurrentAtk}");

                // The opponent's Standby Phase too (EVENT_PHASE+PHASE_STANDBY on either turn).
                TextEffectRuntime.FirePhaseTriggers(e, e.Opponent, EffectTiming.StandbyPhase);
                var again = e.IsAwaitingEffectTarget && Legal(e).Contains(iq);
                var picked2 = again && e.TrySelectEffectTarget(iq);
                Check("Infernalqueen: it also triggers in the opponent's Standby Phase (Infernalqueen 900 → 1900)",
                    again && picked2 && iq.CurrentAtk == 1900, $"again={again} picked={picked2} iq={iq.CurrentAtk}");
            }

            {
                // Face-down: no effect; no other face-up Archfiend but itself face-down: nothing to target.
                var e = Setup(db, pDeck, aDeck);
                var iq = PlaceMonster(e, e.Player, Infernalqueen, 0, BattlePosition.Defense, false);
                var vp = Up(e, e.Player, Vilepawn, 1);
                TextEffectRuntime.FirePhaseTriggers(e, e.Player, EffectTiming.StandbyPhase);
                Check("Infernalqueen: a face-down Infernalqueen does nothing",
                    !e.IsAwaitingEffectTarget && vp.CurrentAtk == 1200, $"pick={e.IsAwaitingEffectTarget} vp={vp.CurrentAtk}");
            }

            {
                // The AI's own Standby Phase (real turn flow): it boosts its strongest Archfiend; the boost ends at the End
                // Phase and comes back in the player's next Standby Phase.
                var e = Setup(db, pDeck, aDeck);
                Up(e, e.Opponent, Infernalqueen, 0);
                var tk = Up(e, e.Opponent, Terrorking, 1);
                var reached = ReachOpponentMain(e);
                var duringAiTurn = tk.CurrentAtk;
                if (reached) e.TryEndTurnSafe(e.Opponent);
                if (e.IsAwaitingResponse) e.PassResponse();
                var nextTurn = e.TurnPlayer == e.Player ? tk.CurrentAtk : -1;
                Check("Infernalqueen (AI): +1000 to Terrorking in its Standby (3000), again in the player's Standby",
                    reached && duringAiTurn == 3000 && nextTurn == 3000,
                    $"reached={reached} aiTurn={duringAiTurn} next={nextTurn} turn={e.TurnPlayer?.Name}");
            }
        }

        // ───────────────────────────── Checkmate ─────────────────────────────

        static void CheckmateDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Player, Terrorking, 0);
                var vp = Up(e, e.Player, Vilepawn, 1);
                var cm = PutInHand(e, e.Player, Checkmate);
                var activated = e.TryActivateSpellTrap(e.Player, cm, fromHand: true);
                var tributes = Legal(e);
                var onlyVilepawn = e.IsAwaitingEffectTarget && tributes.Count == 1 && tributes[0] == vp;
                Drive(e, c => c == vp || c == tk);
                Check("Checkmate: the Tribute is an Archfiend other than the only Terrorking; Terrorking may attack directly",
                    activated && onlyVilepawn && e.Player.Graveyard.Contains(vp) && OnField(e.Player, tk) &&
                    PgdSpellTrapBasicsRuntime.HasSpellDirectAttackGrant(e, tk),
                    $"activated={activated} tributes=[{string.Join(",", tributes.Select(t => t.Name))}] " +
                    $"grant={PgdSpellTrapBasicsRuntime.HasSpellDirectAttackGrant(e, tk)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                Up(e, e.Player, Terrorking, 0);
                var cm = PutInHand(e, e.Player, Checkmate);
                var lone = e.CanActivateSpellTrap(e.Player, cm, fromHand: true);
                var e2 = Setup(db, pDeck, aDeck);
                Up(e2, e2.Player, Vilepawn, 0);
                var cm2 = PutInHand(e2, e2.Player, Checkmate);
                var noTarget = e2.CanActivateSpellTrap(e2.Player, cm2, fromHand: true);
                var e3 = Setup(db, pDeck, aDeck);
                Up(e3, e3.Player, Terrorking, 0);
                Up(e3, e3.Player, Terrorking, 1);
                var cm3 = PutInHand(e3, e3.Player, Checkmate);
                var two = e3.CanActivateSpellTrap(e3.Player, cm3, fromHand: true);
                Check("Checkmate: not with a lone Terrorking (its Tribute leaves no target) nor without one; two Terrorkings ok",
                    !lone && !noTarget && two, $"lone={lone} noTarget={noTarget} two={two}");
            }

            {
                // The AI's Checkmate: its Terrorking attacks directly over the player's monster.
                var e = Setup(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                var tk = Up(e, e.Opponent, Terrorking, 0);
                Up(e, e.Opponent, Vilepawn, 1);
                Up(e, e.Player, Celtic, 0);
                var cm = PutInHand(e, e.Opponent, Checkmate);
                var activated = reached && e.TryActivateSpellTrap(e.Opponent, cm, fromHand: true);
                Drive(e, _ => false);
                var battle = e.TryEnterBattlePhase(e.Opponent);
                Drive(e, _ => false);
                var lp = e.Player.LifePoints;
                var direct = e.TryAttack(e.Opponent, tk, null);
                DrainCombat(e);
                Check("Checkmate (AI): Terrorking attacks directly while the player controls a monster (2000)",
                    reached && activated && battle && direct && lp - e.Player.LifePoints == 2000,
                    $"reached={reached} activated={activated} battle={battle} direct={direct} lp {lp}→{e.Player.LifePoints}");
            }
        }

        // ───────────────────────────── Pandemonium ─────────────────────────────

        static void PandemoniumDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Dark Hole destroys two of the player's Archfiends at once: ONE search, lower than the highest Level (4).
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Player, Pandemonium);
                Up(e, e.Player, Terrorking, 0);
                Up(e, e.Player, Vilepawn, 1);
                e.Player.Deck.Insert(0, Infernalqueen);
                e.Player.Deck.Insert(0, Vilepawn);
                e.Player.Deck.Insert(0, Desrook);
                var deckDesrook = e.Player.Deck.Count(id => id == Desrook);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                for (var i = 0; i < 6 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++) e.PassResponse();
                var legal = Legal(e);
                var offer = e.IsAwaitingEffectTarget && legal.Exists(c => c.CardId == Desrook) &&
                            legal.Exists(c => c.CardId == Vilepawn) && !legal.Exists(c => c.CardId == Infernalqueen);
                var pick = legal.FirstOrDefault(c => c.CardId == Desrook);
                var took = pick != null && e.TrySelectEffectTarget(pick);
                var secondOffer = e.IsAwaitingEffectTarget && Legal(e).Exists(c => c.CardId == Desrook || c.CardId == Vilepawn);
                Check("Pandemonium: two Archfiends destroyed together → one optional search below Level 4 (Desrook taken)",
                    offer && took && !secondOffer && e.Player.Hand.Exists(c => c.CardId == Desrook) &&
                    e.Player.Deck.Count(id => id == Desrook) == deckDesrook - 1,
                    $"offer={offer} [{string.Join(",", legal.Select(c => c.Name))}] took={took} second={secondOffer}");
            }

            foreach (var sanganFirst in new[] { false, true })
            {
                // Dark Hole destroys Terrorking and Sangan together (either zone order): Pandemonium's search and Sangan's
                // search are triggers of the same event and both resolve (review finding: with Sangan's send replayed
                // first, its prompt replaced Pandemonium's and the Archfiend search was lost).
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Player, Pandemonium);
                Up(e, e.Player, sanganFirst ? Sangan : Terrorking, 0);
                Up(e, e.Player, sanganFirst ? Terrorking : Sangan, 1);
                e.Player.Deck.Insert(0, Vilepawn);
                e.Player.Deck.Insert(0, Celtic);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                var prompts = new List<string>();
                for (var i = 0; i < 30; i++)
                {
                    if (e.IsAwaitingEffectTarget)
                    {
                        var l = Legal(e);
                        prompts.Add("[" + string.Join(",", l.Select(c => c.Name)) + "]");
                        // Sangan's prompt offers Celtic Guardian (1400 ATK); Pandemonium's offers Vilepawn (Lv2 < 4), never Celtic.
                        var pick = l.FirstOrDefault(c => c.CardId == Celtic) ?? l.FirstOrDefault(c => c.CardId == Vilepawn);
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

                Check($"Pandemonium + Sangan destroyed together ({(sanganFirst ? "Sangan" : "Terrorking")} in zone 0): " +
                      "both searches resolve",
                    e.Player.Hand.Exists(c => c.CardId == Vilepawn) && e.Player.Hand.Exists(c => c.CardId == Celtic),
                    string.Join(" ", prompts) + " hand=" + string.Join(",", e.Player.Hand.Select(c => c.Name)));
            }

            {
                // The player's Archfiend destroyed under the OPPONENT's Pandemonium: the player searches; CANCEL declines.
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Opponent, Pandemonium);
                Up(e, e.Player, Terrorking, 0);
                e.Player.Deck.Insert(0, Vilepawn);
                var hand = e.Player.Hand.Count;
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                for (var i = 0; i < 6 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++) e.PassResponse();
                var offered = e.IsAwaitingEffectTarget && Legal(e).Exists(c => c.CardId == Vilepawn);
                e.CancelEffectTargeting();
                Drive(e, _ => false);
                Check("Pandemonium: the destroyed monster's player searches with the opponent's copy, and may decline",
                    offered && e.Player.Hand.Count == hand && e.Player.Deck.Contains(Vilepawn),
                    $"offered={offered} hand {hand}→{e.Player.Hand.Count}");
            }

            {
                // The AI's Archfiend destroyed by the player's Dark Hole: the AI adds its highest lower-Level Archfiend
                // (Infernalqueen, Lv4: a destroyed Terrorking would let the AI spend the searched Desrook at once).
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Opponent, Pandemonium);
                Up(e, e.Opponent, Infernalqueen, 0);
                e.Opponent.Deck.Insert(0, Vilepawn);
                e.Opponent.Deck.Insert(0, Desrook);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                Drive(e, _ => false);
                Check("Pandemonium (AI): adds Desrook (Lv3), the highest Level below 4",
                    e.Opponent.Hand.Exists(c => c.CardId == Desrook) && !e.Opponent.Hand.Exists(c => c.CardId == Vilepawn),
                    $"hand=[{string.Join(",", e.Opponent.Hand.Select(c => c.Name))}]");
            }

            {
                // A lone Archfiend destroyed by a Chain (Fissure, nothing held): the search is queued and activates once
                // that Chain is over, for the destroyed monster's player (the AI, under the player's Pandemonium).
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Player, Pandemonium);
                var tk = Up(e, e.Opponent, Terrorking, 0);
                e.Opponent.Deck.Insert(0, Vilepawn);
                var fis = PutInHand(e, e.Player, Fissure);
                // Fissure's pick is treated as a target by the engine's Archfiend die roll (a pre-existing gap: Lua
                // c66788016 selects without targeting). A roll of 1 never negates, so the test is deterministic.
                e.Rng.QueueDie(1);
                var activated = e.TryActivateSpellTrap(e.Player, fis, fromHand: true);
                Drive(e, c => c == tk); // Fissure's lowest-ATK pick
                Check("Pandemonium: a lone Archfiend destroyed by Fissure → its player searches after the Chain",
                    activated && e.Opponent.Graveyard.Exists(c => c.CardId == Terrorking) &&
                    e.Opponent.Hand.Exists(c => c.CardId == Vilepawn) && !e.IsAwaitingEffectTarget,
                    $"activated={activated} hand=[{string.Join(",", e.Opponent.Hand.Select(c => c.Name))}]");
            }

            {
                // Cross-group audit (v176): the search is a Spell effect (TRIGGER_O), so a Sasuke Samurai #2 lock applied
                // before Fissure resolves stops it (re:IsSpellTrapEffect()).
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Player, Pandemonium);
                var tk = Up(e, e.Opponent, Terrorking, 0);
                e.Opponent.Deck.Insert(0, Vilepawn);
                var fis = PutInHand(e, e.Player, Fissure);
                e.Rng.QueueDie(1);
                var activated = e.TryActivateSpellTrap(e.Player, fis, fromHand: true);
                SpellTrapAnswersAndActivationLocksRuntime.RegisterTurnSpellTrapLock(e, e.Opponent,
                    e.CreateCardInstance(11760174));
                Drive(e, c => c == tk);
                Check("Pandemonium: under Sasuke Samurai #2's Spell/Trap lock the search is not activated",
                    activated && e.Opponent.Graveyard.Exists(c => c.CardId == Terrorking) &&
                    !e.Opponent.Hand.Exists(c => c.CardId == Vilepawn) && e.Opponent.Deck.Contains(Vilepawn),
                    $"activated={activated} hand=[{string.Join(",", e.Opponent.Hand.Select(c => c.Name))}]");
            }

            {
                // Destroyed by battle: no search.
                var e = Setup(db, pDeck, aDeck);
                PlaceField(e, e.Player, Pandemonium);
                var vp = Up(e, e.Player, Vilepawn, 0);
                Up(e, e.Player, Terrorking, 1);
                var be = Up(e, e.Opponent, BlueEyes, 2);
                e.Player.Deck.Insert(0, Vilepawn);
                var reached = ReachOpponentBattle(e);
                var hand = e.Player.Hand.Count;
                var attacked = reached && e.TryAttack(e.Opponent, be, vp);
                DrainCombat(e);
                Check("Pandemonium: an Archfiend destroyed by battle triggers no search",
                    reached && attacked && e.Player.Graveyard.Contains(vp) && !e.IsAwaitingEffectTarget &&
                    e.Player.Hand.Count == hand,
                    $"reached={reached} attacked={attacked} pick={e.IsAwaitingEffectTarget} hand {hand}→{e.Player.Hand.Count}");
            }

            {
                // The waiver reads the compiled clause: a negated Pandemonium waives nothing.
                var e = Setup(db, pDeck, aDeck);
                var pd = PlaceField(e, e.Player, Pandemonium);
                Up(e, e.Player, Vilepawn, 0);
                var lp = e.Player.LifePoints;
                TextEffectRuntime.FirePhaseTriggers(e, e.Player, EffectTiming.StandbyPhase);
                var waived = lp == e.Player.LifePoints;
                pd.IsNegated = true;
                TextEffectRuntime.FirePhaseTriggers(e, e.Player, EffectTiming.StandbyPhase);
                var paid = lp - e.Player.LifePoints;
                Check("Pandemonium: waives Vilepawn's 500; negated, the cost is paid",
                    waived && paid == 500, $"waived={waived} paid={paid}");
            }
        }

        // ───────────────────────────── Pandemonium Watchbear ─────────────────────────────

        static void WatchbearDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var pd = PlaceField(e, e.Opponent, Pandemonium);
                Up(e, e.Opponent, Watchbear, 0);
                var mst = PutInHand(e, e.Player, Mst);
                var activated = e.TryActivateSpellTrap(e.Player, mst, fromHand: true);
                Drive(e, c => c == pd);
                Check("Watchbear: the opponent's MST does not destroy its controller's Pandemonium",
                    activated && e.Opponent.FieldSpellZone.Occupant == pd && !e.Opponent.Graveyard.Contains(pd),
                    $"activated={activated} field={e.Opponent.FieldSpellZone.Occupant?.Name}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var pd = PlaceField(e, e.Player, Pandemonium);
                Up(e, e.Player, Watchbear, 0);
                var mst = PutInHand(e, e.Player, Mst);
                var activated = e.TryActivateSpellTrap(e.Player, mst, fromHand: true);
                Drive(e, c => c == pd);
                Check("Watchbear: its own controller's MST still destroys Pandemonium (opponent's effects only)",
                    activated && e.Player.FieldSpellZone.Occupant == null && e.Player.Graveyard.Contains(pd),
                    $"activated={activated} field={e.Player.FieldSpellZone.Occupant?.Name}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var pd = PlaceField(e, e.Opponent, Pandemonium);
                PlaceMonster(e, e.Opponent, Watchbear, 0, BattlePosition.Defense, false);
                var mst = PutInHand(e, e.Player, Mst);
                var activated = e.TryActivateSpellTrap(e.Player, mst, fromHand: true);
                Drive(e, c => c == pd);
                Check("Watchbear: face-down, it protects nothing",
                    activated && e.Opponent.FieldSpellZone.Occupant == null && e.Opponent.Graveyard.Contains(pd),
                    $"activated={activated} field={e.Opponent.FieldSpellZone.Occupant?.Name}");
            }
        }

        // ───────────────────────────── Desrook Archfiend ─────────────────────────────

        static void DesrookDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Player, Terrorking, 0);
                var dr = PutInHand(e, e.Player, Desrook);
                var idle = e.CanActivateSpellTrap(e.Player, dr, fromHand: true);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                var window = WaitForWindow(e, e.Player, ResponseTiming.MonsterSentFromFieldToYourGy);
                var offered = window && Offers(e, dr);
                var activated = offered && e.TryActivateSpellTrap(e.Player, dr, fromHand: true);
                Drive(e, _ => false);
                Check("Desrook: after Dark Hole destroys Terrorking, send Desrook from the hand → Terrorking returns",
                    !idle && offered && activated && OnField(e.Player, tk) && tk.FaceUp &&
                    e.Player.Graveyard.Contains(dr) && !e.Player.Hand.Contains(dr),
                    $"idle={idle} window={window} offered={offered} activated={activated} onField={OnField(e.Player, tk)}");
            }

            foreach (var defense in new[] { false, true })
            {
                // Lua s.spop POS_FACEUP: the player chooses face-up Attack or Defense Position (review finding: it was
                // always Attack). The pick holds the Chain.
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Player, Terrorking, 0);
                var dr = PutInHand(e, e.Player, Desrook);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                var window = WaitForWindow(e, e.Player, ResponseTiming.MonsterSentFromFieldToYourGy);
                var activated = window && Offers(e, dr) && e.TryActivateSpellTrap(e.Player, dr, fromHand: true);
                var label = defense ? "Face-up Defense Position" : "Face-up Attack Position";
                var asked = false;
                for (var i = 0; i < 12 && !asked; i++)
                {
                    if (e.IsAwaitingEffectTarget && Legal(e).Exists(c => c.Def?.name == label))
                        asked = true;
                    else if (e.IsAwaitingResponse && !e.IsAwaitingEffectTarget) e.PassResponse();
                    else break;
                }

                var chosen = asked && e.TrySelectEffectTarget(Legal(e).First(c => c.Def?.name == label));
                Drive(e, _ => false);
                var want = defense ? BattlePosition.Defense : BattlePosition.Attack;
                Check($"Desrook: the player Special Summons Terrorking in face-up {(defense ? "Defense" : "Attack")} Position",
                    activated && asked && chosen && OnField(e.Player, tk) && tk.FaceUp && tk.Position == want,
                    $"activated={activated} asked={asked} chosen={chosen} onField={OnField(e.Player, tk)} " +
                    $"faceUp={tk.FaceUp} pos={tk.Position}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Opponent, Terrorking, 0);
                var dr = PutInHand(e, e.Opponent, Desrook);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                Drive(e, _ => false);
                Check("Desrook (AI): the AI brings its destroyed Terrorking back",
                    OnField(e.Opponent, tk) && e.Opponent.Graveyard.Contains(dr),
                    $"onField={OnField(e.Opponent, tk)} drGy={e.Opponent.Graveyard.Contains(dr)}");
            }

            {
                // Destroyed by battle (EFFECT_FLAG_DAMAGE_STEP): the end-of-Damage-Step window offers Desrook.
                var e = Setup(db, pDeck, aDeck);
                var tk = Up(e, e.Player, Terrorking, 0);
                var be = Up(e, e.Opponent, BlueEyes, 2);
                var dr = PutInHand(e, e.Player, Desrook);
                var reached = ReachOpponentBattle(e);
                var attacked = reached && e.TryAttack(e.Opponent, be, tk);
                var window = attacked && WaitForWindow(e, e.Player, ResponseTiming.EndOfDamageStep);
                var offered = window && Offers(e, dr);
                var activated = offered && e.TryActivateSpellTrap(e.Player, dr, fromHand: true);
                Drive(e, _ => false);
                DrainCombat(e);
                Check("Desrook: Terrorking destroyed by battle comes back at the end of the Damage Step",
                    reached && attacked && offered && activated && OnField(e.Player, tk) && e.Player.Graveyard.Contains(dr),
                    $"reached={reached} attacked={attacked} window={window} offered={offered} activated={activated} " +
                    $"onField={OnField(e.Player, tk)}");
            }

            {
                // Only a destroyed "Terrorking Archfiend": Vilepawn's destruction offers nothing.
                var e = Setup(db, pDeck, aDeck);
                Up(e, e.Player, Vilepawn, 0);
                var dr = PutInHand(e, e.Player, Desrook);
                var dh = PutInHand(e, e.Player, DarkHole);
                e.TryActivateSpellTrap(e.Player, dh, fromHand: true);
                var offered = false;
                for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                {
                    offered |= Offers(e, dr);
                    e.PassResponse();
                }

                Check("Desrook: not offered when another Archfiend (Vilepawn) is destroyed",
                    !offered && e.Player.Hand.Contains(dr), $"offered={offered}");
            }
        }
    }
}
