using UnityEngine;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Official battle damage calculation (Konami Rulebook / Yugipedia "Damage calculation").
    ///
    /// ATK vs ATK (both face-up Attack):
    ///   higher ATK destroys lower; damage = difference to controller of destroyed;
    ///   equal ATK → both destroyed, 0 damage.
    ///
    /// ATK vs DEF (defender in Defense Position — including face-down monsters):
    ///   ATK &gt; DEF → destroy defender, 0 battle damage (unless piercing).
    ///   ATK &lt; DEF → defender survives; attacker takes (DEF − ATK) battle damage.
    ///   ATK = DEF → nothing destroyed, 0 damage.
    ///
    /// Face-down monsters are always treated as Defense Position (Rulebook).
    /// </summary>
    public static class BattleMechanics
    {
        public struct BattleResult
        {
            public int DamageToAttackingPlayer;
            public int DamageToDefendingPlayer;
            public bool DestroyAttacker;
            public bool DestroyDefender;
            public bool ReplayRequired;
            public string LogLine;
            public bool PiercingApplied;
            public bool UsedDefenseStat;
            public int AttackerAtk;
            public int DefenderStat;
        }

        /// <summary>
        /// Official: a face-down monster is in Defense Position for battle.
        /// Face-up uses its current battle position.
        /// </summary>
        public static bool UsesDefenseStat(CardInstance defender)
        {
            if (defender == null) return false;
            // Face-down monsters cannot be in ATK (Rulebook / modern TCG)
            if (!defender.FaceUp) return true;
            return defender.Position == BattlePosition.Defense;
        }

        /// <summary>
        /// Canonical printed DEF (with modifiers). Never fall back to ATK for defense battles.
        /// </summary>
        public static int DefenseValue(CardInstance defender)
        {
            if (defender?.Def == null) return 0;
            // Printed DEF may be 0; only treat "N/A" as non-stat when def &lt; 0 in DB
            if (defender.Def.def < 0) return 0;
            return Mathf.Max(0, defender.Def.def + defender.DefModifier + defender.UntilEndOfTurnDef);
        }

        public static int AttackValue(CardInstance monster)
        {
            if (monster?.Def == null) return 0;
            if (monster.AtkBecomesZeroThisCalculation) return 0;
            if (monster.Def.atk < 0) return 0;
            return Mathf.Max(0, monster.Def.atk + monster.AtkModifier + monster.UntilEndOfTurnAtk);
        }

        public static BattleResult Calculate(
            CardInstance attacker,
            CardInstance defender,
            bool attackerHasPiercing,
            bool attackerCannotBeDestroyedByBattle,
            bool defenderCannotBeDestroyedByBattle,
            bool noBattleDamageToAttacker,
            bool noBattleDamageToDefender)
        {
            var r = new BattleResult();
            if (attacker == null)
            {
                r.LogLine = "No attacker.";
                return r;
            }

            var atk = AttackValue(attacker);
            r.AttackerAtk = atk;

            // Direct attack
            if (defender == null)
            {
                var dmg = noBattleDamageToDefender ? 0 : atk;
                r.DamageToDefendingPlayer = dmg;
                r.LogLine = $"Direct attack: {atk} ATK → {dmg} damage.";
                return r;
            }

            // ── Defense Position battle (incl. face-down) ──
            if (UsesDefenseStat(defender))
            {
                var def = DefenseValue(defender);
                r.UsedDefenseStat = true;
                r.DefenderStat = def;
                var posLabel = defender.FaceUp ? "DEF" : "face-down DEF";
                r.LogLine = $"{attacker.Name} {atk} ATK vs {defender.Name} {def} {posLabel}.";

                if (atk > def)
                {
                    // Destroy defender only — NO battle damage unless piercing
                    r.DestroyDefender = !defenderCannotBeDestroyedByBattle;
                    if (attackerHasPiercing && !noBattleDamageToDefender)
                    {
                        r.DamageToDefendingPlayer = atk - def;
                        r.PiercingApplied = true;
                        r.LogLine += $" Destroy defender. Piercing {r.DamageToDefendingPlayer}.";
                    }
                    else
                        r.LogLine += r.DestroyDefender
                            ? " Destroy defender. No battle damage."
                            : " Would destroy (protected). No battle damage.";
                }
                else if (atk < def)
                {
                    // Defender survives. Attacker takes battle damage. NOTHING is destroyed by battle.
                    r.DestroyDefender = false;
                    r.DestroyAttacker = false;
                    r.DamageToAttackingPlayer = noBattleDamageToAttacker ? 0 : def - atk;
                    r.LogLine +=
                        $" Defense holds. Attacker takes {r.DamageToAttackingPlayer} damage. No destruction.";
                }
                else
                {
                    // ATK = DEF: neither destroyed, no damage
                    r.DestroyDefender = false;
                    r.DestroyAttacker = false;
                    r.LogLine += " ATK = DEF. Nothing destroyed. No damage.";
                }

                return r;
            }

            // ── Attack Position battle (face-up ATK only) ──
            var defAtk = AttackValue(defender);
            r.UsedDefenseStat = false;
            r.DefenderStat = defAtk;
            r.LogLine = $"{attacker.Name} {atk} ATK vs {defender.Name} {defAtk} ATK.";

            if (atk > defAtk)
            {
                r.DestroyDefender = !defenderCannotBeDestroyedByBattle;
                r.DamageToDefendingPlayer = noBattleDamageToDefender ? 0 : atk - defAtk;
                r.LogLine += $" Destroy defender. {r.DamageToDefendingPlayer} damage.";
            }
            else if (atk < defAtk)
            {
                r.DestroyAttacker = !attackerCannotBeDestroyedByBattle;
                r.DamageToAttackingPlayer = noBattleDamageToAttacker ? 0 : defAtk - atk;
                r.LogLine += $" Destroy attacker. {r.DamageToAttackingPlayer} damage.";
            }
            else
            {
                r.DestroyAttacker = !attackerCannotBeDestroyedByBattle;
                r.DestroyDefender = !defenderCannotBeDestroyedByBattle;
                r.LogLine += " Equal ATK. Both destroyed. No damage.";
            }

            return r;
        }

        /// <summary>
        /// Safety for Defense Position battles (incl. face-down):
        /// · ATK &lt; DEF → nothing destroyed; attacker takes (DEF−ATK) unless prevented
        /// · ATK = DEF → nothing destroyed, 0 damage
        /// · ATK &gt; DEF → may destroy defender; 0 battle damage unless piercing
        /// </summary>
        public static void Sanitize(ref BattleResult r, CardInstance attacker, CardInstance defender,
            bool noBattleDamageToAttacker = false, bool noBattleDamageToDefender = false)
        {
            if (defender == null || attacker == null) return;
            if (!UsesDefenseStat(defender)) return;

            var atk = AttackValue(attacker);
            var def = DefenseValue(defender);
            r.UsedDefenseStat = true;
            r.AttackerAtk = atk;
            r.DefenderStat = def;

            if (atk < def)
            {
                // Official: defender survives; attacker takes DEF − ATK; nothing destroyed by battle
                if (r.DestroyDefender || r.DestroyAttacker)
                {
                    r.DestroyDefender = false;
                    r.DestroyAttacker = false;
                    r.LogLine += " [RULE FIX: ATK<DEF — no battle destruction]";
                }

                r.PiercingApplied = false;
                r.DamageToDefendingPlayer = 0;
                var expect = noBattleDamageToAttacker ? 0 : def - atk;
                if (r.DamageToAttackingPlayer != expect)
                {
                    r.DamageToAttackingPlayer = expect;
                    r.LogLine +=
                        $" [RULE FIX: Defense holds → attacker takes {expect}]";
                }
            }
            else if (atk == def)
            {
                r.DestroyDefender = false;
                r.DestroyAttacker = false;
                r.DamageToAttackingPlayer = 0;
                r.DamageToDefendingPlayer = 0;
                r.PiercingApplied = false;
            }
            else
            {
                // ATK > DEF: no battle damage to defender without piercing
                r.DestroyAttacker = false;
                if (!r.PiercingApplied)
                {
                    if (r.DamageToDefendingPlayer != 0 && !noBattleDamageToDefender)
                    {
                        r.DamageToDefendingPlayer = 0;
                        r.LogLine += " [RULE FIX: ATK>DEF no battle damage without piercing]";
                    }
                    else if (noBattleDamageToDefender)
                        r.DamageToDefendingPlayer = 0;
                }
                else if (noBattleDamageToDefender)
                    r.DamageToDefendingPlayer = 0;

                // Never leak "attacker damage" on a successful DEF crush
                r.DamageToAttackingPlayer = 0;
            }
        }

        public static bool NeedsReplay(
            CardInstance attacker,
            CardInstance originalTarget,
            DuelistState defendingPlayer,
            bool hadMonstersAtDeclaration)
        {
            if (attacker == null || defendingPlayer == null) return false;
            if (originalTarget == null)
            {
                // Direct: replay only if a monster appeared after an empty-field declaration.
                // MK-3 may declare direct while a Set is already present — that is not a replay.
                return defendingPlayer.MonsterCount > 0 && !hadMonstersAtDeclaration;
            }
            if (!defendingPlayer.TryFindMonster(originalTarget, out _))
                return true;
            return false;
        }
    }
}
