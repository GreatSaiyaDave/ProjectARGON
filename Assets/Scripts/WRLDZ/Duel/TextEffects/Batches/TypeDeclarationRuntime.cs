using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TypeDeclaration batch (v142) runtime: declaring a Monster Type (DNA Surgery, The Regulation of Tribe),
    /// Earthshaker's Attribute selections and The Regulation of Tribe's Standby upkeep. Reads compiled programs
    /// only — no card-id branches.
    ///
    /// <b>Declaration tokens.</b> The engine has no Type / Attribute picker, so a declaration is offered through
    /// the ordinary pick (<see cref="PendingActivation.LegalTargets"/>): one token per Monster Type or Attribute,
    /// a <see cref="CardInstance"/> that is not a card (CardId 0, <see cref="CardDef.type"/>
    /// <see cref="TypeTokenKind"/> / <see cref="AttributeTokenKind"/>, named after the Type / Attribute). The card
    /// picker shows it as a blank frame with that name. A token never enters a zone and is never a target
    /// (<see cref="EffectClause.ChoiceDoesNotTarget"/>).
    ///
    /// <b>Declared Type.</b> <see cref="EffectActionKind.DeclareMonsterType"/> is picked when the card is activated
    /// (the pick is the Chain Link's choice) and recorded on the card when its activation resolves, before any
    /// negation gate — the Lua announces it in the target step, so a negated resolution still leaves it.
    /// <see cref="CurrentRace"/> (<see cref="CardInstance.Race"/>) reads it live: while a card with
    /// <see cref="EffectActionKind.AllFaceUpMonstersBecomeDeclaredType"/> is face-up in a Spell &amp; Trap Zone and
    /// not negated, every face-up monster on that field is the declared Type (the latest declaration wins).
    /// The Regulation of Tribe's ContinuousCannotAttack reads it through <see cref="DeclaredTypeOf"/>
    /// (<see cref="EffectClause.RaceFilterIsDeclared"/>, DuelEngine.ContinuousCannotAttackBlocks).
    ///
    /// <b>Upkeep.</b> <see cref="EffectActionKind.StandbyTributeOrDestroyThis"/> is queued by the Standby Phase
    /// triggers and run by <see cref="RunStandbyUpkeep"/> once they have fired: a human picks the Tribute (Cancel =
    /// no Tribute: the card is destroyed) while the Standby Phase is held; the AI decides at once.
    /// StandbyBurnsAndDice batch (v162): the same upkeep on a monster (The Unfriendly Amazon) with
    /// <see cref="EffectClause.TributeExceptThis"/> (never itself) and <see cref="EffectClause.UpkeepTributeMandatory"/>
    /// (while a monster can be Tributed one must be: Cancel keeps waiting, the AI pays with its cheapest monster, a lone
    /// candidate is Tributed with no prompt); with none, it is destroyed.
    ///
    /// <b>Earthshaker.</b> <see cref="ResolveEarthshaker"/>: the activator selects 2 of the Attributes shown by the
    /// face-up monsters, then the opponent selects 1 of those 2, then every face-up monster with it is destroyed.
    /// A human's selection holds the Chain (<see cref="PendingActivation.HoldsChainResolution"/>,
    /// DuelEngine.ResumeChainAfterChoice) so later links wait for it.
    /// </summary>
    public static class TypeDeclarationRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: the face-up monsters on the field show at least 2
        /// different Attributes (Earthshaker's target check).
        /// </summary>
        public const string TwoAttributesFaceUp = "FaceUpMonstersShowTwoAttributes";

        /// <summary><see cref="CardDef.type"/> of a Monster Type declaration token.</summary>
        public const string TypeTokenKind = "Declaration (Monster Type)";

        /// <summary><see cref="CardDef.type"/> of an Attribute declaration token.</summary>
        public const string AttributeTokenKind = "Declaration (Attribute)";

        /// <summary>The classic card pool's Monster Types, used only when the duel has no card database.</summary>
        static readonly string[] FallbackTypes =
        {
            "Aqua", "Beast", "Beast-Warrior", "Dinosaur", "Dragon", "Fairy", "Fiend", "Fish", "Insect", "Machine",
            "Plant", "Pyro", "Reptile", "Rock", "Sea Serpent", "Spellcaster", "Thunder", "Warrior", "Winged Beast",
            "Zombie"
        };

        static readonly ConditionalWeakTable<CardDatabase, string[]> s_poolTypes = new();

        sealed class Declaration
        {
            public string Type;
            public long Stamp;
        }

        /// <summary>The Type each card declared at its latest resolved activation.</summary>
        static readonly ConditionalWeakTable<CardInstance, Declaration> s_declared = new();
        static long s_nextStamp;

        sealed class EngineState
        {
            /// <summary>Cards with a Type-changing continuous clause that have declared a Type in this duel.</summary>
            public readonly List<CardInstance> TypeChangers = new();
            /// <summary>Standby upkeeps waiting to run this turn, in trigger order.</summary>
            public readonly List<(DuelistState who, CardInstance card, int turn)> Upkeep = new();
            /// <summary>Upkeeps decided this Standby Phase (tribute null = not paid), applied together.</summary>
            public readonly List<(DuelistState who, CardInstance card, CardInstance tribute)> UpkeepDecided = new();
            /// <summary>Earthshaker being resolved: its controller, the card, the Attributes selected so far.</summary>
            public DuelistState EarthshakerActivator;
            public CardInstance EarthshakerSource;
            public readonly List<string> EarthshakerSelected = new();
            /// <summary>1 = the activator selects 2 Attributes; 2 = the opponent selects 1 of them.</summary>
            public int EarthshakerStep;
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();

        /// <summary>Duels with a Type-changing card that declared a Type (read by <see cref="CurrentRace"/>).</summary>
        static readonly List<WeakReference<DuelEngine>> s_typeChangeEngines = new();

        static EngineState StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        // ------------------------------------------------------------------ declaration tokens

        /// <summary>The Monster Types of this duel's card pool (every Type a monster card in the database has), A–Z.</summary>
        public static string[] MonsterTypesOf(DuelEngine engine)
        {
            var db = engine?.Database;
            if (db == null) return FallbackTypes;
            return s_poolTypes.GetValue(db, d =>
            {
                var set = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var def in d.GetAllCards())
                {
                    if (def == null || !def.IsMonster || string.IsNullOrWhiteSpace(def.race)) continue;
                    set.Add(def.race.Trim());
                }

                return set.Count > 0 ? set.ToArray() : FallbackTypes;
            });
        }

        /// <summary>One declaration token per Monster Type of the card pool (fresh instances).</summary>
        public static List<CardInstance> MonsterTypeTokens(DuelEngine engine) =>
            MonsterTypesOf(engine).Select(t => Token(TypeTokenKind, t)).ToList();

        /// <summary>
        /// Every Attribute a card can have, A–Z (Lua ATTRIBUTE_ALL). Earthshaker only ever offers the Attributes the
        /// face-up monsters show; Abyssal Designator lets its controller declare any of the seven.
        /// </summary>
        public static readonly string[] AllAttributes =
        {
            "DARK", "DIVINE", "EARTH", "FIRE", "LIGHT", "WATER", "WIND"
        };

        /// <summary>One declaration token per Attribute given (fresh instances).</summary>
        public static List<CardInstance> AttributeTokens(IEnumerable<string> attributes) =>
            attributes.Select(a => Token(AttributeTokenKind, a)).ToList();

        static CardInstance Token(string kind, string value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = value,
                type = kind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = 0,
                race = kind == TypeTokenKind ? value : "",
                attribute = kind == AttributeTokenKind ? value : "",
                archetype = ""
            }
        };

        public static bool IsTypeToken(CardInstance c) =>
            c != null && c.CardId == 0 && c.Def != null && c.Def.type == TypeTokenKind &&
            !string.IsNullOrEmpty(c.Def.race);

        public static bool IsAttributeToken(CardInstance c) =>
            c != null && c.CardId == 0 && c.Def != null && c.Def.type == AttributeTokenKind &&
            !string.IsNullOrEmpty(c.Def.attribute);

        /// <summary>
        /// A declaration picked while activating a card (the Chain Link's choice) is announced at once, so the
        /// opponent knows the Type before responding.
        /// </summary>
        public static void NoteActivationChoice(DuelEngine engine, CardInstance card, CardInstance pick)
        {
            if (engine == null) return;
            // TokensDeclarationsAndPicks batch: DNA Transplant's Attribute is announced the same way.
            if (IsAttributeToken(pick))
                engine.Log($"{card?.Name ?? "Effect"}: declares {pick.Def.attribute}.");
            if (!IsTypeToken(pick)) return;
            engine.Log($"{card?.Name ?? "Effect"}: declares {pick.Def.race}.");
        }

        // ------------------------------------------------------------------ declared Type

        /// <summary>The Type <paramref name="card"/> declared at its latest resolved activation, or null.</summary>
        public static string DeclaredTypeOf(CardInstance card) =>
            card != null && s_declared.TryGetValue(card, out var d) ? d.Type : null;

        /// <summary>
        /// <see cref="EffectActionKind.DeclareMonsterType"/> resolves: remember the declared Type on the card
        /// (before any negation gate — the Type was declared on activation).
        /// </summary>
        public static void RecordDeclaredType(DuelEngine engine, CardInstance source, CardInstance token)
        {
            if (engine == null || source == null) return;
            if (!IsTypeToken(token))
            {
                engine.Log($"{source.Name}: no Monster Type was declared.");
                return;
            }

            s_declared.Remove(source);
            // Destroyed in response to its activation: it is not on the field, so its Type applies to nothing.
            // A monster ignition (Tribe-Infecting Virus) uses its declaration in the same resolution, wherever the
            // monster is now: the Lua keeps the announced Type in the effect's label, so its destruction still
            // happens after the Virus has left the field in response.
            if (!OnSpellTrapField(engine, source) && source.Def?.IsMonster != true)
            {
                engine.Log($"{source.Name} is no longer on the field — its declared Type applies to nothing.");
                return;
            }

            var type = token.Def.race;
            s_declared.Add(source, new Declaration { Type = type, Stamp = ++s_nextStamp });
            if (!ChangesTypes(source))
            {
                engine.Log($"{source.Name}: {type} is the declared Type.");
                return;
            }

            engine.Log($"{source.Name}: {type} is the declared Type — face-up monsters on the field become {type}.");
            var st = StateOf(engine);
            if (!st.TypeChangers.Contains(source))
                st.TypeChangers.Add(source);
            RegisterTypeChangeEngine(engine);
        }

        static void RegisterTypeChangeEngine(DuelEngine engine)
        {
            for (var i = s_typeChangeEngines.Count - 1; i >= 0; i--)
            {
                if (!s_typeChangeEngines[i].TryGetTarget(out var e) || e == null)
                {
                    s_typeChangeEngines.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(e, engine)) return;
            }

            s_typeChangeEngines.Add(new WeakReference<DuelEngine>(engine));
        }

        /// <summary>
        /// <see cref="CardInstance.Race"/>: the printed Type, unless <paramref name="card"/> is a face-up monster on
        /// the field of a duel where a Type-changing card applies (DNA Surgery), then that card's declared Type.
        /// </summary>
        public static string CurrentRace(CardInstance card)
        {
            var def = card?.Def;
            if (def == null) return null;
            if (!card.FaceUp || !def.IsMonster) return def.race;
            // EquipFamily batch: an Equip Card that changes the equipped monster's Type (Dragonic Attack).
            var equipped = card.Equips.Count > 0;
            // PgdSpellTrapBasics batch (v162): D. Tribe — "treated as Dragon until the End Phase" on this monster.
            var byEffect = PgdSpellTrapBasicsRuntime.RaceUntilEndPhase(card, out var effectStamp);
            // PerCopyDeck batch (v163): Parasite Paracide — its controller's monsters are Insect while it is face-up.
            var byAura = PerCopyDeckRuntime.RaceOnField(card, out var auraStamp);
            if (s_typeChangeEngines.Count == 0 && !equipped && byEffect == null && byAura == null) return def.race;
            string changed = null;
            var changedStamp = long.MinValue;
            foreach (var wr in s_typeChangeEngines)
            {
                if (!wr.TryGetTarget(out var engine) || engine == null) continue;
                if (!OnMonsterField(engine, card)) continue;
                changed = ChangedTypeOnField(engine, out changedStamp);
                break;
            }

            // Both apply: the change applied last wins (DNA Surgery's declaration vs. the Equip Card's equip vs. an
            // "until the End Phase" change such as D. Tribe's).
            var best = changed;
            var bestStamp = changed != null ? changedStamp : long.MinValue;
            if (equipped)
            {
                var byEquip = EquipFamilyRuntime.EquippedRace(card, out var equipStamp);
                if (byEquip != null && (best == null || equipStamp > bestStamp))
                {
                    best = byEquip;
                    bestStamp = equipStamp;
                }
            }

            if (byEffect != null && (best == null || effectStamp > bestStamp))
            {
                best = byEffect;
                bestStamp = effectStamp;
            }

            if (byAura != null && (best == null || auraStamp > bestStamp))
                best = byAura;

            return best ?? def.race;
        }

        /// <summary>A timestamp shared by every Type change (declarations and Type-changing Equip Cards).</summary>
        internal static long NextStamp() => ++s_nextStamp;

        /// <summary>
        /// The Type every face-up monster on the field has in this duel right now, or null when no Type-changing
        /// card applies: the one declared last among the face-up, non-negated cards with
        /// <see cref="EffectActionKind.AllFaceUpMonstersBecomeDeclaredType"/> in a Spell &amp; Trap Zone.
        /// </summary>
        public static string ChangedTypeOnField(DuelEngine engine) => ChangedTypeOnField(engine, out _);

        /// <summary><see cref="ChangedTypeOnField(DuelEngine)"/> and the timestamp of that declaration.</summary>
        public static string ChangedTypeOnField(DuelEngine engine, out long stamp)
        {
            stamp = long.MinValue;
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.TypeChangers.Count == 0)
                return null;
            string best = null;
            var bestStamp = long.MinValue;
            foreach (var src in st.TypeChangers)
            {
                if (src == null || !src.FaceUp || src.IsNegated || !OnSpellTrapField(engine, src)) continue;
                if (!s_declared.TryGetValue(src, out var d) || string.IsNullOrEmpty(d.Type)) continue;
                if (!ChangesTypes(src)) continue;
                if (d.Stamp <= bestStamp) continue;
                bestStamp = d.Stamp;
                best = d.Type;
            }

            if (best != null) stamp = bestStamp;
            return best;
        }

        static bool ChangesTypes(CardInstance src)
        {
            var prog = CompiledEffectCache.GetOrCompile(src?.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.AllFaceUpMonstersBecomeDeclaredType)
                    return true;
            }

            return false;
        }

        static bool OnMonsterField(DuelEngine engine, CardInstance card) =>
            (engine.Player != null && engine.Player.TryFindMonster(card, out _)) ||
            (engine.Opponent != null && engine.Opponent.TryFindMonster(card, out _));

        static bool OnSpellTrapField(DuelEngine engine, CardInstance card) =>
            (engine.Player != null && engine.Player.TryFindSpellTrap(card, out _)) ||
            (engine.Opponent != null && engine.Opponent.TryFindSpellTrap(card, out _)) ||
            // BattleRules batch: a Field Spell in its Field Zone (Array of Revealing Light) is on the field too.
            (card != null && (ReferenceEquals(engine.Player?.FieldSpellZone?.Occupant, card) ||
                              ReferenceEquals(engine.Opponent?.FieldSpellZone?.Occupant, card)));

        /// <summary>
        /// AI declaration: the Type whose face-up monsters are worth most to the opponent and least to the AI
        /// (The Regulation of Tribe locks them; for DNA Surgery any Type is legal). Ties keep the A–Z order.
        /// </summary>
        public static CardInstance AiPickMonsterType(DuelEngine engine, DuelistState who, List<CardInstance> tokens)
        {
            if (tokens == null || tokens.Count == 0) return null;
            var opp = engine?.OpponentOf(who);
            CardInstance best = null;
            var bestScore = long.MinValue;
            foreach (var t in tokens)
            {
                if (!IsTypeToken(t)) continue;
                var score = FaceUpAtkOfType(opp, t.Def.race) - FaceUpAtkOfType(who, t.Def.race);
                if (score <= bestScore) continue;
                bestScore = score;
                best = t;
            }

            return best ?? tokens[0];
        }

        static long FaceUpAtkOfType(DuelistState side, string type)
        {
            if (side == null || string.IsNullOrEmpty(type)) return 0;
            long sum = 0;
            foreach (var m in side.MonstersOnField())
            {
                if (m == null || !m.FaceUp) continue;
                if (!string.Equals(m.Race, type, StringComparison.OrdinalIgnoreCase)) continue;
                sum += Math.Max(1, m.CurrentAtk);
            }

            return sum;
        }

        // ------------------------------------------------------------------ The Regulation of Tribe's upkeep

        /// <summary>A face-up card with the upkeep reached its controller's Standby Phase.</summary>
        public static void QueueStandbyUpkeep(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || !OwesUpkeep(engine, who, card)) return;
            var st = StateOf(engine);
            // A new Standby Phase: nothing is left from an earlier one (a run cut short is over).
            if (st.Upkeep.Count == 0 && engine.PendingActivation?.AwaitingUpkeepTribute != true)
                st.UpkeepDecided.Clear();
            st.Upkeep.RemoveAll(u => u.turn != engine.TurnNumber);
            if (!st.Upkeep.Exists(u => ReferenceEquals(u.card, card)))
                st.Upkeep.Add((who, card, engine.TurnNumber));
        }

        /// <summary>
        /// Run the queued upkeeps in order: first every controller decides (a human's Tribute pick holds the
        /// Standby Phase and resumes the run: <see cref="FinishPick"/> / <see cref="CancelPick"/>), then the
        /// chosen Tributes are sent to the GY and the unpaid cards destroyed, together. Their "sent to the GY"
        /// triggers therefore start once the upkeep is over and never interrupt another card's upkeep.
        /// </summary>
        public static void RunStandbyUpkeep(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            while (st.Upkeep.Count > 0)
            {
                if (engine.GameOver)
                {
                    st.Upkeep.Clear();
                    st.UpkeepDecided.Clear();
                    return;
                }

                if (engine.PendingActivation != null) return;
                var (who, card, turn) = st.Upkeep[0];
                st.Upkeep.RemoveAt(0);
                if (turn != engine.TurnNumber || engine.Phase != DuelPhase.Standby) continue;
                if (!OwesUpkeep(engine, who, card)) continue;
                var upkeep = UpkeepClauseOf(card);
                var mandatory = upkeep?.UpkeepTributeMandatory == true;
                var tributes = UpkeepTributes(engine, who, card, upkeep);
                // A monster already chosen for another card's upkeep cannot be Tributed twice.
                tributes.RemoveAll(t => st.UpkeepDecided.Exists(d => ReferenceEquals(d.tribute, t)));
                if (tributes.Count == 0)
                {
                    engine.Log(ContinuousProtections.NeitherPlayerCanTribute(engine)
                        ? $"{card.Name}: neither player can Tribute (Mask of Restrict)."
                        : $"{card.Name}: {who.Name} controls no monster to Tribute.");
                    st.UpkeepDecided.Add((who, card, null));
                    continue;
                }

                if (mandatory && tributes.Count == 1)
                {
                    // A lone legal Tribute of a mandatory upkeep is made with no prompt.
                    st.UpkeepDecided.Add((who, card, tributes[0]));
                    continue;
                }

                if (engine.IsHumanControlled(who))
                {
                    var p = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        WasSetOnField = true,
                        UsesTextProgram = true,
                        // Routed through TryResolveTextTarget (not the monster path), a monster's upkeep included.
                        AwaitingUpkeepTribute = true,
                        TargetKind = EffectTargetKind.TributeMonsterYouControl,
                        DeclarationPrompt = mandatory
                            ? $"{card.Name}: Tribute 1 other monster you control (you must)."
                            : $"{card.Name}: Tribute 1 monster you control to keep this card, or Cancel to let it be destroyed."
                    };
                    p.LegalTargets.AddRange(tributes);
                    engine.SetPendingActivation(p);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return;
                }

                var pick = mandatory ? AiMandatoryTribute(tributes) : AiUpkeepTribute(who, card, tributes, engine);
                if (pick == null)
                    engine.Log($"{card.Name}: {who.Name} does not Tribute.");
                st.UpkeepDecided.Add((who, card, pick));
            }

            ApplyUpkeepDecisions(engine, st);
        }

        /// <summary>Every upkeep of this Standby Phase is decided: Tribute or destroy, in order.</summary>
        static void ApplyUpkeepDecisions(DuelEngine engine, EngineState st)
        {
            if (st.UpkeepDecided.Count == 0) return;
            var decided = st.UpkeepDecided.ToList();
            st.UpkeepDecided.Clear();
            foreach (var (who, card, tribute) in decided)
            {
                if (engine.GameOver) return;
                if (!OnFieldForUpkeep(engine, who, card)) continue;
                if (tribute != null && !ContinuousProtections.NeitherPlayerCanTribute(engine) &&
                    engine.ControllerOf(tribute) != null)
                {
                    ReleaseEvents.Record(engine, tribute, who); // EngineHygiene: EVENT_RELEASE
                    engine.SendCardToGrave(engine.ControllerOf(tribute), tribute);
                    engine.Log($"{card.Name}: {who.Name} Tributes {tribute.Name} to keep it.");
                    continue;
                }

                DestroyForUpkeep(engine, who, card);
            }
        }

        /// <summary>A face-up card of <paramref name="who"/>'s that is really on the field owes the upkeep.</summary>
        static bool OwesUpkeep(DuelEngine engine, DuelistState who, CardInstance card) =>
            card != null && card.FaceUp && OnFieldForUpkeep(engine, who, card);

        /// <summary>
        /// The card is where its upkeep applies: a Spell &amp; Trap Zone (The Regulation of Tribe), or a Monster Zone for a
        /// monster's upkeep (The Unfriendly Amazon, StandbyBurnsAndDice batch v162).
        /// </summary>
        static bool OnFieldForUpkeep(DuelEngine engine, DuelistState who, CardInstance card) =>
            InSpellTrapZone(engine, who, card) || (who != null && card != null && who.TryFindMonster(card, out _));

        /// <summary>The card's Standby Tribute upkeep clause, or null.</summary>
        static EffectClause UpkeepClauseOf(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog?.ClauseList.Find(c =>
                c != null && c.Timing == EffectTiming.StandbyPhase &&
                c.Action == EffectActionKind.StandbyTributeOrDestroyThis);
        }

        /// <summary>True for a mandatory upkeep Tribute pick (Cancel cannot decline it).</summary>
        static bool IsMandatoryUpkeepPick(PendingActivation p) =>
            p != null && p.AwaitingUpkeepTribute && UpkeepClauseOf(p.Card)?.UpkeepTributeMandatory == true;

        /// <summary>AI, mandatory upkeep: Tribute the cheapest monster (a Token, then the lowest ATK).</summary>
        static CardInstance AiMandatoryTribute(List<CardInstance> tributes) =>
            tributes == null || tributes.Count == 0
                ? null
                : tributes.OrderBy(m => m.IsToken ? 0 : 1)
                    .ThenBy(m => m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0)
                    .First();

        /// <summary>
        /// <paramref name="card"/> sits in <paramref name="who"/>'s Spell &amp; Trap Zone and in no GY or banished
        /// pile. A Continuous Trap destroyed in response to its own activation can be put back into a zone while it
        /// is also in the GY (<c>FinishSpellTrap(stays: true)</c>); that leftover copy owes no upkeep and is not
        /// destroyed again.
        /// </summary>
        static bool InSpellTrapZone(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (who == null || card == null || !who.TryFindSpellTrap(card, out _)) return false;
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                if (side.Graveyard.Contains(card) || side.Banished.Contains(card)) return false;
            }

            return true;
        }

        /// <summary>Monsters <paramref name="who"/> may Tribute for the upkeep (face-up or face-down).</summary>
        static List<CardInstance> UpkeepTributes(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause upkeep)
        {
            if (ContinuousProtections.NeitherPlayerCanTribute(engine)) return new List<CardInstance>();
            // "(excluding this monster)": The Unfriendly Amazon never Tributes itself.
            var list = TextEffectRuntime.CollectTributeCost(who, card,
                new EffectClause { RequiresTributeCount = 1, TributeExceptThis = upkeep?.TributeExceptThis == true },
                engine);
            // Soul Exchange: a Tribute this player makes must be the granted monster.
            return TributeSummonsRuntime.RestrictToForced(who, list)?.ToList() ?? new List<CardInstance>();
        }

        /// <summary>"If you do not, this card is destroyed" (the Lua destroys it as a cost).</summary>
        static void DestroyForUpkeep(DuelEngine engine, DuelistState who, CardInstance card)
        {
            // StandbyBurnsAndDice batch (v162): a monster with the upkeep (The Unfriendly Amazon) is destroyed (Lua
            // Duel.Destroy(c, REASON_COST)).
            if (who != null && who.TryFindMonster(card, out _))
            {
                engine.Log($"{card.Name} is destroyed (no other monster to Tribute during the Standby Phase).");
                TextEffectRuntime.DestroyCard(engine, card, card, byPlayer: who);
                return;
            }

            if (!InSpellTrapZone(engine, who, card)) return;
            // EquipAndSpellProtection batch: a Magic Reflector counter is removed instead (the Lua destroys it as a cost).
            if (EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, card)) return;
            engine.Log($"{card.Name} is destroyed (no Tribute during the Standby Phase).");
            engine.SendCardToGrave(who, card, card);
        }

        /// <summary>
        /// AI upkeep: keep the card only while its lock stops more of the opponent's attack than the AI's own,
        /// paying with the cheapest monster (one the lock already stops first).
        /// </summary>
        static CardInstance AiUpkeepTribute(DuelistState who, CardInstance card, List<CardInstance> tributes,
            DuelEngine engine)
        {
            var type = DeclaredTypeOf(card);
            if (string.IsNullOrEmpty(type) || tributes.Count == 0) return null;
            var gain = FaceUpAtkOfType(engine.OpponentOf(who), type) - FaceUpAtkOfType(who, type);
            if (gain <= 0) return null;
            long Cost(CardInstance m) =>
                m.FaceUp && string.Equals(m.Race, type, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : Math.Max(0, m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0);
            var pick = tributes.OrderBy(Cost).First();
            return gain > Cost(pick) ? pick : null;
        }

        // ------------------------------------------------------------------ Earthshaker

        /// <summary>Distinct Attributes of the face-up monsters on both fields, A–Z.</summary>
        public static List<string> FaceUpAttributes(DuelEngine engine)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m?.Def == null || !m.FaceUp || string.IsNullOrWhiteSpace(m.Attribute)) continue;
                    set.Add(m.Attribute.Trim().ToUpperInvariant());
                }
            }

            return set.ToList();
        }

        /// <summary><see cref="TwoAttributesFaceUp"/>; false when <paramref name="c"/> is not this batch's condition.</summary>
        public static bool TryActivationCondition(DuelEngine engine, EffectClause c, out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c?.ActivationCondition != TwoAttributesFaceUp) return false;
            ok = FaceUpAttributes(engine).Count >= 2;
            reason = "Needs face-up monsters with at least 2 different Attributes on the field.";
            return true;
        }

        /// <summary>
        /// Earthshaker resolves: the activator selects 2 of the Attributes on the field (both, when only 2 are
        /// there), the opponent selects 1 of them, and every face-up monster with it is destroyed.
        /// </summary>
        public static void ResolveEarthshaker(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var present = FaceUpAttributes(engine);
            if (present.Count < 2)
            {
                engine.Log($"{source?.Name ?? "Effect"}: the face-up monsters do not show 2 Attributes — no effect.");
                return;
            }

            var st = StateOf(engine);
            st.EarthshakerActivator = who;
            st.EarthshakerSource = source;
            st.EarthshakerSelected.Clear();
            st.EarthshakerStep = 1;
            if (present.Count == 2)
            {
                st.EarthshakerSelected.AddRange(present);
                LogSelection(engine, st);
                OpponentSelects(engine, st);
                return;
            }

            if (engine.IsHumanControlled(who))
            {
                var p = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingDeclaration = true,
                    HoldsChainResolution = true,
                    TargetKind = EffectTargetKind.DeclareAttribute,
                    TargetPicksRemaining = 2,
                    DeclarationPrompt = SelectTwoPrompt(source, 2)
                };
                p.LegalTargets.AddRange(AttributeTokens(present));
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return;
            }

            st.EarthshakerSelected.AddRange(AiSelectTwoAttributes(engine, who, present));
            LogSelection(engine, st);
            OpponentSelects(engine, st);
        }

        static string SelectTwoPrompt(CardInstance source, int remaining) =>
            $"{source?.Name ?? "Earthshaker"}: select {remaining} more Attribute{(remaining == 1 ? "" : "s")}. " +
            "Your opponent then selects 1 of your 2; every face-up monster with it is destroyed.";

        static void LogSelection(DuelEngine engine, EngineState st) =>
            engine.Log($"{st.EarthshakerSource?.Name ?? "Effect"}: {st.EarthshakerActivator?.Name} selects " +
                       $"{string.Join(" and ", st.EarthshakerSelected)}.");

        static void OpponentSelects(DuelEngine engine, EngineState st)
        {
            st.EarthshakerStep = 2;
            var who = st.EarthshakerActivator;
            var source = st.EarthshakerSource;
            var opp = engine.OpponentOf(who);
            if (opp != null && engine.IsHumanControlled(opp))
            {
                var p = new PendingActivation
                {
                    Controller = opp,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingDeclaration = true,
                    HoldsChainResolution = true,
                    TargetKind = EffectTargetKind.DeclareAttribute,
                    TargetPicksRemaining = 1,
                    DeclarationPrompt =
                        $"{source?.Name ?? "Earthshaker"}: your opponent selected " +
                        $"{string.Join(" and ", st.EarthshakerSelected)}. Select 1 — every face-up monster with " +
                        "that Attribute is destroyed."
                };
                p.LegalTargets.AddRange(AttributeTokens(st.EarthshakerSelected));
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return;
            }

            var pick = AiPickAttributeToDestroy(opp, who, st.EarthshakerSelected);
            FinishEarthshaker(engine, st, pick);
        }

        static void FinishEarthshaker(DuelEngine engine, EngineState st, string attribute)
        {
            var source = st.EarthshakerSource;
            var chooser = engine.OpponentOf(st.EarthshakerActivator);
            st.EarthshakerActivator = null;
            st.EarthshakerSource = null;
            st.EarthshakerSelected.Clear();
            st.EarthshakerStep = 0;
            if (string.IsNullOrEmpty(attribute)) return;
            var victims = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m?.Def == null || !m.FaceUp) continue;
                    if (!string.Equals(m.Attribute?.Trim(), attribute, StringComparison.OrdinalIgnoreCase)) continue;
                    victims.Add(m);
                }
            }

            engine.Log($"{source?.Name ?? "Effect"}: {chooser?.Name} selects {attribute} — destroy every face-up " +
                       $"{attribute} monster ({victims.Count}).");
            // Destroyed at the same time: their "sent to the GY" triggers activate once all of them are in the GY, so a
            // flipped Spear Cretin destroyed with them can select any of them (FlipMassEffects; v142 cross-group audit).
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in victims)
                    TextEffectRuntime.DestroyCard(engine, m, source);
            });
            if (victims.Count > 0)
                DuelPresentationPacer.Extend(DuelPresentationPacer.ReadAfterCombatResult,
                    "Monsters destroyed — read the field…");
        }

        /// <summary>
        /// AI "use it or lose it" (SpellTrapEffects.AiAutoRespond chains a threatened Set card): false for a card
        /// with Earthshaker's effect unless it is worth it — the opponent keeps the worse of the AI's 2 Attributes,
        /// so the second-best Attribute must still destroy more of the opponent's face-up ATK than the AI's own.
        /// True for every other card.
        /// </summary>
        public static bool AiWorthChainingBeforeLoss(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            if (prog == null || !prog.ClauseList.Exists(c =>
                    c != null && c.Action == EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo))
                return true;
            var present = FaceUpAttributes(engine);
            if (present.Count < 2) return false;
            var opp = engine.OpponentOf(ai);
            var worst = AiSelectTwoAttributes(engine, ai, present)
                .Min(a => FaceUpAtkOfAttribute(opp, a) - FaceUpAtkOfAttribute(ai, a));
            return worst > 0;
        }

        /// <summary>
        /// AI activator: the 2 Attributes whose face-up monsters are worth most to the opponent and least to the
        /// AI (the opponent will keep the worse one for the AI, so both should be good).
        /// </summary>
        static IEnumerable<string> AiSelectTwoAttributes(DuelEngine engine, DuelistState who, List<string> present)
        {
            var opp = engine.OpponentOf(who);
            return present
                .Select((a, i) => (a, i, v: FaceUpAtkOfAttribute(opp, a) - FaceUpAtkOfAttribute(who, a)))
                .OrderByDescending(x => x.v).ThenBy(x => x.i)
                .Take(2)
                .OrderBy(x => x.i)
                .Select(x => x.a)
                .ToList();
        }

        /// <summary>AI chooser: of the 2 Attributes, the one that costs the activator most and the AI least.</summary>
        static string AiPickAttributeToDestroy(DuelistState chooser, DuelistState activator, List<string> two)
        {
            string best = null;
            var bestValue = long.MinValue;
            foreach (var a in two)
            {
                var v = FaceUpAtkOfAttribute(activator, a) - FaceUpAtkOfAttribute(chooser, a);
                if (v <= bestValue) continue;
                bestValue = v;
                best = a;
            }

            return best;
        }

        static long FaceUpAtkOfAttribute(DuelistState side, string attribute)
        {
            if (side == null || string.IsNullOrEmpty(attribute)) return 0;
            long sum = 0;
            foreach (var m in side.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp) continue;
                if (!string.Equals(m.Attribute?.Trim(), attribute, StringComparison.OrdinalIgnoreCase)) continue;
                sum += Math.Max(1, m.CurrentAtk);
            }

            return sum;
        }

        // ------------------------------------------------------------------ player picks

        /// <summary>A human picked an Earthshaker Attribute or an upkeep Tribute.</summary>
        public static bool FinishPick(DuelEngine engine, PendingActivation p, CardInstance pick)
        {
            if (engine == null || p == null || pick == null || !p.LegalTargets.Contains(pick)) return false;
            if (p.AwaitingUpkeepTribute)
            {
                engine.ClearPendingActivation();
                if (p.Controller != null && p.Card != null)
                    StateOf(engine).UpkeepDecided.Add((p.Controller, p.Card, pick));
                ContinueStandby(engine);
                return true;
            }

            if (!p.AwaitingDeclaration || !IsAttributeToken(pick)) return false;
            var st = StateOf(engine);
            var attribute = pick.Def.attribute;
            if (st.EarthshakerStep == 1)
            {
                st.EarthshakerSelected.Add(attribute);
                p.LegalTargets.Remove(pick);
                p.ChosenTargets.Add(pick);
                p.TargetPicksRemaining--;
                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    p.DeclarationPrompt = SelectTwoPrompt(p.Card, p.TargetPicksRemaining);
                    engine.Log($"{p.Card?.Name}: {attribute} selected.");
                    engine.NotifyPublic();
                    return true;
                }

                engine.ClearPendingActivation();
                LogSelection(engine, st);
                OpponentSelects(engine, st);
            }
            else
            {
                engine.ClearPendingActivation();
                FinishEarthshaker(engine, st, attribute);
            }

            // A trigger opened by the destruction (Sangan) is answered while the rest of the chain resolves, as
            // for any destruction inside a resolving chain; only another held selection keeps the chain waiting.
            if (engine.PendingActivation == null || !engine.PendingActivation.HoldsChainResolution)
                engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Cancel: the upkeep is declined (the card is destroyed); an Earthshaker selection is mandatory and keeps
        /// waiting.
        /// </summary>
        public static bool CancelPick(DuelEngine engine, PendingActivation p)
        {
            if (engine == null || p == null) return false;
            if (IsMandatoryUpkeepPick(p))
            {
                engine.Log($"{p.Card?.Name ?? "Effect"}: this Tribute is mandatory — tap a monster to Tribute.");
                engine.NotifyPublic();
                return false;
            }

            if (p.AwaitingUpkeepTribute)
            {
                engine.ClearPendingActivation();
                if (p.Controller != null && p.Card != null)
                {
                    engine.Log($"{p.Card.Name}: {p.Controller.Name} does not Tribute.");
                    StateOf(engine).UpkeepDecided.Add((p.Controller, p.Card, null));
                }

                ContinueStandby(engine);
                return true;
            }

            engine.Log($"{p.Card?.Name ?? "Effect"}: this selection is mandatory — tap an Attribute.");
            engine.NotifyPublic();
            return false;
        }

        /// <summary>
        /// After an upkeep pick: ask the next upkeep, or apply them all and finish the Standby Phase (a trigger
        /// opened by a Tribute is answered after that, as any Standby Phase trigger's pick is).
        /// </summary>
        static void ContinueStandby(DuelEngine engine)
        {
            RunStandbyUpkeep(engine);
            if (engine.PendingActivation == null || !engine.PendingActivation.AwaitingUpkeepTribute)
                engine.ResumeStandbyAfterUpkeep();
            engine.NotifyPublic();
        }
    }
}
