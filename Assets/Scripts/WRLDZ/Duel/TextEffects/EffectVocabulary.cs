using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Finite effect kinds. Yu-Gi-Oh has thousands of cards and a few dozen
    /// timings / costs / resolutions. New cards must map onto this vocabulary.
    /// A unique card (Cyber Jar, Time Wizard's wrong-call, Ring of Destruction)
    /// is an <see cref="EffectResolutionKind.UniqueException"/> — one named
    /// action, never a silent approximation, never <c>if (cardId == …)</c>
    /// unless no other card shares the mechanic.
    /// </summary>
    public enum EffectCostKind
    {
        None = 0,
        Discard,
        Tribute,
        SendToGy,
        PayLp,
        BanishFromGy,
        RemoveCounters
    }

    public enum EffectResolutionKind
    {
        None = 0,
        Destroy,
        Banish,
        ReturnToHand,
        Draw,
        Search,
        SpecialSummon,
        Damage,
        GainLp,
        ChangePosition,
        ModifyStats,
        DirectAttack,
        ExtraAttack,
        Token,
        Equip,
        TakeControl,
        PlaceCounters,
        NegateAttack,
        PreventDamage,
        TreatAsName,
        /// <summary>Coin or die then a vocabulary resolution (Barrel: toss then Destroy).</summary>
        Randomize,
        /// <summary>Unaffected / cannot-be-attacked / cannot-be-targeted (Fisherman family).</summary>
        Protection,
        /// <summary>One-card or tiny-family mechanic. Handle as an exception, not a new kind.</summary>
        UniqueException
    }

    public static class EffectVocabulary
    {
        /// <summary>Shared resolution kinds the runtime already executes.</summary>
        public static readonly EffectResolutionKind[] SharedResolutions =
        {
            EffectResolutionKind.Destroy,
            EffectResolutionKind.Banish,
            EffectResolutionKind.ReturnToHand,
            EffectResolutionKind.Draw,
            EffectResolutionKind.Search,
            EffectResolutionKind.SpecialSummon,
            EffectResolutionKind.Damage,
            EffectResolutionKind.GainLp,
            EffectResolutionKind.ChangePosition,
            EffectResolutionKind.ModifyStats,
            EffectResolutionKind.DirectAttack,
            EffectResolutionKind.ExtraAttack,
            EffectResolutionKind.Token,
            EffectResolutionKind.Equip,
            EffectResolutionKind.TakeControl,
            EffectResolutionKind.PlaceCounters,
            EffectResolutionKind.NegateAttack,
            EffectResolutionKind.PreventDamage,
            EffectResolutionKind.TreatAsName,
            EffectResolutionKind.Randomize,
            EffectResolutionKind.Protection
        };

        /// <summary>
        /// Actions that are unique (or a 1–3 card family). Adding a 4th card
        /// with the same text means promote it to a shared kind instead.
        /// </summary>
        public static bool IsUniqueException(EffectActionKind action) =>
            action == EffectActionKind.CyberJarStyle ||
            action == EffectActionKind.ApplySwordsOfRevealingLight ||
            action == EffectActionKind.BothPlayersDiscardAndRedraw ||
            action == EffectActionKind.FusionSummonRegistered ||
            action == EffectActionKind.EffectDamageBothFromOriginalAtk ||
            action == EffectActionKind.DiscardSelfNoBattleDamageThisBattle ||
            action == EffectActionKind.CoinCallDestroyOppOrSelf ||
            action == EffectActionKind.CoinCallDoubleOrHalveAtk ||
            action == EffectActionKind.RollDieZorc ||
            action == EffectActionKind.ApplyWabokuStyle ||
            action == EffectActionKind.SkipOpponentNextDrawPhase;

        public static EffectCostKind CostOf(EffectClause c)
        {
            if (c == null) return EffectCostKind.None;
            if (c.RequiresDiscardCost || c.RequiresDiscardSelf)
                return EffectCostKind.Discard;
            if (c.RequiresTributeThis || c.RequiresTributeCount > 0)
                return EffectCostKind.Tribute;
            if (c.RequiresSendThisToGy || c.RequiresSendNamedToGy ||
                c.RequiresSendHandToGy || c.RequiresSendOtherYouControl)
                return EffectCostKind.SendToGy;
            if (c.PayLpAmount > 0 || c.RequiresLpCostMultiple > 0)
                return EffectCostKind.PayLp;
            if (c.BanishFromGyCount > 0)
                return EffectCostKind.BanishFromGy;
            if (c.RequiresRemoveSpellCounters > 0 || c.RequiresSpellCounters > 0)
                return EffectCostKind.RemoveCounters;
            return EffectCostKind.None;
        }

        public static EffectResolutionKind ResolutionOf(EffectClause c)
        {
            if (c == null) return EffectResolutionKind.None;
            if (IsUniqueException(c.Action))
                return EffectResolutionKind.UniqueException;
            return c.Action switch
            {
                EffectActionKind.Destroy or
                    EffectActionKind.DestroySpecialSummonedMonsters or
                    EffectActionKind.DestroyOppMonstersAtkLeq or
                    EffectActionKind.SelfDestroyUnlessNamedFaceUp =>
                    EffectResolutionKind.Destroy,
                EffectActionKind.Banish => EffectResolutionKind.Banish,
                EffectActionKind.ReturnToHand => EffectResolutionKind.ReturnToHand,
                EffectActionKind.Draw => EffectResolutionKind.Draw,
                EffectActionKind.AddFromGyToHand or
                    EffectActionKind.AddFromDeckToHand or
                    EffectActionKind.AddNamedFromDeckToHand =>
                    EffectResolutionKind.Search,
                EffectActionKind.SpecialSummonFromGy or
                    EffectActionKind.SpecialSummonFromHand or
                    EffectActionKind.SpecialSummonNamed or
                    EffectActionKind.SpecialSummonThisFromHand or
                    EffectActionKind.SpecialSummonFusionFromExtra =>
                    EffectResolutionKind.SpecialSummon,
                EffectActionKind.SpecialSummonToken => EffectResolutionKind.Token,
                EffectActionKind.TakeEffectDamage or
                    EffectActionKind.InflictDamageToOpponent or
                    EffectActionKind.InflictDamageHalfTributedAtk or
                    EffectActionKind.InflictDamageEqualToAtk =>
                    EffectResolutionKind.Damage,
                EffectActionKind.GainLifePoints or
                    EffectActionKind.GainLpEqualToAtk =>
                    EffectResolutionKind.GainLp,
                EffectActionKind.ChangeBattlePosition or
                    EffectActionKind.SetThisFaceDownDefense or
                    EffectActionKind.ChangeThisBattlePosition =>
                    EffectResolutionKind.ChangePosition,
                EffectActionKind.ContinuousGainAtkDef or
                    EffectActionKind.ContinuousReduceLevel or
                    EffectActionKind.LoseAtkDefUntilEndOfTurn or
                    EffectActionKind.GainThisAtkUntilEnd or
                    EffectActionKind.GainAtkPerSpellCounter or
                    EffectActionKind.SetAttackingMonsterAtkToZeroThisCalc =>
                    EffectResolutionKind.ModifyStats,
                EffectActionKind.CanAttackDirectly or
                    EffectActionKind.GrantDirectAttackThisTurn or
                    EffectActionKind.ForceOpponentDirectAttacksThisTurn =>
                    EffectResolutionKind.DirectAttack,
                EffectActionKind.ExtraAttacks => EffectResolutionKind.ExtraAttack,
                EffectActionKind.EquipThisToTarget or
                    EffectActionKind.UnequipThisSpecialSummon or
                    EffectActionKind.EquipTargetToThis =>
                    EffectResolutionKind.Equip,
                EffectActionKind.TakeControlLevelLeq => EffectResolutionKind.TakeControl,
                EffectActionKind.PlaceSpellCounters => EffectResolutionKind.PlaceCounters,
                EffectActionKind.NegateAttack or
                    EffectActionKind.NegateThisAttack =>
                    EffectResolutionKind.NegateAttack,
                EffectActionKind.PreventControllerBattleDamage =>
                    EffectResolutionKind.PreventDamage,
                EffectActionKind.AlwaysTreatedAsName or
                    EffectActionKind.FieldTreatedAsName =>
                    EffectResolutionKind.TreatAsName,
                EffectActionKind.CoinTossNDestroyIfHeads =>
                    EffectResolutionKind.Randomize,
                EffectActionKind.CannotBeAttackTarget or
                    EffectActionKind.UnaffectedByCardEffects or
                    EffectActionKind.CannotBeTargetedByEffects or
                    EffectActionKind.ContinuousCannotTargetDragons =>
                    EffectResolutionKind.Protection,
                EffectActionKind.SetTargetFaceDownDefense =>
                    EffectResolutionKind.ChangePosition,
                EffectActionKind.None => EffectResolutionKind.None,
                _ => EffectResolutionKind.UniqueException
            };
        }

        public static string Describe(EffectClause c)
        {
            if (c == null) return "none";
            var cost = CostOf(c);
            var res = ResolutionOf(c);
            var costBit = cost == EffectCostKind.None ? "" : cost + " → ";
            return $"{c.Timing}: {costBit}{res}";
        }

        public static bool IsSharedKind(EffectClause c)
        {
            if (c == null || c.Action == EffectActionKind.None)
                return false;
            var r = ResolutionOf(c);
            return r != EffectResolutionKind.None && r != EffectResolutionKind.UniqueException;
        }

        /// <summary>
        /// How to grow the engine when a new card shows up.
        /// </summary>
        public static string NewCardRule =>
            "1) Name the timing, cost, and resolution from EffectVocabulary. " +
            "2) If all three exist, compile a PSCT fragment — do not add an Action. " +
            "3) If a kind is missing and 2+ cards need it, add one shared kind + a test. " +
            "4) If the mechanic is unique, add UniqueException (fail loud). " +
            "Never if (cardId == …) unless no other card shares it.";

        public static List<string> KindsUsed(CompiledCardProgram prog)
        {
            var list = new List<string>();
            if (prog == null) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                var d = Describe(c);
                if (seen.Add(d)) list.Add(d);
            }

            return list;
        }
    }
}
