using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Official-text scripts for main-deck monster effects used in starter / lab decks.
    /// Pattern mirrors open-source YGOPro Lua (initial_effect / flip / to-GY triggers)
    /// under <c>~/ygopro-scripts</c> — resolution only for registered IDs.
    ///
    /// Source texts: StreamingAssets/Cards/cards_db.json (Konami-aligned).
    /// </summary>
    public static class MonsterEffects
    {
        public const int ManEaterBug = 54652250;
        public const int MagicianOfFaith = 31560081;
        public const int Sangan = 26202165;
        public const int Kuriboh = 40640057;
        public const int CyberJar = 34124316;
        public const int LordOfD = 17985575;
        public const int AbyssSoldier = 18318842;
        /// <summary>
        /// Amphibious Bugroth MK-3 (64342551) — Yugipedia / YGOPro c64342551.lua:
        /// While "Umi" is face-up (A Legendary Ocean is always treated as Umi),
        /// this card can attack directly. Continuous; not a Chain Link.
        /// </summary>
        public const int AmphibiousBugrothMk3 = 64342551;
        /// <summary>
        /// Star Boy (8201910) — Yugipedia / YGOPro c8201910.lua:
        /// Face-up continuous: all WATER +500 ATK, all FIRE −400 ATK (both fields).
        /// </summary>
        public const int StarBoy = 8201910;
        /// <summary>
        /// Mermaid Knight (24435369) — Yugipedia / YGOPro c24435369.lua:
        /// While "Umi" is face-up, this card can attack twice (EFFECT_EXTRA_ATTACK 1).
        /// </summary>
        public const int MermaidKnight = 24435369;

        public static bool IsFlipEffectMonster(int cardId) =>
            cardId == ManEaterBug || cardId == MagicianOfFaith || cardId == CyberJar;

        public static bool IsRegisteredMonsterEffect(int cardId) =>
            IsFlipEffectMonster(cardId) || cardId == Sangan || cardId == Kuriboh || cardId == LordOfD ||
            cardId == AbyssSoldier;

        // ── Hand Quick Effects during Damage Calculation (official PSCT) ─────

        /// <summary>
        /// Kuriboh (40640057) — Konami / cards_db:
        /// "During damage calculation, if your opponent's monster attacks (Quick Effect):
        ///  You can discard this card; you take no battle damage from that battle."
        /// </summary>
        public static bool IsLegalHandDamageCalculationEffect(
            DuelistState who,
            CardInstance card,
            ResponseTiming timing,
            DuelistState attackingPlayer,
            CardInstance attacker)
        {
            if (who == null || card?.Def == null) return false;
            if (!who.Hand.Contains(card)) return false;
            if (timing != ResponseTiming.DamageCalculation) return false;

            switch (card.CardId)
            {
                case Kuriboh:
                    // "if your opponent's monster attacks" — attacker controller ≠ who
                    if (attackingPlayer == null || attackingPlayer == who) return false;
                    if (attacker == null) return false;
                    return true;
                default:
                    return false;
            }
        }

        public static void CollectLegalHandDamageCalculation(
            DuelistState who,
            ResponseTiming timing,
            DuelistState attackingPlayer,
            CardInstance attacker,
            List<CardInstance> into)
        {
            if (who?.Hand == null || into == null) return;
            foreach (var c in who.Hand)
            {
                if (c != null &&
                    IsLegalHandDamageCalculationEffect(who, c, timing, attackingPlayer, attacker))
                    into.Add(c);
            }
        }

        /// <summary>
        /// Activate Kuriboh (or other DC hand QEs). Cost before semicolon: discard this card.
        /// Operation: take no battle damage from that battle.
        /// </summary>
        public static bool TryActivateHandDamageCalculation(
            DuelEngine engine, DuelistState who, CardInstance card)
        {
            var pr = engine?.PendingResponse;
            if (pr == null ||
                !IsLegalHandDamageCalculationEffect(who, card, pr.Timing, pr.AttackingPlayer, pr.Attacker))
            {
                engine?.Log($"{card?.Name ?? "Card"} is not legal during damage calculation.");
                return false;
            }

            if (pr.Responder != who)
            {
                engine.Log("Not your response window.");
                return false;
            }

            switch (card.CardId)
            {
                case Kuriboh:
                {
                    // Cost before semicolon: discard this card (to GY).
                    // Resolve by InstanceId so UI/intent clones still hit the real hand card.
                    var discarded = DiscardFromHandByInstance(who, card);
                    if (discarded == null)
                    {
                        engine.Log("Kuriboh: discard cost failed (not in hand).");
                        return false;
                    }

                    // Operation: no battle damage from that battle
                    who.PreventBattleDamageThisBattle = true;
                    engine.Log(
                        $"{who.Name} activates Kuriboh (Quick Effect)! " +
                        $"Discard {discarded.Name} → GY; take no battle damage from that battle. " +
                        $"[Hand={who.Hand.Count} GY={who.Graveyard.Count}] " +
                        "[Konami: During damage calculation, if your opponent's monster attacks.]");
                    // Non-negating: continue Damage Step resolution
                    engine.ContinueAfterResponseActivation(attackNegated: false, battlePhaseEnded: false);
                    engine.NotifyPublic();
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Remove a hand card by reference or InstanceId and place it in GY.
        /// Returns the instance that was discarded, or null if not found.
        /// </summary>
        /// <summary>
        /// Ignition effects while face-up on the field (Main Phase, open game state).
        /// Abyss Soldier — official: "Once per turn: You can discard 1 WATER monster to the Graveyard
        /// to target 1 card on the field; return it to the hand."
        /// </summary>
        public static bool CanActivateIgnition(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            if (engine.GameOver || engine.TurnPlayer != who || !engine.InMainPhase) return false;
            if (engine.PendingActivation != null || engine.PendingResponse != null) return false;
            if (engine.HasDeclaredAttack) return false;
            if (!who.TryFindMonster(card, out _) || !card.FaceUp) return false;
            if (card.EffectUsedThisTurn) return false;

            switch (card.CardId)
            {
                case AbyssSoldier:
                    if (!HandHasAttributeMonster(who, "WATER")) return false;
                    return CollectBounceTargets(engine, who).Count > 0;
                default:
                    return false;
            }
        }

        public static bool TryActivateIgnition(
            DuelEngine engine, DuelistState who, CardInstance card, bool autoPick)
        {
            if (!CanActivateIgnition(engine, who, card)) return false;
            if (card.CardId != AbyssSoldier) return false;

            var costs = CollectHandAttributeMonsters(who, "WATER");
            var targets = CollectBounceTargets(engine, who);
            if (costs.Count == 0 || targets.Count == 0) return false;

            engine.Log($"Activate: {card.Name} (Ignition).");
            if (autoPick || !who.IsPlayer)
            {
                var cost = costs.OrderBy(c => c.CurrentAtk).First();
                var discarded = DiscardFromHandByInstance(who, cost);
                if (discarded == null) return false;
                engine.Log($"Cost: discard {discarded.Name}.");
                targets = CollectBounceTargets(engine, who);
                if (targets.Count == 0)
                {
                    card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                var opp = engine.OpponentOf(who);
                var pick = targets.FirstOrDefault(t => opp.TryFindMonster(t, out _))
                           ?? targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _))
                           ?? targets[0];
                engine.ReturnCardToHand(pick);
                card.EffectUsedThisTurn = true;
                engine.NotifyPublic();
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.DiscardMonsterInHand,
                IsMonsterEffect = true,
                UsesTextProgram = false,
                AwaitingDiscardCost = true,
                DiscardCostAttribute = "WATER"
            };
            pending.LegalTargets.AddRange(costs);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveIgnitionTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine?.PendingActivation;
            if (p == null || p.UsesTextProgram || !p.IsMonsterEffect || p.Card == null)
                return false;
            if (p.Card.CardId != AbyssSoldier) return false;
            var resolved = engine.ResolveLegalEffectTarget(target) ?? target;
            if (!p.LegalTargets.Contains(resolved)) return false;

            var who = p.Controller;
            var card = p.Card;
            if (p.AwaitingDiscardCost)
            {
                var discarded = DiscardFromHandByInstance(who, resolved);
                if (discarded == null) return false;
                engine.Log($"Cost: discard {discarded.Name}.");
                var bounce = CollectBounceTargets(engine, who);
                if (bounce.Count == 0)
                {
                    engine.ClearPendingActivation();
                    card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                p.AwaitingDiscardCost = false;
                p.TargetKind = EffectTargetKind.AnyCardOnField;
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(bounce);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            engine.ClearPendingActivation();
            engine.ReturnCardToHand(resolved);
            card.EffectUsedThisTurn = true;
            engine.NotifyPublic();
            return true;
        }

        static bool HandHasAttributeMonster(DuelistState who, string attribute) =>
            CollectHandAttributeMonsters(who, attribute).Count > 0;

        static List<CardInstance> CollectHandAttributeMonsters(DuelistState who, string attribute)
        {
            var list = new List<CardInstance>();
            if (who?.Hand == null) return list;
            foreach (var c in who.Hand)
            {
                if (c?.Def == null || !c.Def.IsMonster) continue;
                if (string.IsNullOrEmpty(attribute) ||
                    (c.Def.attribute != null &&
                     c.Def.attribute.Equals(attribute, System.StringComparison.OrdinalIgnoreCase)))
                    list.Add(c);
            }

            return list;
        }

        static List<CardInstance> CollectBounceTargets(DuelEngine engine, DuelistState who)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null) return list;
            var opp = engine.OpponentOf(who);
            void AddMonsters(DuelistState side)
            {
                foreach (var m in side.MonstersOnField())
                {
                    if (engine.IsDragonTargetProtected(m)) continue;
                    list.Add(m);
                }
            }

            AddMonsters(who);
            AddMonsters(opp);
            foreach (var st in who.SpellTrapsOnField())
                list.Add(st);
            foreach (var st in opp.SpellTrapsOnField())
                list.Add(st);
            return list;
        }

        public static CardInstance DiscardFromHandByInstance(DuelistState who, CardInstance card)
        {
            if (who?.Hand == null || card == null) return null;
            if (who.Hand.Remove(card))
            {
                who.Graveyard.Add(card);
                return card;
            }

            for (var i = 0; i < who.Hand.Count; i++)
            {
                var c = who.Hand[i];
                if (c == null || c.InstanceId != card.InstanceId) continue;
                who.Hand.RemoveAt(i);
                who.Graveyard.Add(c);
                return c;
            }

            return null;
        }

        /// <summary>
        /// After a successful Flip Summon (or flip by effect that should trigger Flip),
        /// resolve the Flip effect when scripted.
        /// </summary>
        public static void OnFlipSummoned(DuelEngine engine, DuelistState controller, CardInstance monster)
        {
            if (engine == null || controller == null || monster?.Def == null) return;

            // Registered Flip scripts first — never silent-skip via incomplete text apply
            switch (monster.CardId)
            {
                case ManEaterBug:
                    BeginManEaterBug(engine, controller, monster);
                    return;
                case MagicianOfFaith:
                    BeginMagicianOfFaith(engine, controller, monster);
                    return;
                case CyberJar:
                    ResolveCyberJarPublic(engine, controller);
                    return;
            }

            // Learn text on first flip, then run remembered program (other Flip monsters)
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(monster);
            if (prog != null && prog.HasTiming(TextEffects.EffectTiming.Flip))
                TextEffects.TextEffectRuntime.TryResolveFlip(engine, controller, monster, prog);
        }

        /// <summary>Text-effect runtime entry (no re-entry into text flip path).</summary>
        public static void ResolveCyberJarPublic(DuelEngine engine, DuelistState flipController) =>
            ResolveCyberJar(engine, flipController);

        /// <summary>When a monster leaves the field for the GY (destroy, tribute, fusion material, etc.).</summary>
        public static void OnSentFromFieldToGy(DuelEngine engine, DuelistState owner, CardInstance card,
            bool destroyed = false, bool destroyedByBattle = false, CardInstance battleDestroyer = null)
        {
            if (engine == null || owner == null || card == null) return;

            // Registered field→GY scripts first (never silent-skip via empty text apply)
            if (card.CardId == Sangan)
            {
                ResolveSangan(engine, owner, card);
                return;
            }

            var prog = TextEffects.CompiledEffectCache.GetOrCompile(card);
            if (prog != null && prog.HasTiming(TextEffects.EffectTiming.SentFromFieldToGy))
            {
                if (TextEffects.TextEffectRuntime.TryResolveSentToGy(engine, owner, card, prog,
                        destroyed, destroyedByBattle, battleDestroyer))
                    return;
            }
        }

        static void BeginManEaterBug(DuelEngine engine, DuelistState who, CardInstance source)
        {
            // FLIP: Target 1 monster on the field; destroy it.
            // Lord of D. continuous: cannot target Dragon monsters (including Lord of D. himself).
            var targets = CollectManEaterTargets(engine, who, source);

            if (targets.Count == 0)
            {
                engine.Log(
                    "Man-Eater Bug Flip: no legal targets " +
                    "(Dragons protected by face-up Lord of D. cannot be targeted).");
                return;
            }

            if (!who.IsPlayer)
            {
                // AI: destroy highest ATK opponent monster, else any legal
                var opp = engine.OpponentOf(who);
                var pick = targets.Where(t => opp.TryFindMonster(t, out _))
                               .OrderByDescending(t => t.CurrentAtk)
                               .FirstOrDefault()
                           ?? targets.OrderByDescending(t => t.CurrentAtk).First();
                engine.Log($"Man-Eater Bug destroys {pick.Name}!");
                DestroyFieldMonster(engine, pick);
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.AnyMonsterOnField,
                IsMonsterEffect = true
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            engine.Log(
                "Man-Eater Bug FLIP: choose a monster on the field to destroy " +
                "(Dragons protected by Lord of D. are not legal).");
            engine.NotifyPublic();
        }

        static List<CardInstance> CollectManEaterTargets(DuelEngine engine, DuelistState who,
            CardInstance source)
        {
            var targets = new List<CardInstance>();
            var skippedDragons = 0;
            void Consider(CardInstance m)
            {
                if (m == null || m == source) return;
                if (engine.IsDragonTargetProtected(m))
                {
                    skippedDragons++;
                    return;
                }

                targets.Add(m);
            }

            foreach (var m in who.MonstersOnField())
                Consider(m);
            foreach (var m in engine.OpponentOf(who).MonstersOnField())
                Consider(m);
            if (skippedDragons > 0)
                engine.Log(
                    $"[RULE] Man-Eater Bug: {skippedDragons} Dragon(s) illegal to target " +
                    "(face-up Lord of D. protection — including Lord of D. himself).");
            return targets;
        }

        static void BeginMagicianOfFaith(DuelEngine engine, DuelistState who, CardInstance source)
        {
            // FLIP: Target 1 Spell in your GY; add that target to your hand.
            var spells = who.Graveyard.Where(c => c?.Def != null && c.Def.IsSpell).ToList();
            if (spells.Count == 0)
            {
                engine.Log("Magician of Faith Flip: no Spells in your GY.");
                return;
            }

            if (!who.IsPlayer)
            {
                var pick = spells[0];
                who.Graveyard.Remove(pick);
                who.Hand.Add(pick);
                engine.Log($"Magician of Faith adds {pick.Name} from GY to hand.");
                engine.NotifyPublic();
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.SpellInYourGy,
                IsMonsterEffect = true
            };
            pending.LegalTargets.AddRange(spells);
            engine.SetPendingActivation(pending);
            engine.Log("Magician of Faith FLIP: choose a Spell in your GY to add to hand.");
            engine.NotifyPublic();
        }

        /// <summary>Resolve Man-Eater Bug / Magician of Faith / Sangan target choice.</summary>
        public static bool TryResolveMonsterTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.IsMonsterEffect || target == null) return false;
            var resolved = engine.ResolveLegalEffectTarget(target);
            if (resolved == null) return false;
            target = resolved;

            var who = p.Controller;
            var kind = p.TargetKind;
            engine.ClearPendingActivation();

            switch (kind)
            {
                case EffectTargetKind.AnyMonsterOnField:
                    engine.Log($"Man-Eater Bug destroys {target.Name}!");
                    DestroyFieldMonster(engine, target);
                    break;
                case EffectTargetKind.SpellInYourGy:
                    if (!who.Graveyard.Contains(target) || target.Def == null || !target.Def.IsSpell)
                    {
                        engine.Log("Magician of Faith: illegal target.");
                        break;
                    }

                    who.Graveyard.Remove(target);
                    who.Hand.Add(target);
                    engine.Log($"Magician of Faith adds {target.Name} to hand.");
                    break;
                case EffectTargetKind.MonsterInYourDeckAtkLeq:
                {
                    // Proxies are not in Deck — resolve by CardId
                    var id = target.CardId;
                    if (target.Def == null || !target.Def.IsMonster ||
                        target.Def.atk < 0 || target.Def.atk > 1500 || target.Def.IsExtraDeck)
                    {
                        engine.Log("Sangan: illegal deck search target.");
                        break;
                    }

                    if (!AddCardIdFromDeckToHand(engine, who, id, "Sangan"))
                        engine.Log("Sangan: failed to add from Deck (copy missing).");
                    break;
                }
            }

            engine.NotifyPublic();
            return true;
        }

        static void ResolveCyberJar(DuelEngine engine, DuelistState flipController)
        {
            // Official (simplified structural resolution matching PSCT flow):
            // Destroy all monsters, then each player reveals top 5; SS Level 4 or lower;
            // other revealed cards to GY.
            engine.Log("Cyber Jar FLIP: Destroy all monsters on the field!");
            var all = engine.Player.MonstersOnField().Concat(engine.Opponent.MonstersOnField()).ToList();
            foreach (var m in all)
            {
                var owner = engine.Player.TryFindMonster(m, out _) ? engine.Player : engine.Opponent;
                engine.DestroyMonsterPublic(owner, m);
            }

            RevealAndSummonFromDeck(engine, engine.Player, 5);
            RevealAndSummonFromDeck(engine, engine.Opponent, 5);
            engine.NotifyPublic();
        }

        static void RevealAndSummonFromDeck(DuelEngine engine, DuelistState who, int n)
        {
            if (who == null || engine == null) return;
            var revealed = new List<CardInstance>();
            for (var i = 0; i < n && who.Deck.Count > 0; i++)
            {
                var id = who.Deck[0];
                who.Deck.RemoveAt(0);
                revealed.Add(engine.CreateCardInstance(id));
            }

            if (revealed.Count == 0) return;
            engine.Log($"{who.Name} reveals: {string.Join(", ", revealed.Select(c => c.Name))}");

            foreach (var c in revealed)
            {
                if (c.Def != null && c.Def.IsMonster && c.Level <= 4 && c.Level >= 1 &&
                    !c.Def.IsExtraDeck)
                {
                    if (engine.FirstEmpty(who.MonsterZones) >= 0 &&
                        engine.SpecialSummonToField(who, c, BattlePosition.Attack, faceUp: true))
                    {
                        engine.Log($"Cyber Jar Special Summons {c.Name}!");
                        continue;
                    }
                }

                who.Graveyard.Add(c);
            }
        }

        /// <summary>
        /// Sangan (26202165) — official:
        /// "If this card is sent from the field to the GY: Add 1 monster with 1500 or less ATK
        /// from your Deck to your hand…"
        /// Player: choose from highlighted deck options. AI: auto-pick lowest ATK legal.
        /// </summary>
        static void ResolveSangan(DuelEngine engine, DuelistState owner, CardInstance sangan)
        {
            const int maxAtk = 1500;
            var legal = CollectDeckMonstersAtkLeq(engine, owner, maxAtk);
            if (legal.Count == 0)
            {
                engine.Log(
                    $"Sangan: no legal monster (≤{maxAtk} ATK) left in Deck " +
                    $"(deck={owner.DeckCount}).");
                engine.NotifyPublic();
                return;
            }

            // AI / non-player: resolve immediately (lowest ATK, then name)
            if (!owner.IsPlayer)
            {
                var pick = legal
                    .OrderBy(c => c.Def != null ? c.Def.atk : 9999)
                    .ThenBy(c => c.Name)
                    .First();
                if (AddCardIdFromDeckToHand(engine, owner, pick.CardId, "Sangan"))
                    engine.NotifyPublic();
                return;
            }

            // Player: open target chooser (same panel as Magician of Faith / MEB)
            var pending = new PendingActivation
            {
                Controller = owner,
                Card = sangan,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.MonsterInYourDeckAtkLeq,
                IsMonsterEffect = true
            };
            pending.LegalTargets.AddRange(legal);
            engine.SetPendingActivation(pending);
            engine.Log(
                $"Sangan TRIGGER: choose a monster (≤{maxAtk} ATK) from your Deck " +
                $"({legal.Count} options) to add to hand.");
            engine.NotifyPublic();
        }

        /// <summary>Proxy CardInstances for each unique legal Deck search hit (UI targets).</summary>
        public static List<CardInstance> CollectDeckMonstersAtkLeq(
            DuelEngine engine, DuelistState owner, int maxAtk)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            for (var i = 0; i < owner.Deck.Count; i++)
            {
                var id = owner.Deck[i];
                if (!seen.Add(id)) continue;
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster)
                    continue;
                if (def.atk < 0 || def.atk > maxAtk || def.IsExtraDeck) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        /// <summary>Remove one copy of <paramref name="cardId"/> from Deck and add a new instance to hand.</summary>
        public static bool AddCardIdFromDeckToHand(
            DuelEngine engine, DuelistState owner, int cardId, string effectName)
        {
            if (engine == null || owner?.Deck == null) return false;
            var idx = owner.Deck.IndexOf(cardId);
            if (idx < 0)
            {
                engine.Log($"{effectName}: {cardId} no longer in Deck.");
                return false;
            }

            owner.Deck.RemoveAt(idx);
            var inst = engine.CreateCardInstance(cardId);
            owner.Hand.Add(inst);
            engine.Log(
                $"{effectName}: add {inst.Name} (ATK {(inst.Def != null ? inst.Def.atk : 0)}) " +
                $"from Deck to hand. Hand={owner.HandCount} Deck={owner.DeckCount}.");
            // Soft: "cannot activate that name rest of turn" not fully enforced yet
            return true;
        }

        static void DestroyFieldMonster(DuelEngine engine, CardInstance m)
        {
            if (engine.Player.TryFindMonster(m, out _))
                engine.DestroyMonsterPublic(engine.Player, m);
            else if (engine.Opponent.TryFindMonster(m, out _))
                engine.DestroyMonsterPublic(engine.Opponent, m);
        }
    }
}
