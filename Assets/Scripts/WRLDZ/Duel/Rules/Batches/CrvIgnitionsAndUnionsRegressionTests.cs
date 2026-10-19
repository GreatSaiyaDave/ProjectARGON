using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// CrvIgnitionsAndUnions batch: D.D.M. - Different Dimension Master, Patroid, Doitsu, Protective Soul Ailin. Compile
    /// shapes through the real compiler, activations through the engine entry points (TryActivateSpellTrap, the discard
    /// and target picks, response windows, turn ends, DestroyMonsterByBattle), and look-alike sentences that must stay
    /// uncompiled. Cybernetic Magician stays closed (no timestamped "set final ATK" layer yet).
    /// </summary>
    public static class CrvIgnitionsAndUnionsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int CyberneticMagician = 59023523; // Spellcaster Lv6 2400/1000
        const int Ddm = 82112775;                // Spellcaster Lv5 1700/1500
        const int Patroid = 71930383;            // Machine Lv4 1200/1200
        const int Doitsu = 57062206;             // Union, Fairy Lv4 100/200
        const int Ailin = 11678191;              // Union, Fairy Lv1 0/0
        const int Soitsu = 60246171;             // Normal, Fairy 0/0
        const int LeiLei = 84173492;             // Beast-Warrior 2300/0

        const int SummonedSkull = 70781052;      // Fiend Normal 2500/1200
        const int CelticGuardian = 91152256;     // Warrior Normal 1400/1200
        const int MysticalElf = 15025844;        // Spellcaster Normal 800/2000
        const int Kuriboh = 40640057;
        const int PotOfGreed = 55144522;         // Normal Spell (discard fodder / a Set card)

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Chk Check = (name, ok, detail) => check("CrvIgnitionsAndUnions: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(Check);
            CyberneticMagicianStaysClosed(db, Check);
            DdmDuels(db, pDeck, aDeck, Check);
            PatroidDuels(db, pDeck, aDeck, Check);
            DoitsuDuels(db, pDeck, aDeck, Check);
            AilinDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>The Player's opening Main Phase, empty boards, hands and GYs.</summary>
        static DuelEngine Board(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static void Settle(DuelEngine e)
        {
            e.NotifyPublic();
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        /// <summary>The open pick, when one opened (a lone legal choice may resolve with no prompt).</summary>
        static bool Pick(DuelEngine e, CardInstance c)
        {
            if (e.IsAwaitingEffectTarget)
            {
                var pend = e.PendingActivation;
                if (pend == null || !pend.LegalTargets.Contains(c)) return false;
                if (!e.TrySelectEffectTarget(c)) return false;
            }

            Settle(e);
            return true;
        }

        static CardInstance Banish(DuelEngine e, DuelistState who, int id, bool faceUp)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = faceUp;
            who.Banished.Add(c);
            return c;
        }

        static bool InSt(DuelistState who, CardInstance c) => who.SpellTrapZones.Any(z => z.Occupant == c);
        static bool InMz(DuelistState who, CardInstance c) => who.MonsterZones.Any(z => z.Occupant == c);

        static bool EquipUnion(DuelEngine e, CardInstance union, CardInstance host)
        {
            var used = e.TryActivateSpellTrap(e.Player, union, fromHand: false);
            Settle(e);
            if (e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(host))
                e.TrySelectEffectTarget(host);
            Settle(e);
            FieldSpellEffects.RefreshBoard(e);
            return used && union.EquippedTo == host && InSt(e.Player, union);
        }

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [" + string.Join("; ", prog.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action}/{c.Zone} amt={c.Amount} disc={c.RequiresDiscardCost}:{c.DiscardCostAttribute} " +
                    $"opt={c.OncePerTurn} host={c.EquipHostName} bonus={c.EquipAtkBonus}/{c.EquipDefBonus}")) + "] " +
                  $"unparsed=[{string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}]";

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            CompiledCardProgram P(int id) => db.Get(id) != null ? CardTextEffectCompiler.Compile(db.Get(id)) : null;
            bool Implemented(int id) =>
                db.Get(id) != null && CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;
            EffectClause Find(CompiledCardProgram p, EffectActionKind a) =>
                p?.ClauseList.Find(c => c != null && c.Action == a);

            var ddm = P(Ddm);
            var ddmc = Find(ddm, EffectActionKind.SpecialSummonBanishedTarget);
            Check("D.D.M.: once per turn, discard 1 Spell (cost), target 1 of your face-up banished monsters, Special Summon it",
                ddm != null && ddm.FullyCompiled && ddm.ClauseList.Count == 1 && ddmc != null &&
                ddmc.Timing == EffectTiming.Activate && ddmc.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters &&
                ddmc.RequiresTargetChoice && ddmc.IsPsctTarget && ddmc.RequiresDiscardCost &&
                ddmc.DiscardCostAttribute == FaceUpQuickEffectsRuntime.DiscardSpellCard && ddmc.OncePerTurn &&
                ddmc.MakesChainLink && Implemented(Ddm), Describe(ddm));

            var pat = P(Patroid);
            var patc = Find(pat, EffectActionKind.LookAtTargetSetCard);
            Check("Patroid: once per turn ignition, target 1 face-down card the opponent controls and look at it",
                pat != null && pat.FullyCompiled && pat.ClauseList.Count == 1 && patc != null &&
                patc.Timing == EffectTiming.Activate && patc.Zone == EffectZoneFilter.OppAnyCardOnField &&
                patc.RequiresTargetFaceDown && patc.RequiresTargetChoice && patc.IsPsctTarget && patc.OncePerTurn &&
                !patc.RequiresDiscardCost && patc.MakesChainLink && Implemented(Patroid), Describe(pat));

            var doi = P(Doitsu);
            var doiEq = Find(doi, EffectActionKind.EquipThisToTarget);
            Check("Doitsu: the CRV Union procedure on \"Soitsu\" (equip / unequip to Special Summon), +2500 ATK in Union state",
                doi != null && doi.FullyCompiled && doi.ClauseList.Count == 2 && doiEq != null &&
                doiEq.EquipHostName == "Soitsu" && doiEq.EquipAtkBonus == 2500 && doiEq.EquipDefBonus == 0 &&
                doiEq.OncePerTurn && Find(doi, EffectActionKind.UnequipThisSpecialSummon) != null &&
                Implemented(Doitsu), Describe(doi));

            var ail = P(Ailin);
            var ailEq = Find(ail, EffectActionKind.EquipThisToTarget);
            var ailRider = Find(ail, EffectActionKind.ChangeEquippedMonsterBattlePosition);
            Check("Protective Soul Ailin: the CRV Union procedure on \"Indomitable Fighter Lei Lei\" + Second Goblin's " +
                  "once-per-turn position change of the equipped monster",
                ail != null && ail.FullyCompiled && ail.ClauseList.Count == 3 && ailEq != null &&
                ailEq.EquipHostName == "Indomitable Fighter Lei Lei" && ailEq.EquipAtkBonus == 0 &&
                Find(ail, EffectActionKind.UnequipThisSpecialSummon) != null && ailRider != null &&
                ailRider.Timing == EffectTiming.UnionStateIgnition && ailRider.OncePerTurn && Implemented(Ailin),
                Describe(ail));
        }

        static void NearMisses(Chk Check)
        {
            var near = new (string Desc, string Type, string Race, EffectActionKind Action)[]
            {
                ("Once per turn, you can discard 1 Spell Card to Special Summon 1 of your removed from play monsters in " +
                 "face-up Defense Position.",
                    "Effect Monster", "Spellcaster", EffectActionKind.SpecialSummonBanishedTarget),
                ("Once per turn, during your Main Phase, you can look at 1 face-down card your opponent controls, then " +
                 "destroy it.",
                    "Effect Monster", "Machine", EffectActionKind.LookAtTargetSetCard),
                ("Once per turn, you can equip this card to a \"Soitsu\" you control, OR unequip it to Special Summon this " +
                 "card in face-up Defense Position.",
                    "Union Effect Monster", "Fairy", EffectActionKind.UnequipThisSpecialSummon),
                ("Once per turn, you can equip this card to an \"Indomitable Fighter Lei Lei\" you control, OR unequip it to " +
                 "Special Summon this card in face-up Attack Position. When equipped to a monster by this card's effect, you " +
                 "can change that monster's battle position twice per turn.",
                    "Union Effect Monster", "Fairy", EffectActionKind.ChangeEquippedMonsterBattlePosition),
            };
            var leaked = near.Where((n, i) =>
            {
                var def = new CardDef
                {
                    id = 90918300 + i, name = "Synthetic CrvIgnitionsAndUnions near miss " + i, type = n.Type,
                    race = n.Race, desc = n.Desc
                };
                var p = CardTextEffectCompiler.Compile(def);
                return p != null && (p.FullyCompiled || p.ClauseList.Exists(c => c.Action == n.Action));
            }).Select(n => n.Action.ToString()).ToList();
            Check("Near misses: look-alike sentences stay uncompiled", leaked.Count == 0, string.Join(",", leaked));

            // Doitsu's bonus with an extra rider: the procedure compiles, the leftover keeps the card a Stub.
            var stub = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 90918390, name = "Synthetic CrvIgnitionsAndUnions near miss bonus", type = "Union Effect Monster",
                race = "Fairy",
                desc = "Once per turn, you can equip this card to a \"Soitsu\" you control, OR unequip it to Special Summon " +
                       "this card in face-up Attack Position. When equipped to a monster by this card's effect, that monster " +
                       "gains 2500 ATK and cannot be destroyed by card effects."
            });
            Check("Near miss: a Union bonus with an extra rider is not FullyCompiled", stub != null && !stub.FullyCompiled,
                Describe(stub));
        }

        // ───────────────────────────── Cybernetic Magician ─────────────────────────────

        /// <summary>
        /// Cybernetic Magician (c59023523) is SINGLE EFFECT_SET_ATTACK_FINAL until the End Phase: it wipes every earlier ATK
        /// modifier (a debuff below 0 included, and a boost that ends later this turn) and only later ones add to it. The
        /// until-end-of-turn delta model (Triangle Ecstasy Spark's) gets both wrong (review v212: 0 -> 1500 instead of 2000;
        /// 2000 -> 1500 when an earlier +500 ends), so the card must stay out of the decks until a timestamped set-final
        /// ATK layer exists. Rewrite this guard when it does.
        /// </summary>
        static void CyberneticMagicianStaysClosed(CardDatabase db, Chk Check)
        {
            var def = db.Get(CyberneticMagician);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            Check("Cybernetic Magician stays closed: its \"ATK becomes 2000 until the End Phase\" is a set-final ATK the " +
                  "engine cannot layer yet",
                def != null && prog != null && !prog.FullyCompiled &&
                CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented, Describe(prog));
        }

        // ───────────────────────────── D.D.M. ─────────────────────────────

        static void DdmDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var kuriboh = PutInHand(e, p, Kuriboh);
                var pot = PutInHand(e, p, PotOfGreed);
                var ddm = PlaceMonster(e, p, Ddm, 0, BattlePosition.Attack, true);
                var celtic = Banish(e, p, CelticGuardian, faceUp: true);
                var faceDown = Banish(e, p, SummonedSkull, faceUp: false);
                var can = e.CanActivateSpellTrap(p, ddm, fromHand: false);
                e.TryActivateSpellTrap(p, ddm, fromHand: false);
                var costPick = e.PendingActivation;
                var onlySpell = costPick != null && costPick.LegalTargets.Contains(pot) &&
                                !costPick.LegalTargets.Contains(kuriboh);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(pot);
                var targetPick = e.PendingActivation;
                var noFaceDown = targetPick == null || !targetPick.LegalTargets.Contains(faceDown);
                var picked = Pick(e, celtic);
                Check("D.D.M.: discard 1 Spell (cost), Special Summon 1 of your face-up banished monsters",
                    can && onlySpell && noFaceDown && picked && p.Graveyard.Contains(pot) && p.Hand.Contains(kuriboh) &&
                    InMz(p, celtic) && celtic.FaceUp && !p.Banished.Contains(celtic) && p.Banished.Contains(faceDown),
                    $"can={can} onlySpell={onlySpell} noFaceDown={noFaceDown} picked={picked} " +
                    $"celtic={InMz(p, celtic)} potGy={p.Graveyard.Contains(pot)}");

                PutInHand(e, p, PotOfGreed);
                Banish(e, p, MysticalElf, faceUp: true);
                Check("D.D.M.: once per turn", !e.CanActivateSpellTrap(p, ddm, fromHand: false),
                    $"used={ddm.EffectUsedThisTurn}");
            }

            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, Kuriboh);
                var ddm = PlaceMonster(e, p, Ddm, 0, BattlePosition.Attack, true);
                Banish(e, p, CelticGuardian, faceUp: true);
                var noSpell = !e.CanActivateSpellTrap(p, ddm, fromHand: false);
                p.Hand.Clear();
                PutInHand(e, p, PotOfGreed);
                p.Banished.Clear();
                Banish(e, p, CelticGuardian, faceUp: false);
                Banish(e, e.Opponent, SummonedSkull, faceUp: true);
                var noTarget = !e.CanActivateSpellTrap(p, ddm, fromHand: false);
                Check("D.D.M.: no Spell in hand, or no face-up banished monster of your own → cannot be activated",
                    noSpell && noTarget, $"noSpell={noSpell} noTarget={noTarget}");
            }

            {
                var e = Fresh(db, pDeck, aDeck);
                if (ReachOpponentMain(e))
                {
                    ClearBoard(e);
                    var o = e.Opponent;
                    e.Player.Hand.Clear();
                    o.Hand.Clear();
                    o.Banished.Clear();
                    var pot = PutInHand(e, o, PotOfGreed);
                    var ddm = PlaceMonster(e, o, Ddm, 0, BattlePosition.Attack, true);
                    var celtic = Banish(e, o, CelticGuardian, faceUp: true);
                    e.TryActivateSpellTrap(o, ddm, fromHand: false);
                    Settle(e);
                    Check("D.D.M. (AI): discards its Spell and Special Summons its banished monster, no prompt left",
                        o.Graveyard.Contains(pot) && InMz(o, celtic) && !e.IsAwaitingEffectTarget,
                        $"potGy={o.Graveyard.Contains(pot)} celtic={InMz(o, celtic)}");
                }
                else
                    Check("D.D.M. (AI): reached the opponent's Main Phase", false, "");
            }
        }

        // ───────────────────────────── Patroid ─────────────────────────────

        static void PatroidDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var log = new List<string>();
                e.OnLog += s => log.Add(s);
                var patroid = PlaceMonster(e, p, Patroid, 0, BattlePosition.Attack, true);
                var setSpell = PlaceSetTrap(e, o, PotOfGreed, 0);
                var setElf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Defense, false);
                var faceUp = PlaceMonster(e, o, CelticGuardian, 1, BattlePosition.Attack, true);
                var can = e.CanActivateSpellTrap(p, patroid, fromHand: false);
                e.TryActivateSpellTrap(p, patroid, fromHand: false);
                var pick = e.PendingActivation;
                var legal = pick != null && pick.LegalTargets.Contains(setSpell) && pick.LegalTargets.Contains(setElf) &&
                            !pick.LegalTargets.Contains(faceUp);
                var picked = Pick(e, setSpell);
                var looked = log.Exists(l => l.Contains("looks at") && l.Contains(setSpell.Name));
                Check("Patroid: targets 1 face-down card the opponent controls (monster or Spell/Trap) and looks at it; it " +
                      "stays Set",
                    can && legal && picked && looked && !setSpell.FaceUp && InSt(o, setSpell) && !setElf.FaceUp &&
                    InMz(o, setElf),
                    $"can={can} legal={legal} picked={picked} looked={looked} " + string.Join(" / ", log.Skip(Math.Max(0, log.Count - 4))));
                Check("Patroid: once per turn", !e.CanActivateSpellTrap(p, patroid, fromHand: false),
                    $"used={patroid.EffectUsedThisTurn}");
            }

            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var patroid = PlaceMonster(e, p, Patroid, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, CelticGuardian, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, PotOfGreed, 0);
                Check("Patroid: no face-down card on the opponent's field → cannot be activated (its own Set cards do not count)",
                    !e.CanActivateSpellTrap(p, patroid, fromHand: false), "");
            }
        }

        // ───────────────────────────── Doitsu ─────────────────────────────

        static void DoitsuDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var doitsu = PlaceMonster(e, p, Doitsu, 0, BattlePosition.Attack, true);
                var soitsu = PlaceMonster(e, p, Soitsu, 1, BattlePosition.Attack, true);
                var equipped = EquipUnion(e, doitsu, soitsu);
                Check("Doitsu: equips to Soitsu; Soitsu gains 2500 ATK",
                    equipped && soitsu.CurrentAtk == 2500 && soitsu.CurrentDef == 0,
                    $"equipped={equipped} atk={soitsu.CurrentAtk} def={soitsu.CurrentDef}");
                var again = e.CanActivateSpellTrap(p, doitsu, fromHand: false);
                Check("Doitsu: once per turn (no unequip the turn it equipped)", !again, $"again={again}");

                doitsu.EffectUsedThisTurn = false;
                e.TryActivateSpellTrap(p, doitsu, fromHand: false);
                Settle(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Doitsu: unequips to Special Summon itself in face-up Attack Position; the bonus ends",
                    doitsu.EquippedTo == null && InMz(p, doitsu) && doitsu.FaceUp &&
                    doitsu.Position == BattlePosition.Attack && soitsu.CurrentAtk == 0,
                    $"eq={doitsu.EquippedTo?.Name} mz={InMz(p, doitsu)} pos={doitsu.Position} atk={soitsu.CurrentAtk}");
            }

            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var doitsu = PlaceMonster(e, p, Doitsu, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, CelticGuardian, 1, BattlePosition.Attack, true);
                Check("Doitsu: no \"Soitsu\" → cannot be equipped (Celtic Guardian is not its host)",
                    !e.CanActivateSpellTrap(p, doitsu, fromHand: false), "");
            }

            {
                var e = Board(db, pDeck, aDeck);
                var p = e.Player;
                var doitsu = PlaceMonster(e, p, Doitsu, 0, BattlePosition.Attack, true);
                var soitsu = PlaceMonster(e, p, Soitsu, 1, BattlePosition.Attack, true);
                EquipUnion(e, doitsu, soitsu);
                e.DestroyMonsterByBattle(p, soitsu, attacker: null);
                Check("Doitsu: destroyed instead of Soitsu by battle (the printed Union rule)",
                    InMz(p, soitsu) && p.Graveyard.Contains(doitsu) && doitsu.EquippedTo == null,
                    $"soitsu={InMz(p, soitsu)} doitsuGy={p.Graveyard.Contains(doitsu)}");
            }
        }

        // ───────────────────────────── Protective Soul Ailin ─────────────────────────────

        static void AilinDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Board(db, pDeck, aDeck);
            var p = e.Player;
            var ailin = PlaceMonster(e, p, Ailin, 0, BattlePosition.Attack, true);
            var leiLei = PlaceMonster(e, p, LeiLei, 1, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, CelticGuardian, 2, BattlePosition.Attack, true);
            var equipped = EquipUnion(e, ailin, leiLei);
            var atk = leiLei.CurrentAtk;
            var used = e.TryActivateSpellTrap(p, ailin, fromHand: false);
            Settle(e);
            Check("Protective Soul Ailin: equips to Lei Lei (no stat bonus) and changes it to face-up Defense Position",
                equipped && atk == 2300 && used && leiLei.Position == BattlePosition.Defense && leiLei.FaceUp &&
                ailin.EquippedTo == leiLei && InSt(p, ailin) && celtic.Position == BattlePosition.Attack,
                $"equipped={equipped} atk={atk} used={used} pos={leiLei.Position}");
            var again = e.CanActivateSpellTrap(p, ailin, fromHand: false);
            Check("Protective Soul Ailin: its position change is once per turn", !again && leiLei.Position == BattlePosition.Defense,
                $"again={again}");

            var e2 = Board(db, pDeck, aDeck);
            var p2 = e2.Player;
            var ailin2 = PlaceMonster(e2, p2, Ailin, 0, BattlePosition.Attack, true);
            PlaceMonster(e2, p2, Soitsu, 1, BattlePosition.Attack, true);
            Check("Protective Soul Ailin: only an \"Indomitable Fighter Lei Lei\" is its host",
                !e2.CanActivateSpellTrap(p2, ailin2, fromHand: false), "");
        }
    }
}
