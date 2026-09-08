#nullable enable

using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// The embedded fallback configuration: every number of design doc sections 4 and 5, in code.
    /// <para>
    /// This is the offline safety net. The client asks Cloud Code for <c>GetGameConfig</c>; if that
    /// call fails, this is what it plays with, so vs-AI and local modes keep working with no
    /// network at all. Remote Config carries the same values under <c>GameConfig.rc</c>.
    /// </para>
    /// <para>
    /// This file is the ONLY place in the client where balance numbers are allowed to live.
    /// If a number here disagrees with the design doc, the design doc wins and this file is the
    /// bug. Tests assert the two agree.
    /// </para>
    /// </summary>
    public static class DefaultGameConfig
    {
        public static GameConfig Create()
        {
            return new GameConfig
            {
                ConfigVersion = 1,
                Boards = CreateBoards(),
                RuleSets = CreateRuleSets(),
                Timers = CreateTimers(),
                Ai = CreateAiParams(),
                Economy = CreateEconomy(),
                Elo = CreateElo(),
                Season = new SeasonConfig(),
                Matchmaking = CreateMatchmaking(),
                Ads = new AdConfig(),
                Switches = new KillSwitches()
            };
        }

        // Design doc section 4.1. Fleet density stays near 18 % on every board.
        private static IReadOnlyList<BoardConfig> CreateBoards()
        {
            return new List<BoardConfig>
            {
                new BoardConfig
                {
                    Id = BoardConfig.BlitzId,
                    Width = 8,
                    Height = 8,
                    Fleet = new List<ShipSpec>
                    {
                        new ShipSpec(ShipClass.Battleship, 4),
                        new ShipSpec(ShipClass.Cruiser, 3),
                        new ShipSpec(ShipClass.Submarine, 3),
                        new ShipSpec(ShipClass.Destroyer, 2)
                    }
                },
                new BoardConfig
                {
                    Id = BoardConfig.ClassicId,
                    Width = 10,
                    Height = 10,
                    Fleet = new List<ShipSpec>
                    {
                        new ShipSpec(ShipClass.Carrier, 5),
                        new ShipSpec(ShipClass.Battleship, 4),
                        new ShipSpec(ShipClass.Cruiser, 3),
                        new ShipSpec(ShipClass.Submarine, 3),
                        new ShipSpec(ShipClass.Destroyer, 2)
                    }
                },
                new BoardConfig
                {
                    Id = BoardConfig.AdmiralId,
                    Width = 12,
                    Height = 12,
                    Fleet = new List<ShipSpec>
                    {
                        new ShipSpec(ShipClass.Carrier, 5),
                        new ShipSpec(ShipClass.Battleship, 5),
                        new ShipSpec(ShipClass.Battleship, 4),
                        new ShipSpec(ShipClass.Cruiser, 4),
                        new ShipSpec(ShipClass.Submarine, 3),
                        new ShipSpec(ShipClass.Destroyer, 3),
                        new ShipSpec(ShipClass.PatrolBoat, 2)
                    }
                }
            };
        }

        // Design doc section 4.3.
        private static IReadOnlyList<RuleFlags> CreateRuleSets()
        {
            return new List<RuleFlags>
            {
                new RuleFlags
                {
                    Id = RuleFlags.ClassicId,
                    AllowAdjacentShips = true,
                    AnnounceSunkShipType = true,
                    RevealSunkCells = true,
                    ExtraTurnOnHit = false,
                    ShotsPerTurn = ShotsPerTurnMode.Fixed,
                    FixedShotsPerTurn = 1,
                    SalvoAggregateReport = false,
                    DiagonalPlacement = false
                },
                new RuleFlags
                {
                    Id = RuleFlags.BlitzId,
                    AllowAdjacentShips = true,
                    AnnounceSunkShipType = true,
                    RevealSunkCells = true,
                    ExtraTurnOnHit = true,
                    ShotsPerTurn = ShotsPerTurnMode.Fixed,
                    FixedShotsPerTurn = 1,
                    SalvoAggregateReport = false,
                    DiagonalPlacement = false
                },
                new RuleFlags
                {
                    Id = RuleFlags.SalvoId,
                    AllowAdjacentShips = true,
                    AnnounceSunkShipType = false,
                    RevealSunkCells = true,
                    ExtraTurnOnHit = false,
                    ShotsPerTurn = ShotsPerTurnMode.OneShotPerAfloatShip,
                    FixedShotsPerTurn = 1,
                    SalvoAggregateReport = true,
                    DiagonalPlacement = false
                }
            };
        }

        // Design doc section 4.4.
        private static TimersConfig CreateTimers()
        {
            return new TimersConfig
            {
                RealTime = new TimerProfile
                {
                    PlacementSeconds = 90,
                    TurnSeconds = 30,
                    MaxConsecutiveTimeouts = 3,
                    ReconnectGraceSeconds = 45,
                    AbandonSeconds = 180,
                    RematchOfferSeconds = 30
                },
                Async = new TimerProfile
                {
                    PlacementSeconds = 86400,
                    TurnSeconds = 86400,
                    MaxConsecutiveTimeouts = 2,
                    ReconnectGraceSeconds = 0,
                    AbandonSeconds = 172800,
                    RematchOfferSeconds = 0
                }
            };
        }

        // Design doc section 4.6. MaxAverageShotsToClearClassic is a QA acceptance threshold:
        // 17 fleet cells divided by the expected accuracy of each difficulty.
        private static AiParams CreateAiParams()
        {
            return new AiParams
            {
                MaxBorderCellRatio = 0.60,
                MaxPlacementRetries = 10,
                TimeoutShotDifficulty = AiDifficulty.Medium,
                Adaptive = new AdaptiveParams
                {
                    WindowShots = 20,
                    TargetWinRate = 0.50,
                    NoiseFloor = 0,
                    NoiseCeiling = 6,
                    AdjustStep = 1
                },
                Difficulties = new List<AiDifficultyParams>
                {
                    new AiDifficultyParams
                    {
                        Difficulty = AiDifficulty.Easy,
                        IgnoreObviousFollowUpChance = 0.25,
                        UseParityHunt = false,
                        UseProbabilityDensity = false,
                        UseLineInference = false,
                        IsAdaptive = false,
                        MaxAverageShotsToClearClassic = 93.5,
                        TargetWinRateVsAveragePlayer = 0.20
                    },
                    new AiDifficultyParams
                    {
                        Difficulty = AiDifficulty.Medium,
                        IgnoreObviousFollowUpChance = 0.0,
                        UseParityHunt = false,
                        UseProbabilityDensity = false,
                        UseLineInference = false,
                        IsAdaptive = false,
                        MaxAverageShotsToClearClassic = 71.4,
                        TargetWinRateVsAveragePlayer = 0.40
                    },
                    new AiDifficultyParams
                    {
                        Difficulty = AiDifficulty.Hard,
                        IgnoreObviousFollowUpChance = 0.0,
                        UseParityHunt = true,
                        UseProbabilityDensity = true,
                        UseLineInference = true,
                        IsAdaptive = false,
                        MaxAverageShotsToClearClassic = 48.0,
                        TargetWinRateVsAveragePlayer = 0.65
                    },
                    new AiDifficultyParams
                    {
                        Difficulty = AiDifficulty.Adaptive,
                        IgnoreObviousFollowUpChance = 0.0,
                        UseParityHunt = true,
                        UseProbabilityDensity = true,
                        UseLineInference = true,
                        IsAdaptive = true,

                        // Accuracy is variable by design, so there is no fixed threshold to assert.
                        MaxAverageShotsToClearClassic = 0.0,
                        TargetWinRateVsAveragePlayer = 0.50
                    }
                }
            };
        }

        // Design doc sections 5.2, 5.3 and 5.4.
        private static EconomyConfig CreateEconomy()
        {
            return new EconomyConfig
            {
                OnlineWin = new Payout(100, 100),
                OnlineLoss = new Payout(40, 40),
                WinByOpponentAbandon = new Payout(100, 100),
                LossByOwnAbandon = new Payout(0, 0),
                AiWin = new Dictionary<AiDifficulty, Payout>
                {
                    { AiDifficulty.Easy, new Payout(10, 10) },
                    { AiDifficulty.Medium, new Payout(20, 20) },
                    { AiDifficulty.Hard, new Payout(35, 35) },
                    { AiDifficulty.Adaptive, new Payout(35, 35) }
                },
                AiLoss = new Payout(5, 10),
                LocalTwoPlayer = new Payout(0, 0),
                FirstWinOfDayBonus = new Payout(150, 100),
                WinStreakStepMultiplier = 0.10,
                WinStreakMaxMultiplier = 0.50,
                DailyQuestCount = 3,
                DailyQuestCoinMin = 50,
                DailyQuestCoinMax = 120,
                DailyQuestXp = 50,
                Levels = new LevelConfig
                {
                    MaxLevel = 60,
                    XpBase = 500,
                    XpPerLevel = 250,
                    LevelUpCoinBase = 200,
                    LevelUpCoinPerLevel = 25,
                    CosmeticEveryLevels = 5
                },
                AntiFarm = new AntiFarmCaps
                {
                    MinMatchDurationSeconds = 60,
                    MinShotsForReward = 12,
                    MinShotsForRewardBlitz = 8,
                    MinActionsPerPlayerAfterPlacement = 1,
                    MaxMatchesPerPairPer24h = 3,
                    DailyCoinCapFromAi = 150,
                    DailyCoinCapTotal = 2500
                }
            };
        }

        // Design doc section 4.8. Tier ids are Localization key suffixes (rank.<id>), never display text.
        private static EloConfig CreateElo()
        {
            return new EloConfig
            {
                StartingRating = 1000,
                RatingFloor = 100,
                KFactorNovice = 40,
                KFactorNoviceGames = 10,
                KFactorIntermediate = 32,
                KFactorIntermediateGames = 50,
                KFactorVeteran = 24,
                PlacementMatches = 5,
                AbandonPenalty = 15,
                AbandonsBeforeCooldown = 3,
                AbandonCooldownSeconds = 300,
                Tiers = new List<RankTier>
                {
                    new RankTier("recruit", 0),
                    new RankTier("sailor", 900),
                    new RankTier("boatswain", 1100),
                    new RankTier("lieutenant", 1300),
                    new RankTier("captain", 1500),
                    new RankTier("commodore", 1700),
                    new RankTier("admiral", 1900)
                }
            };
        }

        // Design doc section 4.9. Bot fill is casual-only and additionally gated by a kill switch.
        private static MatchmakingConfig CreateMatchmaking()
        {
            return new MatchmakingConfig
            {
                Casual = new MatchmakingQueueConfig
                {
                    InitialEloWindow = 250,
                    WindowExpansionPerSecond = 25,
                    MaxEloWindow = 1000,
                    TicketTtlSeconds = 120,
                    BotFillAfterSeconds = 45
                },
                Ranked = new MatchmakingQueueConfig
                {
                    InitialEloWindow = 100,
                    WindowExpansionPerSecond = 25,
                    MaxEloWindow = 600,
                    TicketTtlSeconds = 180,

                    // Never. Bot fill in ranked is forbidden outright, not merely discouraged.
                    BotFillAfterSeconds = 0
                }
            };
        }
    }
}
