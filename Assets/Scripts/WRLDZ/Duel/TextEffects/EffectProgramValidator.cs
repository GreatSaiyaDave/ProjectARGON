using System;
using System.Collections.Generic;
using System.Text;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Schema validation for AI- or regex-produced <see cref="CompiledCardProgram"/>s.
    /// Rejects invented enums, empty programs for effect cards, and mismatched identity.
    /// Live duel never runs invalid programs.
    /// </summary>
    public static class EffectProgramValidator
    {
        public struct ValidationResult
        {
            public bool Ok;
            public string Error;
            public List<string> Warnings;
        }

        public static ValidationResult Validate(CompiledCardProgram prog, CardDef def = null)
        {
            var warnings = new List<string>();
            if (prog == null)
                return Fail("Program is null.");

            if (prog.CardId <= 0)
                return Fail("CardId must be positive.");

            if (def != null)
            {
                if (prog.CardId != def.id)
                    return Fail($"CardId mismatch: program {prog.CardId} vs def {def.id}.");
                var hash = OfficialCardAuthority.TextHash(def);
                if (!string.IsNullOrEmpty(prog.TextHash) && prog.TextHash != hash)
                    warnings.Add("TextHash differs from current official text (errata?).");
            }

            if (prog.CompilerVersion <= 0)
                return Fail("CompilerVersion missing.");

            if (prog.Clauses == null)
                prog.Clauses = Array.Empty<EffectClause>();

            var clauses = prog.ClauseList;
            if (clauses.Count == 0)
            {
                if (def != null && OfficialCardAuthority.HasNoActivatableEffect(def))
                    return Ok(warnings);
                // Empty effect program for an effect card is only OK if marked structural
                return Fail("No clauses — effect card must not resolve empty AI invent.");
            }

            for (var i = 0; i < clauses.Count; i++)
            {
                var c = clauses[i];
                if (c == null)
                    return Fail($"Clause[{i}] is null.");
                if (c.Timing == EffectTiming.None)
                    return Fail($"Clause[{i}] Timing is None.");
                if (c.Action == EffectActionKind.None)
                    return Fail($"Clause[{i}] Action is None.");
                if (!Enum.IsDefined(typeof(EffectTiming), c.Timing))
                    return Fail($"Clause[{i}] unknown Timing {(int)c.Timing}.");
                if (!Enum.IsDefined(typeof(EffectActionKind), c.Action))
                    return Fail($"Clause[{i}] unknown Action {(int)c.Action}.");
                if (!Enum.IsDefined(typeof(EffectSide), c.Side))
                    return Fail($"Clause[{i}] unknown Side.");
                if (!Enum.IsDefined(typeof(EffectZoneFilter), c.Zone))
                    return Fail($"Clause[{i}] unknown Zone.");
                if (c.Amount < 0 || c.Amount > 100000)
                    return Fail($"Clause[{i}] Amount out of range: {c.Amount}.");

                // Soft consistency checks
                if (c.RequiresTargetChoice && c.Zone == EffectZoneFilter.None &&
                    c.Action != EffectActionKind.NegateAttack)
                    warnings.Add($"Clause[{i}] RequiresTargetChoice but Zone=None.");
                if (c.Action == EffectActionKind.Draw && c.Amount <= 0)
                    return Fail($"Clause[{i}] Draw requires Amount >= 1.");
            }

            return Ok(warnings);
        }

        /// <summary>Parse AI JSON DTO → program, then validate.</summary>
        public static bool TryParseAndValidate(string json, CardDef def, out CompiledCardProgram prog,
            out string error)
        {
            prog = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty AI response.";
                return false;
            }

            json = ExtractJsonObject(json);
            AiProgramDto dto;
            try
            {
                dto = UnityEngine.JsonUtility.FromJson<AiProgramDto>(json);
            }
            catch (Exception ex)
            {
                error = "JSON parse failed: " + ex.Message;
                return false;
            }

            if (dto == null)
            {
                error = "JSON parsed null.";
                return false;
            }

            prog = FromDto(dto, def);
            var v = Validate(prog, def);
            if (!v.Ok)
            {
                error = v.Error;
                prog = null;
                return false;
            }

            if (v.Warnings != null && v.Warnings.Count > 0)
                UnityEngine.Debug.LogWarning("[WRLDZ AI FX] " + string.Join("; ", v.Warnings));
            return true;
        }

        public static CompiledCardProgram FromDto(AiProgramDto dto, CardDef def)
        {
            var clauses = new List<EffectClause>();
            if (dto.clauses != null)
            {
                foreach (var d in dto.clauses)
                {
                    if (d == null) continue;
                    if (!TryParseEnum(d.timing, out EffectTiming timing)) continue;
                    if (!TryParseEnum(d.action, out EffectActionKind action)) continue;
                    TryParseEnum(d.side, out EffectSide side);
                    TryParseEnum(d.zone, out EffectZoneFilter zone);
                    clauses.Add(new EffectClause
                    {
                        Timing = timing,
                        Action = action,
                        Side = side,
                        Zone = zone,
                        Amount = d.amount,
                        RequiresTargetChoice = d.requiresTargetChoice,
                        RequiresLordOfDOnField = d.requiresLordOfDOnField,
                        OpponentTurnOnly = d.opponentTurnOnly,
                        StaysOnField = d.staysOnField,
                        SourceSnippet = string.IsNullOrEmpty(d.sourceSnippet)
                            ? "ai"
                            : d.sourceSnippet
                    });
                }
            }

            var prog = new CompiledCardProgram
            {
                CardId = def?.id ?? dto.cardId,
                CardName = def?.name ?? dto.cardName ?? "",
                TextHash = def != null ? OfficialCardAuthority.TextHash(def) : dto.textHash,
                SourceText = def != null ? OfficialCardAuthority.OfficialText(def) : dto.sourceText,
                CompiledUtc = DateTime.UtcNow.ToString("o"),
                CompilerVersion = CardTextEffectCompiler.Version,
                FullyCompiled = dto.fullyCompiled,
                CompileSource = "ai"
            };
            prog.SetClauses(clauses);
            if (dto.unparsedFragments != null)
                prog.SetUnparsed(new List<string>(dto.unparsedFragments));
            return prog;
        }

        static bool TryParseEnum<T>(string s, out T value) where T : struct
        {
            value = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return Enum.TryParse(s.Trim(), ignoreCase: true, out value) && Enum.IsDefined(typeof(T), value);
        }

        static string ExtractJsonObject(string raw)
        {
            raw = raw.Trim();
            if (raw.StartsWith("```"))
            {
                var firstNl = raw.IndexOf('\n');
                if (firstNl > 0) raw = raw.Substring(firstNl + 1);
                var fence = raw.LastIndexOf("```", StringComparison.Ordinal);
                if (fence > 0) raw = raw.Substring(0, fence);
                raw = raw.Trim();
            }

            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
                return raw.Substring(start, end - start + 1);
            return raw;
        }

        static ValidationResult Fail(string e) =>
            new() { Ok = false, Error = e, Warnings = new List<string>() };

        static ValidationResult Ok(List<string> w) =>
            new() { Ok = true, Error = null, Warnings = w ?? new List<string>() };

        // ── AI JSON DTOs (string enums for model output) ────────────────────

        [Serializable]
        public class AiProgramDto
        {
            public int cardId;
            public string cardName;
            public string textHash;
            public string sourceText;
            public bool fullyCompiled;
            public AiClauseDto[] clauses;
            public string[] unparsedFragments;
        }

        [Serializable]
        public class AiClauseDto
        {
            public string timing;
            public string action;
            public string side;
            public string zone;
            public int amount;
            public bool requiresTargetChoice;
            public bool requiresLordOfDOnField;
            public bool opponentTurnOnly;
            public bool staysOnField;
            public string sourceSnippet;
        }

        public static string SchemaPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You compile Yu-Gi-Oh official card text into a STRICT JSON effect program.");
            sb.AppendLine("Output ONLY one JSON object (no markdown). Schema:");
            sb.AppendLine(@"{
  ""cardId"": <int>,
  ""cardName"": <string>,
  ""fullyCompiled"": <bool>,
  ""clauses"": [
    {
      ""timing"": ""Activate|Flip|SentFromFieldToGy|AttackDeclared|OpponentNormalOrFlipSummon|ContinuousWhileFaceUp|DamageCalculation|ThisCardSummoned"",
      ""action"": ""Draw|Destroy|SpecialSummonFromGy|SpecialSummonFromHand|AddFromGyToHand|AddFromDeckToHand|ChangeBattlePosition|NegateAttack|EndBattlePhase|ApplyWabokuStyle|ApplySwordsOfRevealingLight|BothPlayersDiscardAndRedraw|FusionSummonRegistered|EffectDamageBothFromOriginalAtk|CyberJarStyle|ContinuousCannotTargetDragons|DiscardSelfNoBattleDamageThisBattle|ContinuousGainAtkDef|ContinuousReduceLevel|AlwaysTreatedAsName|ReturnToHand|CanAttackDirectly|ExtraAttacks|LoseAtkDefUntilEndOfTurn|NegateThisAttack|InflictDamageEqualToAtk|GainLpEqualToAtk|Banish|HalveOriginalAtk|DestroyTokensInflictPer|ReturnAllFaceUpFusionsToExtra|BanishThenSameNameFromOppHandDeck|DestroySameNameInControllerHandAndDeck|DestroyOppAttackThenDamage|DestroyAllEquips|DestroyAllEquippedMonsters|FieldTreatedAsName|PreventControllerBattleDamage|SelfDestroyUnlessNamedFaceUp|GainLifePoints|TakeEffectDamage|InflictDamageToOpponent|InflictDamageHalfTributedAtk|SetThisFaceDownDefense|ChangeThisBattlePosition|GrantDirectAttackThisTurn|GainThisAtkUntilEnd|SpecialSummonNamed|AddNamedFromDeckToHand|DestroySpecialSummonedMonsters|DestroyOppMonstersAtkLeq"",
      ""side"": ""Controller|Opponent|Both|Either"",
      ""zone"": ""None|FieldMonsters|FieldSpellTraps|OppAttackPositionMonsters|OppFaceUpMonsters|EitherGyMonsters|ControllerGySpells|ControllerHandDragons|DeckMonstersAtkLeq|FieldAnyMonster|AttackingMonster|AnyCardOnField|ControllerHandMonsters|AllOtherCardsOnField"",
      ""amount"": <int>,
      ""requiresTargetChoice"": <bool>,
      ""requiresLordOfDOnField"": <bool>,
      ""opponentTurnOnly"": <bool>,
      ""staysOnField"": <bool>,
      ""sourceSnippet"": <string short quote from card text>
    }
  ],
  ""unparsedFragments"": [<strings for text you could not map>]
}");
            sb.AppendLine("Rules:");
            sb.AppendLine("- NEVER invent effects not present in the given official text.");
            sb.AppendLine("- Prefer precise clauses; put uncertain text in unparsedFragments.");
            sb.AppendLine("- fullyCompiled=true only if unparsedFragments is empty and all effects mapped.");
            sb.AppendLine("- Use only the enum strings listed above.");
            return sb.ToString();
        }
    }
}
