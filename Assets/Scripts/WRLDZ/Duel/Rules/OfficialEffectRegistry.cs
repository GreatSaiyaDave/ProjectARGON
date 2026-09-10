using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Registry of effect/summon scripts that are implemented from <b>official card text</b>
    /// (card DB desc = Konami text). If a card ID is not registered, the engine must NOT
    /// invent a resolution — activation/summon procedure is rejected with a clear reason.
    ///
    /// Source of truth for text: <see cref="OfficialCardAuthority"/>.
    /// Rulings reference: Yugipedia card pages + Rulebook.
    ///
    /// Starter/demo IDs match historical Konami text for those printings in cards_db.json.
    /// </summary>
    public static class OfficialEffectRegistry
    {
        // ── Known official card IDs with full structural scripts ──
        public const int PotOfGreed = 55144522;
        public const int DarkHole = 53129443;
        public const int Raigeki = 12580477;
        public const int HeavyStorm = 19613556;
        public const int Mst = 5318639;
        public const int MonsterReborn = 83764719;
        public const int CardDestruction = 72892473;
        public const int Polymerization = 24094653;
        public const int SwordsOfRevealingLight = 72302403;
        public const int MirrorForce = 44095762;
        public const int TrapHole = 4206964;
        public const int Waboku = 12607053;
        public const int NegateAttack = 14315573;
        public const int RingOfDestruction = 83555666;
        public const int EnemyController = 98045062;
        public const int FluteOfSummoningDragon = 43973174;

        static readonly HashSet<int> ActivatableScripts = new()
        {
            PotOfGreed, DarkHole, Raigeki, HeavyStorm, Mst, MonsterReborn, CardDestruction,
            Polymerization, SwordsOfRevealingLight, MirrorForce, TrapHole, Waboku, NegateAttack,
            RingOfDestruction, EnemyController, FluteOfSummoningDragon
        };

        // Fusion recipes registered from official material lines (see SpellTrapEffects)
        static readonly HashSet<int> SummonProcedureScripts = new()
        {
            11901678, // Black Skull Dragon
            66889139  // Gaia the Dragon Champion
        };
        static readonly HashSet<string> SpecialSummonKeys = new();

        static bool ContainsIgnoreCase(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) &&
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasActivatableScript(int cardId) => ActivatableScripts.Contains(cardId);

        /// <summary>
        /// True when a card has a complete activation program (registry script or FullyCompiled text).
        /// Structural / stub / unimplemented cards return false.
        /// </summary>
        public static bool ProgramMayActivate(CardDef def)
        {
            if (def == null) return false;
            if (HasActivatableScript(def.id)) return true;
            var prog = CompiledEffectCache.GetOrCompile(def);
            return prog != null && prog.FullyCompiled && prog.CanResolveAny;
        }

        public static bool HasSummonProcedure(int cardId, SummonKind kind) =>
            kind == SummonKind.FusionSummon && SummonProcedureScripts.Contains(cardId);

        public static bool HasSpecialSummonProcedure(string key) =>
            !string.IsNullOrEmpty(key) && SpecialSummonKeys.Contains(key);

        public static bool HasPendulumSummonSupport() => false; // needs scale fields in CardDef

        /// <summary>
        /// Spell Speed of an activated card type (structural, Rulebook).
        /// </summary>
        public static SpellSpeed SpeedOf(CardDef def)
        {
            if (def == null) return SpellSpeed.None;
            if (def.IsTrap)
            {
                // cards_db: type often "Trap Card", race "Counter" | "Continuous" | "Normal"
                // Some feeds also use type "Counter Trap"
                if (ContainsIgnoreCase(def.type, "Counter") || ContainsIgnoreCase(def.race, "Counter"))
                    return SpellSpeed.Speed3;
                return SpellSpeed.Speed2; // Normal/Continuous Trap
            }

            if (def.IsSpell)
            {
                if (ContainsIgnoreCase(def.race, "Quick") || ContainsIgnoreCase(def.type, "Quick"))
                    return SpellSpeed.Speed2;
                // Normal, Continuous, Equip, Field, Ritual = Speed 1
                return SpellSpeed.Speed1;
            }

            // Monster Quick Effects are Speed 2 (PSCT "(Quick Effect)" / "during either player's").
            if (ContainsIgnoreCase(def.desc, "Quick Effect") ||
                ContainsIgnoreCase(def.desc, "during either player's"))
                return SpellSpeed.Speed2;

            return SpellSpeed.Speed1;
        }

        public static EffectClass ClassOfActivation(CardDef def, bool isTriggerWindow)
        {
            if (def == null) return EffectClass.None;
            if (def.IsTrap || (def.IsSpell && SpeedOf(def) == SpellSpeed.Speed2))
                return EffectClass.Quick;
            if (def.IsMonster && SpeedOf(def) == SpellSpeed.Speed2)
                return EffectClass.Quick;
            if (isTriggerWindow) return EffectClass.Trigger;
            if (def.IsSpell || def.IsTrap) return EffectClass.Ignition; // Normal Spell as ignition-like
            return EffectClass.Ignition;
        }

        /// <summary>
        /// May this card be activated right now?
        /// 1) Compile/learn official text on first play (cache forever after).
        /// 2) If text program has usable clauses → TextEffectRuntime timing check.
        /// 3) Else legacy hardcoded registry scripts.
        /// Never invent effects for unparseable text.
        /// </summary>
        public static bool CanActivateOfficial(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null)
            {
                reason = "Invalid activation context.";
                return false;
            }

            var def = card.Def;
            if (def.IsTrap && ContinuousNegations.TrapsCannotActivate(engine, who, card))
            {
                reason = "Trap Cards cannot be activated.";
                return false;
            }

            var text = OfficialCardAuthority.OfficialText(def);
            if (string.IsNullOrEmpty(text) && !OfficialCardAuthority.HasNoActivatableEffect(def))
            {
                reason = "No official card text on file.";
                return false;
            }

            // Normal / effectless Extra Deck (classic Fusions) — no activation
            if (OfficialCardAuthority.HasNoActivatableEffect(def))
            {
                reason = OfficialCardAuthority.IsNormalMonsterNoEffect(def)
                    ? "Normal Monster — no effect to activate."
                    : "Effectless Extra Deck monster — no effect to activate (materials only).";
                return false;
            }

            // Field Spell: Speed 1 placement in the Field Zone is the activation (Rulebook).
            // Continuous clauses apply while face-up; they are not Trap Cards and are not Set in S/T Zones.
            if (def.IsFieldSpell)
                return CanActivateFieldSpell(engine, who, card, fromHand, out reason);

            // Response window: LegalCards is authoritative (field traps + hand QEs like Kuriboh).
            // Stubs / uncompiled programs must not activate even if timing-legal or catalog-listed.
            if (engine.PendingResponse != null && engine.PendingResponse.Responder == who)
            {
                var pr = engine.PendingResponse;
                if (!ProgramMayActivate(def))
                {
                    var tag = CardEffectStatus.RefusalTag(def);
                    reason =
                        $"{tag} [{card.Name}] effect program is incomplete (not FullyCompiled). " +
                        "Response activation refused (will not invent resolution).";
                    return false;
                }

                if (pr.LegalCards != null &&
                    pr.LegalCards.Exists(c => c != null &&
                                              (c == card || c.InstanceId == card.InstanceId)))
                {
                    reason = "OK";
                    return true;
                }

                if (!fromHand &&
                    SpellTrapEffects.IsLegalResponseCard(engine, who, card, pr.Timing, pr.Summoned))
                {
                    reason = "OK";
                    return true;
                }

                // Hand Damage Calculation (Kuriboh) even if LegalCards not yet rebuilt
                if (fromHand &&
                    MonsterEffects.IsLegalHandDamageCalculationEffect(
                        who, card, pr.Timing, pr.AttackingPlayer, pr.Attacker))
                {
                    reason = "OK";
                    return true;
                }

                reason = "Not legal for this response window.";
                return false;
            }

            // Face-up monster ignition (Abyss Soldier, compiled Once-per-turn You can …)
            // Hand: Fenrir-family Special Summon this card (ActivatesFromHand).
            // Text path requires FullyCompiled — stubs must not activate a fragment.
            if (def.IsMonster && fromHand)
            {
                var hProg = CompiledEffectCache.GetOrCompile(def);
                if (hProg != null && hProg.FullyCompiled && hProg.CanResolveAny &&
                    hProg.HasTiming(EffectTiming.Activate) &&
                    hProg.ClauseList.Exists(c => c != null && c.ActivatesFromHand) &&
                    TextEffectRuntime.CanActivate(engine, who, card, fromHand, hProg, out reason))
                    return true;
            }

            if (def.IsMonster && !fromHand)
            {
                var mProg = CompiledEffectCache.GetOrCompile(def);
                if (mProg != null && mProg.FullyCompiled && mProg.CanResolveAny &&
                    mProg.HasTiming(EffectTiming.Activate) &&
                    TextEffectRuntime.CanActivate(engine, who, card, fromHand, mProg, out reason))
                    return true;
                if (MonsterEffects.CanActivateIgnition(engine, who, card))
                {
                    reason = "OK";
                    return true;
                }

                // Incomplete programs: refuse without inventing; keep timing wording if FullyCompiled.
                if (mProg != null && !mProg.FullyCompiled && !HasActivatableScript(card.CardId))
                {
                    var tag = CardEffectStatus.RefusalTag(def);
                    reason =
                        $"{tag} [{card.Name}] effect program is incomplete (not FullyCompiled). " +
                        "Activation refused (will not invent resolution).";
                    if (CardEffectStatus.Classify(def) == CardEffectStatusKind.Unimplemented)
                        CardEffectStatus.LogUnimplementedActivation(def, reason);
                    return false;
                }

                reason = reason ??
                         $"[{card.Name}] has no legal ignition effect right now " +
                         "(Main Phase, face-up, cost / once-per-turn / targets).";
                return false;
            }

            // Learn / recall official text program — FullyCompiled required on text path
            var prog = CompiledEffectCache.GetOrCompile(def);
            var hasRegistry = HasActivatableScript(card.CardId);
            // Continuous S/T: playing the card (hand Spell / Set Trap) is the activation
            // even when the only compiled clauses are End Phase / Standby / while-face-up.
            var continuousPlay = def.IsContinuousSpellOrTrap && prog != null &&
                                 prog.FullyCompiled && prog.CanResolveAny;
            if (prog != null && prog.FullyCompiled && prog.CanResolveAny &&
                (continuousPlay ||
                 prog.HasTiming(EffectTiming.Activate) ||
                 prog.HasTiming(EffectTiming.AttackDeclared) ||
                 prog.HasTiming(EffectTiming.OpponentNormalOrFlipSummon) ||
                 prog.HasTiming(EffectTiming.DamageCalculation) ||
                 prog.HasTiming(EffectTiming.YouTakeLifePointDamage)))
            {
                if (TextEffectRuntime.CanActivate(engine, who, card, fromHand, prog, out reason))
                    return true;
                // Fall through to legacy if text path rejects but legacy might allow
            }

            if (!hasRegistry)
            {
                var tag = CardEffectStatus.RefusalTag(def);
                var status = CardEffectStatus.Classify(def);
                if (prog != null && prog.FullyCompiled && prog.ClauseList.Count > 0)
                {
                    // Timing/cost/target illegal — not an unimplemented invent refusal
                    reason =
                        $"[{card.Name}] learned text not legal now: {reason ?? "timing / targets"}.";
                }
                else if (prog != null && prog.ClauseList.Count > 0)
                {
                    reason =
                        $"{tag} [{card.Name}] effect program is incomplete (not FullyCompiled). " +
                        "Activation refused (will not invent resolution).";
                    if (status == CardEffectStatusKind.Unimplemented)
                        CardEffectStatus.LogUnimplementedActivation(def, reason);
                }
                else
                {
                    reason =
                        $"{tag} [{card.Name}] official text could not be compiled into effects. " +
                        "Activation refused (will not invent resolution). " +
                        $"Text: \"{Truncate(text, 80)}\"";
                    if (status == CardEffectStatusKind.Unimplemented)
                        CardEffectStatus.LogUnimplementedActivation(def, reason);
                }

                return false;
            }

            // Structural windows — legacy hardcoded scripts
            if (!SpellTrapEffects.CanManualActivate(engine, who, card, fromHand))
            {
                reason = reason ?? "Activation illegal under current structural rules / timing.";
                return false;
            }

            reason = "OK";
            return true;
        }

        public static bool ValidateFusionMaterials(DuelEngine engine, DuelistState who, CardInstance fusion,
            List<CardInstance> materials, CardInstance fusionSpell, out string reason)
        {
            // Full material validation is performed by SpellTrapEffects.TryResolveFusion
            // against official printed material lists only.
            if (fusion == null || !HasSummonProcedure(fusion.CardId, SummonKind.FusionSummon))
            {
                reason = "Fusion recipes not yet registered for this printing.";
                return false;
            }

            if (!SpellTrapEffects.CanResolveAnyFusion(engine, who))
            {
                reason = "Required Fusion Materials are not available on hand/field.";
                return false;
            }

            reason = "OK";
            return true;
        }

        public static bool ValidateSynchroMaterials(DuelEngine engine, DuelistState who, CardInstance synchro,
            List<CardInstance> materials, out string reason)
        {
            reason = "Synchro materials not registered.";
            return false;
        }

        public static bool ValidateXyzMaterials(DuelEngine engine, DuelistState who, CardInstance xyz,
            List<CardInstance> materials, out string reason)
        {
            reason = "Xyz materials not registered.";
            return false;
        }

        public static bool ValidateLinkMaterials(DuelEngine engine, DuelistState who, CardInstance link,
            List<CardInstance> materials, out string reason)
        {
            reason = "Link materials not registered.";
            return false;
        }

        public static bool ValidateRitual(DuelEngine engine, DuelistState who, CardInstance ritual,
            CardInstance spell, List<CardInstance> tributes, out string reason)
        {
            reason = "Ritual procedure not registered.";
            return false;
        }

        public static bool ValidatePendulumSummon(DuelEngine engine, DuelistState who,
            List<CardInstance> toSummon, out string reason)
        {
            reason = "Pendulum Summon not enabled (scale data missing).";
            return false;
        }

        public static bool ValidateSpecialSummon(DuelEngine engine, DuelistState who, CardInstance card,
            string key, out string reason)
        {
            reason = "Special Summon key not registered.";
            return false;
        }

        /// <summary>
        /// Continuous properties derived only from registered continuous scripts / flags.
        /// Never invent piercing etc. from free text parse.
        /// </summary>
        public static bool HasPiercing(CardInstance card) =>
            card != null && card.HasPiercing; // set only by registered continuous effects

        public static bool CannotBeDestroyedByBattle(CardInstance card) =>
            card != null && card.CannotBeDestroyedByBattle;

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', ' ');
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        static bool CanActivateFieldSpell(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, out string reason)
        {
            if (!fromHand)
            {
                reason = "Field Spell continuous effects apply while face-up in the Field Zone.";
                return false;
            }

            if (engine.PendingResponse != null)
            {
                reason = "Cannot activate a Field Spell in a response window (Spell Speed 1).";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = "Field Spells can only be activated during your Main Phase.";
                return false;
            }

            if (engine.HasDeclaredAttack || engine.IsAwaitingResponse)
            {
                reason = "Finish the open window first.";
                return false;
            }

            if (!who.Hand.Contains(card))
            {
                reason = "Not in hand.";
                return false;
            }

            reason = "OK";
            return true;
        }

        public static IReadOnlyCollection<int> RegisteredActivationIds => ActivatableScripts;

        /// <summary>Monster Flip / field→GY scripts (see <see cref="MonsterEffects"/>).</summary>
        public static bool HasMonsterEffectScript(int cardId) =>
            MonsterEffects.IsRegisteredMonsterEffect(cardId);
    }
}
