using System;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Compositional Main Phase ignition compiler (PSCT cost ; resolution).
    /// Covers Main Phase ignition primitives this engine can pay and resolve from
    /// official text. Coin/dice, counters, tokens, Extra Deck, equip, and take-control
    /// live in <see cref="AdvancedEffectTemplates"/>.
    /// </summary>
    public static class IgnitionTemplates
    {
        static readonly Regex RxSkip = new(
            @"toss a coin|roll a six-sided|spell counter|token|extra deck|" +
            @"take control|equip (?:that target|this card)|change the attribute|" +
            @"fusion monster|look at the top|place that monster on top|" +
            @"instead of conducting your normal draw",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTriggerLead = new(
            @"^(?:when|if|after|at the |during the end|during your (?:standby|end|draw)|" +
            @"during damage|during the damage|at the start of the damage)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPhaseLock = new(
            @"standby phase|end phase|draw phase|damage step|damage calculation|battle phase|" +
            @"inflicts battle damage|destroyed a monster by battle",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsMainPhaseIgnition(PsctGrammar.Sentence sent)
        {
            if (sent == null || !sent.MakesChainLink) return false;
            if (sent.IsFlip || sent.IsQuickEffect || sent.IsParenthetical) return false;
            var cond = sent.Condition ?? "";
            if (RxPhaseLock.IsMatch(cond) || RxPhaseLock.IsMatch(sent.Raw ?? ""))
            {
                // "Once per turn, during your Main Phase 1" is still ignition.
                if (!Regex.IsMatch(cond + " " + sent.Raw,
                        @"during your main phase", RegexOptions.IgnoreCase))
                    return false;
            }

            var lead = cond.Trim();
            if (lead.Length > 0 && RxTriggerLead.IsMatch(lead) &&
                !Regex.IsMatch(lead, @"^once per turn\.?$", RegexOptions.IgnoreCase))
                return false;
            return true;
        }

        public static EffectClause TryCompile(PsctGrammar.Sentence sent, CardDef def)
        {
            if (def == null || !def.IsMonster) return null;
            if (!IsMainPhaseIgnition(sent)) return null;
            var act = sent.Activation ?? "";
            var res = sent.Resolution ?? "";
            var raw = sent.Raw ?? "";
            if (RxSkip.IsMatch(raw)) return null;
            if (string.IsNullOrWhiteSpace(act) && string.IsNullOrWhiteSpace(res))
                return null;

            var clause = new EffectClause
            {
                Timing = EffectTiming.Activate,
                MakesChainLink = true,
                IsOptional = sent.IsOptional,
                OncePerTurn = sent.OncePerTurn
            };

            ParseCost(string.IsNullOrWhiteSpace(act) ? raw : act, clause);
            ParseTarget(act, clause);
            if (!ParseResolution(string.IsNullOrWhiteSpace(res) ? raw : res, clause))
                return null;
            if (clause.Action == EffectActionKind.None)
                return null;
            return clause;
        }

        static void ParseCost(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act)) return;

            if (Regex.IsMatch(act, @"discard this card", RegexOptions.IgnoreCase))
            {
                clause.RequiresDiscardSelf = true;
                clause.ActivatesFromHand = true;
            }

            var discAttr = Regex.Match(act, @"discard 1 (\w+) monster", RegexOptions.IgnoreCase);
            var discCard = Regex.Match(act, @"discard 1 (?:random )?cards?(?: from your hand)?",
                RegexOptions.IgnoreCase);
            if (discAttr.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = discAttr.Groups[1].Value;
            }
            else if (discCard.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = "*";
            }

            var sendNamed = Regex.Match(act,
                @"send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (sendNamed.Success)
            {
                clause.RequiresSendNamedToGy = true;
                clause.RequiresFaceUpName = sendNamed.Groups[1].Value;
            }

            if (Regex.IsMatch(act,
                    @"send this (?:face-up )?card (?:you control )?to the (?:GY|Graveyard)",
                    RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"send this card to the (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                if (!clause.RequiresDiscardSelf)
                    clause.RequiresSendThisToGy = true;
            }

            if (Regex.IsMatch(act, @"send 1 monster from your hand to the (?:GY|Graveyard)",
                    RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"send 1 monster from your hand to the GY", RegexOptions.IgnoreCase))
                clause.RequiresSendHandToGy = true;

            var sendOther = Regex.Match(act,
                @"send 1 other face-up (\w+) monster you control to the (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (sendOther.Success)
            {
                clause.RequiresSendOtherYouControl = true;
                clause.AttributeFilter = sendOther.Groups[1].Value;
            }

            if (Regex.IsMatch(act, @"tribute this (?:face-up )?card", RegexOptions.IgnoreCase))
                clause.RequiresTributeThis = true;

            var tribUp = Regex.Match(act, @"tribute up to (\d+) (\w+)(?:-Type)? monsters?",
                RegexOptions.IgnoreCase);
            var tribN = Regex.Match(act, @"tribute (\d+) monsters?", RegexOptions.IgnoreCase);
            var trib1typed = Regex.Match(act,
                @"tribute 1 ""([^""]+)"" monster, except", RegexOptions.IgnoreCase);
            var trib1 = Regex.Match(act, @"tribute 1 (?:face-up )?monster", RegexOptions.IgnoreCase);
            if (tribUp.Success)
            {
                clause.RequiresTributeCount = int.TryParse(tribUp.Groups[1].Value, out var n) ? n : 1;
                clause.TributeUpTo = true;
                clause.TributeRaceFilter = tribUp.Groups[2].Value;
            }
            else if (tribN.Success && !clause.RequiresTributeThis)
            {
                clause.RequiresTributeCount = int.TryParse(tribN.Groups[1].Value, out var n) ? n : 1;
            }
            else if (trib1typed.Success)
            {
                clause.RequiresTributeCount = 1;
                clause.NamedCard = trib1typed.Groups[1].Value;
                clause.TributeExceptThis = true;
            }
            else if (trib1.Success && !clause.RequiresTributeThis)
                clause.RequiresTributeCount = 1;

            var pay = Regex.Match(act, @"pay (\d+) (?:LP|Life Points)", RegexOptions.IgnoreCase);
            if (pay.Success)
                clause.PayLpAmount = int.TryParse(pay.Groups[1].Value, out var lp) ? lp : 0;

            var banUp = Regex.Match(act,
                @"banish up to (\d+)(?: (\w+))? monsters? from your (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            var banN = Regex.Match(act,
                @"banish (\d+)(?: (\w+))? monsters? from your (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (banUp.Success)
            {
                clause.BanishFromGyCount = int.TryParse(banUp.Groups[1].Value, out var b) ? b : 1;
                clause.BanishFromGyUpTo = true;
                if (banUp.Groups.Count > 2 && banUp.Groups[2].Success)
                    clause.AttributeFilter = banUp.Groups[2].Value;
            }
            else if (banN.Success)
            {
                clause.BanishFromGyCount = int.TryParse(banN.Groups[1].Value, out var b) ? b : 1;
                if (banN.Groups.Count > 2 && banN.Groups[2].Success)
                    clause.AttributeFilter = banN.Groups[2].Value;
            }
        }

        static void ParseTarget(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act)) return;
            if (Regex.IsMatch(act, @"target 1 (?:spell/?trap|spell or trap)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.FieldSpellTraps;
            }
            else if (Regex.IsMatch(act, @"target 1 card your opponent controls", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.OppAnyCardOnField;
            }
            else if (Regex.IsMatch(act, @"target 1 card on the field", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(act, @"to target 1 card on the field", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.AnyCardOnField;
            }
            else if (Regex.IsMatch(act, @"target 1 monster in either (?:GY|Graveyard)",
                         RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(act,
                         @"select 1 monster card from you or your opponent's graveyard",
                         RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.EitherGyMonsters;
            }
            else if (Regex.IsMatch(act, @"target 1 monster in (?:your|the) (?:GY|Graveyard)",
                         RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(act,
                         @"target 1 (\w+)(?:-Type)? monster in (?:your|the) (?:GY|Graveyard)",
                         RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                var raced = Regex.Match(act,
                    @"target 1 (\w+)(?:-Type)? monster in (?:your|the) (?:GY|Graveyard)",
                    RegexOptions.IgnoreCase);
                if (raced.Success)
                    clause.RaceFilter = raced.Groups[1].Value;
            }
            else if (Regex.IsMatch(act, @"target 1 Spell in your GY", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGySpells;
            }
            else if (Regex.IsMatch(act, @"target 1 Trap in your GY", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyTraps;
            }
            else if (Regex.IsMatch(act,
                         @"target 1 (?:face-up )?monster your opponent controls",
                         RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.OppFaceUpMonsters;
            }
            else if (Regex.IsMatch(act, @"target 1 face-up monster on the field", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(act, @"target 1 monster on the field", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(act, @"to target 1 monster on the field", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.FieldAnyMonster;
            }

            if (Regex.IsMatch(act, @"with ATK less than or equal to", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"with ATK less than or equal to the sent", RegexOptions.IgnoreCase))
                clause.RequiresAtkLeqCost = true;
        }

        static bool ParseResolution(string res, EffectClause clause)
        {
            if (string.IsNullOrEmpty(res)) return false;

            if (Regex.IsMatch(res, @"change this card to face-down defense position",
                    RegexOptions.IgnoreCase) ||
                Regex.IsMatch(res, @"flip this card into face-down defense position",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.SetThisFaceDownDefense;
                return true;
            }

            if (Regex.IsMatch(res, @"change the battle position of this card", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ChangeThisBattlePosition;
                return true;
            }

            if (Regex.IsMatch(res, @"can attack (?:your opponent(?:'s Life Points)? )?directly this turn",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.GrantDirectAttackThisTurn;
                return true;
            }

            if (Regex.IsMatch(res, @"banish (?:that target|it|them)", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Banish;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    clause.RequiresTargetChoice = true;
                }

                return true;
            }

            if (Regex.IsMatch(res, @"return (?:it|that target) to the hand", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ReturnToHand;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.AnyCardOnField;
                    clause.RequiresTargetChoice = true;
                }

                return true;
            }

            if (Regex.IsMatch(res, @"destroy all special summoned monsters", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.DestroySpecialSummonedMonsters;
                clause.RequiresTargetChoice = false;
                return true;
            }

            if (Regex.IsMatch(res,
                    @"destroy all monsters your opponent controls with ATK less than or equal",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.DestroyOppMonstersAtkLeq;
                clause.RequiresAtkLeqCost = true;
                clause.RequiresTargetChoice = false;
                return true;
            }

            if (Regex.IsMatch(res, @"destroy all other cards on the field", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(res, @"destroy all cards on the field except this card",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.AllOtherCardsOnField;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
                return true;
            }

            if (Regex.IsMatch(res, @"destroy all monsters your opponent controls", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Side = EffectSide.Opponent;
                clause.Zone = EffectZoneFilter.FieldMonsters;
                clause.RequiresTargetChoice = false;
                return true;
            }

            if (Regex.IsMatch(res, @"destroy (?:that target|it)\.?", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    clause.RequiresTargetChoice = true;
                }

                return true;
            }

            var dmg = Regex.Match(res, @"inflict (\d+) damage to your opponent", RegexOptions.IgnoreCase);
            if (dmg.Success)
            {
                if (Regex.IsMatch(res.Substring(dmg.Index + dmg.Length), @"\bfor each\b",
                        RegexOptions.IgnoreCase))
                    return false;
                clause.Action = EffectActionKind.InflictDamageToOpponent;
                clause.Amount = int.TryParse(dmg.Groups[1].Value, out var n) ? n : 0;
                return true;
            }

            if (Regex.IsMatch(res,
                    @"inflict damage to your opponent equal to half the tributed monster's ATK",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.InflictDamageHalfTributedAtk;
                return true;
            }

            var draw = Regex.Match(res, @"draw (\d+) cards?", RegexOptions.IgnoreCase);
            if (draw.Success)
            {
                clause.Action = EffectActionKind.Draw;
                clause.Side = EffectSide.Controller;
                clause.Amount = int.TryParse(draw.Groups[1].Value, out var n) ? n : 1;
                return true;
            }

            var gain = Regex.Match(res,
                @"this card gains (\d+) ATK(?: for each)?", RegexOptions.IgnoreCase);
            if (gain.Success)
            {
                clause.Action = EffectActionKind.GainThisAtkUntilEnd;
                clause.Amount = int.TryParse(gain.Groups[1].Value, out var n) ? n : 0;
                clause.ScaleAmountByCostCount = Regex.IsMatch(res, @"for each", RegexOptions.IgnoreCase);
                return true;
            }

            var addUp = Regex.Match(res,
                @"add up to (\d+) ""([^""]+)"" from your Deck to your hand",
                RegexOptions.IgnoreCase);
            var add1 = Regex.Match(res,
                @"add 1 ""([^""]+)"" from your Deck to your hand",
                RegexOptions.IgnoreCase);
            if (addUp.Success)
            {
                clause.Action = EffectActionKind.AddNamedFromDeckToHand;
                clause.Amount = int.TryParse(addUp.Groups[1].Value, out var n) ? n : 1;
                clause.NamedCard = addUp.Groups[2].Value;
                clause.FromDeck = true;
                return true;
            }

            if (add1.Success)
            {
                clause.Action = EffectActionKind.AddNamedFromDeckToHand;
                clause.Amount = 1;
                clause.NamedCard = add1.Groups[1].Value;
                clause.FromDeck = true;
                return true;
            }

            if (Regex.IsMatch(res, @"add (?:that target|it) to your hand", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.AddFromGyToHand;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return false;
                return true;
            }

            if (Regex.IsMatch(res, @"special summon (?:it|that target)", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.SpecialSummonFromGy;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                return true;
            }

            var ssNamed = Regex.Match(res,
                @"special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                @"(?: or (hand|deck|graveyard|gy))?(?: or (hand|deck|graveyard|gy))?",
                RegexOptions.IgnoreCase);
            if (ssNamed.Success)
            {
                clause.Action = EffectActionKind.SpecialSummonNamed;
                clause.NamedCard = ssNamed.Groups[1].Value;
                ApplyOrigin(clause, ssNamed.Groups[2].Value);
                if (ssNamed.Groups.Count > 3 && ssNamed.Groups[3].Success)
                    ApplyOrigin(clause, ssNamed.Groups[3].Value);
                if (ssNamed.Groups.Count > 4 && ssNamed.Groups[4].Success)
                    ApplyOrigin(clause, ssNamed.Groups[4].Value);
                return true;
            }

            var ssRace = Regex.Match(res,
                @"special summon 1 (\w+) monster from your hand", RegexOptions.IgnoreCase);
            if (ssRace.Success)
            {
                clause.Action = EffectActionKind.SpecialSummonFromHand;
                clause.RaceFilter = ssRace.Groups[1].Value;
                clause.Amount = 1;
                clause.Zone = EffectZoneFilter.ControllerHandMonsters;
                return true;
            }

            return false;
        }

        static void ApplyOrigin(EffectClause clause, string loc)
        {
            if (string.IsNullOrEmpty(loc)) return;
            if (loc.Equals("hand", StringComparison.OrdinalIgnoreCase))
                clause.FromHand = true;
            else if (loc.Equals("deck", StringComparison.OrdinalIgnoreCase))
                clause.FromDeck = true;
            else
                clause.FromGrave = true;
        }
    }
}
