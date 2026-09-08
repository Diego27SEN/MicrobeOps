#nullable enable

using System;

namespace Armada.Core
{
    /// <summary>Elo maths for ranked. Design doc section 4.8.</summary>
    public static class EloRules
    {
        /// <summary>
        /// New ratings after a match. <paramref name="scoreA"/> is 1 for an A win, 0 for a loss.
        /// The K-factor is picked per player from their own games played, so a newcomer beating a
        /// veteran moves further than the veteran does, and both results are clamped to the floor.
        /// </summary>
        public static (int newA, int newB) Apply(int ratingA, int ratingB, double scoreA, int gamesA, int gamesB, EloConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (scoreA < 0.0 || scoreA > 1.0) throw new ArgumentOutOfRangeException(nameof(scoreA));

            double expectedA = ExpectedScore(ratingA, ratingB);
            double expectedB = 1.0 - expectedA;
            double scoreB = 1.0 - scoreA;

            int newA = ratingA + RoundHalfAwayFromZero(cfg.KFactorFor(gamesA) * (scoreA - expectedA));
            int newB = ratingB + RoundHalfAwayFromZero(cfg.KFactorFor(gamesB) * (scoreB - expectedB));

            return (ClampToFloor(newA, cfg), ClampToFloor(newB, cfg));
        }

        /// <summary>Expected score for A against B under the standard logistic curve.</summary>
        public static double ExpectedScore(int ratingA, int ratingB)
        {
            return 1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));
        }

        /// <summary>
        /// Rating after abandoning a ranked match: the defeat, plus the extra penalty. Abandoning
        /// must cost more than losing, or it becomes the cheap way out of a bad position.
        /// </summary>
        public static int ApplyAbandonPenalty(int ratingAfterLoss, EloConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            return ClampToFloor(ratingAfterLoss - cfg.AbandonPenalty, cfg);
        }

        private static int ClampToFloor(int rating, EloConfig cfg)
        {
            return rating < cfg.RatingFloor ? cfg.RatingFloor : rating;
        }

        private static int RoundHalfAwayFromZero(double value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
