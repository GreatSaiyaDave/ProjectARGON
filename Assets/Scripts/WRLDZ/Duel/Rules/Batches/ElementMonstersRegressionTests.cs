using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Coverage family F1 (v180, AttributeBullets): the Element monsters — Element Dragon / Saurus / Valkyrie / Doom /
    /// Magician / Soldier (<see cref="AttributeBulletsAtoms"/>, <see cref="AttributeBulletsRuntime"/>). Compile shapes,
    /// near misses (an unknown bullet body or Attribute must fail the whole card closed), and mini-duels for each of the
    /// four printed bullets: the FIRE +500 ATK aura, the WATER control lock, the WIND "attack once again" after
    /// destroying by battle, and the EARTH negate of an Effect Monster destroyed by battle.
    /// </summary>
    public static class ElementMonstersRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int ElementDragon = 30314994;   // LIGHT; ● FIRE +500 ATK, ● WIND extra attack
        const int ElementSaurus = 92755808;   // DARK;  ● FIRE +500 ATK, ● EARTH negate
        const int ElementValkyrie = 97623219; // LIGHT; ● FIRE +500 ATK, ● WATER control lock
        const int ElementDoom = 23118924;     // DARK;  ● EARTH negate,   ● WIND extra attack
        const int ElementMagician = 65260293; // LIGHT; ● WATER control lock, ● WIND extra attack
        const int ElementSoldier = 66712593;  // DARK;  ● WATER control lock, ● EARTH negate

        const int GigaTechWolf = 8471389;   // FIRE Normal 1200/1400
        const int SkullRedBird = 10202894;  // WIND Normal 1550/1200
        const int IslandTurtle = 4042268;   // WATER Normal 1100/2000
        const int Uraby = 1784619;          // EARTH Normal 1500/800
        const int Witch = 78010363;         // DARK Effect 1100/1200 ("sent to the GY" search — must be negated)
        const int SkullServant = 32274490;  // DARK Normal 300/200 (battle fodder)

        const string Header =
            "This monster gets the following effect(s) while there is a monster(s) with the following Attribute(s) on " +
            "the field:";

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ElementMonsters: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            AtkAuraDuels(db, pDeck, aDeck, Check);
            ControlLockDuels(db, pDeck, aDeck, Check);
            ExtraAttackDuels(db, pDeck, aDeck, Check);
            BattleNegationDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} clauses=[{string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe))}] " +
                  $"unparsed=[{string.Join(" | ", p.UnparsedFragments)}]";

        static CompiledCardProgram Compile(int id, CardDatabase db) =>
            db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;

        static CompiledCardProgram CompileText(int id, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = $"Near-miss Element {id}", type = "Effect Monster", frameType = "effect",
                race = "Dragon", attribute = "LIGHT", level = 4, atk = 1500, def = 1200, desc = text
            });

        static bool Has(CompiledCardProgram p, EffectActionKind action, string attr) =>
            p != null && p.FullyCompiled &&
            p.ClauseList.Exists(c => c != null && c.Action == action &&
                                     string.Equals(c.RequiresFieldAttribute, attr, StringComparison.OrdinalIgnoreCase));

        static string Pairs(CompiledCardProgram p) =>
            p == null
                ? "null"
                : string.Join(",", p.ClauseList.Where(c => c != null)
                    .Select(c => $"{c.Action}/{c.RequiresFieldAttribute ?? "-"}"));

        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            Wipe(engine);
            return engine;
        }

        static void Wipe(DuelEngine engine)
        {
            ClearBoard(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.LifePoints = 8000;
            }
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 10 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool InGy(DuelistState who, CardInstance c) => who.Graveyard.Contains(c);

        /// <summary>Put <paramref name="who"/> (the turn player) into the Battle Phase.</summary>
        static bool PlayerBattle(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            if (engine.Phase == DuelPhase.Main1 && engine.TurnPlayer == engine.Player)
                engine.TryEnterBattlePhase(engine.Player);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == engine.Player;
        }

        static bool AttackAndDrain(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            if (engine.Phase != DuelPhase.Battle) PlayerBattle(engine);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            foreach (var id in new[]
                     {
                         ElementDragon, ElementSaurus, ElementValkyrie, ElementDoom, ElementMagician, ElementSoldier
                     })
            {
                var def = db.Get(id);
                var implemented = def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
                var p = Compile(id, db);
                Check($"{def?.name} is Implemented and FullyCompiled with two Attribute bullets",
                    implemented && p != null && p.FullyCompiled && p.ClauseList.Count == 2,
                    $"implemented={implemented} {Describe(p)}");
            }

            var dragon = Compile(ElementDragon, db);
            Check("Element Dragon: ● FIRE gains 500 ATK (self) and ● WIND extra attack (destroyed-by-battle rider)",
                Has(dragon, EffectActionKind.ContinuousGainAtkDef, "FIRE") &&
                Has(dragon, EffectActionKind.ExtraAttacks, "WIND") &&
                dragon.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                                              c.Amount == 500 && c.AffectsThisCardOnly) &&
                dragon.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ExtraAttacks &&
                                              c.ActivationCondition == AttributeBulletsRuntime.DestroyedMonsterByBattleThisTurn),
                Pairs(dragon));

            var saurus = Compile(ElementSaurus, db);
            Check("Element Saurus: ● FIRE gains 500 ATK and ● EARTH negates Effect Monsters destroyed by battle with it",
                Has(saurus, EffectActionKind.ContinuousGainAtkDef, "FIRE") &&
                Has(saurus, EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle, "EARTH") &&
                saurus.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle &&
                    c.AffectsThisCardOnly && string.Equals(c.TargetCardKind, "Effect", StringComparison.OrdinalIgnoreCase)),
                Pairs(saurus));

            Check("Element Valkyrie: ● FIRE gains 500 ATK and ● WATER control cannot switch",
                Has(Compile(ElementValkyrie, db), EffectActionKind.ContinuousGainAtkDef, "FIRE") &&
                Has(Compile(ElementValkyrie, db), EffectActionKind.ControlOfThisCannotSwitch, "WATER"),
                Pairs(Compile(ElementValkyrie, db)));

            Check("Element Doom: ● EARTH negate and ● WIND extra attack",
                Has(Compile(ElementDoom, db), EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle, "EARTH") &&
                Has(Compile(ElementDoom, db), EffectActionKind.ExtraAttacks, "WIND"),
                Pairs(Compile(ElementDoom, db)));

            Check("Element Magician: ● WATER control lock and ● WIND extra attack",
                Has(Compile(ElementMagician, db), EffectActionKind.ControlOfThisCannotSwitch, "WATER") &&
                Has(Compile(ElementMagician, db), EffectActionKind.ExtraAttacks, "WIND"),
                Pairs(Compile(ElementMagician, db)));

            Check("Element Soldier: ● WATER control lock and ● EARTH negate",
                Has(Compile(ElementSoldier, db), EffectActionKind.ControlOfThisCannotSwitch, "WATER") &&
                Has(Compile(ElementSoldier, db), EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle, "EARTH"),
                Pairs(Compile(ElementSoldier, db)));
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(Chk Check)
        {
            // A bullet body the parser does not know: the whole card fails closed (no unconditional ignition).
            var badBody = CompileText(991001, Header + " ● FIRE: This card gains 500 ATK. ● WATER: Destroy all monsters on the field.");
            Check("an unrecognised bullet body fails the whole card closed",
                badBody != null && !badBody.FullyCompiled && badBody.ClauseList.Count == 0 &&
                !badBody.ClauseList.Exists(c => c != null && c.RequiresFieldAttribute == "FIRE"),
                Describe(badBody));

            // A bullet whose "Attribute" is not an Attribute: fail closed.
            var badAttr = CompileText(991002, Header + " ● NORMAL: This card gains 500 ATK.");
            Check("an unrecognised bullet Attribute fails the whole card closed",
                badAttr != null && !badAttr.FullyCompiled && badAttr.ClauseList.Count == 0, Describe(badAttr));

            // A bare bullet without the header is not this family and must not carry RequiresFieldAttribute.
            var noHeader = CompileText(991003, "● FIRE: This card gains 500 ATK.");
            Check("a bullet without the header is not the Element family",
                noHeader == null || !noHeader.ClauseList.Exists(c => c != null && !string.IsNullOrEmpty(c.RequiresFieldAttribute)),
                Describe(noHeader));

            // A two-bullet block where one bullet's action compiles but the other does not: still fail closed.
            var partial = CompileText(991004, Header + " ● FIRE: This card gains 500 ATK. ● WIND: Draw 1 card.");
            Check("a partially recognised bullet block fails the whole card closed",
                partial != null && !partial.FullyCompiled && partial.ClauseList.Count == 0, Describe(partial));
        }

        // ───────────────────────────── FIRE: +500 ATK while a FIRE monster is on the field ─────────────────────────────

        static void AtkAuraDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var dragon = PlaceMonster(e, p, ElementDragon, 0, BattlePosition.Attack, true);
            var fire = PlaceMonster(e, p, GigaTechWolf, 1, BattlePosition.Defense, true);
            FieldSpellEffects.RefreshBoard(e);
            var withFire = dragon.CurrentAtk;

            // Face-down: the Attribute is not live on the field.
            fire.FaceUp = false;
            FieldSpellEffects.RefreshBoard(e);
            var faceDown = dragon.CurrentAtk;

            // Removed entirely.
            p.MonsterZones[1].Occupant = null;
            FieldSpellEffects.RefreshBoard(e);
            var gone = dragon.CurrentAtk;

            Check("Element Dragon: +500 ATK only while a FIRE monster is face-up on the field",
                reached && dragon.Def.atk == 1500 && withFire == 2000 && faceDown == 1500 && gone == 1500,
                $"reached={reached} printed={dragon.Def.atk} withFire={withFire} faceDown={faceDown} gone={gone}");
        }

        // ───────────────────────────── WATER: control cannot switch ─────────────────────────────

        static void ControlLockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var magician = PlaceMonster(e, p, ElementMagician, 0, BattlePosition.Attack, true);
            var water = PlaceMonster(e, p, IslandTurtle, 1, BattlePosition.Defense, true);
            FieldSpellEffects.RefreshBoard(e);
            var locked = MonsterNegationAndControlLocksRuntime.ControlLocked(e, magician);

            water.FaceUp = false;
            FieldSpellEffects.RefreshBoard(e);
            var unlocked = MonsterNegationAndControlLocksRuntime.ControlLocked(e, magician);

            Check("Element Magician: control cannot switch only while a WATER monster is face-up on the field",
                reached && locked && !unlocked, $"reached={reached} locked={locked} afterFaceDown={unlocked}");
        }

        // ───────────────────────────── WIND: attack once again after destroying by battle ─────────────────────────────

        static void ExtraAttackDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // With a WIND monster on the field: destroying a monster by battle grants a second attack.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var dragon = PlaceMonster(e, p, ElementDragon, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, SkullRedBird, 1, BattlePosition.Defense, true); // WIND present
                var fodder = PlaceMonster(e, o, SkullServant, 0, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var battle = PlayerBattle(e);
                var ok = battle && e.TryAttack(p, dragon, fodder);
                DrainCombat(e);
                PassAll(e);
                var extra = e.ExtraAttacksAllowed(dragon);
                Check("Element Dragon: destroying a monster by battle allows a second attack while a WIND monster is on the field",
                    reached && ok && InGy(o, fodder) && dragon.DestroyedByBattleThisTurn && extra == 1,
                    $"reached={reached} ok={ok} gy={InGy(o, fodder)} flag={dragon.DestroyedByBattleThisTurn} extra={extra}");
            }

            // Without a WIND monster: no second attack even after the battle destruction.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var dragon = PlaceMonster(e, p, ElementDragon, 0, BattlePosition.Attack, true);
                var fodder = PlaceMonster(e, o, SkullServant, 0, BattlePosition.Attack, true);
                FieldSpellEffects.RefreshBoard(e);
                var battle = PlayerBattle(e);
                var ok = battle && e.TryAttack(p, dragon, fodder);
                DrainCombat(e);
                PassAll(e);
                var extra = e.ExtraAttacksAllowed(dragon);
                Check("Element Dragon: no second attack when no WIND monster is on the field",
                    reached && ok && InGy(o, fodder) && dragon.DestroyedByBattleThisTurn && extra == 0,
                    $"reached={reached} ok={ok} gy={InGy(o, fodder)} flag={dragon.DestroyedByBattleThisTurn} extra={extra}");
            }
        }

        // ───────────────────────────── EARTH: negate an Effect Monster destroyed by battle ─────────────────────────────

        static void BattleNegationDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // With an EARTH monster on the field: Witch's "sent to the GY" search is negated.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var saurus = PlaceMonster(e, p, ElementSaurus, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, Uraby, 1, BattlePosition.Defense, true); // EARTH present
                var witch = PlaceMonster(e, o, Witch, 0, BattlePosition.Attack, true);
                var hand0 = o.Hand.Count;
                var deck0 = o.Deck.Count;
                FieldSpellEffects.RefreshBoard(e);
                var ok = AttackAndDrain(e, p, saurus, witch);
                PassAll(e);
                Check("Element Saurus: an Effect Monster destroyed by battle has its effects negated while an EARTH monster is on the field",
                    reached && ok && InGy(o, witch) && witch.EffectsNegatedAfterBattle &&
                    o.Hand.Count == hand0 && o.Deck.Count == deck0 && !e.IsAwaitingEffectTarget,
                    $"reached={reached} ok={ok} gy={InGy(o, witch)} flag={witch.EffectsNegatedAfterBattle} " +
                    $"hand={hand0}->{o.Hand.Count} deck={deck0}->{o.Deck.Count}");
            }

            // Without an EARTH monster: the negation does not apply and Witch's search resolves.
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var saurus = PlaceMonster(e, p, ElementSaurus, 0, BattlePosition.Attack, true);
                var witch = PlaceMonster(e, o, Witch, 0, BattlePosition.Attack, true);
                var deck0 = o.Deck.Count;
                FieldSpellEffects.RefreshBoard(e);
                var ok = AttackAndDrain(e, p, saurus, witch);
                PassAll(e);
                Check("Element Saurus: no negation when no EARTH monster is on the field",
                    reached && ok && InGy(o, witch) && !witch.EffectsNegatedAfterBattle && o.Deck.Count == deck0 - 1,
                    $"reached={reached} ok={ok} gy={InGy(o, witch)} flag={witch.EffectsNegatedAfterBattle} " +
                    $"deck={deck0}->{o.Deck.Count}");
            }
        }
    }
}
