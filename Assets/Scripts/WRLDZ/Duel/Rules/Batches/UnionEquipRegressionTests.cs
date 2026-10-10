using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Union shell (v167). The procedure sentences already compiled a clause; the monster never left its
    /// zone, so the effect could not be played. These checks drive the real activation: the Union occupies
    /// a Spell/Trap Zone, a second Union is refused, unequip Special Summons it back, and battle destroys
    /// the Union instead of the host.
    /// </summary>
    public static class UnionEquipRegressionTests
    {
        const int ZombieTiger = 47693640;
        const int DecayedCommander = 10209545;
        const int PitchDark = 47415292;
        const int DarkBlade = 11321183;
        const int ZMetal = 64500000;
        const int XHead = 62651957;
        const int YDragon = 65622692;
        const int GeminiElf = 69140098;
        const int Koitsu = 69456283;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("Union: " + n, ok, d ?? "");
            ProcedureSentences(db, Check);
            EquipLandsInSpellTrapZone(db, pDeck, aDeck, Check);
            SecondUnionRefused(db, pDeck, aDeck, Check);
            UnequipSpecialSummons(db, pDeck, aDeck, Check);
            BattleDestroysUnionInstead(db, pDeck, aDeck, Check);
            AltHost(db, pDeck, aDeck, Check);
        }

        static void ProcedureSentences(CardDatabase db, Action<string, bool, string> Check)
        {
            var tiger = CardTextEffectCompiler.Compile(db.Get(ZombieTiger));
            var frags = tiger?.UnparsedFragments ?? System.Array.Empty<string>();
            Check("Zombie Tiger procedure leaves no 'Once per turn, in Attack Position' residue",
                tiger != null && tiger.FullyCompiled &&
                !System.Array.Exists(frags, f => f != null && f.Contains("Attack Position")),
                tiger == null ? "null" : $"full={tiger.FullyCompiled} | {string.Join(" | ", frags)}");
            Check("Zombie Tiger equips to Decayed Commander and can unequip",
                tiger != null &&
                tiger.ClauseList.Exists(c => c.Action == EffectActionKind.EquipThisToTarget &&
                                             c.EquipHostName == "Decayed Commander") &&
                tiger.ClauseList.Exists(c => c.Action == EffectActionKind.UnequipThisSpecialSummon),
                "clauses");

            var pitch = CardTextEffectCompiler.Compile(db.Get(PitchDark));
            Check("Pitch-Dark Dragon classic sentence compiles the same two actions",
                pitch != null &&
                pitch.ClauseList.Exists(c => c.Action == EffectActionKind.EquipThisToTarget &&
                                             c.EquipHostName == "Dark Blade") &&
                pitch.ClauseList.Exists(c => c.Action == EffectActionKind.UnequipThisSpecialSummon),
                pitch == null ? "null" : $"full={pitch.FullyCompiled} | {string.Join(" | ", pitch.UnparsedFragments ?? System.Array.Empty<string>())}");
            Check("Pitch-Dark Dragon fully compiles (procedure + 400 ATK/DEF + pierce)",
                pitch != null && pitch.FullyCompiled &&
                pitch.ClauseList.Exists(c => c.Action == EffectActionKind.EquippedGainsPiercing),
                pitch == null ? "null" : $"full={pitch.FullyCompiled} | {string.Join(" | ", pitch.UnparsedFragments ?? System.Array.Empty<string>())}");

            var koitsu = CardTextEffectCompiler.Compile(db.Get(Koitsu));
            Check("Koitsu fully compiles (procedure + 3000 ATK + pierce)",
                koitsu != null && koitsu.FullyCompiled &&
                koitsu.ClauseList.Exists(c => c.Action == EffectActionKind.EquippedGainsPiercing) &&
                koitsu.ClauseList.Exists(c => c.EquipAtkBonus == 3000 && c.EquipDefBonus == 0),
                koitsu == null ? "null" : $"full={koitsu.FullyCompiled} | {string.Join(" | ", koitsu.UnparsedFragments ?? System.Array.Empty<string>())}");

            var tank = CardTextEffectCompiler.Compile(db.Get(ZMetal));
            var equip = tank?.ClauseList.Find(c => c.Action == EffectActionKind.EquipThisToTarget);
            Check("Z-Metal Tank keeps both printed host names",
                equip != null && equip.EquipHostName == "X-Head Cannon" && equip.AltNamedCard == "Y-Dragon Head",
                equip == null ? "no equip clause" : $"{equip.EquipHostName} / {equip.AltNamedCard} full={tank?.FullyCompiled} | {string.Join(" | ", tank.UnparsedFragments ?? System.Array.Empty<string>())}");
            Check("Z-Metal Tank fully compiles (procedure + 600 ATK/DEF)",
                tank != null && tank.FullyCompiled &&
                tank.ClauseList.Exists(c => c.EquipAtkBonus == 600 && c.EquipDefBonus == 600),
                tank == null ? "null" : $"full={tank.FullyCompiled} | {string.Join(" | ", tank.UnparsedFragments ?? System.Array.Empty<string>())}");
        }

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

        static bool InMonsterZone(DuelistState who, CardInstance c) =>
            who.MonsterZones.Any(z => z.Occupant == c);

        static bool InSpellTrap(DuelistState who, CardInstance c) =>
            who.SpellTrapZones.Any(z => z.Occupant == c);

        static bool ActivateEquip(DuelEngine e, CardInstance union, CardInstance host)
        {
            var used = e.TryActivateSpellTrap(e.Player, union, fromHand: false);
            Settle(e);
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host))
                e.TrySelectEffectTarget(host);
            Settle(e);
            return used;
        }

        static void EquipLandsInSpellTrapZone(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var tiger = PlaceMonster(e, e.Player, ZombieTiger, 0, BattlePosition.Attack, true);
            var host = PlaceMonster(e, e.Player, DecayedCommander, 1, BattlePosition.Attack, true);
            var before = host.CurrentAtk;
            var used = e.TryActivateSpellTrap(e.Player, tiger, fromHand: false);
            var awaiting = e.IsAwaitingEffectTarget;
            var legal = awaiting && e.IsLegalEffectTarget(host);
            if (legal) e.TrySelectEffectTarget(host);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            Check("Zombie Tiger leaves the Monster Zone and occupies a Spell/Trap Zone",
                used && !InMonsterZone(e.Player, tiger) && InSpellTrap(e.Player, tiger) &&
                tiger.EquippedTo == host && host.Equips.Contains(tiger),
                $"used={used} awaiting={awaiting} legal={legal} monster={InMonsterZone(e.Player, tiger)} st={InSpellTrap(e.Player, tiger)} eq={tiger.EquippedTo?.Name}");
            Check("the equipped monster gains the printed 500 ATK",
                host.CurrentAtk == before + 500, $"atk {before}->{host.CurrentAtk}");
        }

        static void SecondUnionRefused(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var first = PlaceMonster(e, e.Player, ZombieTiger, 0, BattlePosition.Attack, true);
            var host = PlaceMonster(e, e.Player, DecayedCommander, 1, BattlePosition.Attack, true);
            var second = PlaceMonster(e, e.Player, ZombieTiger, 2, BattlePosition.Attack, true);
            ActivateEquip(e, first, host);
            var refused = !e.TryActivateSpellTrap(e.Player, second, fromHand: false);
            Check("a monster already equipped with a Union refuses a second",
                refused && InMonsterZone(e.Player, second),
                $"refused={refused} stillMonster={InMonsterZone(e.Player, second)}");
        }

        static void UnequipSpecialSummons(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var tiger = PlaceMonster(e, e.Player, ZombieTiger, 0, BattlePosition.Attack, true);
            var host = PlaceMonster(e, e.Player, DecayedCommander, 1, BattlePosition.Attack, true);
            ActivateEquip(e, tiger, host);
            var sameTurn = e.TryActivateSpellTrap(e.Player, tiger, fromHand: false);
            Check("equip or unequip is once per turn, not both",
                !sameTurn && tiger.EquippedTo == host, $"sameTurn={sameTurn}");

            for (var i = 0; i < 4 && e.TurnPlayer == e.Player; i++)
            {
                Settle(e);
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }
            for (var i = 0; i < 4 && e.TurnPlayer != e.Player; i++)
            {
                Settle(e);
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }
            Settle(e);
            var back = e.TryActivateSpellTrap(e.Player, tiger, fromHand: false);
            Settle(e);
            Check("unequip Special Summons the Union back in Attack Position",
                back && tiger.EquippedTo == null && InMonsterZone(e.Player, tiger) &&
                !InSpellTrap(e.Player, tiger) && tiger.Position == BattlePosition.Attack && tiger.FaceUp,
                $"back={back} eq={tiger.EquippedTo != null} monster={InMonsterZone(e.Player, tiger)} st={InSpellTrap(e.Player, tiger)} phase={e.Phase} turn={(e.TurnPlayer == e.Player ? "P" : "O")}");
        }

        static void BattleDestroysUnionInstead(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var tiger = PlaceMonster(e, e.Player, ZombieTiger, 0, BattlePosition.Attack, true);
            var host = PlaceMonster(e, e.Player, DecayedCommander, 1, BattlePosition.Attack, true);
            ActivateEquip(e, tiger, host);
            e.DestroyMonsterByBattle(e.Player, host, attacker: null);
            Check("battle destroys the Union instead of the equipped monster",
                InMonsterZone(e.Player, host) && e.Player.Graveyard.Contains(tiger) && tiger.EquippedTo == null,
                $"hostOnField={InMonsterZone(e.Player, host)} tigerInGy={e.Player.Graveyard.Contains(tiger)}");
        }

        static void AltHost(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var tank = PlaceMonster(e, e.Player, ZMetal, 0, BattlePosition.Attack, true);
            var y = PlaceMonster(e, e.Player, YDragon, 1, BattlePosition.Attack, true);
            PlaceMonster(e, e.Player, GeminiElf, 2, BattlePosition.Attack, true);
            var used = e.TryActivateSpellTrap(e.Player, tank, fromHand: false);
            Settle(e);
            var yLegal = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(y);
            Check("Z-Metal Tank may equip to the second printed name, not to an unrelated monster",
                used && yLegal && !e.IsLegalEffectTarget(
                    e.Player.MonsterZones.First(z => z.Occupant != null && z.Occupant.CardId == GeminiElf).Occupant),
                $"used={used} y={yLegal}");
            if (yLegal) e.TrySelectEffectTarget(y);
            Settle(e);
            Check("Z-Metal Tank occupies a Spell/Trap Zone on Y-Dragon Head",
                InSpellTrap(e.Player, tank) && tank.EquippedTo == y,
                $"st={InSpellTrap(e.Player, tank)} host={tank.EquippedTo?.Name}");

            var e2 = Setup(db, pDeck, aDeck);
            var dragon = PlaceMonster(e2, e2.Player, PitchDark, 0, BattlePosition.Attack, true);
            var blade = PlaceMonster(e2, e2.Player, DarkBlade, 1, BattlePosition.Attack, true);
            var classic = ActivateEquip(e2, dragon, blade);
            Check("classic Union wording also moves the monster into a Spell/Trap Zone",
                classic && InSpellTrap(e2.Player, dragon) && dragon.EquippedTo == blade,
                $"used={classic} st={InSpellTrap(e2.Player, dragon)}");
        }
    }
}
