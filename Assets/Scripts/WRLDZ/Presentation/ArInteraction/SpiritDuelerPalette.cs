using System;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Palette swap for the Spirit Dueler: the Duel Disk body colour and the aura colour are chosen separately
    /// (<see cref="AvatarAppearance.diskHex"/> / <see cref="AvatarAppearance.auraHex"/>, catalogs in
    /// <see cref="AvatarCatalog.DiskPalettes"/> / <see cref="AvatarCatalog.AuraPalettes"/>); empty = the avatar's
    /// Spirit magic colour, which is how every disk looked before palettes. The avatar customizer edits them;
    /// <see cref="Changed"/> re-applies them to a live disk (crossfade, no pop).
    /// </summary>
    public static class SpiritDuelerPalette
    {
        public static readonly Color DefaultSpirit = new(0.25f, 0.9f, 1f, 1f);

        /// <summary>Raised when the player's palettes change (customizer preview / save, lab cycle).</summary>
        public static event Action Changed;

        public static void NotifyChanged() => Changed?.Invoke();

        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        /// <summary>Disk + aura colours of an appearance (null = defaults).</summary>
        public static void Resolve(AvatarAppearance a, out Color disk, out Color aura)
        {
            var spirit = Parse(a?.accentHex, DefaultSpirit);
            disk = Parse(a?.diskHex, spirit);
            aura = Parse(a?.auraHex, spirit);
            disk.a = aura.a = 1f;
        }

        /// <summary>The signed-in player's palettes (the lab preview override wins while it is set).</summary>
        public static void ForPlayer(out Color disk, out Color aura)
        {
            var acc = AppSession.Ensure()?.Account;
            var a = acc?.avatar;
            if (a != null && string.IsNullOrEmpty(a.accentHex) && !string.IsNullOrEmpty(acc.avatarColor))
            {
                a = a.Clone();
                a.accentHex = acc.avatarColor;
            }

            Resolve(a, out disk, out aura);
            if (_previewDisk >= 0) disk = Parse(AvatarCatalog.DiskPalettes[_previewDisk], disk);
            if (_previewAura >= 0) aura = Parse(AvatarCatalog.AuraPalettes[_previewAura], aura);
        }

        // ── Editor / development preview: cycle palettes live in a duel (not saved) ────────────────────────
        static int _previewDisk = -1, _previewAura = -1;

        public static string PreviewLabel =>
            "Disk: " + (_previewDisk >= 0 ? AvatarCatalog.DiskPaletteNames[_previewDisk] : "saved") +
            " · Aura: " + (_previewAura >= 0 ? AvatarCatalog.AuraPaletteNames[_previewAura] : "saved");

        public static void CyclePreviewDisk()
        {
            _previewDisk = (_previewDisk + 1) % AvatarCatalog.DiskPalettes.Length;
            if (_previewDisk == 0) _previewDisk = 1;   // index 0 is "match Spirit magic", i.e. no override
            NotifyChanged();
        }

        public static void CyclePreviewAura()
        {
            _previewAura = (_previewAura + 1) % AvatarCatalog.AuraPalettes.Length;
            if (_previewAura == 0) _previewAura = 1;
            NotifyChanged();
        }

        public static void ClearPreview()
        {
            _previewDisk = _previewAura = -1;
            NotifyChanged();
        }
    }
}
