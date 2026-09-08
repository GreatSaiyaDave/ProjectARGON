using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Duel.Rules
{
    /// <summary>One link on the Chain (official Chain rules).</summary>
    public sealed class ChainLink
    {
        public int LinkNumber;
        public DuelistState Controller;
        public CardInstance Card;
        public CardLocation FromLocation;
        public SpellSpeed Speed;
        public EffectClass Class;
        public bool WasSetOnField;
        public bool FromHand;
        public CardInstance Target;
        public readonly List<CardInstance> Targets = new();
        public string EffectKey;
        /// Link this effect answers (response links only).
        public ChainLink TargetLink;
        /// Response effect metadata, stamped when the link is activated.
        public bool NegatesActivation;
        public bool DestroyNegatedCard;
        public bool FlipSelfFaceUpDefense;
        public bool Negated;
        public bool Resolved;
        /// <summary>Official text at activation time (immutable snapshot).</summary>
        public string OfficialTextSnapshot;
        /// <summary>
        /// Combat/summon window this link answered. None = open-game Activate clauses.
        /// Resolution must apply the matching trigger timing, not re-enter the window.
        /// </summary>
        public ResponseTiming SeatedFrom;
        public CardInstance SeatedSummoned;
    }

    /// <summary>
    /// Chain stack: CL1…CLn, resolve last-to-first.
    /// Spell Speed: Speed 1 cannot be chained to Speed 2/3; only equal-or-higher speed chains
    /// (with Speed 1 only as CL1, except Trigger effects building SEGOC).
    /// </summary>
    public sealed class ChainStack
    {
        readonly List<ChainLink> _links = new();
        public IReadOnlyList<ChainLink> Links => _links;
        public bool IsBuilding { get; private set; }
        public bool IsResolving { get; private set; }
        public int Count => _links.Count;
        public bool HasLinks => _links.Count > 0;
        public ChainLink Cl1 => _links.Count > 0 ? _links[0] : null;
        public ChainLink Last => _links.Count > 0 ? _links[_links.Count - 1] : null;

        public event Action OnChainChanged;
        public event Action OnChainFullyResolved;

        public void BeginBuilding()
        {
            IsBuilding = true;
            IsResolving = false;
        }

        public bool CanAddLink(SpellSpeed newSpeed, EffectClass cls)
        {
            if (IsResolving) return false;
            if (_links.Count == 0) return true; // CL1 may be Speed 1+

            var last = Last.Speed;
            // Counter Traps (3) can chain to anything activatable; Speed 2 to Speed 1 or 2; Speed 1 only as CL1
            // (Trigger effects that form SEGOC are handled by adding before players respond)
            if (newSpeed == SpellSpeed.None) return false;
            if (newSpeed == SpellSpeed.Speed1 && cls != EffectClass.Trigger && cls != EffectClass.TriggerLike)
                return false; // cannot chain Speed 1 Ignition/Normal Spell after chain started
            if ((int)newSpeed < (int)last && newSpeed != SpellSpeed.Speed3)
            {
                // Only Speed 3 can chain "down" onto higher? Actually Speed 3 chains to 1/2/3;
                // Speed 2 chains to 1 or 2; Speed 1 cannot chain to open chain except SEGOC triggers.
                if (newSpeed == SpellSpeed.Speed2 && last == SpellSpeed.Speed3) return false;
                if (newSpeed == SpellSpeed.Speed1) return false;
            }

            if (newSpeed == SpellSpeed.Speed2 && last == SpellSpeed.Speed3) return false;
            return true;
        }

        public ChainLink AddLink(DuelistState controller, CardInstance card, SpellSpeed speed,
            EffectClass cls, CardLocation from, bool fromHand, bool wasSet, string effectKey,
            CardInstance target = null)
        {
            if (!CanAddLink(speed, cls)) return null;
            if (!IsBuilding && _links.Count == 0)
                BeginBuilding();

            var link = new ChainLink
            {
                LinkNumber = _links.Count + 1,
                Controller = controller,
                Card = card,
                FromLocation = from,
                Speed = speed,
                Class = cls,
                FromHand = fromHand,
                WasSetOnField = wasSet,
                EffectKey = effectKey,
                Target = target,
                OfficialTextSnapshot = OfficialCardAuthority.OfficialText(card)
            };
            if (target != null) link.Targets.Add(target);
            _links.Add(link);
            OnChainChanged?.Invoke();
            return link;
        }

        /// <summary>Both players passed — resolve reverse order.</summary>
        public void StartResolution()
        {
            IsBuilding = false;
            IsResolving = true;
        }

        public ChainLink PopNextToResolve()
        {
            if (_links.Count == 0)
            {
                IsResolving = false;
                OnChainFullyResolved?.Invoke();
                return null;
            }

            var last = _links[_links.Count - 1];
            _links.RemoveAt(_links.Count - 1);
            if (_links.Count == 0)
            {
                IsResolving = false;
                OnChainFullyResolved?.Invoke();
            }

            OnChainChanged?.Invoke();
            return last;
        }

        public void Clear()
        {
            _links.Clear();
            IsBuilding = false;
            IsResolving = false;
            OnChainChanged?.Invoke();
        }

        public string Describe() =>
            _links.Count == 0
                ? "(empty chain)"
                : string.Join(" → ", _links.Select(l =>
                    $"CL{l.LinkNumber}:{l.Card?.Name ?? "?"}(SS{(int)l.Speed})"));
    }
}
