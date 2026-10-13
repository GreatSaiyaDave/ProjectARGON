using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Coverage family F1 (v180, AttributeBullets): the Element monsters' conditional bullet effects —
    /// "This monster gets the following effect(s) while there is a monster(s) with the following Attribute(s) on the
    /// field: ● FIRE: This card gains 500 ATK. ● WIND: If this card destroyed your opponent's monster by battle, it can
    /// attack once again in a row." The header is a whole-card construct (not one sentence among others), so the
    /// compiler recognises the block as a unit (<see cref="TryParse"/>, called from CompileOrdered where the old
    /// fail-closed reject used to sit) instead of a per-sentence atom. Each bullet compiles to the same continuous
    /// clause its standalone text would, stamped with <see cref="EffectClause.RequiresFieldAttribute"/> = the bullet's
    /// Attribute; <see cref="AttributeBulletsRuntime"/> honors the clause only while such a monster is face-up on the
    /// field. If any bullet body is not one of the four printed effects the whole program fails closed (the whole text
    /// stays unparsed) rather than emitting an unconditional effect.
    /// </summary>
    public static class AttributeBulletsAtoms
    {
        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        /// <summary>The opening clause of the block (Element Dragon / Saurus / Valkyrie / Doom / Magician / Soldier).</summary>
        static readonly Regex Header = new(
            @"(?:gets|gains) the following effect\(s\) while there (?:is|are) (?:a )?monster\(s\) " +
            @"with the following Attribute\(s\) on the field",
            Opt);

        /// <summary>One "● ATTR: body" bullet; the body is non-greedy up to the next bullet or the end of the text.</summary>
        static readonly Regex Bullet = new(
            @"●\s*([A-Za-z]+)\s*:\s*(.*?)(?=\s*●|\s*$)",
            Opt | RegexOptions.Singleline);

        static readonly Regex BulletAtk = new(@"^This card gains (\d+) ATK\.?$", Opt);
        static readonly Regex BulletControlLock = new(@"^Control of this card cannot switch\.?$", Opt);
        static readonly Regex BulletExtraAttack = new(
            @"^If this card destroyed your opponent['’]s monster by battle, it can attack once again in a row\.?$", Opt);
        static readonly Regex BulletNegateBattleDestroyed = new(
            @"^Negate the effects? of (?:an )?Effect Monsters? " +
            @"(?:destroyed by battle with this card|that this card destroyed by battle)\.?$", Opt);

        static readonly HashSet<string> Attributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE"
        };

        /// <summary>
        /// Parse the whole conditional-Attribute block. Returns one clause per bullet, or null when the text is not
        /// this block or any bullet body is unrecognised (the caller then fails the whole program closed).
        /// </summary>
        public static List<EffectClause> TryParse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (!Header.IsMatch(text)) return null;

            var list = new List<EffectClause>();
            foreach (Match b in Bullet.Matches(text))
            {
                var attr = b.Groups[1].Value.Trim().ToUpperInvariant();
                if (!Attributes.Contains(attr)) return null;
                var body = Regex.Replace(b.Groups[2].Value.Trim(), @"\s+", " ");
                var clause = ParseBullet(attr, body);
                if (clause == null) return null;
                clause.SourceSnippet = b.Value.Trim();
                list.Add(clause);
            }

            return list.Count > 0 ? list : null;
        }

        static EffectClause ParseBullet(string attr, string body)
        {
            // ● FIRE: This card gains 500 ATK.   (self aura; the value is read live by FieldSpellEffects.ApplyStatClause)
            var atk = BulletAtk.Match(body);
            if (atk.Success)
                return Continuous(attr, EffectActionKind.ContinuousGainAtkDef, c =>
                {
                    c.Amount = int.TryParse(atk.Groups[1].Value, out var n) ? n : 0;
                    c.DefAmount = 0;
                    c.AffectsThisCardOnly = true;
                });

            // ● WATER: Control of this card cannot switch.
            if (BulletControlLock.IsMatch(body))
                return Continuous(attr, EffectActionKind.ControlOfThisCannotSwitch, null);

            // ● WIND: If this card destroyed your opponent's monster by battle, it can attack once again in a row.
            if (BulletExtraAttack.IsMatch(body))
                return Continuous(attr, EffectActionKind.ExtraAttacks, c =>
                {
                    c.Amount = 1;
                    c.ActivationCondition = AttributeBulletsRuntime.DestroyedMonsterByBattleThisTurn;
                });

            // ● EARTH: Negate the effects of Effect Monsters destroyed by battle with this card.
            if (BulletNegateBattleDestroyed.IsMatch(body))
                return Continuous(attr, EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle, c =>
                {
                    // The negator IS the battling monster ("with this card" / "that this card destroyed by battle").
                    c.AffectsThisCardOnly = true;
                    c.TargetCardKind = "Effect";
                });

            return null;
        }

        static EffectClause Continuous(string attr, EffectActionKind action, Action<EffectClause> tweak)
        {
            var c = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = action,
                Side = EffectSide.Controller,
                RequiresFieldAttribute = attr,
                StaysOnField = true,
                MakesChainLink = false
            };
            tweak?.Invoke(c);
            return c;
        }
    }
}
