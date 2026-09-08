#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>A COIN + XP payout for one reward source.</summary>
    public readonly struct Payout
    {
        public readonly int Coin;
        public readonly int Xp;

        public Payout(int coin, int xp)
        {
            Coin = coin;
            Xp = xp;
        }

        public override string ToString()
        {
            return string.Concat(Coin.ToString(), "c/", Xp.ToString(), "xp");
        }
    }

    /// <summary>
    /// Server-side anti-farming gates. A match only grants economy when ALL of these hold.
    /// Design doc section 5.3. Validated in Cloud Code, never in the client.
    /// </summary>
    public sealed class AntiFarmCaps
    {
        /// <summary>Minimum server-clock match duration.</summary>
        public int MinMatchDurationSeconds { get; set; } = 60;

        /// <summary>Minimum shots by the rewarded player on a standard board.</summary>
        public int MinShotsForReward { get; set; } = 12;

        /// <summary>Minimum shots on blitz, which is shorter by design.</summary>
        public int MinShotsForRewardBlitz { get; set; } = 8;

        /// <summary>Both players must have acted at least this many times after placement.</summary>
        public int MinActionsPerPlayerAfterPlacement { get; set; } = 1;

        /// <summary>
        /// Beyond this many online matches between the same pair in 24 h, rewards drop to zero and
        /// <c>reward_suppressed_pair</c> is emitted. Collusion detection, section 11.4.
        /// </summary>
        public int MaxMatchesPerPairPer24h { get; set; } = 3;

        /// <summary>Daily COIN ceiling from AI matches plus bot-filled matches.</summary>
        public int DailyCoinCapFromAi { get; set; } = 150;

        /// <summary>Hard daily COIN ceiling per account, all sources included.</summary>
        public int DailyCoinCapTotal { get; set; } = 2500;
    }

    public sealed class LevelConfig
    {
        public int MaxLevel { get; set; } = 60;

        /// <summary>XP required for level n = <see cref="XpBase"/> + <see cref="XpPerLevel"/> * (n - 1).</summary>
        public int XpBase { get; set; } = 500;

        public int XpPerLevel { get; set; } = 250;

        /// <summary>COIN on level up = <see cref="LevelUpCoinBase"/> + <see cref="LevelUpCoinPerLevel"/> * level.</summary>
        public int LevelUpCoinBase { get; set; } = 200;

        public int LevelUpCoinPerLevel { get; set; } = 25;

        /// <summary>A cosmetic is granted every this many levels.</summary>
        public int CosmeticEveryLevels { get; set; } = 5;

        public int XpForLevel(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            return XpBase + (XpPerLevel * (level - 1));
        }
    }

    /// <summary>
    /// Reward table from design doc section 5.2. Mirrored in Remote Config under
    /// <c>ECONOMY_REWARDS</c> and <c>ANTIFARM_CAPS</c>.
    /// <para>
    /// The client never mirrors reward maths: the server returns a computed
    /// <see cref="RewardBreakdown"/> and the UI just paints it. This config exists in Core because
    /// the server computes with it, and because offline modes need the level curve.
    /// </para>
    /// </summary>
    public sealed class EconomyConfig
    {
        public Payout OnlineWin { get; set; } = new Payout(100, 100);
        public Payout OnlineLoss { get; set; } = new Payout(40, 40);
        public Payout WinByOpponentAbandon { get; set; } = new Payout(100, 100);
        public Payout LossByOwnAbandon { get; set; } = new Payout(0, 0);

        /// <summary>Payout for beating the AI, per difficulty. Subject to the daily AI cap.</summary>
        public IReadOnlyDictionary<AiDifficulty, Payout> AiWin { get; set; } =
            new Dictionary<AiDifficulty, Payout>();

        public Payout AiLoss { get; set; } = new Payout(5, 10);

        /// <summary>Local pass-and-play never grants economy.</summary>
        public Payout LocalTwoPlayer { get; set; } = new Payout(0, 0);

        /// <summary>Bonus on the first win of the UTC day.</summary>
        public Payout FirstWinOfDayBonus { get; set; } = new Payout(150, 100);

        /// <summary>Added multiplier per consecutive online win.</summary>
        public double WinStreakStepMultiplier { get; set; } = 0.10;

        /// <summary>Ceiling of the streak multiplier (+50 %).</summary>
        public double WinStreakMaxMultiplier { get; set; } = 0.50;

        public int DailyQuestCount { get; set; } = 3;
        public int DailyQuestCoinMin { get; set; } = 50;
        public int DailyQuestCoinMax { get; set; } = 120;
        public int DailyQuestXp { get; set; } = 50;

        public LevelConfig Levels { get; set; } = new LevelConfig();
        public AntiFarmCaps AntiFarm { get; set; } = new AntiFarmCaps();
    }
}
