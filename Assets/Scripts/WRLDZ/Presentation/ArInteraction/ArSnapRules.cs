using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// AR disk snap gate: validation is 100% rules-engine owned.
    /// Holograms update only after <see cref="Commit"/> succeeds — never override game state.
    /// </summary>
    public static class ArSnapRules
    {
        public struct SnapResult
        {
            public bool Ok;
            public string Reason;
            public ArZoneOrientation Orientation;
            public bool CommittedToEngine;
            public string OfficialText;
        }

        public static bool IsFieldSpell(CardDef def) => def != null && def.IsFieldSpell;

        public static bool IsPendulumMonster(CardDef def) =>
            def != null && def.IsMonster &&
            def.type != null &&
            def.type.IndexOf("Pendulum", System.StringComparison.OrdinalIgnoreCase) >= 0;

        static RulesZoneKind ToRulesZone(ArDuelZoneKind k) => k switch
        {
            ArDuelZoneKind.Monster => RulesZoneKind.Monster,
            ArDuelZoneKind.SpellTrap => RulesZoneKind.SpellTrap,
            ArDuelZoneKind.FieldSpell => RulesZoneKind.FieldSpell,
            ArDuelZoneKind.PendulumLeft => RulesZoneKind.PendulumLeft,
            ArDuelZoneKind.PendulumRight => RulesZoneKind.PendulumRight,
            _ => RulesZoneKind.Monster
        };

        /// <summary>
        /// Validate against current board + official rules before any snap/hologram update.
        /// </summary>
        public static SnapResult Validate(DuelEngine engine, DuelistState who, CardInstance card,
            ArDiskZone zone, bool preferSet)
        {
            var r = new SnapResult { Ok = false, Orientation = ArZoneOrientation.FaceDownSet };
            if (zone == null)
            {
                r.Reason = "No zone.";
                return r;
            }

            // Occupied monster zones can still be legal: Tribute Summon tributes
            // the occupant (face-up or face-down) and sits in that zone.
            // Engine is sole legality authority (IDuelEngine.ValidatePlacement).
            var verdict = engine.ValidatePlacement(who, card, ToRulesZone(zone.Kind), zone.Index, preferSet);
            r.OfficialText = verdict.OfficialText;
            r.Ok = verdict.Legal;
            r.Reason = verdict.Reason;
            if (!r.Ok) return r;

            // Orientation from intent + card type (official: NS face-up ATK, Set face-down DEF)
            switch (zone.Kind)
            {
                case ArDuelZoneKind.Monster:
                    r.Orientation = preferSet
                        ? ArZoneOrientation.FaceDownSet
                        : ArZoneOrientation.FaceUpAttack;
                    break;
                case ArDuelZoneKind.SpellTrap:
                    r.Orientation = (card.Def.IsTrap || preferSet)
                        ? ArZoneOrientation.FaceDownSet
                        : ArZoneOrientation.FaceUpAttack;
                    break;
                default:
                    r.Orientation = ArZoneOrientation.FaceUpAttack;
                    break;
            }

            return r;
        }

        /// <summary>
        /// Commit only after engine validation. AR visuals update from engine state after Notify.
        /// </summary>
        /// <summary>Engine-legal drop actions for a card over a zone (no commit).</summary>
        public struct LegalDrop
        {
            public bool CanSummonAtk;
            public bool CanSetMonster;
            public bool CanActivateSpell;
            public bool CanSetSpellTrap;
            public bool CanPlaceField;
            public string Hint;
            public string BlockedReason;

            public bool Any =>
                CanSummonAtk || CanSetMonster || CanActivateSpell ||
                CanSetSpellTrap || CanPlaceField;

            public int Count
            {
                get
                {
                    var n = 0;
                    if (CanSummonAtk) n++;
                    if (CanSetMonster) n++;
                    if (CanActivateSpell) n++;
                    if (CanSetSpellTrap) n++;
                    if (CanPlaceField) n++;
                    return n;
                }
            }
        }

        public static LegalDrop QueryLegal(DuelEngine engine, DuelistState who,
            CardInstance card, ArDiskZone zone)
        {
            var d = new LegalDrop();
            if (engine == null || who == null || card?.Def == null || zone == null)
            {
                d.BlockedReason = "Cannot play this card here.";
                d.Hint = d.BlockedReason;
                return d;
            }

            var name = card.Name ?? "Card";
            var label = ArZoneLayout.ZoneDisplayLabel(zone.Kind, zone.Index);

            switch (zone.Kind)
            {
                case ArDuelZoneKind.Monster:
                    d.CanSummonAtk = Validate(engine, who, card, zone, preferSet: false).Ok;
                    d.CanSetMonster = Validate(engine, who, card, zone, preferSet: true).Ok;
                    var tribNeed = TcgRules.TributesRequired(card.Level);
                    if (!zone.IsEmpty && tribNeed > 0 && (d.CanSummonAtk || d.CanSetMonster))
                    {
                        var face = zone.Occupant != null && zone.Occupant.FaceUp
                            ? zone.Occupant.Name
                            : "face-down monster";
                        d.Hint = $"{name} → tribute {face} · Tribute Summon or Set here";
                        return d;
                    }
                    if (d.CanSummonAtk && d.CanSetMonster)
                        d.Hint = $"{name} → {label} · Normal Summon (face-up ATK) or Set (face-down DEF)";
                    else if (d.CanSummonAtk)
                        d.Hint = $"{name} → {label} · Normal Summon (face-up ATK)";
                    else if (d.CanSetMonster)
                        d.Hint = $"{name} → {label} · Set (face-down Defense)";
                    else
                    {
                        d.BlockedReason = Validate(engine, who, card, zone, false).Reason
                                          ?? "Cannot Normal Summon/Set here.";
                        d.Hint = $"{name} · {d.BlockedReason}";
                    }

                    return d;

                case ArDuelZoneKind.SpellTrap:
                    if (card.Def.IsTrap)
                    {
                        d.CanSetSpellTrap = Validate(engine, who, card, zone, preferSet: true).Ok;
                        d.Hint = d.CanSetSpellTrap
                            ? $"{name} → {label} · Set Trap (face-down)"
                            : $"{name} · {(d.BlockedReason = Validate(engine, who, card, zone, true).Reason)}";
                        return d;
                    }

                    d.CanActivateSpell = Validate(engine, who, card, zone, preferSet: false).Ok
                                         && engine.CanActivateSpellTrap(who, card, fromHand: true);
                    d.CanSetSpellTrap = Validate(engine, who, card, zone, preferSet: true).Ok;
                    if (d.CanActivateSpell && d.CanSetSpellTrap)
                        d.Hint = $"{name} → {label} · Activate or Set";
                    else if (d.CanActivateSpell)
                        d.Hint = $"{name} → {label} · Activate";
                    else if (d.CanSetSpellTrap)
                        d.Hint = $"{name} → {label} · Set Spell (face-down)";
                    else
                    {
                        d.BlockedReason = Validate(engine, who, card, zone, true).Reason
                                          ?? "Cannot play Spell/Trap here.";
                        d.Hint = $"{name} · {d.BlockedReason}";
                    }

                    return d;

                case ArDuelZoneKind.FieldSpell:
                    d.CanPlaceField = Validate(engine, who, card, zone, preferSet: false).Ok;
                    d.Hint = d.CanPlaceField
                        ? $"{name} → FIELD · Activate Field Spell"
                        : $"{name} · {(d.BlockedReason = Validate(engine, who, card, zone, false).Reason)}";
                    return d;

                default:
                    d.BlockedReason = "Not a legal zone for this card.";
                    d.Hint = d.BlockedReason;
                    return d;
            }
        }

        public static SnapResult Commit(DuelEngine engine, DuelistState who, CardInstance card,
            ArDiskZone zone, bool preferSet)
        {
            var v = Validate(engine, who, card, zone, preferSet);
            if (!v.Ok) return v;

            switch (zone.Kind)
            {
                case ArDuelZoneKind.Monster:
                {
                    var asSet = preferSet || v.Orientation == ArZoneOrientation.FaceDownSet;
                    var ok = engine.TryNormalSummonToZone(who, card, asSet, zone.Index);
                    v.CommittedToEngine = ok;
                    if (!ok) { v.Ok = false; v.Reason = "Engine rejected summon/set."; }
                    return v;
                }
                case ArDuelZoneKind.SpellTrap:
                {
                    bool ok;
                    if (!preferSet && card.Def.IsSpell && !card.Def.IsTrap &&
                        engine.CanActivateSpellTrap(who, card, fromHand: true))
                        ok = engine.TryActivateSpellTrap(who, card, fromHand: true);
                    else
                        ok = engine.TrySetSpellTrapToZone(who, card, zone.Index);
                    v.CommittedToEngine = ok;
                    if (!ok) { v.Ok = false; v.Reason = "Engine rejected Spell/Trap play."; }
                    return v;
                }
                case ArDuelZoneKind.FieldSpell:
                {
                    var ok = engine.TryPlaceFieldSpell(who, card);
                    v.CommittedToEngine = ok;
                    if (!ok) { v.Ok = false; v.Reason = "Engine rejected Field Spell."; }
                    return v;
                }
                case ArDuelZoneKind.PendulumLeft:
                case ArDuelZoneKind.PendulumRight:
                {
                    var left = zone.Kind == ArDuelZoneKind.PendulumLeft;
                    var ok = engine.TryPlacePendulum(who, card, left);
                    v.CommittedToEngine = ok;
                    if (!ok) { v.Ok = false; v.Reason = "Engine rejected Pendulum scale."; }
                    return v;
                }
            }

            return v;
        }
    }
}
