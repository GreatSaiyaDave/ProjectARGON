using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared sentence shapes, not a passcode list. Each atom is the card's whole text.
    /// <list type="bullet">
    /// <item>Activate 1 of these effects; ● Gain N LP. ● Inflict M damage to your opponent.</item>
    /// <item>If your opponent's Life Points are higher than N: Inflict M damage to your opponent.</item>
    /// <item>Equip only to a Type monster. It gains N ATK. If it inflicts battle damage to your opponent: Gain LP equal to the damage inflicted.</item>
    /// <item>If you control a Type monster: Target 1 Spell/Trap on the field; destroy that target, and if you do, inflict N damage to its controller.</item>
    /// <item>Activate this card by choosing ATK or DEF; equip only to a Type monster. It gains N ATK or DEF, depending on the choice.</item>
    /// </list>
    /// Deliberately does not match "you can activate 1 of these effects", LP "or less" / "at least",
    /// "face-up" control, damage "to your opponent" after a destroy, any-monster battle damage, or an
    /// equip that always grants both ATK and DEF.
    /// </summary>
    public static class PoisonMeteorCestusAtoms
    {
        const string TypeWord = @"([A-Za-z]+(?:-(?!Type\b)[A-Za-z]+)*)(?:-Type)?";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Two untargeted bullets. Mandatory: there is no "you can".
            AtomRegistration.Atom.Sequence(
                @"^\s*Activate 1 of these effects;\s*●\s*Gain (\d+) LP\.\s*●\s*Inflict (\d+) damage to your opponent\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.GainLifePoints,
                        Side = EffectSide.Controller,
                        Amount = Pos(m, 1),
                        EffectChoiceIndex = 1,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = Pos(m, 2),
                        EffectChoiceIndex = 2,
                        MakesChainLink = true
                    }
                }),

            // "higher than N" is strict (>), checked when the card is activated.
            new AtomRegistration.Atom(
                @"^\s*If your opponent's Life Points are higher than (\d+):\s*Inflict (\d+) damage to your opponent\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Pos(m, 2),
                    ActivationCondition = PoisonMeteorCestusRuntime.OppLpGreaterThan,
                    ConditionAmount = Pos(m, 1),
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            AtomRegistration.Atom.Sequence(
                @"^\s*Equip only to an? " + TypeWord + @" monster\.\s*It gains (\d+) ATK\.\s*" +
                @"If it inflicts battle damage to your opponent:\s*Gain LP equal to the damage inflicted\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        Zone = EffectZoneFilter.ControllerMonsters,
                        RaceFilter = m.Groups[1].Value.Trim(),
                        EquipAtkBonus = Pos(m, 2),
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.YourMonsterInflictsBattleDamage,
                        Action = EffectActionKind.GainLifePoints,
                        Side = EffectSide.Controller,
                        AmountIsEquippedBattleDamage = true,
                        OnlyEquippedMonster = true
                    }
                }),

            new AtomRegistration.Atom(
                @"^\s*If you control an? " + TypeWord + @" monster:\s*Target 1 Spell/?Trap on the field;\s*" +
                @"destroy that target, and if you do, inflict (\d+) damage to its controller\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    // The Type is the activation condition, not a target filter: the target is a Spell/Trap.
                    ConditionName = m.Groups[1].Value.Trim(),
                    ActivationCondition = PoisonMeteorCestusRuntime.YouControlRace,
                    CheckedAt = ConditionCheckedAt.Activation,
                    DamageItsController = Pos(m, 2),
                    MakesChainLink = true
                }),

            new AtomRegistration.Atom(
                @"^\s*Activate this card by choosing ATK or DEF;\s*equip only to an? " + TypeWord +
                @" monster\.\s*It gains (\d+) ATK or DEF, depending on the choice\.?\s*$",
                m =>
                {
                    var n = Pos(m, 2);
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        Zone = EffectZoneFilter.ControllerMonsters,
                        RaceFilter = m.Groups[1].Value.Trim(),
                        EquipAtkBonus = n,
                        EquipDefBonus = n,
                        ChooseAtkOrDef = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    };
                }),
        };

        static int Pos(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;
    }

    /// <summary>
    /// Runtime for those sentence shapes. Choices use the existing card picker (one token per option).
    /// The AI takes the damage bullet, and ATK for an ATK-or-DEF equip.
    /// </summary>
    public static class PoisonMeteorCestusRuntime
    {
        public const string OppLpGreaterThan = "OppLpGreaterThan";
        public const string YouControlRace = "YouControlRace";

        const string BulletKind = "Effect choice";
        const string StatKind = "Stat choice";

        sealed class Remembered
        {
            public int Bullet;
            public string Stat;
        }

        static readonly ConditionalWeakTable<CardInstance, Remembered> s_memory = new();

        public static bool HasBulletChoice(IEnumerable<EffectClause> activate)
        {
            if (activate == null) return false;
            foreach (var c in activate)
                if (c != null && c.EffectChoiceIndex > 0 && c.Timing == EffectTiming.Activate &&
                    // ChoiceAmongEffects batch: an "Apply 1 of these effects" bullet is chosen as the card resolves.
                    !c.EffectChoiceAppliedAtResolution)
                    return true;
            return false;
        }

        public static bool HasChosenBullet(CardInstance card) =>
            card != null && s_memory.TryGetValue(card, out var r) && r.Bullet > 0;

        public static int ChosenBullet(CardInstance card) =>
            card != null && s_memory.TryGetValue(card, out var r) ? r.Bullet : 0;

        /// <summary>The damage bullet when there is one, otherwise the first bullet.</summary>
        public static int PreferredBullet(IEnumerable<EffectClause> activate)
        {
            var first = 0;
            if (activate == null) return 0;
            foreach (var c in activate)
            {
                if (c == null || c.EffectChoiceIndex <= 0) continue;
                if (first == 0) first = c.EffectChoiceIndex;
                if (c.Action == EffectActionKind.InflictDamageToOpponent)
                    return c.EffectChoiceIndex;
            }

            return first;
        }

        public static void RememberBullet(CardInstance card, int index)
        {
            if (card == null || index <= 0) return;
            var r = s_memory.GetOrCreateValue(card);
            r.Bullet = index;
        }

        public static void ForgetBullet(CardInstance card)
        {
            if (card != null && s_memory.TryGetValue(card, out var r))
                r.Bullet = 0;
        }

        public static bool NeedsStatChoice(IEnumerable<EffectClause> activate, CardInstance card)
        {
            if (card != null && s_memory.TryGetValue(card, out var r) && !string.IsNullOrEmpty(r.Stat))
                return false;
            if (activate == null) return false;
            foreach (var c in activate)
                if (c != null && c.ChooseAtkOrDef) return true;
            return false;
        }

        public static string ChosenStat(CardInstance card) =>
            card != null && s_memory.TryGetValue(card, out var r) ? r.Stat : null;

        public static void RememberStat(CardInstance card, string stat)
        {
            if (card == null || string.IsNullOrEmpty(stat)) return;
            var r = s_memory.GetOrCreateValue(card);
            r.Stat = stat;
        }

        public static void ClearStatIfNotEquipped(CardInstance card)
        {
            if (card == null || card.EquippedTo != null) return;
            if (s_memory.TryGetValue(card, out var r))
                r.Stat = null;
        }

        /// <summary>
        /// Chain-seated untargeted spell: the player picks one bullet before the card is placed.
        /// Returns true when a human is now choosing. The AI's pick is remembered and the caller continues.
        /// </summary>
        public static bool OfferChainBullet(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, bool autoPick)
        {
            if (prog == null || !HasBulletChoice(prog.ClausesFor(EffectTiming.Activate))) return false;
            // A bullet left from an earlier activation (a negated one) is not this activation's choice.
            ForgetBullet(card);
            if (autoPick || who == null || !who.IsPlayer)
            {
                RememberBullet(card, PreferredBullet(prog.ClausesFor(EffectTiming.Activate)));
                return false;
            }

            var pending = BulletPending(engine, who, card, prog);
            pending.ChainActivation = true;
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// A response seated on the chain (<see cref="TextEffectRuntime.SeatResponseOnChain"/>: a Set Poison of the Old Man
        /// answering an attack) picks its bullet as it is activated, like <see cref="OfferChainBullet"/>. The card is
        /// already placed on its Chain Link. Returns true when a human is now choosing; the AI's pick is remembered.
        /// </summary>
        public static bool OfferSeatedBullet(DuelEngine engine, DuelistState who, CardInstance card,
            IEnumerable<EffectClause> pickClauses, bool human, bool fromHand, ResponseTiming timing,
            CardInstance summoned, int paidLp)
        {
            if (!HasBulletChoice(pickClauses)) return false;
            ForgetBullet(card);
            if (!human)
            {
                RememberBullet(card, PreferredBullet(pickClauses));
                return false;
            }

            var pending = BulletPending(engine, who, card, pickClauses);
            pending.FromHand = fromHand;
            pending.WasSetOnField = !fromHand;
            pending.ChainActivation = true;
            pending.SeatTiming = timing;
            pending.SeatSummoned = summoned;
            pending.PaidLpCost = paidLp;
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>A seated resolution applies only the chosen bullet (no bullet when none was chosen).</summary>
        public static bool SkipsBullet(CardInstance card, EffectClause c) =>
            c != null && c.EffectChoiceIndex > 0 && c.Timing == EffectTiming.Activate &&
            c.EffectChoiceIndex != ChosenBullet(card);

        /// <summary>Inline activation (no chain seat): remember the pick, then the caller resolves that bullet.</summary>
        public static bool OfferInlineBullet(DuelEngine engine, DuelistState who, CardInstance card,
            IList<EffectClause> activate, bool fromHand, bool autoPick)
        {
            if (!HasBulletChoice(activate) || HasChosenBullet(card)) return false;
            if (autoPick || who == null || !who.IsPlayer)
            {
                RememberBullet(card, PreferredBullet(activate));
                return false;
            }

            var pending = BulletPending(engine, who, card, activate);
            pending.FromHand = fromHand;
            pending.UsesTextProgram = true;
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>ATK or DEF, before the equip's monster is chosen. AI takes ATK.</summary>
        public static bool OfferStat(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick)
        {
            if (autoPick || who == null || !who.IsPlayer)
            {
                RememberStat(card, "ATK");
                return false;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = false,
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                PromptOverride = "choose ATK or DEF."
            };
            pending.LegalTargets.Add(Token(StatKind, "ATK"));
            pending.LegalTargets.Add(Token(StatKind, "DEF"));
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// A picker token for one of these choices. False when <paramref name="target"/> is an ordinary card.
        /// A stat token continues the equip's monster pick. An inline bullet token resolves that bullet.
        /// A chain bullet token is finished by <see cref="SpellTrapEffects"/>, not here.
        /// </summary>
        public static bool TryFinishInline(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (engine == null || p == null || target?.Def == null) return false;
            if (target.Def.type == StatKind)
            {
                RememberStat(p.Card, target.Def.name);
                var who = p.Controller;
                var card = p.Card;
                var fromHand = p.FromHand;
                engine.ClearPendingActivation();
                var prog = CompiledEffectCache.GetOrCompile(card?.Def);
                return TextEffectRuntime.TryResolveActivation(engine, who, card, fromHand, prog, false);
            }

            if (target.Def.type == BulletKind && p.UsesTextProgram && !p.ChainActivation)
            {
                RememberBullet(p.Card, target.Def.level);
                var who = p.Controller;
                var card = p.Card;
                var fromHand = p.FromHand;
                engine.ClearPendingActivation();
                var prog = CompiledEffectCache.GetOrCompile(card?.Def);
                return TextEffectRuntime.TryResolveActivation(engine, who, card, fromHand, prog, false);
            }

            return false;
        }

        public static bool IsChainBulletToken(CardInstance target) =>
            target?.Def != null && target.CardId == 0 && target.Def.type == BulletKind;

        public static int BulletIndexOf(CardInstance token) =>
            token?.Def != null && token.Def.type == BulletKind ? token.Def.level : 0;

        static bool NeedsBulletOffer(CompiledCardProgram prog, CardInstance card)
        {
            if (prog == null || HasChosenBullet(card)) return false;
            return HasBulletChoice(prog.ClausesFor(EffectTiming.Activate));
        }

        static PendingActivation BulletPending(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog) =>
            BulletPending(engine, who, card, prog?.ClausesFor(EffectTiming.Activate));

        static PendingActivation BulletPending(DuelEngine engine, DuelistState who, CardInstance card,
            IEnumerable<EffectClause> activate)
        {
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                TargetPicksRemaining = 1,
                PromptOverride = "activate 1 of these effects."
            };
            if (activate == null) return pending;
            foreach (var c in activate)
            {
                if (c == null || c.EffectChoiceIndex <= 0) continue;
                pending.LegalTargets.Add(Token(BulletKind, Describe(c), c.EffectChoiceIndex));
            }

            return pending;
        }

        static string Describe(EffectClause c)
        {
            if (c == null) return "that effect";
            if (c.Action == EffectActionKind.GainLifePoints)
                return $"Gain {Math.Max(0, c.Amount)} LP";
            if (c.Action == EffectActionKind.InflictDamageToOpponent)
                return $"Inflict {Math.Max(0, c.Amount)} damage";
            // SimpleSpellTraps batch: Dragon's Gunfire's targeted bullet.
            if (c.Action == EffectActionKind.DestroyTargetWithDefAtMost)
                return $"Destroy 1 monster with {Math.Max(0, c.Amount)} or less DEF";
            // PgdSpellTrapBasics batch (v162): Pyramid Energy's ATK / DEF options.
            if (c.Action == EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase)
                return PgdSpellTrapBasicsRuntime.DescribeStatBullet(c);
            // ChoiceAmongEffects batch: Penalty Game!'s locks (and the mass-destroy / draw bullets).
            return ChoiceAmongEffectsRuntime.DescribeBullet(c) ?? "that effect";
        }

        static CardInstance Token(string kind, string name, int index = 0) => new CardInstance
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = name,
                type = kind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = index,
                race = "",
                attribute = "",
                archetype = ""
            }
        };
    }
}
