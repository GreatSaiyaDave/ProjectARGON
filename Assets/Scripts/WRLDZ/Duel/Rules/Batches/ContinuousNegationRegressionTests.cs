using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ContinuousNegation batch (v141): Jinzo, Royal Decree, Imperial Order, Amplifier (+ Spell Canceller,
    /// which shares Jinzo's two sentences for Spells). Compile shapes, mini-duels through real engine entry
    /// points, and fail-closed near misses. Runtime: TextEffects/Batches/ContinuousNegationRuntime.cs.
    /// </summary>
    public static class ContinuousNegationRegressionTests
    {
        const int Jinzo = 77585513;
        const int RoyalDecree = 51452091;
        const int Amplifier = 303660;
        const int ImperialOrder = 61740673;
        const int SpellCanceller = 84636823;
        const int TalismanOfTrapSealing = 19312169;
        const int SasukeSamurai2 = 11760174;

        const int MirrorForce = 44095762;
        const int TrapHole = 4206964;
        const int DustTornado = 60082869;
        const int GravityBind = 85742772;
        const int MagicJammer = 77414722;
        const int MessengerOfPeace = 44656491;
        const int PotOfGreed = 55144522;
        const int AxeOfDespair = 40619825;
        const int Mst = 5318639;
        const int Yami = 59197169;
        const int Celtic = 91152256;
        const int BlueEyes = 89631139;
        const int DarkMagician = 46986414;
        const int Jinzo7 = 32809211;
        const int Spiritualism = 15866454;
        const int CallOfTheHaunted = 97077563;
        const int SwordsOfRevealingLight = 72302403;
        const int SwordsOfConcealingLight = 12923641;
        const int PrematureBurial = 70828912;
        const int BarkOfDarkRuler = 41925941;
        const int MagicalHats = 81210420;
        const int LaJinn = 97590747;
        const int SnatchSteal = 45986603;
        const int BurningLand = 24294108;
        const int LabyrinthOfNightmare = 66526672;
        const int Ectoplasmer = 97342942;
        const int GorgonsEye = 52648457;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("ContinuousNegation: " + name, ok, detail ?? "");

            void Guard(string name, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    check("ContinuousNegation: " + name + " (threw)", false,
                        ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace);
                }
            }

            CompiledCardProgram Prog(int id) => CardTextEffectCompiler.Compile(db.Get(id));

            CompiledCardProgram Synthetic(int id, string name, string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = name, type = type, race = race,
                    frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                    desc = desc
                });

            bool Has(CompiledCardProgram prog, EffectActionKind action, Func<EffectClause, bool> more = null) =>
                prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action &&
                                                            (more == null || more(c)));

            bool HasAnySource(CompiledCardProgram prog) =>
                Has(prog, EffectActionKind.ContinuousCannotActivateTraps) ||
                Has(prog, EffectActionKind.ContinuousCannotActivateSpells) ||
                Has(prog, EffectActionKind.ContinuousNegateTrapEffectsOnField) ||
                Has(prog, EffectActionKind.ContinuousNegateSpellEffectsOnField);

            // ───────────────────────────── compile shapes ─────────────────────────────
            Guard("compile", () =>
            {
                var jz = Prog(Jinzo);
                Check("Jinzo compiles to a continuous Trap lock + Trap negation (Implemented)",
                    jz != null && jz.FullyCompiled && jz.ClauseList.Count == 2 &&
                    CardEffectStatus.Classify(db.Get(Jinzo)) == CardEffectStatusKind.Implemented &&
                    Has(jz, EffectActionKind.ContinuousCannotActivateTraps,
                        c => c.Timing == EffectTiming.ContinuousWhileFaceUp) &&
                    Has(jz, EffectActionKind.ContinuousNegateTrapEffectsOnField,
                        c => c.Timing == EffectTiming.ContinuousWhileFaceUp && !c.ExceptThisCard),
                    jz == null ? "null" : string.Join(" | ", jz.ClauseList.Select(c => c?.Action.ToString())));

                var rd = Prog(RoyalDecree);
                Check("Royal Decree compiles to Trap negation except itself, no activation lock",
                    rd != null && rd.FullyCompiled && rd.ClauseList.Count == 1 &&
                    Has(rd, EffectActionKind.ContinuousNegateTrapEffectsOnField,
                        c => c.Timing == EffectTiming.ContinuousWhileFaceUp && c.ExceptThisCard) &&
                    !Has(rd, EffectActionKind.ContinuousCannotActivateTraps));

                var io = Prog(ImperialOrder);
                Check("Imperial Order compiles to Spell negation + 700 LP upkeep every Standby Phase",
                    io != null && io.FullyCompiled && io.ClauseList.Count == 2 &&
                    Has(io, EffectActionKind.ContinuousNegateSpellEffectsOnField,
                        c => c.Timing == EffectTiming.ContinuousWhileFaceUp && !c.ExceptThisCard) &&
                    Has(io, EffectActionKind.PayLpOrDestroyThis,
                        c => c.Timing == EffectTiming.StandbyPhase && c.PayLpAmount == 700 && c.EachStandbyPhase) &&
                    !Has(io, EffectActionKind.ContinuousCannotActivateSpells));

                var amp = Prog(Amplifier);
                Check("Amplifier compiles to one Jinzo-only Equip that spares its controller's Traps",
                    amp != null && amp.FullyCompiled && amp.ClauseList.Count == 1 &&
                    Has(amp, EffectActionKind.EquipThisToTarget, c =>
                        c.Timing == EffectTiming.Activate && c.RequiresTargetChoice &&
                        c.EquipHostName == "Jinzo" && c.EquipSparesControllerTrapsFromHostNegation &&
                        c.DestroyHostWhenThisLeaves && !c.DestroyHostOnlyIfThisDestroyed &&
                        !c.ActivationNegatable && !c.EffectNegatable));

                var sc = Prog(SpellCanceller);
                Check("Spell Canceller shares Jinzo's sentences for Spells (lock + negation)",
                    sc != null && sc.FullyCompiled && sc.ClauseList.Count == 2 &&
                    Has(sc, EffectActionKind.ContinuousCannotActivateSpells) &&
                    Has(sc, EffectActionKind.ContinuousNegateSpellEffectsOnField, c => !c.ExceptThisCard));

                Check("Vocabulary: the four new actions are shared Protection kinds",
                    new[]
                    {
                        EffectActionKind.ContinuousCannotActivateTraps,
                        EffectActionKind.ContinuousNegateTrapEffectsOnField,
                        EffectActionKind.ContinuousCannotActivateSpells,
                        EffectActionKind.ContinuousNegateSpellEffectsOnField
                    }.All(a => EffectVocabulary.ResolutionOf(new EffectClause { Action = a }) ==
                               EffectResolutionKind.Protection));

                // Fail-closed near misses.
                var msg = Prog(MessengerOfPeace);
                Check("Near miss: \"during your Standby Phase\" upkeep (Messenger of Peace) is not every Standby Phase",
                    Has(msg, EffectActionKind.PayLpOrDestroyThis, c => !c.EachStandbyPhase) &&
                    !Has(msg, EffectActionKind.PayLpOrDestroyThis, c => c.EachStandbyPhase));
                var both = Synthetic(90141001, "Near-miss Spell and Trap lock", "Trap Card", "Continuous",
                    "Spell and Trap Cards, and their effects on the field, cannot be activated.");
                Check("Near miss: \"Spell and Trap Cards, and their effects …\" is not half-read as a Trap lock",
                    both != null && !both.FullyCompiled && !HasAnySource(both));
                var thisTurn = Synthetic(90141002, "Near-miss negate this turn", "Trap Card", "Normal",
                    "Negate all Trap effects on the field this turn.");
                Check("Near miss: \"Negate all Trap effects on the field this turn\" is not the continuous negation",
                    thisTurn != null && !thisTurn.FullyCompiled && !HasAnySource(thisTurn));
                var shortLock = Synthetic(90141003, "Near-miss short Trap lock", "Spell Card", "Continuous",
                    "Trap Cards cannot be activated.");
                Check("Near miss: a bare \"Trap Cards cannot be activated\" is not Jinzo's sentence",
                    shortLock == null || !HasAnySource(shortLock));
                // SpellTrapSealsAndImmunity batch: Talisman of Trap Sealing is Implemented — Jinzo's lock and negation as
                // sources, bound to a face-up "Sealmaster Meisei" in your Monster Zone (SpellTrapSealsAndImmunityRegressionTests).
                var talisman = Prog(TalismanOfTrapSealing);
                Check("Talisman of Trap Sealing: Jinzo's lock and negation, bound to a face-up \"Sealmaster Meisei\" in your " +
                      "Monster Zone",
                    talisman != null && talisman.FullyCompiled && HasAnySource(talisman) &&
                    talisman.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.SelfDestroyUnlessNamedFaceUp &&
                                                    c.NamedFaceUpInYourMonsterZone));
                var sasuke = Prog(SasukeSamurai2);
                Check("Near miss: Sasuke Samurai #2 (pay to seal until the End Phase) gets no continuous lock",
                    sasuke == null || !HasAnySource(sasuke));
                var namedEquip = Synthetic(90141004, "Near-miss Jinzo Equip", "Spell Card", "Equip",
                    "Equip only to \"Jinzo\". The equipped monster gains 500 ATK.");
                Check("Near miss: another \"Equip only to \\\"Jinzo\\\"\" text does not spare Traps",
                    namedEquip == null ||
                    !namedEquip.ClauseList.Exists(c => c != null && c.EquipSparesControllerTrapsFromHostNegation));
            });

            // ───────────────────────────── Jinzo ─────────────────────────────
            Guard("jinzo lock", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var jz = PlaceMonster(e, o, Jinzo, 0, BattlePosition.Attack, true);
                var dt = PlaceSetTrap(e, p, DustTornado, 0);
                var oppDt = PlaceSetTrap(e, o, DustTornado, 1);
                PlaceSetTrap(e, o, MirrorForce, 0);
                PlaceSetTrap(e, p, MirrorForce, 1);
                e.NotifyPublic();
                var canPlayer = e.CanActivateSpellTrap(p, dt, fromHand: false);
                var tried = e.TryActivateSpellTrap(p, dt, fromHand: false);
                OfficialEffectRegistry.CanActivateOfficial(e, o, oppDt, false, out var oppReason);
                Check("Jinzo: no Trap Card can be activated while it is face-up (either player)",
                    reached && !canPlayer && !tried && !dt.FaceUp && p.TryFindSpellTrap(dt, out _) &&
                    oppReason != null && oppReason.Contains("cannot be activated"),
                    $"reached={reached} can={canPlayer} tried={tried} faceUp={dt.FaceUp} oppReason={oppReason}");
                jz.FaceUp = false;
                jz.Position = BattlePosition.Defense;
                e.NotifyPublic();
                Check("Jinzo face-down does not lock Traps",
                    e.CanActivateSpellTrap(p, dt, fromHand: false));
                jz.FaceUp = true;
                jz.Position = BattlePosition.Attack;
                e.SendCardToGrave(o, jz);
                e.NotifyPublic();
                Check("Jinzo gone: Traps can be activated again",
                    e.CanActivateSpellTrap(p, dt, fromHand: false));
            });

            Guard("jinzo counter trap", () =>
            {
                // A Counter Trap cannot be chained while Jinzo is face-up (engine entry point refuses it too).
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, p);
                ClearAll(e);
                PlaceMonster(e, p, Jinzo, 0, BattlePosition.Attack, true);
                var pog = PutInHand(e, p, PotOfGreed);
                var jam = PlaceSetTrap(e, o, MagicJammer, 0);
                var oppMst = PlaceSetTrap(e, o, Mst, 1); // a Quick-Play keeps the chain window open
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pog, fromHand: true);
                var window = e.PendingResponse?.Responder == o &&
                             e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                             e.PendingResponse.LegalCards.Contains(oppMst);
                var offered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(jam);
                var forced = window && e.TryActivateChainResponse(o, jam);
                PassAll(e);
                Check("Jinzo: Magic Jammer cannot be chained (not offered, refused), Pot of Greed draws 2",
                    act && window && !offered && !forced && !jam.FaceUp && deck - p.Deck.Count == 2,
                    $"act={act} window={window} offered={offered} forced={forced} drew={deck - p.Deck.Count}");
            });

            Guard("jinzo summon", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var th = PlaceSetTrap(e, o, TrapHole, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var jz = PutInHand(e, p, Jinzo);
                var summoned = e.TryNormalSummonToZone(p, jz, false, 0);
                var offered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(th);
                var canTh = e.PendingResponse != null && e.CanActivateSpellTrap(o, th, fromHand: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                Check("Jinzo: its successful Normal Summon cannot be answered by Trap Hole",
                    reached && summoned && jz.FaceUp && !offered && !canTh && !th.FaceUp &&
                    p.TryFindMonster(jz, out _),
                    $"reached={reached} summoned={summoned} offered={offered} can={canTh} window={e.PendingResponse?.Timing}");

                // Control: a vanilla 1400-ATK summon is answerable by the same Trap Hole.
                var e2 = FreshHvh(db, pDeck, aDeck);
                ReachMain(e2, e2.Player);
                ClearAll(e2);
                var th2 = PlaceSetTrap(e2, e2.Opponent, TrapHole, 0);
                var celtic = PutInHand(e2, e2.Player, Celtic);
                var ok2 = e2.TryNormalSummon(e2.Player, celtic, false);
                var offered2 = e2.PendingResponse != null && e2.PendingResponse.LegalCards.Contains(th2);
                if (e2.IsAwaitingResponse) e2.PassResponse();
                Check("Control: Trap Hole is offered against a non-Jinzo summon",
                    ok2 && offered2, $"ok={ok2} offered={offered2}");
            });

            Guard("jinzo negates", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                ClearAll(e);
                var gb = PlaceSetTrap(e, p, GravityBind, 0);
                gb.FaceUp = true;
                var jz = PlaceMonster(e, o, Jinzo, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, o, BlueEyes, 1, BattlePosition.Attack, true);
                var reached = ReachMain(e, o);
                e.NotifyPublic();
                var negated = gb.IsNegated;
                var blocked = e.ContinuousCannotAttackBlocks(o, bewd);
                var bp = e.TryEnterBattlePhase(o);
                if (e.IsAwaitingResponse) e.PassResponse();
                var lp = p.LifePoints;
                var attacked = e.CanAttack(o, jz) && e.TryAttack(o, jz, null);
                Drain(e);
                Check("Jinzo negates a face-up Gravity Bind: Level 4+ monsters attack through it",
                    reached && negated && !blocked && bp && attacked && p.LifePoints == lp - 2400 &&
                    gb.FaceUp && p.TryFindSpellTrap(gb, out _),
                    $"reached={reached} negated={negated} blocked={blocked} bp={bp} attacked={attacked} lp {lp}->{p.LifePoints}");

                // Fail-closed: face-down Jinzo negates nothing; Gravity Bind applies again.
                jz.FaceUp = false;
                jz.Position = BattlePosition.Defense;
                e.NotifyPublic();
                Check("Jinzo face-down: Gravity Bind is live again",
                    !gb.IsNegated && e.ContinuousCannotAttackBlocks(o, bewd),
                    $"negated={gb.IsNegated}");
            });

            Guard("jinzo ai", () =>
            {
                // The AI holds Mirror Force against Jinzo's attack: it cannot activate it, no soft-lock.
                var e = new DuelEngine();
                e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, o) && ReachMain(e, p); // turn 3: the first player may battle
                ClearAll(e);
                var jz = PlaceMonster(e, p, Jinzo, 0, BattlePosition.Attack, true);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse) e.PassResponse();
                var lp = o.LifePoints;
                var attacked = e.TryAttack(p, jz, null);
                Drain(e);
                Check("Jinzo: the AI cannot answer its attack with Mirror Force (no soft-lock)",
                    reached && bp && attacked && o.LifePoints == lp - 2400 && !mf.FaceUp &&
                    o.TryFindSpellTrap(mf, out _) && p.TryFindMonster(jz, out _) && !e.IsAwaitingResponse,
                    $"reached={reached} bp={bp} attacked={attacked} lp {lp}->{o.LifePoints} mfFaceUp={mf.FaceUp}");
            });

            Guard("jinzo mid-chain", () =>
            {
                // A Trap already on the Chain resolves after Jinzo became face-up: its effect is negated.
                bool DustTornadoDestroys(bool jinzoArrives)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    ClearAll(e);
                    var p = e.Player;
                    var o = e.Opponent;
                    var dt = PlaceSetTrap(e, p, DustTornado, 0);
                    dt.FaceUp = true;
                    var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                    e.Chain.AddLink(p, dt, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.SpellTrapZone,
                        false, true, "test", mf);
                    if (jinzoArrives) PlaceMonster(e, o, Jinzo, 0, BattlePosition.Attack, true);
                    e.Chain.StartResolution();
                    e.ResolveChainStackForActivation();
                    return o.Graveyard.Contains(mf) && !o.TryFindSpellTrap(mf, out _) && p.Graveyard.Contains(dt);
                }

                var negated = !DustTornadoDestroys(jinzoArrives: true);
                var control = DustTornadoDestroys(jinzoArrives: false);
                Check("Jinzo: a Trap on the Chain that resolves while Jinzo is face-up has no effect",
                    negated && control, $"negated={negated} control={control}");
            });

            // ───────────────────────────── Royal Decree ─────────────────────────────
            Guard("decree", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var rd = PlaceSetTrap(e, p, RoyalDecree, 0);
                var can = e.CanActivateSpellTrap(p, rd, fromHand: false);
                var act = e.TryActivateSpellTrap(p, rd, fromHand: false);
                PassAll(e);
                Check("Royal Decree activates and stays face-up (Continuous Trap)",
                    reached && can && act && rd.FaceUp && p.TryFindSpellTrap(rd, out _) && !rd.IsNegated &&
                    !e.Chain.HasLinks,
                    $"reached={reached} can={can} act={act} faceUp={rd.FaceUp} chain={e.Chain.Describe()}");

                var gb = PlaceSetTrap(e, o, GravityBind, 0);
                gb.FaceUp = true;
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Royal Decree negates other face-up Traps (either side), never itself",
                    gb.IsNegated && !rd.IsNegated && !e.ContinuousCannotAttackBlocks(p, bewd),
                    $"gb={gb.IsNegated} rd={rd.IsNegated}");

                // Traps can still be activated, but resolve with no effect.
                var dt = PlaceSetTrap(e, p, DustTornado, 1);
                var mf = PlaceSetTrap(e, o, MirrorForce, 1);
                var canDt = e.CanActivateSpellTrap(p, dt, fromHand: false);
                var actDt = e.TryActivateSpellTrap(p, dt, fromHand: false);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(mf);
                PassAll(e);
                Check("Royal Decree: a Trap still activates, but its effect is negated at resolution",
                    canDt && actDt && sel && o.TryFindSpellTrap(mf, out _) && p.Graveyard.Contains(dt),
                    $"can={canDt} act={actDt} sel={sel} mfOnField={o.TryFindSpellTrap(mf, out _)} dtGy={p.Graveyard.Contains(dt)}");

                // Control: without Royal Decree the same Dust Tornado destroys the target.
                e.SendCardToGrave(p, rd);
                e.NotifyPublic();
                var dt2 = PlaceSetTrap(e, p, DustTornado, 2);
                var act2 = e.TryActivateSpellTrap(p, dt2, fromHand: false);
                var sel2 = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(mf);
                PassAll(e);
                Check("Control: Royal Decree gone — Dust Tornado destroys its target; Gravity Bind is live again",
                    act2 && sel2 && o.Graveyard.Contains(mf) && !gb.IsNegated,
                    $"act={act2} sel={sel2} mfGy={o.Graveyard.Contains(mf)} gb={gb.IsNegated}");
            });

            Guard("decree call of the haunted", () =>
            {
                bool MonsterSurvives(bool decree)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    ClearAll(e);
                    var p = e.Player;
                    var o = e.Opponent;
                    var coth = PlaceSetTrap(e, p, CallOfTheHaunted, 0);
                    coth.FaceUp = true;
                    var mon = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                    coth.EquippedTo = mon;
                    mon.Equips.Add(coth);
                    if (decree)
                    {
                        var rd = PlaceSetTrap(e, o, RoyalDecree, 0);
                        rd.FaceUp = true;
                    }

                    e.NotifyPublic();
                    var negated = coth.IsNegated;
                    e.SendCardToGrave(p, coth);
                    return negated == decree && p.TryFindMonster(mon, out _);
                }

                var kept = MonsterSurvives(decree: true);
                var control = !MonsterSurvives(decree: false);
                Check("Royal Decree: a negated Call of the Haunted leaves without destroying its monster",
                    kept && control, $"kept={kept} controlDestroyed={control}");

                // Activated under Royal Decree, Call of the Haunted resolves with no effect: no summon.
                var e2 = FreshHvh(db, pDeck, aDeck);
                var p2 = e2.Player;
                ReachMain(e2, p2);
                ClearAll(e2);
                var rd2 = PlaceSetTrap(e2, e2.Opponent, RoyalDecree, 0);
                rd2.FaceUp = true;
                var ghost = e2.CreateCardInstance(BlueEyes);
                p2.Graveyard.Add(ghost);
                var coth2 = PlaceSetTrap(e2, p2, CallOfTheHaunted, 0);
                e2.NotifyPublic();
                var act = e2.TryActivateSpellTrap(p2, coth2, fromHand: false);
                var sel = !e2.IsAwaitingEffectTarget || e2.TrySelectEffectTarget(ghost);
                PassAll(e2);
                Check("Royal Decree: Call of the Haunted activated now summons nothing and stays face-up",
                    act && sel && p2.Graveyard.Contains(ghost) && !p2.TryFindMonster(ghost, out _) &&
                    coth2.FaceUp && p2.TryFindSpellTrap(coth2, out _),
                    $"act={act} sel={sel} ghostGy={p2.Graveyard.Contains(ghost)} coth={coth2.FaceUp}");
            });

            Guard("decree mirror force", () =>
            {
                // The opponent answers an attack with Mirror Force under Royal Decree: it resolves with no effect.
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, o) && ReachMain(e, p);
                ClearAll(e);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                var rd = PlaceSetTrap(e, p, RoyalDecree, 1);
                rd.FaceUp = true;
                e.NotifyPublic();
                var bp = e.TryEnterBattlePhase(p);
                if (e.IsAwaitingResponse && e.PendingResponse.Timing != ResponseTiming.AttackDeclared) e.PassResponse();
                var lp = o.LifePoints;
                var declared = e.TryAttack(p, celtic, null);
                var offered = e.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                              e.PendingResponse.LegalCards.Contains(mf);
                var act = offered && e.TryActivateSpellTrap(o, mf, fromHand: false);
                Drain(e);
                Check("Royal Decree: Mirror Force answering an attack is negated — the attacker survives and hits",
                    reached && bp && declared && offered && act && o.Graveyard.Contains(mf) &&
                    p.TryFindMonster(celtic, out _) && o.LifePoints == lp - 1400,
                    $"reached={reached} bp={bp} declared={declared} offered={offered} act={act} mfGy={o.Graveyard.Contains(mf)} celtic={p.TryFindMonster(celtic, out _)} lp {lp}->{o.LifePoints}");
            });

            Guard("decree counter", () =>
            {
                // Magic Jammer (a Counter Trap) under Royal Decree: its negation is negated, Pot of Greed draws.
                var e = FreshHvh(db, pDeck, aDeck);
                ClearAll(e);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, p);
                ClearAll(e);
                var rd = PlaceSetTrap(e, o, RoyalDecree, 2);
                rd.FaceUp = true;
                var pog = PutInHand(e, p, PotOfGreed);
                var jam = PlaceSetTrap(e, o, MagicJammer, 0);
                PutInHand(e, o, Celtic);
                e.NotifyPublic();
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pog, fromHand: true);
                var jOk = e.PendingResponse != null && e.TryActivateChainResponse(o, jam);
                PassAll(e);
                Check("Royal Decree: Magic Jammer resolves with no effect, so Pot of Greed still draws 2",
                    act && jOk && deck - p.Deck.Count == 2 && o.Graveyard.Contains(jam) && p.Graveyard.Contains(pog),
                    $"act={act} jam={jOk} drew={deck - p.Deck.Count} jamGy={o.Graveyard.Contains(jam)}");
            });

            Guard("decree pair", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                ClearAll(e);
                var p = e.Player;
                var o = e.Opponent;
                var first = PlaceSetTrap(e, p, RoyalDecree, 3);
                first.FaceUp = true;
                e.NotifyPublic();
                var second = PlaceSetTrap(e, o, RoyalDecree, 0); // lower zone, later timestamp
                second.FaceUp = true;
                e.NotifyPublic();
                Check("Two Royal Decrees: the one face-up first negates the later one",
                    !first.IsNegated && second.IsNegated,
                    $"first={first.IsNegated} second={second.IsNegated}");
                var jz = PlaceMonster(e, o, Jinzo, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Jinzo negates Royal Decree",
                    first.IsNegated && second.IsNegated, $"first={first.IsNegated} second={second.IsNegated}");
                e.SendCardToGrave(o, jz);
                e.NotifyPublic();
                Check("Jinzo gone: the first Royal Decree applies again",
                    !first.IsNegated && second.IsNegated, $"first={first.IsNegated} second={second.IsNegated}");
            });

            // ───────────────────────────── Imperial Order ─────────────────────────────
            Guard("imperial order", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var io = PlaceSetTrap(e, p, ImperialOrder, 0);
                var act = e.TryActivateSpellTrap(p, io, fromHand: false);
                PassAll(e);
                Check("Imperial Order activates and stays face-up (Continuous Trap)",
                    reached && act && io.FaceUp && p.TryFindSpellTrap(io, out _) && !e.Chain.HasLinks,
                    $"reached={reached} act={act} faceUp={io.FaceUp}");

                var pog = PutInHand(e, p, PotOfGreed);
                var deck = p.Deck.Count;
                var can = e.CanActivateSpellTrap(p, pog, fromHand: true);
                var actPog = e.TryActivateSpellTrap(p, pog, fromHand: true);
                PassAll(e);
                Check("Imperial Order: Pot of Greed can be activated but its effect is negated (no draw)",
                    can && actPog && p.Deck.Count == deck && p.Graveyard.Contains(pog) && !p.Hand.Contains(pog),
                    $"can={can} act={actPog} drew={deck - p.Deck.Count}");

                var msg = PlaceSetTrap(e, o, MessengerOfPeace, 0);
                msg.FaceUp = true;
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                var host = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                var axe = PlaceSetTrap(e, p, AxeOfDespair, 1);
                axe.FaceUp = true;
                axe.EquippedTo = host;
                host.Equips.Add(axe);
                var dm = PlaceMonster(e, p, DarkMagician, 2, BattlePosition.Attack, true);
                var yami = e.CreateCardInstance(Yami);
                yami.FaceUp = true;
                o.FieldSpellZone.Occupant = yami;
                e.NotifyPublic();
                Check("Imperial Order negates face-up Continuous, Equip and Field Spells",
                    msg.IsNegated && !e.ContinuousCannotAttackBlocks(p, bewd) &&
                    axe.IsNegated && host.CurrentAtk == 1400 && yami.IsNegated && dm.CurrentAtk == 2500 &&
                    !io.IsNegated,
                    $"msg={msg.IsNegated} axe={axe.IsNegated} hostAtk={host.CurrentAtk} yami={yami.IsNegated} dmAtk={dm.CurrentAtk}");

                // An Equip Spell activated under Imperial Order equips nothing and goes to the GY.
                var axe2 = PutInHand(e, p, AxeOfDespair);
                var target = PlaceMonster(e, p, Celtic, 3, BattlePosition.Attack, true);
                var actAxe = e.TryActivateSpellTrap(p, axe2, fromHand: true);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(target);
                PassAll(e);
                e.NotifyPublic();
                Check("Imperial Order: an Equip Spell activated now is negated, not equipped, and sent to the GY",
                    actAxe && sel && p.Graveyard.Contains(axe2) && !p.TryFindSpellTrap(axe2, out _) &&
                    target.Equips.Count == 0 && axe2.EquippedTo == null && target.CurrentAtk == 1400,
                    $"act={actAxe} sel={sel} gy={p.Graveyard.Contains(axe2)} onField={p.TryFindSpellTrap(axe2, out _)} equips={target.Equips.Count}");

                // Imperial Order leaves: every Spell applies again.
                e.SendCardToGrave(p, io);
                e.NotifyPublic();
                Check("Imperial Order gone: Messenger, Axe and Yami apply again",
                    !msg.IsNegated && e.ContinuousCannotAttackBlocks(p, bewd) && host.CurrentAtk == 2400 &&
                    dm.CurrentAtk == 2700,
                    $"msg={msg.IsNegated} hostAtk={host.CurrentAtk} dmAtk={dm.CurrentAtk}");
            });

            Guard("imperial order upkeep", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var io = PlaceSetTrap(e, p, ImperialOrder, 0);
                io.FaceUp = true;
                var msg = PlaceSetTrap(e, p, MessengerOfPeace, 1);
                msg.FaceUp = true;
                e.NotifyPublic();
                p.LifePoints = 8000;
                var toOpp = ReachMain(e, o);
                var afterOpp = p.LifePoints;
                var toMe = ReachMain(e, p);
                var afterMine = p.LifePoints;
                Check("Imperial Order: 700 LP during EVERY Standby Phase (the opponent's too); Messenger only on yours",
                    reached && toOpp && toMe && afterOpp == 7300 && afterMine == 7300 - 700 - 100 &&
                    io.FaceUp && p.TryFindSpellTrap(io, out _),
                    $"reached={reached} opp={toOpp} me={toMe} lp {afterOpp}/{afterMine}");

                // Not enough LP: Imperial Order is destroyed, no LP paid.
                e.SendCardToGrave(p, msg);
                p.LifePoints = 500;
                ReachMain(e, o);
                Check("Imperial Order: with under 700 LP it is destroyed instead",
                    p.Graveyard.Contains(io) && !p.TryFindSpellTrap(io, out _) && p.LifePoints == 500,
                    $"gy={p.Graveyard.Contains(io)} lp={p.LifePoints}");
            });

            Guard("imperial order vs decree", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var io = PlaceSetTrap(e, p, ImperialOrder, 0);
                io.FaceUp = true;
                e.NotifyPublic();
                var rd = PlaceSetTrap(e, o, RoyalDecree, 0);
                rd.FaceUp = true;
                e.NotifyPublic();
                var pog = PutInHand(e, p, PotOfGreed);
                var deck = p.Deck.Count;
                var act = e.TryActivateSpellTrap(p, pog, fromHand: true);
                PassAll(e);
                Check("Royal Decree negates Imperial Order: Spells resolve again",
                    reached && io.IsNegated && !rd.IsNegated && act && deck - p.Deck.Count == 2,
                    $"io={io.IsNegated} rd={rd.IsNegated} act={act} drew={deck - p.Deck.Count}");
                p.LifePoints = 8000;
                ReachMain(e, o);
                Check("Negated Imperial Order still pays its upkeep (a cost, not an effect)",
                    p.LifePoints == 7300 && io.FaceUp, $"lp={p.LifePoints}");
            });

            // ───────────────────────────── Amplifier ─────────────────────────────
            Guard("amplifier equip", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var j7 = PlaceMonster(e, p, Jinzo7, 1, BattlePosition.Attack, true);
                var amp = PutInHand(e, p, Amplifier);
                var noJinzo = !e.CanActivateSpellTrap(p, amp, fromHand: true);
                var fd = PlaceMonster(e, p, Jinzo, 2, BattlePosition.Defense, false);
                var faceDownOnly = !e.CanActivateSpellTrap(p, amp, fromHand: true);
                fd.FaceUp = true;
                fd.Position = BattlePosition.Attack;
                e.NotifyPublic();
                var can = e.CanActivateSpellTrap(p, amp, fromHand: true);
                var act = e.TryActivateSpellTrap(p, amp, fromHand: true);
                var targets = e.PendingActivation?.LegalTargets?.ToList();
                var onlyJinzo = targets == null ||
                                (targets.Contains(fd) && !targets.Contains(celtic) && !targets.Contains(j7));
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(fd);
                PassAll(e);
                Check("Amplifier: equips only to a face-up \"Jinzo\" (not Jinzo #7, not a face-down Jinzo)",
                    reached && noJinzo && faceDownOnly && can && act && onlyJinzo && sel &&
                    amp.EquippedTo == fd && fd.Equips.Contains(amp) && p.TryFindSpellTrap(amp, out _),
                    $"reached={reached} noJinzo={noJinzo} fdOnly={faceDownOnly} can={can} act={act} onlyJinzo={onlyJinzo} sel={sel} eq={amp.EquippedTo?.Name}");
            });

            Guard("amplifier under imperial order", () =>
            {
                // "This card's activation and effect cannot be negated": Imperial Order does not stop the equip.
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, p);
                ClearAll(e);
                var io = PlaceSetTrap(e, o, ImperialOrder, 0);
                io.FaceUp = true;
                var jz = PlaceMonster(e, p, Jinzo, 0, BattlePosition.Attack, true);
                var amp = PutInHand(e, p, Amplifier);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, amp, fromHand: true);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(jz);
                PassAll(e);
                e.NotifyPublic();
                Check("Amplifier activated under Imperial Order still equips and is not negated",
                    act && sel && amp.EquippedTo == jz && p.TryFindSpellTrap(amp, out _) && !amp.IsNegated,
                    $"act={act} sel={sel} eq={amp.EquippedTo?.Name} neg={amp.IsNegated}");
            });

            Guard("amplifier exemption", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var jz = PlaceMonster(e, p, Jinzo, 0, BattlePosition.Attack, true);
                var amp = PlaceSetTrap(e, p, Amplifier, 0);
                amp.FaceUp = true;
                amp.EquippedTo = jz;
                jz.Equips.Add(amp);
                var myGb = PlaceSetTrap(e, p, GravityBind, 1);
                myGb.FaceUp = true;
                var oppGb = PlaceSetTrap(e, o, GravityBind, 1);
                oppGb.FaceUp = true;
                var dt = PlaceSetTrap(e, p, DustTornado, 2);
                var oppDt = PlaceSetTrap(e, o, DustTornado, 2);
                var mf = PlaceSetTrap(e, o, MirrorForce, 0);
                e.NotifyPublic();
                OfficialEffectRegistry.CanActivateOfficial(e, o, oppDt, false, out var oppReason);
                Check("Amplifier: Jinzo still negates the opponent's Traps, not its controller's",
                    reached && !myGb.IsNegated && oppGb.IsNegated && !amp.IsNegated &&
                    oppReason != null && oppReason.Contains("cannot be activated"),
                    $"myGb={myGb.IsNegated} oppGb={oppGb.IsNegated} oppReason={oppReason}");
                var can = e.CanActivateSpellTrap(p, dt, fromHand: false);
                var act = e.TryActivateSpellTrap(p, dt, fromHand: false);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(mf);
                PassAll(e);
                Check("Amplifier: Jinzo's controller activates Dust Tornado and it resolves",
                    can && act && sel && o.Graveyard.Contains(mf) && p.Graveyard.Contains(dt),
                    $"can={can} act={act} sel={sel} mfGy={o.Graveyard.Contains(mf)}");

                // Imperial Order cannot negate Amplifier ("effect cannot be negated").
                var io = PlaceSetTrap(e, o, ImperialOrder, 3);
                io.FaceUp = true;
                e.NotifyPublic();
                Check("Amplifier: Imperial Order does not negate it; the exemption holds",
                    !amp.IsNegated && !myGb.IsNegated && oppGb.IsNegated,
                    $"amp={amp.IsNegated} myGb={myGb.IsNegated}");
            });

            Guard("amplifier leaves", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                ReachMain(e, p);
                ClearAll(e);
                var jz = PlaceMonster(e, p, Jinzo, 0, BattlePosition.Attack, true);
                var amp = PlaceSetTrap(e, p, Amplifier, 0);
                amp.FaceUp = true;
                amp.EquippedTo = jz;
                jz.Equips.Add(amp);
                e.SendCardToGrave(p, amp);
                Check("Amplifier leaves the field (sent to the GY): the equipped Jinzo is destroyed",
                    p.Graveyard.Contains(amp) && p.Graveyard.Contains(jz) && !p.TryFindMonster(jz, out _),
                    $"ampGy={p.Graveyard.Contains(amp)} jzGy={p.Graveyard.Contains(jz)}");

                var jz2 = PlaceMonster(e, p, Jinzo, 1, BattlePosition.Attack, true);
                var amp2 = PlaceSetTrap(e, p, Amplifier, 1);
                amp2.FaceUp = true;
                amp2.EquippedTo = jz2;
                jz2.Equips.Add(amp2);
                var bounced = e.ReturnCardToHand(amp2);
                Check("Amplifier returned to the hand: the equipped Jinzo is destroyed too",
                    bounced && p.Hand.Contains(amp2) && p.Graveyard.Contains(jz2),
                    $"bounced={bounced} jzGy={p.Graveyard.Contains(jz2)}");

                var jz3 = PlaceMonster(e, p, Jinzo, 2, BattlePosition.Attack, true);
                var amp3 = PlaceSetTrap(e, p, Amplifier, 2);
                amp3.FaceUp = true;
                amp3.EquippedTo = jz3;
                jz3.Equips.Add(amp3);
                var celtic = PlaceMonster(e, p, Celtic, 3, BattlePosition.Attack, true);
                e.SendCardToGrave(p, jz3);
                Check("Jinzo leaves first: Amplifier goes to the GY and destroys nothing else",
                    p.Graveyard.Contains(amp3) && p.TryFindMonster(celtic, out _),
                    $"ampGy={p.Graveyard.Contains(amp3)}");
            });

            Guard("cannot be negated", () =>
            {
                // "This card's activation … cannot be negated": no negate-activation answer is offered.
                // (Against Amplifier the opponent's Counter Traps are already locked by Jinzo, so the shared
                // check is exercised with Spiritualism, the other card carrying the same sentence.)
                bool JammerOffered(int spellId, out bool resolved)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    var p = e.Player;
                    var o = e.Opponent;
                    ReachMain(e, p);
                    ClearAll(e);
                    var jam = PlaceSetTrap(e, o, MagicJammer, 0);
                    var mf = PlaceSetTrap(e, o, MirrorForce, 1);
                    PutInHand(e, o, Celtic);
                    var card = PutInHand(e, p, spellId);
                    var deck = p.Deck.Count;
                    e.TryActivateSpellTrap(p, card, fromHand: true);
                    if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(mf);
                    var offered = e.PendingResponse != null && e.PendingResponse.LegalCards.Contains(jam);
                    var chained = offered && e.TryActivateChainResponse(o, jam);
                    PassAll(e);
                    resolved = o.Hand.Contains(mf) || deck - p.Deck.Count == 2;
                    return offered || chained;
                }

                var spiritOffered = JammerOffered(Spiritualism, out var spiritResolved);
                var potOffered = JammerOffered(PotOfGreed, out _);
                Check("Activation that cannot be negated: Magic Jammer is not offered against Spiritualism",
                    !spiritOffered && spiritResolved && potOffered,
                    $"spiritualismOffered={spiritOffered} resolved={spiritResolved} controlPotOffered={potOffered}");
            });

            Guard("ai turn", () =>
            {
                // The AI plays a Main Phase holding Amplifier / Pot of Greed with Jinzo, Royal Decree and
                // Imperial Order around: it finishes its turn (no soft-lock, no exception).
                var e = new DuelEngine();
                e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
                if (e.IsAwaitingResponse) e.PassResponse();
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, o);
                ClearAll(e);
                PlaceMonster(e, o, Jinzo, 0, BattlePosition.Attack, true);
                PutInHand(e, o, Amplifier);
                PutInHand(e, o, PotOfGreed);
                PutInHand(e, o, AxeOfDespair);
                PlaceSetTrap(e, o, RoyalDecree, 0);
                PlaceSetTrap(e, o, ImperialOrder, 1);
                PlaceSetTrap(e, o, MirrorForce, 2);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, DustTornado, 0);
                var gb = PlaceSetTrap(e, p, GravityBind, 1);
                gb.FaceUp = true;
                e.NotifyPublic();
                var done = RunAiTurn(e, 400, out var steps);
                Check("AI turn with Jinzo / Amplifier / Royal Decree / Imperial Order completes",
                    reached && done && (e.GameOver || e.TurnPlayer == p) && !e.IsAwaitingEffectTarget,
                    $"reached={reached} done={done} steps={steps} turnPlayer={(e.TurnPlayer == p ? "player" : "ai")} over={e.GameOver}");
            });

            // ───────────────────────────── Spell Canceller ─────────────────────────────
            Guard("spell canceller", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var sc = PlaceMonster(e, o, SpellCanceller, 0, BattlePosition.Attack, true);
                var pog = PutInHand(e, p, PotOfGreed);
                var yami = PutInHand(e, p, Yami);
                var mst = PlaceSetTrap(e, p, Mst, 0);
                PlaceSetTrap(e, o, MirrorForce, 0);
                var msg = PlaceSetTrap(e, o, MessengerOfPeace, 1);
                msg.FaceUp = true;
                var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Check("Spell Canceller: no Spell can be activated (hand, Field Spell, Set Quick-Play)",
                    reached && !e.CanActivateSpellTrap(p, pog, true) && !e.CanActivateSpellTrap(p, yami, true) &&
                    !e.CanActivateSpellTrap(p, mst, false) && !e.TryActivateSpellTrap(p, pog, true) &&
                    p.Hand.Contains(pog));
                Check("Spell Canceller negates face-up Spells",
                    msg.IsNegated && !e.ContinuousCannotAttackBlocks(p, bewd), $"msg={msg.IsNegated}");
                e.SendCardToGrave(o, sc);
                e.NotifyPublic();
                Check("Spell Canceller gone: Spells can be activated again",
                    e.CanActivateSpellTrap(p, pog, true) && !msg.IsNegated);
            });

            // ─────────────────── review fixes: undisableable setup, other resolution paths ───────────────────
            Guard("swords of revealing light under imperial order", () =>
            {
                // The flip is negated, but the End Phase countdown is registered at activation with
                // EFFECT_FLAG_CANNOT_DISABLE (c72302403): Swords still leaves on schedule.
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var io = PlaceSetTrap(e, o, ImperialOrder, 0);
                io.FaceUp = true;
                var fd = PlaceMonster(e, o, Celtic, 0, BattlePosition.Defense, false);
                var sw = PutInHand(e, p, SwordsOfRevealingLight);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, sw, fromHand: true);
                PassAll(e);
                e.NotifyPublic();
                var stays = p.TryFindSpellTrap(sw, out _);
                var counter = sw.ContinuousTurnsRemaining;
                var lockWhileNegated = e.OpponentHasSwordsOfRevealingLight(o);
                e.SendCardToGrave(o, io);
                e.NotifyPublic();
                var lockOnceIoGone = e.OpponentHasSwordsOfRevealingLight(o);
                for (var i = 0; i < 3; i++)
                {
                    ReachMain(e, o);
                    ReachMain(e, p);
                }

                Check("Imperial Order: Swords of Revealing Light flips nothing, stays, and still counts 3 opponent turns",
                    reached && act && !fd.FaceUp && stays && counter == 3 && !lockWhileNegated && lockOnceIoGone &&
                    !p.TryFindSpellTrap(sw, out _) && !e.OpponentHasSwordsOfRevealingLight(o),
                    $"act={act} flipped={fd.FaceUp} stays={stays} counter={counter} lockNeg={lockWhileNegated} " +
                    $"lockAfterIo={lockOnceIoGone} stillOnField={p.TryFindSpellTrap(sw, out _)}");
            });

            Guard("swords of concealing light countdown", () =>
            {
                // Swords of Concealing Light's Standby countdown (c12923641) is CANNOT_DISABLE too: it runs
                // when the card was activated under Imperial Order, and keeps running once IO negates it.
                bool Gone(bool ioFirst, out string detail)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    var p = e.Player;
                    var o = e.Opponent;
                    var reached = ReachMain(e, p);
                    ClearAll(e);
                    if (ioFirst)
                        PlaceSetTrap(e, o, ImperialOrder, 0).FaceUp = true;
                    PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                    var socl = PutInHand(e, p, SwordsOfConcealingLight);
                    e.NotifyPublic();
                    var act = e.TryActivateSpellTrap(p, socl, fromHand: true);
                    PassAll(e);
                    var counter = socl.ContinuousTurnsRemaining;
                    if (!ioFirst)
                        PlaceSetTrap(e, o, ImperialOrder, 0).FaceUp = true;
                    e.NotifyPublic();
                    var negated = socl.IsNegated;
                    for (var k = 0; k < 3; k++)
                    {
                        ReachMain(e, o);
                        o.LifePoints = 8000;
                        ReachMain(e, p);
                    }

                    detail = $"reached={reached} act={act} counter={counter} neg={negated} " +
                             $"onField={p.TryFindSpellTrap(socl, out _)} now={socl.ContinuousTurnsRemaining}";
                    return reached && act && counter == 2 && negated && !p.TryFindSpellTrap(socl, out _);
                }

                var under = Gone(ioFirst: true, out var d1);
                var after = Gone(ioFirst: false, out var d2);
                Check("Imperial Order: Swords of Concealing Light's 2-Standby countdown runs while it is negated",
                    under && after, $"activatedUnderIo[{d1}] negatedLater[{d2}]");
            });

            Guard("premature burial under imperial order", () =>
            {
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                PlaceSetTrap(e, o, ImperialOrder, 0).FaceUp = true;
                var ghost = e.CreateCardInstance(BlueEyes);
                p.Graveyard.Add(ghost);
                var pb = PutInHand(e, p, PrematureBurial);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = e.TryActivateSpellTrap(p, pb, fromHand: true);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(ghost);
                PassAll(e);
                e.NotifyPublic();
                Check("Imperial Order: Premature Burial pays 800, summons nothing, and goes to the GY unequipped",
                    reached && act && sel && p.LifePoints == lp - 800 && !p.TryFindMonster(ghost, out _) &&
                    !p.TryFindSpellTrap(pb, out _) && p.Graveyard.Contains(pb),
                    $"act={act} sel={sel} lp {lp}->{p.LifePoints} summoned={p.TryFindMonster(ghost, out _)} " +
                    $"pbOnField={p.TryFindSpellTrap(pb, out _)} pbGy={p.Graveyard.Contains(pb)}");

                // Equipped first, then negated, then destroyed: its leave-field destruction (c70828912 desop,
                // no IsDisabled check) still destroys the monster. Call of the Haunted is the exception.
                var e2 = FreshHvh(db, pDeck, aDeck);
                var p2 = e2.Player;
                ReachMain(e2, p2);
                ClearAll(e2);
                var host = PlaceMonster(e2, p2, BlueEyes, 0, BattlePosition.Attack, true);
                var pb2 = PlaceSetTrap(e2, p2, PrematureBurial, 0);
                pb2.FaceUp = true;
                pb2.EquippedTo = host;
                host.Equips.Add(pb2);
                PlaceSetTrap(e2, e2.Opponent, ImperialOrder, 0).FaceUp = true;
                e2.NotifyPublic();
                var neg = pb2.IsNegated;
                e2.SendCardToGrave(p2, pb2);
                Check("Imperial Order: a negated Premature Burial that is destroyed still destroys its monster",
                    neg && !p2.TryFindMonster(host, out _), $"neg={neg} hostOnField={p2.TryFindMonster(host, out _)}");
            });

            Guard("bark of dark ruler under royal decree", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(e))
                {
                    Check("Royal Decree: Bark of Dark Ruler setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearAll(e);
                var p = e.Player;
                var o = e.Opponent;
                p.LifePoints = 8000;
                p.Hand.Clear();
                o.Hand.Clear();
                var fiend = PlaceMonster(e, p, LaJinn, 2, BattlePosition.Attack, true);
                var bark = PlaceSetTrap(e, p, BarkOfDarkRuler, 2);
                PlaceSetTrap(e, p, RoyalDecree, 3).FaceUp = true;
                var attacker = PlaceMonster(e, o, BlueEyes, 2, BattlePosition.Attack, true);
                attacker.ClearAttackFlags();
                e.NotifyPublic();
                if (e.IsAwaitingResponse) e.PassResponse();
                DrainCombat(e);
                var declared = e.TryAttack(o, attacker, fiend);
                if (e.PendingResponse?.Timing == ResponseTiming.AttackDeclared) e.PassResponse();
                var window = e.PendingResponse?.Timing == ResponseTiming.DamageCalculation;
                var act = window && e.TryActivateSpellTrap(p, bark, fromHand: false);
                if (e.IsAwaitingEffectTarget && e.PendingActivation != null && e.PendingActivation.AwaitingLpCost)
                {
                    var choices = e.PendingActivation.LpCostChoices;
                    e.TrySelectLpCost(choices[choices.Count - 1]);
                }

                var paid = 8000 - p.LifePoints;
                var loss = attacker.UntilEndOfTurnAtk;
                DrainCombat(e);
                Check("Royal Decree: Bark of Dark Ruler's LP cost is paid, its ATK/DEF loss is negated",
                    declared && window && act && paid > 0 && loss == 0,
                    $"declared={declared} window={window} act={act} paid={paid} atkLoss={loss}");
            });

            Guard("magical hats under royal decree", () =>
            {
                var e = Fresh(db, pDeck, aDeck);
                if (!ReachOpponentBattle(e))
                {
                    Check("Royal Decree: Magical Hats setup", false, "no opponent Battle Phase");
                    return;
                }

                ClearAll(e);
                var p = e.Player;
                var host = PlaceMonster(e, p, Celtic, 2, BattlePosition.Attack, true);
                p.Deck.Insert(0, PotOfGreed);
                p.Deck.Insert(0, Mst);
                PlaceSetTrap(e, e.Opponent, RoyalDecree, 3).FaceUp = true;
                var hats = PlaceSetTrap(e, p, MagicalHats, 0);
                e.NotifyPublic();
                if (e.IsAwaitingResponse) e.PassResponse();
                var act = e.TryActivateSpellTrap(p, hats, fromHand: false);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(host);
                if (e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                    e.PendingActivation.LegalTargets.Count >= 2)
                {
                    var a = e.PendingActivation.LegalTargets[0];
                    var b = e.PendingActivation.LegalTargets[1];
                    e.TrySelectEffectTarget(a);
                    e.TrySelectEffectTarget(b);
                }

                PassAll(e);
                var dummies = p.MonstersOnField().Count(m => m.MagicalHatDummy);
                Check("Royal Decree: Magical Hats resolves with no effect (no hats, the monster stays face-up)",
                    act && dummies == 0 && host.FaceUp && !p.TryFindSpellTrap(hats, out _),
                    $"act={act} dummies={dummies} hostFaceUp={host.FaceUp} monsters={p.MonsterCount}");
            });

            Guard("negated call of the haunted keeps its place", () =>
            {
                // "When that monster is destroyed, destroy this card" does not apply while Call of the Haunted
                // is negated: by Royal Decree, or by Jinzo when Jinzo is the destroyed monster.
                bool CallStays(bool jinzo, bool decree, out string detail)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    var p = e.Player;
                    ReachMain(e, p);
                    ClearAll(e);
                    var coth = PlaceSetTrap(e, p, CallOfTheHaunted, 0);
                    coth.FaceUp = true;
                    var mon = PlaceMonster(e, p, jinzo ? Jinzo : BlueEyes, 0, BattlePosition.Attack, true);
                    coth.EquippedTo = mon;
                    mon.Equips.Add(coth);
                    if (decree)
                        PlaceSetTrap(e, e.Opponent, RoyalDecree, 0).FaceUp = true;
                    e.NotifyPublic();
                    var neg = coth.IsNegated;
                    e.DestroyMonsterPublic(p, mon);
                    e.NotifyPublic();
                    detail = $"neg={neg} onField={p.TryFindSpellTrap(coth, out _)} linked={coth.EquippedTo != null}";
                    return p.TryFindSpellTrap(coth, out _) && coth.EquippedTo == null;
                }

                var byJinzo = CallStays(jinzo: true, decree: false, out var dj);
                var byDecree = CallStays(jinzo: false, decree: true, out var dd);
                var control = !CallStays(jinzo: false, decree: false, out var dc);
                Check("A negated Call of the Haunted stays when its monster is destroyed (Jinzo, Royal Decree)",
                    byJinzo && byDecree && control, $"jinzo[{dj}] decree[{dd}] control[{dc}]");
            });

            Guard("royal decree chained", () =>
            {
                // Chained to Dust Tornado that targets it: Decree resolves first and Dust Tornado does nothing.
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var rd = PlaceSetTrap(e, o, RoyalDecree, 0);
                var dt = PlaceSetTrap(e, p, DustTornado, 0);
                e.NotifyPublic();
                var act = e.TryActivateSpellTrap(p, dt, fromHand: false);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(rd);
                var offered = e.PendingResponse?.Responder == o &&
                              e.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                              e.PendingResponse.LegalCards.Contains(rd);
                var chained = offered && e.TryActivateChainResponse(o, rd);
                PassAll(e);
                Check("Royal Decree chains to Dust Tornado targeting it, then negates it",
                    reached && act && sel && offered && chained && o.TryFindSpellTrap(rd, out _) && rd.FaceUp &&
                    !p.TryFindSpellTrap(dt, out _),
                    $"act={act} sel={sel} offered={offered} chained={chained} rdOnField={o.TryFindSpellTrap(rd, out _)} " +
                    $"rdFaceUp={rd.FaceUp}");

                // Imperial Order chained to Pot of Greed: Pot of Greed resolves after it, negated.
                var e2 = FreshHvh(db, pDeck, aDeck);
                var p2 = e2.Player;
                var o2 = e2.Opponent;
                var reached2 = ReachMain(e2, p2);
                ClearAll(e2);
                var io = PlaceSetTrap(e2, o2, ImperialOrder, 0);
                var pog = PutInHand(e2, p2, PotOfGreed);
                e2.NotifyPublic();
                var hand = p2.Hand.Count;
                var act2 = e2.TryActivateSpellTrap(p2, pog, fromHand: true);
                var offered2 = e2.PendingResponse?.Responder == o2 &&
                               e2.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                               e2.PendingResponse.LegalCards.Contains(io);
                var chained2 = offered2 && e2.TryActivateChainResponse(o2, io);
                PassAll(e2);
                Check("Imperial Order chains to Pot of Greed: Pot of Greed draws nothing",
                    reached2 && act2 && offered2 && chained2 && io.FaceUp && p2.Hand.Count == hand - 1 &&
                    p2.Graveyard.Contains(pog),
                    $"act={act2} offered={offered2} chained={chained2} ioFaceUp={io.FaceUp} hand {hand}->{p2.Hand.Count}");

                // Removed before its own link resolves: nothing is placed back on the field.
                var e3 = FreshHvh(db, pDeck, aDeck);
                var p3 = e3.Player;
                var o3 = e3.Opponent;
                var reached3 = ReachMain(e3, o3);
                ClearAll(e3);
                var rd3 = PlaceSetTrap(e3, p3, RoyalDecree, 0);
                var dt3 = PlaceSetTrap(e3, o3, DustTornado, 0);
                var mst3 = PlaceSetTrap(e3, o3, Mst, 1);
                e3.NotifyPublic();
                var act3 = e3.TryActivateSpellTrap(o3, dt3, fromHand: false);
                if (e3.IsAwaitingEffectTarget) e3.TrySelectEffectTarget(rd3);
                var chained3 = e3.PendingResponse?.Responder == p3 && e3.TryActivateChainResponse(p3, rd3);
                var mstChained = e3.PendingResponse?.Responder == o3 && e3.TryActivateChainResponse(o3, mst3);
                PassAll(e3);
                Check("A Royal Decree destroyed before its chain link resolves stays in the GY",
                    reached3 && act3 && chained3 && mstChained && p3.Graveyard.Contains(rd3) &&
                    !p3.TryFindSpellTrap(rd3, out _),
                    $"act={act3} chained={chained3} mst={mstChained} rdGy={p3.Graveyard.Contains(rd3)} " +
                    $"rdOnField={p3.TryFindSpellTrap(rd3, out _)}");
            });

            Guard("snatch steal under imperial order", () =>
            {
                // Snatch Steal's control is a continuous Equip effect: it returns while IO negates it and
                // comes back once IO is gone.
                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var reached = ReachMain(e, p);
                ClearAll(e);
                var prey = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var snatch = PutInHand(e, p, SnatchSteal);
                var act = e.TryActivateSpellTrap(p, snatch, fromHand: true);
                var sel = !e.IsAwaitingEffectTarget || e.TrySelectEffectTarget(prey);
                PassAll(e);
                var taken = p.TryFindMonster(prey, out _);
                var io = PlaceSetTrap(e, o, ImperialOrder, 0);
                io.FaceUp = true;
                e.NotifyPublic();
                var back = o.TryFindMonster(prey, out _) && snatch.IsNegated && snatch.EquippedTo == prey;
                e.SendCardToGrave(o, io);
                e.NotifyPublic();
                var retaken = p.TryFindMonster(prey, out _) && !snatch.IsNegated;
                Check("Imperial Order: Snatch Steal's monster returns to its owner, and is taken again once IO leaves",
                    reached && act && sel && taken && back && retaken,
                    $"act={act} sel={sel} taken={taken} back={back} retaken={retaken}");
            });

            Guard("phase triggers under negation", () =>
            {
                // Burning Land (Standby burn, turn player is the subject) under Imperial Order.
                int BurningLandLp(bool io)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    var p = e.Player;
                    var o = e.Opponent;
                    ReachMain(e, p);
                    ClearAll(e);
                    PlaceSetTrap(e, p, BurningLand, 1).FaceUp = true;
                    if (io)
                        PlaceSetTrap(e, p, ImperialOrder, 0).FaceUp = true;
                    e.NotifyPublic();
                    ReachMain(e, o);
                    p.LifePoints = 8000;
                    ReachMain(e, p);
                    return p.LifePoints;
                }

                var negatedLp = BurningLandLp(io: true);
                var controlLp = BurningLandLp(io: false);
                Check("Imperial Order: Burning Land's Standby damage is negated (IO's own 700 upkeep is still paid)",
                    negatedLp == 8000 - 700 && controlLp == 8000 - 500, $"io={negatedLp} control={controlLp}");

                // Labyrinth of Nightmare (End Phase position change) under Royal Decree.
                var e2 = FreshHvh(db, pDeck, aDeck);
                var p2 = e2.Player;
                var o2 = e2.Opponent;
                var reached2 = ReachMain(e2, p2);
                ClearAll(e2);
                var lab = PlaceSetTrap(e2, o2, LabyrinthOfNightmare, 1);
                lab.FaceUp = true;
                PlaceSetTrap(e2, o2, RoyalDecree, 0).FaceUp = true;
                var mon = PlaceMonster(e2, o2, Celtic, 0, BattlePosition.Attack, true);
                e2.NotifyPublic();
                var cycled = ReachMain(e2, o2) && ReachMain(e2, p2);
                Check("Royal Decree: Labyrinth of Nightmare changes no position in the End Phase",
                    reached2 && cycled && lab.IsNegated && mon.Position == BattlePosition.Attack,
                    $"cycled={cycled} labNeg={lab.IsNegated} pos={mon.Position}");

                // Ectoplasmer (End Phase: the turn player Tributes, then burns) under Imperial Order.
                bool TributeHappens(bool io, out string detail)
                {
                    var e = FreshHvh(db, pDeck, aDeck);
                    var p = e.Player;
                    var o = e.Opponent;
                    ReachMain(e, o);
                    ClearAll(e);
                    PlaceSetTrap(e, o, Ectoplasmer, 1).FaceUp = true;
                    if (io)
                        PlaceSetTrap(e, p, ImperialOrder, 0).FaceUp = true;
                    var tribute = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                    e.NotifyPublic();
                    var lp = p.LifePoints;
                    ReachMain(e, p);
                    detail = $"celticOnField={o.TryFindMonster(tribute, out _)} lp {lp}->{p.LifePoints}";
                    return !o.TryFindMonster(tribute, out _) && p.LifePoints == lp - 700;
                }

                var noTribute = !TributeHappens(io: true, out var de1);
                var tributeControl = TributeHappens(io: false, out var de2);
                Check("Imperial Order: Ectoplasmer makes no Tribute and deals no damage",
                    noTribute && tributeControl, $"io[{de1}] control[{de2}]");
            });

            Guard("gorgon's eye", () =>
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(GorgonsEye));
                Check("Gorgon's Eye: FullyCompiled — a Normal Trap negating every Defense Position monster's " +
                      "effects until the end of the turn; Implemented",
                    prog != null && prog.FullyCompiled &&
                    CardEffectStatus.Classify(db.Get(GorgonsEye)) == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action ==
                        EffectActionKind.NegateEffectsOfDefensePositionMonstersThisTurn &&
                    prog.ClauseList[0].Timing == EffectTiming.Activate &&
                    prog.ClauseList[0].Side == EffectSide.Both &&
                    prog.ClauseList[0].MakesChainLink && !prog.ClauseList[0].RequiresTargetChoice,
                    prog == null ? "null" : string.Join(" | ", prog.UnparsedFragments ?? Array.Empty<string>()));

                var e = FreshHvh(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                ReachMain(e, p);
                ClearAll(e);
                var mineDef = PlaceMonster(e, p, Celtic, 0, BattlePosition.Defense, true);
                var mineAtk = PlaceMonster(e, p, DarkMagician, 1, BattlePosition.Attack, true);
                var theirsDef = PlaceMonster(e, o, Celtic, 2, BattlePosition.Defense, true);
                var theirsAtk = PlaceMonster(e, o, DarkMagician, 3, BattlePosition.Attack, true);
                // Gorgon's Eye is a Normal Trap: it must be Set for a turn before it can be activated.
                var gorgon = PlaceSetTrap(e, p, GorgonsEye, 0);
                gorgon.SetThisTurn = false;
                e.NotifyPublic();
                var activated = e.TryActivateSpellTrap(p, gorgon, false);
                PassAll(e);
                FieldSpellEffects.RefreshBoard(e);
                Check("Gorgon's Eye: every face-up Defense Position monster on the field is negated (both sides)",
                    activated && p.Graveyard.Contains(gorgon) &&
                    mineDef.GorgonEyeNegated && theirsDef.GorgonEyeNegated &&
                    !mineAtk.GorgonEyeNegated && !theirsAtk.GorgonEyeNegated &&
                    mineDef.IsNegated && theirsDef.IsNegated && !mineAtk.IsNegated && !theirsAtk.IsNegated,
                    $"activated={activated} mineDef={mineDef.GorgonEyeNegated} theirsDef={theirsDef.GorgonEyeNegated} " +
                    $"mineAtk={mineAtk.GorgonEyeNegated} theirsAtk={theirsAtk.GorgonEyeNegated} " +
                    $"negDef={mineDef.IsNegated} negAtk={mineAtk.IsNegated}");

                var flipped = e.TryChangePosition(p, mineDef);
                FieldSpellEffects.RefreshBoard(e);
                Check("Gorgon's Eye: a monster changed to Attack Position is no longer negated (the rule follows " +
                      "the position)",
                    flipped && !mineDef.GorgonEyeNegated && mineDef.Position == BattlePosition.Attack &&
                    !mineDef.IsNegated && theirsDef.GorgonEyeNegated,
                    $"flipped={flipped} pos={mineDef.Position} neg={mineDef.GorgonEyeNegated} " +
                    $"theirs={theirsDef.GorgonEyeNegated}");

                e.EndTurn(p);
                PassAll(e);
                Check("Gorgon's Eye: after the End Phase the negation is over",
                    !theirsDef.GorgonEyeNegated && !theirsDef.IsNegated,
                    $"theirsDef={theirsDef.GorgonEyeNegated} neg={theirsDef.IsNegated}");
            });
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshHvh(CardDatabase db, DeckFile p, DeckFile a)
        {
            var e = new DuelEngine { HumanVsHuman = true };
            e.StartDuel(db, p, a, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            return e;
        }

        /// <summary>Shared <see cref="InteractionRegressionTests.ClearBoard"/>, plus any open response window.</summary>
        static void ClearAll(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
        }

        /// <summary>End turns (passing every window) until <paramref name="who"/> is in a Main Phase.</summary>
        static bool ReachMain(DuelEngine e, DuelistState who)
        {
            for (var t = 0; t < 8; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                // "Pay N LP or destroy this card" asks a human in their Standby Phase (v142): these checks pay.
                if (e.PendingActivation?.AwaitingUpkeepChoice == true) e.TrySelectLpCost(e.PendingActivation.CostNumeric);
                e.RecoverStuckCombat();
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                if (e.TurnPlayer == who && e.InMainPhase && !e.IsAwaitingResponse) return true;
                if (e.TurnPlayer == who && e.InMainPhase) continue;
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            return e.TurnPlayer == who && e.InMainPhase;
        }

        /// <summary>
        /// Step SimpleAi's turn synchronously (presentation holds cleared each step); the player passes
        /// every window. Same driver as ChainRulesRegressionTests.
        /// </summary>
        static bool RunAiTurn(DuelEngine e, int maxSteps, out int steps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            steps = 0;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                while (steps < maxSteps)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                    steps++;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 6 && (e.IsAwaitingResponse || e.Chain.HasLinks); i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else break;
            }
        }

        static void Drain(DuelEngine e)
        {
            for (var i = 0; i < 12; i++)
            {
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                if (e.IsAwaitingResponse) e.PassResponse();
                else if (e.HasDeclaredAttack) e.ResolveDeclaredAttack();
                else e.RecoverStuckCombat();
            }
        }

    }
}
