using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// LocksRedirectsAndActivity batch: Goblin of Greed (discard-cost lock at every activation site), Grave Protector (battle
    /// GY-to-Deck redirect), Dark Magician of Chaos (leave-field redirect funnel, battle banish, End Phase GY Spell) and Burst
    /// Stream of Destruction (attack activity record + oath). Compile shapes through the real compiler, near-miss texts, and
    /// mini-duels through the engine entry points (CanActivateSpellTrap / TryActivateSpellTrap, TryAttack and its response
    /// windows, DestroyMonsterPublic, ReturnCardToHand / ReturnCardToDeck, SendCardToGrave, SpecialSummonToField,
    /// TryEndTurnSafe). Chaos Emperor Dragon - Envoy of the End stays fail-closed (deferred).
    /// Runtime: <see cref="LocksRedirectsAndActivityRuntime"/>.
    /// </summary>
    public static class LocksRedirectsAndActivityRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GoblinOfGreed = 425934;
        const int GraveProtector = 11448373;
        const int DarkMagicianOfChaos = 40737112;
        const int BurstStream = 17655904;
        const int ChaosEmperorDragon = 82301904;

        const int Bewd = 89631139;           // Normal Dragon LIGHT 3000/2500
        const int Celtic = 91152256;         // Normal Warrior EARTH 1400/1200
        const int SummonedSkull = 70781052;  // Normal Fiend DARK 2500/1200
        const int LabyrinthWall = 67284908;  // Normal Rock 0/3000
        const int FlameSwordsman = 45231177; // Fusion Warrior 1800/1600
        const int LightningVortex = 69162969;
        const int GracefulCharity = 79571449;
        const int ThunderDragon = 31786629;
        const int RaigekiBreak = 4178474;
        const int Baou = 68427465;           // "Send 1 card from your hand to the GY" (not a discard)
        const int Kuriboh = 40640057;
        const int PotOfGreed = 55144522;
        const int Raigeki = 12580477;
        const int RopeOfLife = 93382620;
        const int SpellPurification = 1669772;  // Normal Trap: "Discard 1 card from your hand. Destroy all face-up Continuous Spell Cards."
        const int MaliceDispersion = 13626450;  // Normal Trap: "Discard 1 card from your hand. Destroy all face-up Continuous Trap Cards."
        const int MessengerOfPeace = 44656491;  // Continuous Spell
        const int CallOfTheHaunted = 97077563;  // Continuous Trap

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("LocksRedirectsAndActivity: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            GoblinDuels(db, pDeck, aDeck, Check);
            GraveProtectorDuels(db, pDeck, aDeck, Check);
            DarkMagicianOfChaosDuels(db, pDeck, aDeck, Check);
            BurstStreamDuels(db, pDeck, aDeck, Check);
            CrossGroupAuditDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Clean(DuelEngine e)
        {
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

        /// <summary>The Player's Main Phase 1 (turn 3+) with an empty board, hands and GYs.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            return Clean(e);
        }

        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentBattle(e);
            return Clean(e);
        }

        static bool EnterBattle(DuelEngine e, DuelistState who)
        {
            var ok = e.TryEnterBattlePhase(who) && e.Phase == DuelPhase.Battle;
            if (e.IsAwaitingResponse) e.PassResponse();
            return ok;
        }

        static CardInstance Attacker(DuelEngine e, DuelistState who, int id, int zone)
        {
            var m = PlaceMonster(e, who, id, zone, BattlePosition.Attack, true);
            m.ClearAttackFlags();
            return m;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        static int DeckCount(DuelistState who, int id)
        {
            var n = 0;
            for (var i = 0; i < who.Deck.Count; i++)
                if (who.Deck[i] == id) n++;
            return n;
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "LocksRedirectsAndActivity probe", type = type,
                frameType = type.StartsWith("Spell") || type.StartsWith("Trap") ? "spell" : "effect",
                race = race, attribute = type.StartsWith("Spell") || type.StartsWith("Trap") ? null : "DARK",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static bool HasAction(CompiledCardProgram p, EffectActionKind a) => Has(p, c => c.Action == a);

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var goblin = Prog(db, GoblinOfGreed);
            Check("Goblin of Greed FullyCompiled: one continuous both-player discard-cost lock",
                goblin != null && goblin.FullyCompiled && Implemented(db, GoblinOfGreed) && goblin.ClauseList.Count == 1 &&
                Has(goblin, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                 c.Action == EffectActionKind.ContinuousCannotDiscardAsCost &&
                                 c.Side == EffectSide.Both),
                Describe(goblin));

            var grave = Prog(db, GraveProtector);
            Check("Grave Protector FullyCompiled: battle-destroyed monsters to the Deck instead of the GY",
                grave != null && grave.FullyCompiled && Implemented(db, GraveProtector) && grave.ClauseList.Count == 1 &&
                Has(grave, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                c.Action == EffectActionKind.BattleDestroyedMonstersShuffleIntoDeckInstead),
                Describe(grave));

            var dmoc = Prog(db, DarkMagicianOfChaos);
            Check("Dark Magician of Chaos FullyCompiled: End Phase GY Spell (optional, targets, hard OPT), forced battle " +
                  "banish (no target), leave-field redirect",
                dmoc != null && dmoc.FullyCompiled && Implemented(db, DarkMagicianOfChaos) && dmoc.ClauseList.Count == 3 &&
                Has(dmoc, c => c.Timing == EffectTiming.EndPhase &&
                               c.Action == EffectActionKind.AddGySpellToHandAtEndPhaseIfSummonedThisTurn &&
                               c.IsOptional && c.IsPsctTarget && c.OncePerTurn &&
                               c.OptScope == OncePerTurnScope.PerCardName && c.NamedCard == "Dark Magician of Chaos") &&
                Has(dmoc, c => c.Timing == EffectTiming.AfterDamageCalculation && c.Action == EffectActionKind.Banish &&
                               c.Zone == EffectZoneFilter.OpponentBattlingMonster && c.RequiresBattledMonsterDestroyed &&
                               !c.IsOptional && !c.IsPsctTarget && !c.RequiresTargetChoice) &&
                Has(dmoc, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                               c.Action == EffectActionKind.BanishThisInsteadOfLeavingField),
                Describe(dmoc));

            var burst = Prog(db, BurstStream);
            Check("Burst Stream of Destruction FullyCompiled: one Destroy-all-opponent clause gated on a face-up BEWD you " +
                  "control + the attack record, with the BEWD attack oath",
                burst != null && burst.FullyCompiled && Implemented(db, BurstStream) && burst.ClauseList.Count == 1 &&
                Has(burst, c => c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.Destroy &&
                                c.Side == EffectSide.Opponent && c.Zone == EffectZoneFilter.FieldMonsters &&
                                !c.RequiresTargetChoice && c.RequiresFaceUpName == "Blue-Eyes White Dragon" &&
                                c.RequiresControllerNamedCard &&
                                c.ActivationCondition == LocksRedirectsAndActivityRuntime.NoNamedAttackThisTurn &&
                                c.NamedCannotAttackTurnActivated == "Blue-Eyes White Dragon"),
                Describe(burst));

            var ced = Prog(db, ChaosEmperorDragon);
            Check("Chaos Emperor Dragon - Envoy of the End stays fail-closed (no activity record / player-wide " +
                  "activation lock at every activation site)",
                ced != null && !ced.FullyCompiled && !Implemented(db, ChaosEmperorDragon), Describe(ced));

            // Near misses.
            var oppOnly = CompileText(990001, "Effect Monster", "Fiend",
                "As long as this card remains face-up on your side of the field, your opponent cannot activate an effect " +
                "by discarding from his/her hand.");
            Check("near miss: an opponent-only discard lock is not Goblin of Greed's both-player lock",
                !HasAction(oppOnly, EffectActionKind.ContinuousCannotDiscardAsCost), Describe(oppOnly));

            var banishBattle = CompileText(990002, "Effect Monster", "Fiend",
                "Any monster destroyed by battle is banished instead of going to the Graveyard.");
            Check("near miss: a battle banish redirect is not Grave Protector's Deck redirect",
                !HasAction(banishBattle, EffectActionKind.BattleDestroyedMonstersShuffleIntoDeckInstead),
                Describe(banishBattle));

            var anyLeave = CompileText(990003, "Effect Monster", "Spellcaster",
                "If this card would leave the field, banish it instead.");
            Check("near miss: a leave-field redirect without \"face-up\" is not compiled as Dark Magician of Chaos's",
                !HasAction(anyLeave, EffectActionKind.BanishThisInsteadOfLeavingField), Describe(anyLeave));

            var noOath = CompileText(990004, "Spell Card", "Normal",
                "If you control \"Blue-Eyes White Dragon\": Destroy all monsters your opponent controls.");
            Check("near miss: \"If you control X:\" is never compiled as an unconditional Raigeki",
                !Has(noOath, c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldMonsters &&
                                  string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                                  string.IsNullOrEmpty(c.ActivationCondition)),
                Describe(noOath));

            var raigeki = Prog(db, Raigeki);
            Check("Raigeki keeps its unconditional Destroy-all-opponent clause",
                raigeki != null && raigeki.FullyCompiled &&
                Has(raigeki, c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldMonsters &&
                                  c.Side == EffectSide.Opponent && string.IsNullOrEmpty(c.RequiresFaceUpName)),
                Describe(raigeki));
        }

        // ───────────────────────────── Goblin of Greed ─────────────────────────────

        static void GoblinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Lightning Vortex (Discard 1 card; destroy all face-up monsters your opponent controls) — Spell activation.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var vortex = PutInHand(e, p, LightningVortex);
                PutInHand(e, p, Celtic);
                var free = e.CanActivateSpellTrap(p, vortex, fromHand: true);
                var goblin = PlaceMonster(e, p, GoblinOfGreed, 2, BattlePosition.Attack, true);
                var lockedOwn = !e.CanActivateSpellTrap(p, vortex, fromHand: true);
                // Neither player: Goblin on the opponent's side locks the Player too.
                p.MonsterZones[2].Occupant = null;
                var oppGoblin = PlaceMonster(e, opp, GoblinOfGreed, 3, BattlePosition.Defense, true);
                var lockedOpp = !e.CanActivateSpellTrap(p, vortex, fromHand: true);
                // Face-down Goblin applies nothing.
                oppGoblin.FaceUp = false;
                var freeFaceDown = e.CanActivateSpellTrap(p, vortex, fromHand: true);
                Check("Goblin of Greed: a discard-cost Spell (Lightning Vortex) cannot be activated while a face-up Goblin " +
                      "is on either field; face-down it applies nothing",
                    reached && free && lockedOwn && lockedOpp && freeFaceDown && goblin != null,
                    $"reached={reached} free={free} own={lockedOwn} opp={lockedOpp} fd={freeFaceDown}");
            }

            {
                // Discards made by an effect (Graceful Charity) and "send from your hand" costs (Baou) are not locked.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, GoblinOfGreed, 2, BattlePosition.Attack, true);
                var host = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var charity = PutInHand(e, p, GracefulCharity);
                var baou = PutInHand(e, p, Baou);
                PutInHand(e, p, SummonedSkull);
                var charityOk = e.CanActivateSpellTrap(p, charity, fromHand: true);
                var baouOk = e.CanActivateSpellTrap(p, baou, fromHand: true);
                Check("Goblin of Greed: an effect's discard (Graceful Charity) and a \"send from your hand\" cost (Baou) " +
                      "are still legal",
                    reached && charityOk && baouOk && host != null, $"reached={reached} charity={charityOk} baou={baouOk}");
            }

            {
                // "Discard 1 card from your hand." as its own sentence: the compiler merges it into the next sentence's clause
                // (SourceSnippet = "Destroy all face-up Continuous … Cards."). Lua: DiscardHand(REASON_COST) — locked.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                opp.SpellTrapZones[0].Occupant = e.CreateCardInstance(MessengerOfPeace);
                opp.SpellTrapZones[0].Occupant.FaceUp = true;
                var cott = PlaceSetTrap(e, opp, CallOfTheHaunted, 1);
                cott.FaceUp = true;
                var purification = PlaceSetTrap(e, p, SpellPurification, 1);
                var malice = PlaceSetTrap(e, p, MaliceDispersion, 0);
                PutInHand(e, p, Celtic);
                var spFree = e.CanActivateSpellTrap(p, purification, fromHand: false);
                var mdFree = e.CanActivateSpellTrap(p, malice, fromHand: false);
                PlaceMonster(e, opp, GoblinOfGreed, 2, BattlePosition.Attack, true);
                var spLocked = !e.CanActivateSpellTrap(p, purification, fromHand: false) &&
                               !e.TryActivateSpellTrap(p, purification, fromHand: false) &&
                               p.SpellTrapZones[1].Occupant == purification && !purification.FaceUp;
                var mdLocked = !e.CanActivateSpellTrap(p, malice, fromHand: false) &&
                               !e.TryActivateSpellTrap(p, malice, fromHand: false) &&
                               p.SpellTrapZones[0].Occupant == malice && !malice.FaceUp;
                var sendClause = new EffectClause
                {
                    Timing = EffectTiming.Activate, Action = EffectActionKind.Destroy, RequiresDiscardCost = true,
                    DiscardCostCount = 1, SourceSnippet = "Send 1 card from your hand to the GY, then target 1 monster on the field; destroy it."
                };
                var mergedClause = new EffectClause
                {
                    Timing = EffectTiming.Activate, Action = EffectActionKind.Destroy, RequiresDiscardCost = true,
                    DiscardCostCount = 1, SourceSnippet = "Destroy all face-up Continuous Spell Cards."
                };
                var shapes = !LocksRedirectsAndActivityRuntime.DiscardsAsCost(sendClause) &&
                             LocksRedirectsAndActivityRuntime.DiscardsAsCost(mergedClause);
                Check("Goblin of Greed: a standalone \"Discard 1 card from your hand.\" cost (Set Spell Purification, Malice " +
                      "Dispersion) cannot be paid; only a printed \"send … from your hand to the GY\" is exempt",
                    reached && spFree && mdFree && spLocked && mdLocked && shapes,
                    $"reached={reached} spFree={spFree} mdFree={mdFree} spLocked={spLocked} mdLocked={mdLocked} shapes={shapes}");
            }

            {
                // Thunder Dragon's hand ignition ("discard this card") — a monster effect from the hand.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var thunder = PutInHand(e, p, ThunderDragon);
                p.Deck.Add(ThunderDragon); // something to search
                var free = e.CanActivateSpellTrap(p, thunder, fromHand: true);
                PlaceMonster(e, e.Opponent, GoblinOfGreed, 1, BattlePosition.Attack, true);
                var locked = !e.CanActivateSpellTrap(p, thunder, fromHand: true) &&
                             !e.TryActivateSpellTrap(p, thunder, fromHand: true) && p.Hand.Contains(thunder);
                Check("Goblin of Greed: Thunder Dragon's \"discard this card\" hand effect cannot be activated",
                    reached && free && locked, $"reached={reached} free={free} locked={locked}");
            }

            {
                // Raigeki Break (Discard 1 card; target 1 card on the field) in the attack response window.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var goblin = PlaceMonster(e, p, GoblinOfGreed, 1, BattlePosition.Defense, true);
                var rb = PlaceSetTrap(e, p, RaigekiBreak, 0);
                PutInHand(e, p, PotOfGreed);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var lockedWindow = e.PendingResponse == null || !e.PendingResponse.LegalCards.Contains(rb);
                PassAll(e);
                DrainCombat(e);

                var e2 = OpponentBattle(db, pDeck, aDeck, out var reached2);
                var p2 = e2.Player;
                var celtic2 = PlaceMonster(e2, p2, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e2, p2, GoblinOfGreed, 1, BattlePosition.Defense, false);
                var rb2 = PlaceSetTrap(e2, p2, RaigekiBreak, 0);
                PutInHand(e2, p2, PotOfGreed);
                var skull2 = Attacker(e2, e2.Opponent, SummonedSkull, 0);
                e2.NotifyPublic();
                e2.TryAttack(e2.Opponent, skull2, celtic2);
                var offered = e2.PendingResponse != null && e2.PendingResponse.LegalCards.Contains(rb2);
                PassAll(e2);
                DrainCombat(e2);
                Check("Goblin of Greed: Raigeki Break is not offered in the attack window; with the Goblin face-down it is",
                    reached && reached2 && lockedWindow && offered && goblin != null,
                    $"reached={reached}/{reached2} locked={lockedWindow} offered={offered}");
            }

            {
                // Rope of Life ("Discard your entire hand") in the end-of-Damage-Step window after a battle destruction.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, GoblinOfGreed, 1, BattlePosition.Defense, true);
                var rope = PlaceSetTrap(e, p, RopeOfLife, 0);
                PutInHand(e, p, PotOfGreed);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                var offered = false;
                for (var i = 0; i < 6 && e.IsAwaitingResponse; i++)
                {
                    if (e.PendingResponse?.LegalCards?.Contains(rope) ?? false) offered = true;
                    e.PassResponse();
                }

                DrainCombat(e);
                Check("Goblin of Greed: Rope of Life (discard your entire hand) is never offered after the battle",
                    reached && p.Graveyard.Contains(celtic) && !offered && p.SpellTrapZones[0].Occupant == rope,
                    $"reached={reached} gy={p.Graveyard.Contains(celtic)} offered={offered}");
            }

            {
                // Kuriboh's damage-calculation discard.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var kuriboh = PutInHand(e, p, Kuriboh);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var free = MonsterEffects.IsLegalHandDamageCalculationEffect(p, kuriboh, ResponseTiming.DamageCalculation,
                    opp, skull);
                PlaceMonster(e, opp, GoblinOfGreed, 1, BattlePosition.Defense, true);
                var locked = !MonsterEffects.IsLegalHandDamageCalculationEffect(p, kuriboh,
                    ResponseTiming.DamageCalculation, opp, skull);
                Check("Goblin of Greed: Kuriboh's \"discard this card\" cannot be activated in damage calculation",
                    reached && free && locked, $"reached={reached} free={free} locked={locked}");
            }

            {
                // The AI does not activate a locked discard-cost card and still ends its turn (no soft-lock).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var opp = e.Opponent;
                var p = e.Player;
                PlaceMonster(e, p, GoblinOfGreed, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var vortex = PutInHand(e, opp, LightningVortex);
                PutInHand(e, opp, Celtic);
                var aiLocked = !e.CanActivateSpellTrap(opp, vortex, fromHand: true);
                var ended = e.TryEndTurnSafe(opp) && e.TurnPlayer == p;
                Check("Goblin of Greed: the AI's Lightning Vortex is locked too; its turn still ends",
                    reached && aiLocked && ended && opp.Hand.Contains(vortex),
                    $"reached={reached} locked={aiLocked} ended={ended}");
            }
        }

        // ───────────────────────────── Grave Protector ─────────────────────────────

        static void GraveProtectorDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The opponent's Summoned Skull destroys Celtic Guardian: shuffled into the Player's Deck, not the GY.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, p, GraveProtector, 1, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                var before = DeckCount(p, Celtic);
                var deckBefore = p.Deck.Count;
                e.NotifyPublic();
                e.TryAttack(opp, skull, celtic);
                PassAll(e);
                DrainCombat(e);
                var toDeck = !p.Graveyard.Contains(celtic) && !p.Banished.Contains(celtic) &&
                             !p.TryFindMonster(celtic, out _) && DeckCount(p, Celtic) == before + 1 &&
                             p.Deck.Count == deckBefore + 1;
                Check("Grave Protector: a monster destroyed by battle is shuffled into its owner's Deck, not the GY",
                    reached && toDeck, $"reached={reached} toDeck={toDeck} gy={p.Graveyard.Count}");
            }

            {
                // Grave Protector itself destroyed by battle: its effect does not apply to that battle (to the GY).
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var gp = PlaceMonster(e, p, GraveProtector, 1, BattlePosition.Defense, true);
                var skull = Attacker(e, opp, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(opp, skull, gp);
                PassAll(e);
                DrainCombat(e);
                Check("Grave Protector: destroyed by battle itself, it goes to the GY (no effect while battle destroyed)",
                    reached && p.Graveyard.Contains(gp), $"reached={reached} gy={p.Graveyard.Contains(gp)}");
            }

            {
                // Either field: the Player's attack destroys the opponent's monster — to the opponent's Deck; a Fusion goes
                // to the Extra Deck; destruction by an effect still goes to the GY; a face-down Grave Protector does nothing.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, p, GraveProtector, 1, BattlePosition.Defense, true);
                var skull = Attacker(e, p, SummonedSkull, 0);
                var oppCeltic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var swords = PlaceMonster(e, opp, FlameSwordsman, 1, BattlePosition.Attack, true);
                var before = DeckCount(opp, Celtic);
                var battle = EnterBattle(e, p);
                e.TryAttack(p, skull, oppCeltic);
                PassAll(e);
                DrainCombat(e);
                var oppDeck = !opp.Graveyard.Contains(oppCeltic) && DeckCount(opp, Celtic) == before + 1;
                var skull2 = Attacker(e, p, SummonedSkull, 2);
                var extraBefore = opp.ExtraDeck.Count(id => id == FlameSwordsman);
                e.TryAttack(p, skull2, swords);
                PassAll(e);
                DrainCombat(e);
                var extra = !opp.Graveyard.Contains(swords) &&
                            opp.ExtraDeck.Count(id => id == FlameSwordsman) == extraBefore + 1;
                var victim = PlaceMonster(e, opp, Celtic, 3, BattlePosition.Attack, true);
                e.DestroyMonsterPublic(opp, victim, null);
                var effectToGy = opp.Graveyard.Contains(victim);
                Check("Grave Protector: both fields; a Fusion returns to the Extra Deck; effect destruction is not redirected",
                    reached && battle && oppDeck && extra && effectToGy,
                    $"reached={reached} battle={battle} deck={oppDeck} extra={extra} effect={effectToGy}");
            }

            {
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceMonster(e, p, GraveProtector, 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = Attacker(e, e.Opponent, SummonedSkull, 0);
                e.NotifyPublic();
                e.TryAttack(e.Opponent, skull, celtic);
                PassAll(e);
                DrainCombat(e);
                Check("Grave Protector: face-down it applies nothing (to the GY)",
                    reached && p.Graveyard.Contains(celtic), $"reached={reached}");
            }
        }

        // ───────────────────────────── Dark Magician of Chaos ─────────────────────────────

        static void DarkMagicianOfChaosDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The leave-field funnel: destroyed, returned to the hand, returned to the Deck, sent (Tributed) — banished.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var a = PlaceMonster(e, p, DarkMagicianOfChaos, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(e, p, DarkMagicianOfChaos, 1, BattlePosition.Attack, true);
                var c = PlaceMonster(e, p, DarkMagicianOfChaos, 2, BattlePosition.Attack, true);
                var d = PlaceMonster(e, p, DarkMagicianOfChaos, 3, BattlePosition.Attack, true);
                var deckBefore = p.Deck.Count;
                e.DestroyMonsterPublic(p, a, null);
                var returnedHand = e.ReturnCardToHand(b, e.Opponent);
                var returnedDeck = e.ReturnCardToDeck(c);
                e.SendCardToGrave(p, d);
                var allBanished = new[] { a, b, c, d }.All(x => p.Banished.Contains(x)) && p.Graveyard.Count == 0 &&
                                  !p.Hand.Contains(b) && p.Deck.Count == deckBefore && !returnedHand && !returnedDeck;
                Check("Dark Magician of Chaos: a face-up copy that would leave the field (destroyed, bounced, returned to the " +
                      "Deck, sent to the GY) is banished instead",
                    reached && allBanished,
                    $"reached={reached} banished={p.Banished.Count} gy={p.Graveyard.Count} hand={p.Hand.Count}");
            }

            {
                // Face-down it is not redirected.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var fd = PlaceMonster(e, p, DarkMagicianOfChaos, 0, BattlePosition.Defense, false);
                e.DestroyMonsterPublic(p, fd, null);
                Check("Dark Magician of Chaos: destroyed face-down, it goes to the GY",
                    reached && p.Graveyard.Contains(fd) && !p.Banished.Contains(fd), $"reached={reached}");
            }

            {
                // It destroys an opponent's monster by battle: after damage calculation that monster is banished. A monster
                // it does not destroy stays.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var dmoc = Attacker(e, p, DarkMagicianOfChaos, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var wall = PlaceMonster(e, opp, LabyrinthWall, 1, BattlePosition.Defense, true);
                var battle = EnterBattle(e, p);
                e.TryAttack(p, dmoc, celtic);
                PassAll(e);
                DrainCombat(e);
                var banished = opp.Banished.Contains(celtic) && !opp.Graveyard.Contains(celtic);
                var dmoc2 = Attacker(e, p, DarkMagicianOfChaos, 1);
                e.TryAttack(p, dmoc2, wall);
                PassAll(e);
                DrainCombat(e);
                var wallStays = opp.TryFindMonster(wall, out _) && !opp.Banished.Contains(wall);
                Check("Dark Magician of Chaos: a monster it destroys by battle is banished after damage calculation; one it " +
                      "does not destroy stays",
                    reached && battle && banished && wallStays,
                    $"reached={reached} battle={battle} banished={banished} wall={wallStays}");
            }

            {
                // EVENT_BATTLED: Dark Magician of Chaos's banish (Chain Link 1, turn player) and the FLIP of the face-down
                // monster it attacked (Chain Link 2) trigger together; the FLIP resolves first. Man-Eater Bug (AI) destroys
                // Dark Magician of Chaos (banished by its own redirect), then the Bug, still related to the battle, is banished.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var dmoc = Attacker(e, p, DarkMagicianOfChaos, 0);
                var bug = PlaceMonster(e, opp, MonsterEffects.ManEaterBug, 0, BattlePosition.Defense, false);
                var battle = EnterBattle(e, p);
                e.TryAttack(p, dmoc, bug);
                PassAll(e);
                DrainCombat(e);
                var flipResolved = !p.TryFindMonster(dmoc, out _) && p.Banished.Contains(dmoc) &&
                                   !p.Graveyard.Contains(dmoc);
                var bugBanished = opp.Banished.Contains(bug) && !opp.Graveyard.Contains(bug) &&
                                  !opp.TryFindMonster(bug, out _);
                Check("Dark Magician of Chaos: attacking a face-down FLIP monster, the FLIP resolves first (Man-Eater Bug " +
                      "destroys it: banished), then the Bug it destroyed by battle is banished",
                    reached && battle && flipResolved && bugBanished && !e.HasDeclaredAttack,
                    $"reached={reached} battle={battle} flip={flipResolved} bugBanished={bugBanished} " +
                    $"bugGy={opp.Graveyard.Contains(bug)} dmocOnField={p.TryFindMonster(dmoc, out _)}");
            }

            {
                // The same with a human FLIP target pick: the battle pauses for it, the banish waits, then applies.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bug = PlaceMonster(e, p, MonsterEffects.ManEaterBug, 0, BattlePosition.Defense, false);
                var dmoc = Attacker(e, opp, DarkMagicianOfChaos, 0);
                e.NotifyPublic();
                e.TryAttack(opp, dmoc, bug);
                for (var i = 0; i < 12 && !e.IsAwaitingEffectTarget; i++)
                {
                    if (e.IsAwaitingResponse) e.PassResponse();
                    else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                }

                var paused = e.IsAwaitingEffectTarget && e.PendingActivation?.LegalTargets != null &&
                             e.PendingActivation.LegalTargets.Contains(dmoc);
                var waits = p.TryFindMonster(bug, out _) && !p.Banished.Contains(bug);
                var picked = paused && e.TrySelectEffectTarget(dmoc);
                DrainCombat(e);
                var dmocBanished = opp.Banished.Contains(dmoc) && !opp.TryFindMonster(dmoc, out _);
                var bugBanished = p.Banished.Contains(bug) && !p.Graveyard.Contains(bug);
                Check("Dark Magician of Chaos: a human FLIP target pick pauses the battle; the banish waits for the FLIP, " +
                      "then the Bug it destroyed by battle is banished",
                    reached && paused && waits && picked && dmocBanished && bugBanished,
                    $"reached={reached} paused={paused} waits={waits} picked={picked} dmoc={dmocBanished} " +
                    $"bug={bugBanished} bugGy={p.Graveyard.Contains(bug)}");
            }

            {
                // Leave-field redirect beats Grave Protector: Dark Magician of Chaos destroyed by battle is banished.
                var e = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, opp, GraveProtector, 2, BattlePosition.Defense, true);
                var dmoc = PlaceMonster(e, p, DarkMagicianOfChaos, 0, BattlePosition.Defense, true);
                var skull = Attacker(e, opp, Bewd, 0);
                var deckBefore = p.Deck.Count;
                e.NotifyPublic();
                e.TryAttack(opp, skull, dmoc);
                PassAll(e);
                DrainCombat(e);
                Check("Dark Magician of Chaos: destroyed by battle under Grave Protector, it is banished (leave-field " +
                      "redirect first)",
                    reached && p.Banished.Contains(dmoc) && p.Deck.Count == deckBefore,
                    $"reached={reached} banished={p.Banished.Contains(dmoc)}");
            }

            {
                // End Phase: Special Summoned this turn — the Player targets 1 Spell in their GY and adds it; a second copy
                // cannot use the effect again this turn (hard once per turn).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var pot = e.CreateCardInstance(PotOfGreed);
                var raigeki = e.CreateCardInstance(Raigeki);
                p.Graveyard.Add(pot);
                p.Graveyard.Add(raigeki);
                var a = e.CreateCardInstance(DarkMagicianOfChaos);
                var b = e.CreateCardInstance(DarkMagicianOfChaos);
                var summoned = e.SpecialSummonToField(p, a, BattlePosition.Attack, true) &&
                               e.SpecialSummonToField(p, b, BattlePosition.Attack, true);
                e.TryEndTurnSafe(p);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation?.LegalTargets != null &&
                            e.PendingActivation.LegalTargets.Contains(pot) &&
                            e.PendingActivation.LegalTargets.Contains(raigeki);
                var picked = asked && e.TrySelectEffectTarget(raigeki);
                var secondAsked = e.IsAwaitingEffectTarget;
                if (secondAsked) e.CancelEffectTargeting();
                PassAll(e);
                Check("Dark Magician of Chaos: End Phase of the turn it was Special Summoned — target a GY Spell, add it; " +
                      "once per turn by name",
                    reached && summoned && asked && picked && p.Hand.Contains(raigeki) && p.Graveyard.Contains(pot) &&
                    !secondAsked,
                    $"reached={reached} ss={summoned} asked={asked} picked={picked} second={secondAsked} " +
                    $"hand={p.Hand.Contains(raigeki)}");
            }

            {
                // Not Summoned this turn (only placed): no trigger. The AI's copy picks on its own (opponent's turn).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var pot = e.CreateCardInstance(PotOfGreed);
                p.Graveyard.Add(pot);
                PlaceMonster(e, p, DarkMagicianOfChaos, 0, BattlePosition.Attack, true);
                e.TryEndTurnSafe(p);
                var noPrompt = !e.IsAwaitingEffectTarget && p.Graveyard.Contains(pot);

                var e2 = Fresh(db, pDeck, aDeck);
                var reached2 = ReachOpponentMain(e2);
                Clean(e2);
                var opp = e2.Opponent;
                var spell = e2.CreateCardInstance(PotOfGreed);
                opp.Graveyard.Add(spell);
                var ai = e2.CreateCardInstance(DarkMagicianOfChaos);
                var ss = e2.SpecialSummonToField(opp, ai, BattlePosition.Attack, true);
                e2.TryEndTurnSafe(opp);
                PassAll(e2);
                var aiAdded = opp.Hand.Contains(spell) && !opp.Graveyard.Contains(spell) && !e2.IsAwaitingEffectTarget;
                Check("Dark Magician of Chaos: no End Phase effect unless Summoned this turn; the AI adds its GY Spell itself",
                    reached && noPrompt && reached2 && ss && aiAdded,
                    $"reached={reached}/{reached2} noPrompt={noPrompt} ss={ss} ai={aiAdded}");
            }
        }

        // ───────────────────────────── Burst Stream of Destruction ─────────────────────────────

        static void BurstStreamDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var burst = PutInHand(e, p, BurstStream);
                var noBewd = !e.CanActivateSpellTrap(p, burst, fromHand: true);
                var oppBewd = PlaceMonster(e, opp, Bewd, 4, BattlePosition.Attack, true);
                var oppOnly = !e.CanActivateSpellTrap(p, burst, fromHand: true);
                opp.MonsterZones[4].Occupant = null;
                var bewd = Attacker(e, p, Bewd, 0);
                var celtic = Attacker(e, p, Celtic, 1);
                var skull = PlaceMonster(e, opp, SummonedSkull, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, opp, Celtic, 1, BattlePosition.Defense, false);
                var legal = e.CanActivateSpellTrap(p, burst, fromHand: true);
                var act = legal && e.TryActivateSpellTrap(p, burst, fromHand: true);
                PassAll(e);
                var wiped = opp.Graveyard.Contains(skull) && opp.Graveyard.Contains(set) && opp.MonsterCount == 0 &&
                            p.TryFindMonster(bewd, out _);
                var battle = EnterBattle(e, p);
                var bewdLocked = !e.CanAttack(p, bewd);
                var celticFree = e.CanAttack(p, celtic);
                Check("Burst Stream of Destruction: needs a face-up BEWD you control; destroys every opponent's monster " +
                      "(face-down too); BEWD cannot attack that turn, other monsters can",
                    reached && noBewd && oppOnly && act && wiped && battle && bewdLocked && celticFree && oppBewd != null,
                    $"reached={reached} noBewd={noBewd} oppOnly={oppOnly} act={act} wiped={wiped} battle={battle} " +
                    $"bewd={bewdLocked} celtic={celticFree}");
            }

            {
                // The attack record: BEWD declared an attack this turn (the record survives it leaving the field) — no
                // activation in Main Phase 2; another monster's attack does not count.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var bewd = Attacker(e, p, Bewd, 0);
                var burst = PutInHand(e, p, BurstStream);
                var battle = EnterBattle(e, p);
                var attacked = e.TryAttack(p, bewd, null);
                PassAll(e);
                DrainCombat(e);
                var mp2 = e.TryEnterMainPhase2(p);
                p.MonsterZones[0].Occupant = null;
                PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var refused = !e.CanActivateSpellTrap(p, burst, fromHand: true);

                var e2 = PlayerMain(db, pDeck, aDeck, out var reached2);
                var p2 = e2.Player;
                var celtic = Attacker(e2, p2, Celtic, 0);
                PlaceMonster(e2, p2, Bewd, 1, BattlePosition.Defense, true);
                var burst2 = PutInHand(e2, p2, BurstStream);
                var battle2 = EnterBattle(e2, p2);
                var attacked2 = e2.TryAttack(p2, celtic, null);
                PassAll(e2);
                DrainCombat(e2);
                var mp2b = e2.TryEnterMainPhase2(p2);
                PlaceMonster(e2, e2.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var allowed = e2.CanActivateSpellTrap(p2, burst2, fromHand: true);
                Check("Burst Stream of Destruction: refused after BEWD declared an attack this turn (even once it left); " +
                      "another monster's attack does not count",
                    reached && battle && attacked && mp2 && refused && reached2 && battle2 && attacked2 && mp2b && allowed,
                    $"reached={reached}/{reached2} battle={battle}/{battle2} atk={attacked}/{attacked2} mp2={mp2}/{mp2b} " +
                    $"refused={refused} allowed={allowed}");
            }
        }
            // ───────────────────────────── v209 cross-group audit ─────────────────────────────

        const int HumanWave = 30353551;       // Human-Wave Tactics (DeckSummonsAndSarcophagi)
        const int WeaponChange = 10035717;    // Weapon Change (LpLocksDelayedBurnAndGyNegation)
        const int ThunderOfRuler = 91781589;  // Thunder of Ruler (ZoneCapsAndPhaseWindows)
        const int Kagemusha = 15401633;       // Lv2 Warrior Normal 800/400
        const int HarpieGirl = 34100324;      // Lv2 Winged Beast Normal 500/500
        const int BigShieldGardna = 65240384; // Warrior Lv4 100/2600

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        /// <summary>Every prompt takes its first legal card; every window passes.</summary>
        static void AnswerAll(DuelEngine e)
        {
            for (var i = 0; i < 24 && !e.GameOver; i++)
            {
                if (e.IsAwaitingEffectTarget && e.PendingActivation?.LegalTargets != null &&
                    e.PendingActivation.LegalTargets.Count > 0)
                    e.TrySelectEffectTarget(e.PendingActivation.LegalTargets[0]);
                else if (e.IsAwaitingResponse)
                    e.PassResponse();
                else
                    break;
            }
        }

        static void CrossGroupAuditDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Dark Magician of Chaos x Human-Wave Tactics: Human-Wave's record is EVENT_BATTLED + STATUS_BATTLE_DESTROYED
                // (Lua checkop, a continuous effect), before DMoC's EVENT_BATTLED trigger banishes the monster. A Level 2
                // Normal that DMoC destroys and banishes still counts for its controller.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, opp, HumanWave, 0);
                opp.Deck.Clear();
                opp.Deck.AddRange(new[] { HarpieGirl, Celtic });
                var dmoc = Attacker(e, p, DarkMagicianOfChaos, 0);
                var kage = PlaceMonster(e, opp, Kagemusha, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var battle = EnterBattle(e, p);
                var hit = e.TryAttack(p, dmoc, kage);
                PassAll(e);
                DrainCombat(e);
                var banished = opp.Banished.Contains(kage);
                var counted = DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, opp, 2);
                e.TryEndTurnSafe(p);
                AnswerAll(e);
                var summoned = opp.MonstersOnField().Any(m => m != null && m.CardId == HarpieGirl && m.FaceUp);
                Check("cross-group: a Level 2 Normal that Dark Magician of Chaos destroys and banishes still counts for " +
                      "Human-Wave Tactics (1 summoned at the End Phase)",
                    reached && battle && hit && banished && counted == 1 && summoned,
                    $"reached={reached} battle={battle} hit={hit} banished={banished} counted={counted} summoned={summoned}");
            }

            {
                // Weapon Change x Thunder of Ruler: the human's Standby Weapon Change pick holds the Standby Phase; once it is
                // answered, the AI still gets its Standby window and answers with Thunder of Ruler.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                Clean(e);
                var p = e.Player;
                var o = e.Opponent;
                var wc = FaceUpSpellTrap(e, p, WeaponChange, 0);
                var gardna = PlaceMonster(e, p, BigShieldGardna, 0, BattlePosition.Attack, true);
                var thunder = PlaceSetTrap(e, o, ThunderOfRuler, 0);
                e.NotifyPublic();
                e.TryEndTurnSafe(o);
                var asked = e.TurnPlayer == p && e.Phase == DuelPhase.Standby && e.IsAwaitingEffectTarget &&
                            e.PendingActivation?.Card == wc;
                var picked = asked && e.TrySelectEffectTarget(gardna);
                AnswerAll(e);
                var turn = e.TurnNumber;
                Check("cross-group: Weapon Change's Standby pick, then the AI's Thunder of Ruler window — both apply, Main 1",
                    reached && asked && picked && p.LifePoints == 7300 && gardna.CurrentAtk == 2600 &&
                    o.Graveyard.Contains(thunder) && p.BattlePhaseForbiddenTurn == turn && e.Phase == DuelPhase.Main1 &&
                    e.TurnPlayer == p && e.PendingActivation == null,
                    $"reached={reached} asked={asked} picked={picked} lp={p.LifePoints} atk={gardna.CurrentAtk} " +
                    $"thunderGy={o.Graveyard.Contains(thunder)} forb={p.BattlePhaseForbiddenTurn}/{turn} phase={e.Phase} " +
                    $"tp={e.TurnPlayer?.Name} pend={e.PendingActivation?.Card?.Name}");
            }

            {
                // Dark Magician of Chaos x Human-Wave Tactics, both the human's, in one End Phase: two picks (the GY Spell and
                // the Deck Normal + its placement) queue behind each other; both resolve and the turn passes.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                FaceUpSpellTrap(e, p, HumanWave, 0);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { HarpieGirl, Celtic, Celtic });
                var pot = e.CreateCardInstance(PotOfGreed);
                p.Graveyard.Add(pot);
                var kage = Attacker(e, p, Kagemusha, 1);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var battle = EnterBattle(e, p);
                var hit = e.TryAttack(p, kage, opp.MonsterZones[0].Occupant);
                PassAll(e);
                DrainCombat(e);
                var counted = DeckSummonsAndSarcophagiRuntime.BattleDestroyedThisTurn(e, p, 2);
                var mp2 = e.TryEnterMainPhase2(p);
                var dmoc = e.CreateCardInstance(DarkMagicianOfChaos);
                var ss = e.SpecialSummonToField(p, dmoc, BattlePosition.Attack, true);
                e.TryEndTurnSafe(p);
                AnswerAll(e);
                var harpie = p.MonstersOnField().Any(m => m != null && m.CardId == HarpieGirl);
                var potBack = p.Hand.Contains(pot);
                Check("cross-group: Dark Magician of Chaos's and Human-Wave Tactics' End Phase picks in one End Phase — both " +
                      "resolve, the turn passes",
                    reached && battle && hit && counted == 1 && mp2 && ss && harpie && potBack &&
                    e.TurnPlayer == opp && e.PendingActivation == null,
                    $"reached={reached} battle={battle} hit={hit} counted={counted} mp2={mp2} ss={ss} harpie={harpie} " +
                    $"pot={potBack} tp={e.TurnPlayer?.Name} phase={e.Phase} pend={e.PendingActivation?.Card?.Name}");
            }
        }
    }
}
