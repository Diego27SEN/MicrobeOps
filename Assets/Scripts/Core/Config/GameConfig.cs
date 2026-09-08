#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// Every tunable number in the game, in one object.
    /// <para>
    /// The client NEVER reads Remote Config directly: it calls <c>GetGameConfig</c> on Cloud Code,
    /// which is the single read point. When that call fails, the client falls back to
    /// <see cref="DefaultGameConfig.Create"/>, which carries these very same values embedded so
    /// offline modes keep working (design doc sections 4 and 9.4).
    /// </para>
    /// </summary>
    public sealed class GameConfig
    {
        /// <summary>Bumped by LiveOps whenever the served config changes. Purely informational.</summary>
        public int ConfigVersion { get; set; } = 1;

        public IReadOnlyList<BoardConfig> Boards { get; set; } = Array.Empty<BoardConfig>();
        public IReadOnlyList<RuleFlags> RuleSets { get; set; } = Array.Empty<RuleFlags>();
        public TimersConfig Timers { get; set; } = new TimersConfig();
        public AiParams Ai { get; set; } = new AiParams();
        public EconomyConfig Economy { get; set; } = new EconomyConfig();
        public EloConfig Elo { get; set; } = new EloConfig();
        public SeasonConfig Season { get; set; } = new SeasonConfig();
        public MatchmakingConfig Matchmaking { get; set; } = new MatchmakingConfig();
        public AdConfig Ads { get; set; } = new AdConfig();
        public KillSwitches Switches { get; set; } = new KillSwitches();

        public BoardConfig Board(string boardId)
        {
            for (int i = 0; i < Boards.Count; i++)
            {
                if (string.Equals(Boards[i].Id, boardId, StringComparison.Ordinal)) return Boards[i];
            }
            throw new ArgumentOutOfRangeException(nameof(boardId), "Unknown board id: " + boardId);
        }

        public RuleFlags RuleSet(string ruleSetId)
        {
            for (int i = 0; i < RuleSets.Count; i++)
            {
                if (string.Equals(RuleSets[i].Id, ruleSetId, StringComparison.Ordinal)) return RuleSets[i];
            }
            throw new ArgumentOutOfRangeException(nameof(ruleSetId), "Unknown rule set id: " + ruleSetId);
        }

        public bool TryGetBoard(string boardId, out BoardConfig board)
        {
            for (int i = 0; i < Boards.Count; i++)
            {
                if (string.Equals(Boards[i].Id, boardId, StringComparison.Ordinal))
                {
                    board = Boards[i];
                    return true;
                }
            }
            board = null!;
            return false;
        }

        public bool TryGetRuleSet(string ruleSetId, out RuleFlags ruleSet)
        {
            for (int i = 0; i < RuleSets.Count; i++)
            {
                if (string.Equals(RuleSets[i].Id, ruleSetId, StringComparison.Ordinal))
                {
                    ruleSet = RuleSets[i];
                    return true;
                }
            }
            ruleSet = null!;
            return false;
        }
    }
}
