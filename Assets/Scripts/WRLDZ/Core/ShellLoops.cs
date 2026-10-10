using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Shell counters named in the design notes: win streak, level quiz, Kuriboh bond.
    /// The altar does not convert cards. The streak purse is Digizeni until a pack exists.
    /// </summary>
    public static class ShellLoops
    {
        public const int StreakGoal = 3;
        public const int PurseDigizeni = 150;
        public const int QuizDigizeni = 40;
        public const int BondCap = 10;

        public static void NoteDuel(PlayerProgress p, ArDuelMatchConfig match, bool playerWon)
        {
            if (p == null || match == null) return;
            if (match.IsPracticeOrLab)
                return;
            // Canon (SET_ENERGY_AND_BAZAAR §2): the purse counts consecutive NON-PvP wins. PvP neither
            // advances nor breaks the streak.
            if (ProgressionService.IsPvpXp(match))
                return;

            if (!playerWon)
            {
                p.pveWinStreak = 0;
                return;
            }

            p.pveWinStreak++;
            if (p.pveWinStreak >= StreakGoal)
            {
                p.pveWinStreak = 0;
                p.streakPurses++;
            }

            if (match.Launch == ArDuelLaunchKind.StoryEra && p.kuribohBond < BondCap)
                p.kuribohBond++;
        }

        public static bool TryClaimPurse(PlayerProgress p, PlayerInventory inv, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No account.";
                return false;
            }

            if (p.streakPurses <= 0)
            {
                error = "No streak purse banked.";
                return false;
            }

            p.streakPurses--;
            ArtifactService.Grant(p, inv, ArtifactService.Digizeni, PurseDigizeni);
            return true;
        }

        public static int NextQuizGate(int level)
        {
            if (level < 10) return 0;
            return (level / 10) * 10;
        }

        public static bool QuizReady(PlayerProgress p)
        {
            if (p == null) return false;
            var gate = NextQuizGate(p.level);
            return gate >= 10 && p.quizClaimedThrough < gate;
        }

        /// <summary>One reused question: who woke Mr. Referobot. Wrong answers keep the gate.</summary>
        public static bool TryAnswerQuiz(PlayerProgress p, PlayerInventory inv, bool kuribohs, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No account.";
                return false;
            }

            if (!QuizReady(p))
            {
                error = "No quiz gate open.";
                return false;
            }

            if (!kuribohs)
            {
                error = "Not that. The gate is still open.";
                return false;
            }

            p.quizClaimedThrough = NextQuizGate(p.level);
            ArtifactService.Grant(p, inv, ArtifactService.Digizeni, QuizDigizeni);
            return true;
        }
    }
}
