using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "BothSideAndMonsterEquip" (v161): a monster Card treated as an Equip Spell Card with a granted effect
    /// (Sword Hunter). Compile shape and near misses, and mini-duels through TryAttack and the Battle Phase exit: only its
    /// own battle kills are equipped, its own text stops applying (Jinzo, a Union's bonus), Imperial Order negates the
    /// granted +200, the "more monsters than free zones: none" rule, and the owner's GY when an equip leaves.
    /// Royal Oppression and Fusion Gate are deferred (the DuelUI cannot activate a card in the opponent's zones);
    /// their checks assert they stay uncompiled. Runtime: <see cref="BothSideAndMonsterEquipRuntime"/>.
    /// </summary>
    public static class BothSideAndMonsterEquipRegressionTests
    {
        const int SwordHunter = 51345461;
        const int RoyalOppression = 93016201;
        const int FusionGate = 33550694;

        const int Celtic = 91152256;          // Warrior EARTH 1400/1200
        const int OjamaGreen = 12482652;      // Beast Normal 0/1000
        const int Jinzo = 77585513;           // Machine 2400/1500: Traps cannot be activated
        const int YDragonHead = 65622692;     // Union 1500/1600: +400 ATK/DEF while equipped by its own effect
        const int Reinforcements = 17814387;  // Normal Trap
        const int ImperialOrder = 61740673;   // Continuous Trap: negate all Spell effects on the field
        const int Mst = 5318639;
        const int PotOfGreed = 55144522;
        const int Disarmament = 20727787;     // Normal Trap: destroy all Equip Cards on the field
        const int Sangan = 26202165;          // sent from the field to the GY: search <= 1500 ATK (Lua: previous location ONFIELD)
        const int Witch = 78010363;           // Witch of the Black Forest: the same, <= 1500 DEF

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check("BothSideAndMonsterEquip: " + name, ok, detail);

            CompileShapes(db, Check);
            OwnTurnDuel(db, pDeck, aDeck, Check);
            OpponentTurnDuels(db, pDeck, aDeck, Check);
            UnionDuel(db, pDeck, aDeck, Check);
            LeavesToOwnersGyDuel(db, pDeck, aDeck, Check, Sangan, hostDestroyed: true);
            LeavesToOwnersGyDuel(db, pDeck, aDeck, Check, Witch, hostDestroyed: false);
        }

        static string Left(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} clauses={p.ClauseList.Count} " +
                                 string.Join(",", p.ClauseList.Select(c => c?.Timing + "/" + c?.Action));

        static CompiledCardProgram CompileMonsterText(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990161, name = "Near-miss Hunter", type = "Effect Monster", frameType = "effect",
                race = "Warrior", desc = text, atk = 2450, def = 1700, level = 7
            });

        // ───────────────────────── compile shape ─────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var hunter = db.Get(SwordHunter);
            var prog = CardTextEffectCompiler.Compile(hunter);
            var c = prog?.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            Check("Sword Hunter compiles to one end-of-Battle-Phase equip clause (+200 ATK granted) and is Implemented",
                prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.EndOfBattlePhase &&
                c.Action == EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells && c.Amount == 200 &&
                c.MakesChainLink && CardEffectStatus.Classify(hunter) == CardEffectStatusKind.Implemented,
                Left(prog) + $" status={CardEffectStatus.Classify(hunter)}");
            Check("Sword Hunter's action resolves as an Equip",
                c != null && EffectVocabulary.ResolutionOf(c) == EffectResolutionKind.Equip);

            var text = hunter.desc;
            var extra = CompileMonsterText(text + " Then draw 1 card.");
            Check("near miss: Sword Hunter's text with an extra sentence is not FullyCompiled",
                extra == null || !extra.FullyCompiled, Left(extra));
            var defToo = CompileMonsterText(text.Replace("gains 200 ATK.", "gains 200 ATK and DEF."));
            Check("near miss: \"gains 200 ATK and DEF\" gets no Sword Hunter equip",
                defToo == null || !defToo.ClauseList.Exists(x =>
                    x?.Action == EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells), Left(defToo));
            var effectKill = CompileMonsterText(text.Replace("destroyed a monster(s) by battle", "destroyed a monster(s)"));
            Check("near miss: \"destroyed a monster(s)\" (not by battle) gets no Sword Hunter equip",
                effectKill == null || !effectKill.ClauseList.Exists(x =>
                    x?.Action == EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells), Left(effectKill));

            // Deferred (fail closed, as before the batch): "Either player can" / "The turn player can" use a face-up card
            // the opponent controls, which the DuelUI cannot start.
            foreach (var (id, name) in new[] { (RoyalOppression, "Royal Oppression"), (FusionGate, "Fusion Gate") })
            {
                var def = db.Get(id);
                var p = CardTextEffectCompiler.Compile(def);
                Check($"deferred: {name} stays fail-closed (not FullyCompiled, not Implemented)",
                    (p == null || !p.FullyCompiled) && CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented,
                    Left(p) + $" status={CardEffectStatus.Classify(def)}");
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ReachBattle(DuelEngine engine, DuelistState who)
        {
            for (var t = 0; t < 8 && !(engine.TurnPlayer == who && engine.TurnNumber >= 2); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.TurnPlayer != who) return false;
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1)
                engine.TryEnterBattlePhase(who);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == who;
        }

        static DuelEngine BattleOf(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            reached = ReachBattle(engine, player ? engine.Player : engine.Opponent);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void ToMainPhase2(DuelEngine engine, DuelistState who)
        {
            engine.TryEnterMainPhase2(who);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            FieldSpellEffects.RefreshBoard(engine);
        }

        static CardInstance PlaceSetSpell(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = false;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        // ───────────────────────── Sword Hunter on its controller's turn ─────────────────────────

        static void OwnTurnDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = BattleOf(db, pDeck, aDeck, player: true, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var hunter = PlaceMonster(engine, p, SwordHunter, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
            var jinzo = PlaceMonster(engine, o, Jinzo, 0, BattlePosition.Attack, true);
            var ojama = PlaceMonster(engine, o, OjamaGreen, 1, BattlePosition.Attack, true);
            var reinforcements = PlaceSetTrap(engine, p, Reinforcements, 0);
            FieldSpellEffects.RefreshBoard(engine);
            var lockedBefore = ContinuousNegationRuntime.ActivationLocked(engine, p, reinforcements, out _);

            var a1 = reached && Attack(engine, p, celtic, ojama);
            var a2 = reached && Attack(engine, p, hunter, jinzo);
            ToMainPhase2(engine, p);
            var equipped = p.TryFindSpellTrap(jinzo, out _) && jinzo.EquippedTo == hunter && hunter.Equips.Contains(jinzo);
            Check("Sword Hunter: at the end of the Battle Phase the monster it destroyed by battle is equipped to it (+200 ATK)",
                a1 && a2 && equipped && jinzo.FaceUp && jinzo.IsEquipSpellWithGrantedEffectOnly &&
                !o.Graveyard.Contains(jinzo) && hunter.CurrentAtk == 2650 && engine.Phase == DuelPhase.Main2,
                $"reached={reached} a1={a1} a2={a2} equipped={equipped} atk={hunter.CurrentAtk} phase={engine.Phase}");
            Check("Sword Hunter: a monster another card destroyed by battle stays in the GY",
                o.Graveyard.Contains(ojama) && ojama.EquippedTo == null && !p.TryFindSpellTrap(ojama, out _));
            var lockedAfter = ContinuousNegationRuntime.ActivationLocked(engine, p, reinforcements, out _);
            Check("Sword Hunter: an equipped Jinzo is an Equip Spell Card — its Trap lock no longer applies",
                lockedBefore && !lockedAfter && SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell(jinzo) &&
                !engine.CanActivateSpellTrap(p, jinzo, false),
                $"before={lockedBefore} after={lockedAfter}");

            var order = engine.CreateCardInstance(ImperialOrder);
            order.FaceUp = true;
            order.SetThisTurn = false;
            o.SpellTrapZones[0].Occupant = order;
            FieldSpellEffects.RefreshBoard(engine);
            var negatedAtk = hunter.CurrentAtk;
            o.SpellTrapZones[0].Occupant = null;
            FieldSpellEffects.RefreshBoard(engine);
            Check("Sword Hunter: Imperial Order negates the equipped Spell's granted +200; it applies again once it is gone",
                negatedAtk == 2450 && hunter.CurrentAtk == 2650, $"under IO={negatedAtk} after={hunter.CurrentAtk}");

            var mst = PutInHand(engine, p, Mst);
            var act = engine.TryActivateSpellTrap(p, mst, true);
            if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(jinzo);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            FieldSpellEffects.RefreshBoard(engine);
            Check("Sword Hunter: MST destroys the equipped Jinzo, which goes to its owner's GY; the ATK bonus ends",
                act && o.Graveyard.Contains(jinzo) && !p.Graveyard.Contains(jinzo) && jinzo.EquippedTo == null &&
                !p.TryFindSpellTrap(jinzo, out _) && !hunter.Equips.Contains(jinzo) && hunter.CurrentAtk == 2450,
                $"act={act} oGy={o.Graveyard.Contains(jinzo)} pGy={p.Graveyard.Contains(jinzo)} atk={hunter.CurrentAtk}");
        }

        // ───────────────────────── Sword Hunter attacked on the opponent's turn ─────────────────────────

        static void OpponentTurnDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var setCards in new[] { 4, 3 })
            {
                var engine = BattleOf(db, pDeck, aDeck, player: false, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var hunter = PlaceMonster(engine, p, SwordHunter, 0, BattlePosition.Attack, true);
                var c1 = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                for (var z = 0; z < setCards; z++) PlaceSetSpell(engine, p, PotOfGreed, z);
                var a1 = reached && Attack(engine, o, c1, hunter);
                var a2 = reached && Attack(engine, o, c2, hunter);
                var bothInGy = o.Graveyard.Contains(c1) && o.Graveyard.Contains(c2);
                ToMainPhase2(engine, o);
                if (setCards == 4)
                {
                    Check("Sword Hunter: 2 monsters to equip and 1 free Spell & Trap Zone — none is equipped",
                        a1 && a2 && bothInGy && o.Graveyard.Contains(c1) && o.Graveyard.Contains(c2) &&
                        hunter.Equips.Count == 0 && hunter.CurrentAtk == 2450,
                        $"reached={reached} a1={a1} a2={a2} gy={bothInGy} equips={hunter.Equips.Count} atk={hunter.CurrentAtk}");
                    continue;
                }

                var both = p.TryFindSpellTrap(c1, out _) && p.TryFindSpellTrap(c2, out _) &&
                           c1.EquippedTo == hunter && c2.EquippedTo == hunter;
                Check("Sword Hunter: on the opponent's turn it equips both attackers it destroyed (+400 ATK)",
                    a1 && a2 && bothInGy && both && hunter.CurrentAtk == 2850,
                    $"reached={reached} a1={a1} a2={a2} both={both} atk={hunter.CurrentAtk}");
                engine.DestroyMonsterPublic(p, hunter, null);
                Check("Sword Hunter: when it is destroyed, its equipped monsters go to their owner's GY",
                    o.Graveyard.Contains(c1) && o.Graveyard.Contains(c2) && !p.Graveyard.Contains(c1) &&
                    !p.Graveyard.Contains(c2) && !p.TryFindSpellTrap(c1, out _) && !p.TryFindSpellTrap(c2, out _) &&
                    p.Graveyard.Contains(hunter),
                    $"oGy={o.Graveyard.Contains(c1)}/{o.Graveyard.Contains(c2)} pGy={p.Graveyard.Contains(c1)}");
            }
        }

        // ───────────────────────── a Union equipped by Sword Hunter ─────────────────────────

        static void UnionDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = BattleOf(db, pDeck, aDeck, player: true, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var hunter = PlaceMonster(engine, p, SwordHunter, 0, BattlePosition.Attack, true);
            var other = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
            var head = PlaceMonster(engine, o, YDragonHead, 0, BattlePosition.Attack, true);
            var a = reached && Attack(engine, p, hunter, head);
            ToMainPhase2(engine, p);
            Check("Sword Hunter: an equipped Union gives only the granted +200, not its own equip bonus; it cannot move or activate",
                a && head.EquippedTo == hunter && hunter.CurrentAtk == 2650 &&
                !EquipFamilyRuntime.IsCorrectEquipTarget(engine, head, other) &&
                !engine.CanActivateSpellTrap(p, head, false),
                $"reached={reached} a={a} host={head.EquippedTo?.Name} atk={hunter.CurrentAtk}");

            // Disarmament (Lua IsType(TYPE_EQUIP)) destroys it too: a monster acting as an Equip Card is an Equip Card.
            var disarm = PlaceSetTrap(engine, p, Disarmament, 4);
            var act = engine.TryActivateSpellTrap(p, disarm, false);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            FieldSpellEffects.RefreshBoard(engine);
            Check("Disarmament destroys the monster Sword Hunter equipped; it goes to its owner's GY",
                act && o.Graveyard.Contains(head) && !p.Graveyard.Contains(head) && head.EquippedTo == null &&
                hunter.CurrentAtk == 2450,
                $"act={act} oGy={o.Graveyard.Contains(head)} pGy={p.Graveyard.Contains(head)} atk={hunter.CurrentAtk}");
        }

        // ─────────── an equipped monster's "sent from the field to the GY" effect is its owner's ───────────

        /// <summary>
        /// The opponent's Sangan / Witch of the Black Forest equipped by the player's Sword Hunter leaves the Spell &amp;
        /// Trap Zone for its owner's GY (Sword Hunter destroyed, or the equip itself destroyed). Lua c26202165 /
        /// c78010363 check only IsPreviousLocation(LOCATION_ONFIELD), so the trigger activates; a card in the GY is
        /// controlled by its owner, so it is the opponent's: it searches the opponent's Deck, never the player's.
        /// </summary>
        static void LeavesToOwnersGyDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check, int id,
            bool hostDestroyed)
        {
            var engine = BattleOf(db, pDeck, aDeck, player: true, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var hunter = PlaceMonster(engine, p, SwordHunter, 0, BattlePosition.Attack, true);
            var victim = PlaceMonster(engine, o, id, 0, BattlePosition.Attack, true);
            var a = reached && Attack(engine, p, hunter, victim);
            for (var i = 0; i < 6 && (engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget); i++)
            { if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting(); else engine.PassResponse(); }
            ToMainPhase2(engine, p);
            var eq = victim.EquippedTo == hunter && p.TryFindSpellTrap(victim, out _);
            // OncePerTurnHard batch: the victim's "sent from the field to the GY" effect already activated when it was destroyed
            // by battle this turn (CountLimit(1, id)), so its second departure is checked on the next turn (the opponent's).
            var nextTurn = ReachOpponentMain(engine);
            eq = eq && nextTurn && victim.EquippedTo == hunter && p.TryFindSpellTrap(victim, out _);
            o.Deck.Insert(0, Celtic); // 1400 ATK / 1200 DEF: a legal search for either card
            int pd = p.DeckCount, od = o.DeckCount, ph = p.Hand.Count, oh = o.Hand.Count;
            if (hostDestroyed)
                engine.DestroyMonsterPublic(p, hunter, null);
            else
                TextEffectRuntime.DestroyCard(engine, victim, null, byPlayer: p);
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            var pend = engine.PendingActivation;
            var how = hostDestroyed ? "Sword Hunter destroyed" : "the equip destroyed";
            Check($"Sword Hunter: an equipped {victim.Name} leaving for its owner's GY ({how}) activates for its owner",
                a && eq && o.Graveyard.Contains(victim) && !(pend != null && pend.Controller == p) &&
                p.DeckCount == pd && p.Hand.Count == ph && o.DeckCount == od - 1 && o.Hand.Count == oh + 1,
                $"reached={reached} a={a} eq={eq} oGy={o.Graveyard.Contains(victim)} " +
                $"pend={pend?.Card?.Name}/{(pend?.Controller == p ? "player" : "opp")} pDeck {pd}->{p.DeckCount} " +
                $"oDeck {od}->{o.DeckCount} pHand {ph}->{p.Hand.Count} oHand {oh}->{o.Hand.Count}");
        }
    }
}
