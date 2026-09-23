using System.Collections.Generic;
using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Multi-link chain rules: Spell Speed chain-legality (<see cref="ChainStack.CanAddLink"/>),
    /// Last-In-First-Out resolution order (<see cref="DuelEngine.ResolveChainLifo"/>), and
    /// negation skipping. These lock in the chain engine that Quick-Effect / Counter-Trap
    /// interaction is built on.
    /// </summary>
    public static class ChainRegressionTests
    {
        // Real passcodes so ChainStack.AddLink can snapshot official text.
        const int Raigeki = 12580477;       // Normal Spell (Speed 1)
        const int MysticalSpaceTyphoon = 5318639; // Quick-Play Spell (Speed 2)
        const int MirrorForce = 44095762;   // Normal Trap (Speed 2)
        const int SevenTools = 25213601;    // Counter Trap (Speed 3)

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; sb.AppendLine("PASS  " + name); }
                else { fail++; sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }

            // ── Spell Speed chain-legality (ChainStack.CanAddLink) ──
            // CL1 may be any speed.
            Check("Chain: CL1 accepts Speed 1", new ChainStack().CanAddLink(SpellSpeed.Speed1, EffectClass.Ignition));
            Check("Chain: CL1 accepts Speed 2", new ChainStack().CanAddLink(SpellSpeed.Speed2, EffectClass.Quick));
            Check("Chain: CL1 accepts Speed 3", new ChainStack().CanAddLink(SpellSpeed.Speed3, EffectClass.Quick));

            // Speed 1 (Normal Spell / Ignition) cannot chain onto an open chain.
            Check("Chain: Speed 1 Normal cannot chain onto Speed 1",
                !AfterLink(SpellSpeed.Speed1).CanAddLink(SpellSpeed.Speed1, EffectClass.Ignition));
            Check("Chain: Speed 1 Ignition cannot chain onto Speed 2",
                !AfterLink(SpellSpeed.Speed2).CanAddLink(SpellSpeed.Speed1, EffectClass.Ignition));

            // Speed 2 chains onto Speed 1 or Speed 2, but not onto a Counter Trap (Speed 3).
            Check("Chain: Speed 2 chains onto Speed 1",
                AfterLink(SpellSpeed.Speed1).CanAddLink(SpellSpeed.Speed2, EffectClass.Quick));
            Check("Chain: Speed 2 chains onto Speed 2",
                AfterLink(SpellSpeed.Speed2).CanAddLink(SpellSpeed.Speed2, EffectClass.Quick));
            Check("Chain: Speed 2 cannot chain onto Speed 3",
                !AfterLink(SpellSpeed.Speed3).CanAddLink(SpellSpeed.Speed2, EffectClass.Quick));

            // Counter Trap (Speed 3) chains onto anything.
            Check("Chain: Speed 3 chains onto Speed 1",
                AfterLink(SpellSpeed.Speed1).CanAddLink(SpellSpeed.Speed3, EffectClass.Quick));
            Check("Chain: Speed 3 chains onto Speed 2",
                AfterLink(SpellSpeed.Speed2).CanAddLink(SpellSpeed.Speed3, EffectClass.Quick));
            Check("Chain: Speed 3 chains onto Speed 3",
                AfterLink(SpellSpeed.Speed3).CanAddLink(SpellSpeed.Speed3, EffectClass.Quick));

            // A resolving chain accepts no new links.
            var resolving = AfterLink(SpellSpeed.Speed2);
            resolving.StartResolution();
            Check("Chain: no links added while resolving",
                !resolving.CanAddLink(SpellSpeed.Speed3, EffectClass.Quick));

            // ── LIFO resolution order ──
            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                Check("Chain: CardDatabase load", false);
                return Finish(sb, pass, fail);
            }

            var engine = new DuelEngine();
            var pDeck = CardDatabase.LoadDeck("lab_rules_player.json");
            var aDeck = CardDatabase.LoadDeck("lab_rules_ai.json");
            engine.StartDuel(db, pDeck, aDeck, cinematicOpening: false);

            // Build CL1 (Raigeki, Speed 1) → CL2 (MST, Speed 2) → CL3 (Seven Tools, Speed 3).
            engine.Chain.Clear();
            engine.Chain.BeginBuilding();
            var l1 = AddCard(engine, engine.Player, Raigeki, SpellSpeed.Speed1, EffectClass.Ignition);
            var l2 = AddCard(engine, engine.Opponent, MysticalSpaceTyphoon, SpellSpeed.Speed2, EffectClass.Quick);
            var l3 = AddCard(engine, engine.Player, SevenTools, SpellSpeed.Speed3, EffectClass.Quick);
            Check("Chain: three links built", l1 != null && l2 != null && l3 != null,
                $"l1={l1 != null} l2={l2 != null} l3={l3 != null}");
            Check("Chain: link numbers are 1,2,3",
                l1 != null && l1.LinkNumber == 1 && l2 != null && l2.LinkNumber == 2 && l3 != null && l3.LinkNumber == 3);

            var order = new List<int>();
            var resolvedCount = engine.ResolveChainLifo(link => { order.Add(link.LinkNumber); return true; });
            Check("Chain: resolves LIFO (CL3, CL2, CL1)",
                order.Count == 3 && order[0] == 3 && order[1] == 2 && order[2] == 1,
                "order=" + string.Join(",", order));
            Check("Chain: all three links resolved", resolvedCount == 3, $"resolved={resolvedCount}");
            Check("Chain: stack empty after resolution", !engine.Chain.HasLinks);

            // ── Negation: a negated link is skipped, lower links still resolve ──
            engine.Chain.Clear();
            engine.Chain.BeginBuilding();
            var n1 = AddCard(engine, engine.Player, Raigeki, SpellSpeed.Speed1, EffectClass.Ignition);
            var n2 = AddCard(engine, engine.Opponent, SevenTools, SpellSpeed.Speed3, EffectClass.Quick);
            // CL2 (Seven Tools) negates CL1 (Raigeki): mark CL1 negated before resolving.
            if (n1 != null) n1.Negated = true;
            var negOrder = new List<int>();
            var negResolved = engine.ResolveChainLifo(link => { negOrder.Add(link.LinkNumber); return true; });
            Check("Chain: negated CL1 skipped, CL2 resolves first",
                negOrder.Count == 1 && negOrder[0] == 2, "order=" + string.Join(",", negOrder));
            Check("Chain: only one link resolved when CL1 negated", negResolved == 1, $"resolved={negResolved}");
            Check("Chain: negated link marked resolved", n1 != null && n1.Resolved);

            // ── Empty chain is a no-op ──
            engine.Chain.Clear();
            Check("Chain: empty chain resolves nothing", engine.ResolveChainLifo(_ => true) == 0);

            // ── One-shot Normal Spell leaves the S/T zone for GY after LIFO resolve ──
            engine.Chain.Clear();
            engine.Chain.BeginBuilding();
            var raigeki = engine.CreateCardInstance(Raigeki);
            if (raigeki != null)
            {
                engine.Player.SpellTrapZones[0].Occupant = raigeki;
                raigeki.FaceUp = true;
            }
            var gyLink = engine.Chain.AddLink(engine.Player, raigeki, SpellSpeed.Speed1,
                EffectClass.Ignition, CardLocation.SpellTrapZone,
                fromHand: false, wasSet: false, effectKey: "test");
            engine.ResolveChainLifo(_ => true);
            var stillOnField = engine.Player.TryFindSpellTrap(raigeki, out _);
            var inGy = raigeki != null && engine.Player.Graveyard.Contains(raigeki);
            Check("Chain: one-shot Normal Spell sent to GY after resolve",
                gyLink != null && !stillOnField && inGy,
                $"link={gyLink != null} onField={stillOnField} inGy={inGy}");

            return Finish(sb, pass, fail);
        }

        static ChainStack AfterLink(SpellSpeed cl1Speed)
        {
            var stack = new ChainStack();
            stack.BeginBuilding();
            var cls = cl1Speed == SpellSpeed.Speed1 ? EffectClass.Ignition : EffectClass.Quick;
            stack.AddLink(null, null, cl1Speed, cls, CardLocation.SpellTrapZone,
                fromHand: false, wasSet: true, effectKey: "test");
            return stack;
        }

        static ChainLink AddCard(DuelEngine engine, DuelistState who, int cardId,
            SpellSpeed speed, EffectClass cls)
        {
            var card = engine.CreateCardInstance(cardId);
            return engine.Chain.AddLink(who, card, speed, cls, CardLocation.SpellTrapZone,
                fromHand: false, wasSet: true, effectKey: "test");
        }

        static string Finish(StringBuilder sb, int pass, int fail)
        {
            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }
    }
}
