using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Duel
{
    public static class AiArchetypeDetector
    {
        private static readonly HashSet<int> _revealedCards = new();
        private static string _detectedArchetype = null;
        private static bool _dirty = true;

        private static readonly HashSet<int> BlueEyes     = new() { 38547077, 89631139, 37820506, 53183600, 66970085 };
        private static readonly HashSet<int> DarkMagician = new() { 46986414, 4141828, 59560625, 37520316, 40737112 };
        private static readonly HashSet<int> Zombie       = new() { 74015142, 72283639, 60728397, 94634433, 65929885 };
        private static readonly HashSet<int> Toon         = new() { 4141828, 65884091, 95701283, 39967306, 17168450 };
        private static readonly HashSet<int> Harpie       = new() { 76812113, 72897473, 53183600, 8949584, 19221310 };
        private static readonly HashSet<int> Dinosaur     = new() { 94634433, 60381599, 41482598, 80741821, 68468456 };
        private static readonly HashSet<int> Insect       = new() { 57829584, 94122905, 43950690, 17720742, 35073056 };
        private static readonly HashSet<int> Machine      = new() { 71625208, 77777862, 43970024, 64802465, 23142348 };
        private static readonly HashSet<int> Water        = new() { 30113651, 62101021, 84013241, 21598942, 58258834 };
        private static readonly HashSet<int> Exodia       = new() { 33396948, 7902349, 38120068, 4845173, 23434572 };
        private static readonly HashSet<int> Kuriboh      = new() { 40640057, 57116034, 7350767, 42703248, 39791991 };
        private static readonly HashSet<int> Union        = new() { 31632529, 81386572, 29765339, 29424328, 80168720 };
        private static readonly HashSet<int> Labyrinth    = new() { 50342067, 1581084, 70781052, 65025250, 73430779 };

        public static void Reset()
        {
            _revealedCards.Clear();
            _detectedArchetype = null;
            _dirty = true;
        }

        public static void RevealCard(int cardId)
        {
            if (_revealedCards.Add(cardId))
                _dirty = true;
        }

        public static string DetectedArchetype
        {
            get
            {
                if (_dirty) UpdateDetection();
                return _detectedArchetype;
            }
        }

        private static void UpdateDetection()
        {
            _dirty = false;
            _detectedArchetype = null;

            int blueEyes     = CountMatches(BlueEyes);
            int darkMagician = CountMatches(DarkMagician);
            int zombie       = CountMatches(Zombie);
            int toon         = CountMatches(Toon);
            int harpie       = CountMatches(Harpie);
            int dinosaur     = CountMatches(Dinosaur);
            int insect       = CountMatches(Insect);
            int machine      = CountMatches(Machine);
            int water        = CountMatches(Water);
            int exodia       = CountMatches(Exodia);
            int kuriboh      = CountMatches(Kuriboh);
            int union        = CountMatches(Union);
            int labyrinth    = CountMatches(Labyrinth);

            var best  = new[] { blueEyes, darkMagician, zombie, toon, harpie, dinosaur, insect, machine, water, exodia, kuriboh, union, labyrinth };
            var names = new[] { "Blue-Eyes", "Dark Magician", "Zombie", "Toon", "Harpie", "Dinosaur", "Insect", "Machine", "Water", "Exodia", "Kuriboh", "Union", "Labyrinth" };

            int max = 0;
            string bestName = null;
            for (int i = 0; i < best.Length; i++)
            {
                if (best[i] > max)
                {
                    max = best[i];
                    bestName = names[i];
                }
            }

            if (max >= 2 && bestName != null)
                _detectedArchetype = bestName;
        }

        private static int CountMatches(HashSet<int> pool)
        {
            int count = 0;
            foreach (var id in _revealedCards)
            {
                if (pool.Contains(id)) count++;
            }
            return count;
        }
    }
}
