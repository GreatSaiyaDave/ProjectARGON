using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>Loads card definitions and art from StreamingAssets.</summary>
    public class CardDatabase
    {
        public static CardDatabase Instance { get; private set; }

        readonly Dictionary<int, CardDef> _byId = new();
        readonly Dictionary<int, Sprite> _art = new();
        readonly Dictionary<int, Sprite> _artwork = new();

        public int Count => _byId.Count;

        public static CardDatabase Load()
        {
            var db = new CardDatabase();
            var path = Path.Combine(Application.streamingAssetsPath, "Cards", "cards_db.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[WRLDZ] Missing card DB at {path}");
                Instance = db;
                return db;
            }

            var json = File.ReadAllText(path);
            var file = JsonUtility.FromJson<CardDatabaseFile>(json);
            if (file?.cards == null)
            {
                Debug.LogError("[WRLDZ] Failed to parse cards_db.json");
                Instance = db;
                return db;
            }

            foreach (var c in file.cards)
            {
                if (c == null) continue;
                db._byId[c.id] = c;
            }

            Debug.Log($"[WRLDZ] Loaded {db._byId.Count} cards from {path}");
            Instance = db;
            return db;
        }

        public bool TryGet(int id, out CardDef def) => _byId.TryGetValue(id, out def);

        public CardDef Get(int id) => _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>Snapshot of all loaded card defs (bulk compile / tooling).</summary>
        public List<CardDef> GetAllCards() => new List<CardDef>(_byId.Values);

        /// <summary>Full card face (frame, name, text, art) for disk / hand / inspect UI.</summary>
        public Sprite GetArt(int id)
        {
            if (_art.TryGetValue(id, out var cached) && cached != null)
                return cached;

            // Prefer *_full.* when present so cards like Kuriboh are not shown as a
            // square art-only pack on the disk (that reads as "cropped by default").
            var path = FindCardArtPath(id, preferFullFace: true);
            if (path == null)
                return null;

            var sprite = LoadSpriteFromFile(path, $"card_sprite_{id}");
            if (sprite == null)
                return null;

            _art[id] = sprite;
            return sprite;
        }

        /// <summary>
        /// Illustration window for arena holos. Never double-crops square packs (Kuriboh 624²).
        /// Prefer a dedicated non-_full art file when present; otherwise crop a full-card scan.
        /// </summary>
        public Sprite GetArtwork(int id)
        {
            if (_artwork.TryGetValue(id, out var cached) && cached != null)
                return cached;

            // 1) Dedicated art-only pack (e.g. CardArt/40640057.jpg square) — full image, no crop
            var artOnlyPath = FindCardArtPath(id, preferFullFace: false, artOnlyPreferred: true);
            var fullPath = FindCardArtPath(id, preferFullFace: true);
            if (artOnlyPath != null &&
                (fullPath == null ||
                 !string.Equals(artOnlyPath, fullPath, System.StringComparison.OrdinalIgnoreCase)))
            {
                var artSpr = LoadSpriteFromFile(artOnlyPath, $"card_artwork_{id}");
                if (artSpr != null && artSpr.texture != null)
                {
                    if (Presentation.CardArtFocus.IsPreCroppedIllustration(artSpr.texture) ||
                        !Presentation.CardArtFocus.LooksLikeFullCardScan(artSpr.texture))
                    {
                        _artwork[id] = artSpr;
                        return artSpr;
                    }

                    // Art path was actually a full scan — crop it
                    var croppedFromArt = CreateArtworkCropSprite(artSpr.texture, id);
                    _artwork[id] = croppedFromArt;
                    return croppedFromArt;
                }
            }

            // 2) From full-face texture (may be _full.jpg)
            var full = GetArt(id);
            if (full == null || full.texture == null)
                return null;

            var tex = full.texture;
            if (Presentation.CardArtFocus.IsPreCroppedIllustration(tex) ||
                !Presentation.CardArtFocus.LooksLikeFullCardScan(tex))
            {
                _artwork[id] = full;
                return full;
            }

            var cropped = CreateArtworkCropSprite(tex, id);
            _artwork[id] = cropped;
            return cropped;
        }

        static Sprite CreateArtworkCropSprite(Texture2D tex, int id)
        {
            var r = Presentation.CardArtFocus.ArtworkNormRect;
            var rect = new Rect(
                r.x * tex.width,
                r.y * tex.height,
                r.width * tex.width,
                r.height * tex.height);
            rect.x = Mathf.Clamp(rect.x, 0f, tex.width - 1f);
            rect.y = Mathf.Clamp(rect.y, 0f, tex.height - 1f);
            rect.width = Mathf.Min(rect.width, tex.width - rect.x);
            rect.height = Mathf.Min(rect.height, tex.height - rect.y);
            var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
            sprite.name = $"card_artwork_{id}";
            return sprite;
        }

        static Sprite LoadSpriteFromFile(string path, string spriteName)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            tex.name = Path.GetFileNameWithoutExtension(path);
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = spriteName;
            return sprite;
        }

        /// <summary>
        /// Resolve art file path under StreamingAssets.
        /// preferFullFace: try {id}_full.* then {id}.* (disk/UI full TCG face).
        /// artOnlyPreferred: try non-_full first (square illustration packs).
        /// </summary>
        static string FindCardArtPath(int id, bool preferFullFace, bool artOnlyPreferred = false)
        {
            var dirs = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "CardArt"),
                Path.Combine(Application.streamingAssetsPath, "WRLDZ", "CardArt")
            };
            var exts = new[] { ".jpg", ".JPG", ".png", ".jpeg", ".PNG" };

            string TryNames(params string[] stems)
            {
                foreach (var dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var stem in stems)
                    foreach (var ext in exts)
                    {
                        var candidate = Path.Combine(dir, stem + ext);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }

                return null;
            }

            if (artOnlyPreferred)
            {
                // Square / art-only pack first (e.g. 40640057.jpg 624²), then full scan
                return TryNames($"{id}", $"{id}_full");
            }

            if (preferFullFace)
            {
                // Full TCG face first (Kuriboh: 40640057_full.jpg), then bare id
                return TryNames($"{id}_full", $"{id}");
            }

            return TryNames($"{id}", $"{id}_full");
        }



        /// <summary>Clear cached sprites (e.g. between restarts if art files change).</summary>
        public void ClearArtCache()
        {
            // Artwork may share the same Sprite as full-face (pre-cropped square packs).
            // Only destroy distinct artwork sprites first, then full faces + textures.
            foreach (var kv in _artwork)
            {
                if (kv.Value == null) continue;
                if (_art.TryGetValue(kv.Key, out var full) && full == kv.Value)
                    continue;
                Object.Destroy(kv.Value);
            }

            _artwork.Clear();

            foreach (var kv in _art)
            {
                if (kv.Value != null)
                {
                    if (kv.Value.texture != null)
                        Object.Destroy(kv.Value.texture);
                    Object.Destroy(kv.Value);
                }
            }

            _art.Clear();
        }

        public static DeckFile LoadDeck(string fileName)
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Decks", fileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[WRLDZ] Missing deck {path}");
                return null;
            }

            var json = File.ReadAllText(path);
            var deck = JsonUtility.FromJson<DeckFile>(json);
            if (deck == null)
                Debug.LogError($"[WRLDZ] Failed to parse deck {fileName}");
            else
                Debug.Log($"[WRLDZ] Loaded deck '{deck.name}' from {fileName}");
            return deck;
        }

        /// <summary>Expand qty entries into a shuffled list of card ids (main deck only for slice).</summary>
        public static List<int> BuildMainPile(DeckFile deck)
        {
            var list = new List<int>();
            if (deck?.main == null) return list;
            foreach (var e in deck.main)
            {
                if (e == null || e.qty <= 0) continue;
                for (var i = 0; i < e.qty; i++)
                    list.Add(e.id);
            }

            return list;
        }

        public static void Shuffle(List<int> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
