using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// On-hand (outfit-pocket) deck list as a detached HUD popout of text-fit chips.
    /// Each row's plate is as wide as its title; the stack sizes to the widest chip.
    /// </summary>
    public static class OnHandDeckSwitchMenu
    {
        public const string HostName = "DeckSwitchList";
        public const string EmptyCopy = "No decks on hand";
        public const float RowPx = TextFitButton.MinH;
        const float StackPad = 8f;
        const float StackGap = 6f;

        public static float ContentHeight(int rowCount) =>
            StackPad * 2f + Mathf.Max(1, rowCount) * RowPx
            + Mathf.Max(0, Mathf.Max(1, rowCount) - 1) * StackGap;

        public static float PreferredWidth(float hudW, IList<string> titles)
        {
            var widest = TextFitButton.WidthFor(EmptyCopy, withIcon: false);
            if (titles != null)
            {
                for (var i = 0; i < titles.Count; i++)
                {
                    var line = "▸  " + (string.IsNullOrEmpty(titles[i]) ? "Deck" : titles[i]);
                    widest = Mathf.Max(widest, TextFitButton.WidthFor(line, withIcon: true));
                }
            }

            var needed = widest + StackPad * 2f;
            var maxW = hudW < 32f ? 1080f : Mathf.Clamp(hudW * 0.96f, 320f, 1080f);
            return Mathf.Clamp(needed, 200f, maxW);
        }

        public static float PreferredWidth(float hudW, PlayerInventory inv)
        {
            var titles = new List<string>();
            if (inv != null)
            {
                foreach (var i in inv.OnHandDeckIndices())
                    titles.Add(inv.DeckBoxDisplayName(i));
            }

            return PreferredWidth(hudW, titles);
        }

        public static GameObject Build(RectTransform hud, PlayerInventory inv, Action<int> onPick)
        {
            if (hud == null) return null;
            var existing = hud.Find(HostName);
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);

            inv?.EnsureDeckBoxSlots();
            var hands = inv != null ? inv.OnHandDeckIndices() : new List<int>();
            var active = inv != null ? inv.ActivePlayDeckIndex : -1;

            var host = new GameObject(HostName, typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            host.transform.SetParent(hud, false);
            var rt = host.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);

            var vlg = host.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)StackPad, (int)StackPad, (int)StackPad, (int)StackPad);
            vlg.spacing = StackGap;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var fit = host.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (hands.Count == 0)
            {
                TextFitButton.Create(host.transform, "Empty", EmptyCopy, null,
                    new Color(0.07f, 0.11f, 0.18f, 0.92f),
                    new Color(0.35f, 0.80f, 0.95f, 0.35f),
                    null);
                return host;
            }

            for (var i = 0; i < hands.Count; i++)
            {
                var idx = hands[i];
                var box = inv.deckBoxes != null && idx >= 0 && idx < inv.deckBoxes.Length
                    ? inv.deckBoxes[idx] : null;
                var selected = idx == active;
                var title = (selected ? "▸  " : "") + (inv.DeckBoxDisplayName(idx) ?? "Deck");
                var icon = ImagineAssets.DeckStyleIcon(box?.iconId ?? "") ?? ImagineAssets.IconDeck();
                var captured = idx;
                TextFitButton.Create(host.transform, "D" + i, title, icon,
                    selected
                        ? new Color(0.10f, 0.38f, 0.50f, 0.96f)
                        : new Color(0.07f, 0.11f, 0.18f, 0.96f),
                    selected
                        ? new Color(0.40f, 0.90f, 1f, 0.85f)
                        : new Color(0.35f, 0.80f, 0.95f, 0.40f),
                    onPick == null ? null : () => onPick(captured));
            }

            return host;
        }
    }
}
