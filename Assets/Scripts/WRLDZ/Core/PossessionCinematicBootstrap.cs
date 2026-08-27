using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Core
{
    /// <summary>
    /// Placeholder possession beat before an NPC street duel.
    /// Real cinematic assets will replace these stills.
    /// Sequence: human → Tear opens → spirit takes the body → aether disk → DuelSlice.
    /// </summary>
    public class PossessionCinematicBootstrap : MonoBehaviour
    {
        const float BeatSec = 1.35f;
        Image _still;
        Text _caption;
        CanvasGroup _fade;
        bool _leaving;

        static readonly string[] Captions =
        {
            "They look human.",
            "A Tear opens behind them.",
            "A spirit takes the body.",
            "A dark aetherial disk appears."
        };

        void Start()
        {
            WrldzLab.Apply();
            StartCoroutine(Play());
        }

        IEnumerator Play()
        {
            AppSession.Ensure().RefreshFromStore();
            if (!AppSession.Ensure().IsLoggedIn)
            {
                AppSession.Ensure().GoBoot();
                yield break;
            }

            var match = AppSession.Ensure().PendingArMatch;
            if (match == null || !match.PossessionCinematic)
            {
                AppSession.Ensure().GoDuel(AppSession.SceneOverworld);
                yield break;
            }

            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var canvas = WrldzTheme.Canvas("PossessionCanvas", 80);
            var root = WrldzTheme.StretchFill(canvas, "Void", new Color(0.02f, 0.01f, 0.05f, 1f));

            var stillGo = new GameObject("Still", typeof(RectTransform), typeof(Image));
            stillGo.transform.SetParent(root, false);
            FloatingPanel.Stretch(stillGo.GetComponent<RectTransform>());
            _still = stillGo.GetComponent<Image>();
            _still.sprite = ImagineAssets.CinematicPossessHuman() ?? ImagineAssets.BgOverworldMap();
            _still.color = Color.white;
            _still.preserveAspect = false;
            _still.raycastTarget = false;

            var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            fadeGo.transform.SetParent(root, false);
            FloatingPanel.Stretch(fadeGo.GetComponent<RectTransform>());
            var fadeImg = fadeGo.GetComponent<Image>();
            fadeImg.sprite = UiFoundation.WhiteSprite();
            fadeImg.color = Color.black;
            fadeImg.raycastTarget = false;
            _fade = fadeGo.GetComponent<CanvasGroup>();
            _fade.alpha = 1f;
            _fade.blocksRaycasts = false;

            _caption = WrldzTheme.Label(root, "Caption", Captions[0], 18, DuelystUi.TextCream,
                TextAnchor.LowerCenter, true);
            FloatingPanel.Place(_caption.rectTransform, 0.08f, 0.10f, 0.92f, 0.22f);
            WrldzType.ApplyOutline(_caption, heavy: true, buttonContrast: true);

            var skip = FloatingPanel.PrimaryButton(root, "SKIP", Finish);
            FloatingPanel.Place(skip.GetComponent<RectTransform>(), 0.62f, 0.03f, 0.94f, 0.10f);

            var cancel = FloatingPanel.PrimaryButton(root, "CANCEL", Cancel);
            FloatingPanel.Place(cancel.GetComponent<RectTransform>(), 0.06f, 0.03f, 0.38f, 0.10f);

            var name = match.ZoneTitle;
            if (!string.IsNullOrEmpty(name))
            {
                var who = WrldzTheme.Label(root, "Who", name.ToUpperInvariant(), 14, DuelystUi.GoldHot,
                    TextAnchor.UpperCenter, true);
                FloatingPanel.Place(who.rectTransform, 0.10f, 0.90f, 0.90f, 0.97f);
            }

            Sprite[] frames =
            {
                ImagineAssets.CinematicPossessHuman(),
                ImagineAssets.CinematicPossessTear(),
                ImagineAssets.CinematicPossessSpirit(),
                ImagineAssets.CinematicPossessDisk()
            };

            yield return FadeTo(0f, 0.35f);

            for (var i = 0; i < frames.Length; i++)
            {
                if (_leaving) yield break;
                if (frames[i] != null)
                    _still.sprite = frames[i];
                if (_caption != null)
                    _caption.text = Captions[i];
                var t = 0f;
                while (t < BeatSec && !_leaving)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (!_leaving)
                Finish();
        }

        IEnumerator FadeTo(float target, float dur)
        {
            if (_fade == null) yield break;
            var from = _fade.alpha;
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _fade.alpha = Mathf.Lerp(from, target, t / dur);
                yield return null;
            }

            _fade.alpha = target;
        }

        void Finish()
        {
            if (_leaving) return;
            _leaving = true;
            AppSession.Ensure().ContinuePendingDuel();
        }

        void Cancel()
        {
            if (_leaving) return;
            _leaving = true;
            AppSession.Ensure().ClearPendingArMatch();
            AppSession.Ensure().GoOverworld();
        }
    }
}
