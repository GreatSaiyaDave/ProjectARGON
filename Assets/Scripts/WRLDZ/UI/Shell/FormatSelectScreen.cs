using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Player vs AI format cards. Every unlocked PLAY ends at
    /// <see cref="AppSession.StartArDuel"/> with <see cref="ArDuelOpponentKind.AiLocal"/>.
    /// </summary>
    public static class FormatSelectScreen
    {
        struct FormatCard
        {
            public string Title;
            public string Rules;
            public string Lp;
            public bool Unlocked;
            public System.Action Play;
        }

        public static RectTransform Build(Transform parent, System.Action onClose)
        {
            var hostGo = new GameObject("FormatSelect", typeof(RectTransform));
            hostGo.transform.SetParent(parent, false);
            FloatingPanel.Place(hostGo.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);

            var frame = DualMenuPresenter.BuildFrame(
                hostGo.transform, "PLAYER VS AI",
                "You vs the computer. Field distance sets how far the AR disks sit apart.",
                onClose);
            var body = frame.BodyHost;

            var listTop = 0.98f;
            if (ErazFormat.ShowsBadgeTray("pvai"))
            {
                var hasOriginal = HasOriginalBadgeSafe();
                var erazText = hasOriginal
                    ? "ERAZ · Original"
                    : "ERAZ · LOCKED — finish tutorial";
                var erazLine = FloatingPanel.Body(body, erazText, 13);
                FloatingPanel.Grid.Full(erazLine.rectTransform, 0.90f, 0.98f);
                erazLine.alignment = TextAnchor.MiddleCenter;
                erazLine.color = hasOriginal ? DuelystUi.TextMuted : DuelystUi.Danger;
                listTop = 0.88f;
            }

            var scroll = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.98f, listTop);
            var v = scroll.GetComponent<VerticalLayoutGroup>();
            v.spacing = 12;
            v.childForceExpandHeight = false;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.padding = new RectOffset(4, 4, 4, 4);
            scroll.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cards = new[]
            {
                PvAiCard("STANDING", "Default AR field · face-to-face",
                    ArDuelMatchConfig.DefaultStandM, "Player vs AI · Standing"),
                PvAiCard("TABLE", "Close AR field · tabletop feel",
                    ArDuelMatchConfig.DefaultTableM, "Player vs AI · Table"),
                PvAiCard("STREET", "Wide AR field · open space",
                    ArDuelMatchConfig.DefaultStreetM, "Player vs AI · Street"),
                PvAiCard("PRACTICE", "Same AR stage · no overworld proximity rules",
                    ArDuelMatchConfig.DefaultStandM, "Player vs AI · Practice",
                    practice: true)
            };

            foreach (var c in cards)
                AddCard(scroll.transform, c);

            return hostGo.GetComponent<RectTransform>();
        }

        static FormatCard PvAiCard(string title, string rules, float meters, string formatTitle,
            bool practice = false)
        {
            return new FormatCard
            {
                Title = title,
                Rules = rules + " · vs AI",
                Lp = $"8000 LP · ~{meters:0.0}m",
                Unlocked = true,
                Play = () =>
                {
                    if (!practice && !CanStartHubNonPractice())
                        return;

                    var c = practice
                        ? ArDuelMatchConfig.Practice()
                        : ArDuelMatchConfig.DefaultQuick();
                    c.Opponent = ArDuelOpponentKind.AiLocal;
                    if (!practice)
                    {
                        c.Launch = ArDuelLaunchKind.Hub;
                        c.FormatId = "pvai";
                    }
                    c.EntrySource = AppSession.SceneMainMenu;
                    c.SeparationMeters = meters;
                    c.FormatTitle = formatTitle;
                    c.ClampSeparation();
                    AppSession.Ensure().StartArDuel(c);
                }
            };
        }

        static bool HasOriginalBadgeSafe()
        {
            try
            {
                var acc = AppSession.Instance != null
                    ? AppSession.Instance.Account
                    : (Application.isPlaying ? AppSession.Ensure().Account : null);
                if (acc == null) return false;
                acc.EnsureProgress();
                return acc.progress != null
                    && ErazProgress.HasBadge(acc.progress, ErazFormat.Original);
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        static bool CanStartHubNonPractice()
        {
            try
            {
                return AppSession.Ensure().CanStartConstructedPvAi();
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        static void AddCard(Transform parent, FormatCard c)
        {
            var row = FloatingPanel.Create(parent, c.Title, goldEdge: true);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 152f;
            le.preferredHeight = 160f;

            var t = FloatingPanel.Title(row, c.Title, 16);
            FloatingPanel.Place(t.rectTransform, 0.04f, 0.58f, 0.66f, 0.94f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;

            var rules = FloatingPanel.Body(row, c.Rules + "\n" + c.Lp, 13);
            FloatingPanel.Place(rules.rectTransform, 0.04f, 0.08f, 0.66f, 0.56f);
            rules.verticalOverflow = VerticalWrapMode.Overflow;
            rules.color = DuelystUi.TextMuted;

            if (c.Unlocked && c.Play != null)
            {
                var play = FloatingPanel.PrimaryButton(row, "START", () =>
                {
                    FreeUiKit.PlayConfirm();
                    c.Play();
                }, gold: true);
                FloatingPanel.Place(play.GetComponent<RectTransform>(), 0.70f, 0.18f, 0.96f, 0.82f);
            }
            else
            {
                var lockL = FloatingPanel.Body(row, "LOCKED", 16);
                lockL.color = DuelystUi.Danger;
                lockL.alignment = TextAnchor.MiddleCenter;
                FloatingPanel.Place(lockL.rectTransform, 0.70f, 0.18f, 0.96f, 0.82f);
            }
        }
    }
}
