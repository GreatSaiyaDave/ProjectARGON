using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Legal summoning procedures (Rulebook + Extra Deck mechanics).
    /// Materials and conditions are validated structurally; card-specific
    /// "must first be…" text is only enforced when the official script is registered.
    /// </summary>
    public static class SummonProcedures
    {
        public struct SummonRequest
        {
            public SummonKind Kind;
            public DuelistState Summoner;
            public CardInstance Card;
            public int PreferredMonsterZone;
            public bool AsSet;
            public readonly List<CardInstance> Materials;
            public readonly List<CardInstance> Tributes;

            public SummonRequest(SummonKind kind)
            {
                Kind = kind;
                Summoner = null;
                Card = null;
                PreferredMonsterZone = -1;
                AsSet = false;
                Materials = new List<CardInstance>();
                Tributes = new List<CardInstance>();
            }
        }

        public struct SummonCheck
        {
            public bool Legal;
            public string Reason;
            public SummonKind Kind;
            public int TributesNeeded;
            public bool UsesExtraDeck;
            public bool OpensNegationWindow;
        }

        public static SummonCheck CheckNormalOrTribute(DuelEngine engine, DuelistState who, CardInstance card,
            bool asSet)
        {
            var r = new SummonCheck
            {
                Kind = asSet ? SummonKind.NormalSet : SummonKind.NormalSummon,
                OpensNegationWindow = !asSet // Normal Summon can be negated; Set is not a Summon
            };

            if (engine == null || who == null || card?.Def == null)
            {
                r.Reason = "Invalid summon context.";
                return r;
            }

            if (!card.Def.IsMonster)
            {
                r.Reason = "Only monsters can be Normal Summoned/Set.";
                return r;
            }

            if (card.Def.IsExtraDeck)
            {
                r.Reason = "Extra Deck monsters cannot be Normal Summoned/Set.";
                return r;
            }

            if (card.Def.IsRitualMonster)
            {
                r.Reason = "Ritual Monsters must be Ritual Summoned (cannot Normal Summon/Set).";
                return r;
            }

            // Printed "Cannot be Normal Summoned/Set" / "Must first be Special Summoned".
            // Do not treat inherent SS-from-hand (Cyber Dragon) as nomi — that stays NS-legal.
            if (PsctGrammar.BlocksNormalSummonOrSet(OfficialCardAuthority.OfficialText(card)))
            {
                r.Reason = "This card cannot be Normal Summoned/Set.";
                return r;
            }
            var summonGate = PsctGrammar.SummonGateNamed(OfficialCardAuthority.OfficialText(card));
            if (!string.IsNullOrEmpty(summonGate) && !FieldSpellEffects.ControlsFaceUpNamed(who, summonGate))
            {
                r.Reason = $"Requires face-up \"{summonGate}\" you control.";
                return r;
            }


            if (!who.Hand.Contains(card))
            {
                r.Reason = "Card not in hand.";
                return r;
            }

            if (who.NormalSummonUsed)
            {
                r.Reason = "Normal Summon/Set already used this turn.";
                return r;
            }

            if (engine.Phase != DuelPhase.Main1 && engine.Phase != DuelPhase.Main2)
            {
                r.Reason = "Normal Summon/Set only in Main Phase.";
                return r;
            }

            if (engine.TurnPlayer != who)
            {
                r.Reason = "Not your turn.";
                return r;
            }

            if (engine.Chain.HasLinks || engine.Chain.IsResolving)
            {
                r.Reason = "Cannot Normal Summon/Set while a Chain is open (except as CL resolution effect).";
                return r;
            }

            // Advanced Format: Forbidden cards cannot be summoned
            if (OfficialDataSources.StatusOf(card.CardId) == BanlistStatus.Forbidden)
            {
                r.Reason = "Forbidden on the Advanced Format banlist.";
                return r;
            }

            r.TributesNeeded = TcgRules.TributesRequired(card.Level);
            if (r.TributesNeeded > 0)
                r.Kind = asSet ? SummonKind.TributeSet : SummonKind.TributeSummon;

            if (r.TributesNeeded == 0)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    r.Reason = "No free Monster Zone.";
                    return r;
                }
            }
            else
            {
                if (who.MonsterCount < r.TributesNeeded)
                {
                    r.Reason = $"Need {r.TributesNeeded} Tribute(s).";
                    return r;
                }
            }

            r.Legal = true;
            r.Reason = "OK";
            return r;
        }

        public static SummonCheck CheckFlipSummon(DuelEngine engine, DuelistState who, CardInstance monster)
        {
            var r = new SummonCheck { Kind = SummonKind.FlipSummon, OpensNegationWindow = true };
            if (!engine.CanFlipSummon(who, monster))
            {
                r.Reason = "Illegal Flip Summon (face-down DEF, not Set this turn, Main Phase, etc.).";
                return r;
            }

            r.Legal = true;
            r.Reason = "OK";
            return r;
        }

        /// <summary>
        /// Fusion: materials + Polymerization-type card or contact fusion (scripted).
        /// Without registered fusion recipe matching official text, illegal.
        /// </summary>
        public static SummonCheck CheckFusion(DuelEngine engine, DuelistState who, CardInstance fusionMonster,
            List<CardInstance> materials, CardInstance fusionSpellOrNull)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.FusionSummon,
                UsesExtraDeck = true,
                OpensNegationWindow = true
            };

            if (fusionMonster?.Def == null || !fusionMonster.Def.IsExtraDeck)
            {
                r.Reason = "Not an Extra Deck Fusion Monster.";
                return r;
            }

            if (!OfficialEffectRegistry.HasSummonProcedure(fusionMonster.CardId, SummonKind.FusionSummon))
            {
                r.Reason =
                    "No official Fusion Summon procedure script registered for this card. " +
                    "Will not approximate materials. See OfficialEffectRegistry.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateFusionMaterials(engine, who, fusionMonster, materials,
                fusionSpellOrNull, out r.Reason);
            return r;
        }

        public static SummonCheck CheckSynchro(DuelEngine engine, DuelistState who, CardInstance synchro,
            List<CardInstance> materials)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.SynchroSummon,
                UsesExtraDeck = true,
                OpensNegationWindow = true
            };
            if (synchro?.Def == null ||
                synchro.Def.type == null ||
                synchro.Def.type.IndexOf("Synchro", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                r.Reason = "Not a Synchro Monster.";
                return r;
            }

            if (!OfficialEffectRegistry.HasSummonProcedure(synchro.CardId, SummonKind.SynchroSummon))
            {
                r.Reason = "No official Synchro procedure registered — will not invent tuner/non-tuner math.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateSynchroMaterials(engine, who, synchro, materials, out r.Reason);
            return r;
        }

        public static SummonCheck CheckXyz(DuelEngine engine, DuelistState who, CardInstance xyz,
            List<CardInstance> materials)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.XyzSummon,
                UsesExtraDeck = true,
                OpensNegationWindow = true
            };
            if (xyz?.Def == null ||
                xyz.Def.type == null ||
                xyz.Def.type.IndexOf("XYZ", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                r.Reason = "Not an Xyz Monster.";
                return r;
            }

            if (!OfficialEffectRegistry.HasSummonProcedure(xyz.CardId, SummonKind.XyzSummon))
            {
                r.Reason = "No official Xyz procedure registered.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateXyzMaterials(engine, who, xyz, materials, out r.Reason);
            return r;
        }

        public static SummonCheck CheckLink(DuelEngine engine, DuelistState who, CardInstance link,
            List<CardInstance> materials)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.LinkSummon,
                UsesExtraDeck = true,
                OpensNegationWindow = true
            };
            if (link?.Def == null ||
                link.Def.type == null ||
                link.Def.type.IndexOf("Link", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                r.Reason = "Not a Link Monster.";
                return r;
            }

            if (!OfficialEffectRegistry.HasSummonProcedure(link.CardId, SummonKind.LinkSummon))
            {
                r.Reason = "No official Link procedure registered.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateLinkMaterials(engine, who, link, materials, out r.Reason);
            return r;
        }

        public static SummonCheck CheckRitual(DuelEngine engine, DuelistState who, CardInstance ritualMonster,
            CardInstance ritualSpell, List<CardInstance> tributes)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.RitualSummon,
                OpensNegationWindow = true
            };
            if (ritualMonster?.Def == null || !ritualMonster.Def.IsRitualMonster)
            {
                r.Reason = "Not a Ritual Monster.";
                return r;
            }

            if (!OfficialEffectRegistry.HasSummonProcedure(ritualMonster.CardId, SummonKind.RitualSummon))
            {
                r.Reason = "No official Ritual procedure registered for this card.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateRitual(engine, who, ritualMonster, ritualSpell, tributes,
                out r.Reason);
            return r;
        }

        public static SummonCheck CheckPendulum(DuelEngine engine, DuelistState who,
            List<CardInstance> toSummon)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.PendulumSummon,
                OpensNegationWindow = true
            };
            if (who?.PendulumZones == null ||
                who.PendulumZones[0]?.Occupant == null ||
                who.PendulumZones[1]?.Occupant == null)
            {
                r.Reason = "Both Pendulum Scales required.";
                return r;
            }

            // Scale values require pendulum scale fields — not in basic CardDef; only if registered
            if (!OfficialEffectRegistry.HasPendulumSummonSupport())
            {
                r.Reason =
                    "Pendulum Summon requires official scale values + once-per-turn tracking in registry.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidatePendulumSummon(engine, who, toSummon, out r.Reason);
            return r;
        }

        public static SummonCheck CheckSpecial(DuelEngine engine, DuelistState who, CardInstance card,
            string procedureKey)
        {
            var r = new SummonCheck
            {
                Kind = SummonKind.SpecialSummon,
                OpensNegationWindow = true
            };
            if (!OfficialEffectRegistry.HasSpecialSummonProcedure(procedureKey))
            {
                r.Reason =
                    $"Special Summon procedure '{procedureKey}' not registered from official card text.";
                return r;
            }

            r.Legal = OfficialEffectRegistry.ValidateSpecialSummon(engine, who, card, procedureKey, out r.Reason);
            return r;
        }
    }
}
