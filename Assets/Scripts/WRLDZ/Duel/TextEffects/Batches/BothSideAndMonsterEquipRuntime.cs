using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BothSideAndMonsterEquip" (v161) runtime: a monster Card treated as an Equip Spell Card with a granted
    /// effect (ygopro Duel.Equip of a monster + an EFFECT_TYPE_EQUIP effect registered on it by another card).
    /// <list type="bullet">
    /// <item>The state lives on the card: <see cref="CardInstance.GrantedEquipHost"/> / <see cref="CardInstance.GrantedEquipAtk"/>,
    /// valid while the card is still equipped to that host (<see cref="CardInstance.IsEquipSpellWithGrantedEffectOnly"/>).
    /// Its own monster text does not apply: <see cref="CardInstance.IsNegated"/> reads true, which every Spell &amp; Trap
    /// Zone program scan already honours; the Standby/End Phase scan (upkeep costs run while negated) skips it in
    /// <c>TextEffectRuntime.FirePhaseTriggersFor</c>, and <c>OfficialEffectRegistry.CanActivateOfficial</c> refuses to
    /// activate it.</item>
    /// <item>It is a Spell Card on the field (<see cref="SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell"/>: MST, Heavy
    /// Storm, Harpie's Feather Duster, Imperial Order) and an Equip Card (<see cref="EquipModelRuntime.IsEquipCardShape"/>:
    /// Armored Glass, The Emperor's Holiday). Its equip limit is its host only (EquipFamilyRuntime.IsCorrectEquipTarget).</item>
    /// <item>It goes to its owner's GY / hand / Deck / banished pile when it leaves the field:
    /// <see cref="DuelEngine.RecordOwnerOnFieldEntry"/> records the GY it came from.</item>
    /// <item>The granted "+N ATK" is added by FieldSpellEffects.ApplyEquipStats through <see cref="GrantedAtkOf"/>,
    /// unless that Equip Card's effects are negated.</item>
    /// </list>
    /// Sword Hunter (<see cref="EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells"/>) is fired with the other
    /// end-of-Battle-Phase monster effects by <see cref="BattlePhaseEndRuntime.FireEndOfBattlePhase"/> (inline, like the
    /// engine's other monster battle triggers).
    /// </summary>
    public static class BothSideAndMonsterEquipRuntime
    {
        sealed class State
        {
            /// <summary>Sword Hunter's SetCountLimit(1): the turn each card last used its effect.</summary>
            public readonly Dictionary<CardInstance, int> UsedTurn = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────── Sword Hunter ─────────────────────

        /// <summary>
        /// Lua s.filter: the monsters in either GY that <paramref name="hunter"/> destroyed by battle and that were sent
        /// there this turn (REASON_BATTLE, GetReasonCard()==this card, GetTurnID()==this turn).
        /// </summary>
        public static List<CardInstance> BattleKillsInGy(DuelEngine engine, CardInstance hunter)
        {
            var list = new List<CardInstance>();
            if (engine == null || hunter == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.Graveyard == null) continue;
                foreach (var g in side.Graveyard)
                    if (g?.Def != null && g.Def.IsMonster && g.WasDestroyedByBattle &&
                        ReferenceEquals(g.BattleDestroyer, hunter) && g.SentFromFieldTurnNumber == engine.TurnNumber)
                        list.Add(g);
            }

            return list;
        }

        /// <summary>
        /// Sword Hunter at the end of the Battle Phase (Lua s.eqop). Only when it destroyed a monster(s) by battle that is
        /// still in a GY (the printed condition), once per turn. With more of them than free Spell &amp; Trap Zones, or
        /// none free, nothing is equipped; a card in the GY that cannot leave it (Necrovalley) negates the effect.
        /// Otherwise each one moves from its owner's GY to this card's controller's Spell &amp; Trap Zone as an Equip
        /// Spell Card equipped to this card, with only the granted "+<see cref="EffectClause.Amount"/> ATK".
        /// True when the effect resolved (even with no equip).
        /// </summary>
        public static bool EquipBattleKills(DuelEngine engine, DuelistState who, CardInstance hunter, EffectClause clause)
        {
            if (engine == null || who == null || hunter?.Def == null || clause == null) return false;
            if (!hunter.FaceUp || hunter.IsNegated || !who.TryFindMonster(hunter, out _)) return false;
            var kills = BattleKillsInGy(engine, hunter);
            if (kills.Count == 0) return false;
            var st = StateOf(engine);
            if (st.UsedTurn.TryGetValue(hunter, out var used) && used == engine.TurnNumber) return false;
            st.UsedTurn[hunter] = engine.TurnNumber;

            var free = who.SpellTrapZones.Count(z => z != null && z.IsEmpty);
            if (free <= 0 || kills.Count > free)
            {
                engine.Log($"{hunter.Name}: {kills.Count} monster(s) to equip but {free} free Spell & Trap Zone(s) — " +
                           "none is equipped.");
                return true;
            }

            foreach (var k in kills)
            {
                if (!ContinuousProtections.GyMovementLocked(engine, hunter, k)) continue;
                engine.Log($"{hunter.Name}: {k.Name} cannot leave the GY — the effect is negated.");
                return true;
            }

            var atk = clause.Amount > 0 ? clause.Amount : 0;
            foreach (var k in kills)
                EquipFromGy(engine, who, hunter, k, atk);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>One monster Card from its owner's GY to <paramref name="who"/>'s Spell &amp; Trap Zone, equipped to <paramref name="host"/>.</summary>
        static void EquipFromGy(DuelEngine engine, DuelistState who, CardInstance host, CardInstance card, int atk)
        {
            var gyOwner = engine.Player != null && engine.Player.Graveyard.Contains(card) ? engine.Player
                : engine.Opponent != null && engine.Opponent.Graveyard.Contains(card) ? engine.Opponent : null;
            var zi = engine.FirstEmptySpellTrap(who);
            if (gyOwner == null || zi < 0) return;
            gyOwner.Graveyard.Remove(card);
            EquipFamilyRuntime.ForgetDestroyRecord(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, gyOwner, who);
            who.SpellTrapZones[zi].Occupant = card;
            card.FaceUp = true;
            card.Position = BattlePosition.Attack;
            card.SetThisTurn = false;
            card.ClearAttackFlags();
            card.SentFromFieldTurnNumber = 0;
            card.WasDestroyedByBattle = false;
            card.BattleDestroyer = null;
            card.AtkModifier = 0;
            card.DefModifier = 0;
            card.Counters = 0;
            card.EquipTurnCounter = 0;
            card.AbsorbedBy = null;
            card.EquippedTo = host;
            card.GrantedEquipHost = host;
            card.GrantedEquipAtk = atk;
            if (!host.Equips.Contains(card))
                host.Equips.Add(card);
            // ygopro EVENT_EQUIP (Duel.EquipComplete): Armored Glass's window.
            EquipFamilyRuntime.NoteEquipped(engine, card, host);
            engine.Log($"{card.Name} is equipped to {host.Name} as an Equip Spell Card ({host.Name} gains {atk} ATK).");
        }

        // ───────────────────── the granted equip effect ─────────────────────

        /// <summary>
        /// The ATK <paramref name="eq"/>'s granted equip effect gives <paramref name="host"/> right now: 0 when it is not
        /// such an Equip Card on that host, is face-down, or its effects are negated — stored or turn negation (Armored
        /// Glass), The Emperor's Holiday, or a source that negates Spell effects on the field (Imperial Order).
        /// </summary>
        public static int GrantedAtkOf(DuelEngine engine, CardInstance eq, CardInstance host)
        {
            if (engine == null || eq == null || !eq.IsEquipSpellWithGrantedEffectOnly || eq.EquippedTo != host) return 0;
            if (!eq.FaceUp || GrantedEffectNegated(engine, eq)) return 0;
            return eq.GrantedEquipAtk;
        }

        /// <summary>The granted equip effect of <paramref name="eq"/> is negated (see <see cref="GrantedAtkOf"/>).</summary>
        public static bool GrantedEffectNegated(DuelEngine engine, CardInstance eq) =>
            eq.IsEffectNegatedAsCard || TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, eq) ||
            ContinuousProtections.NegateAllEquipCardEffectsOnField(engine) ||
            ContinuousNegationRuntime.SpellEffectsOnFieldNegatedBy(engine, eq) != null;
    }
}
