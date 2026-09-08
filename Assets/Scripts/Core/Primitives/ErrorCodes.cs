#nullable enable

using System;
using System.Text;

namespace Armada.Core
{
    /// <summary>
    /// The server never sends prose. It sends <c>ERR_CODE|p1|p2</c> and the client resolves
    /// <c>error.&lt;CODE&gt;</c> against Localization. Greppable in logs, stable for test asserts.
    /// Full catalogue in design doc section 7.4.
    /// </summary>
    public static class ErrorCodes
    {
        public const string ProtocolMismatch = "ERR_PROTOCOL_MISMATCH";
        public const string MinVersion = "ERR_MIN_VERSION";
        public const string FeatureDisabled = "ERR_FEATURE_DISABLED";
        public const string MatchNotFound = "ERR_MATCH_NOT_FOUND";
        public const string MatchFinished = "ERR_MATCH_FINISHED";
        public const string WrongPhase = "ERR_WRONG_PHASE";
        public const string NotYourTurn = "ERR_NOT_YOUR_TURN";
        public const string StaleAction = "ERR_STALE_ACTION";
        public const string OutOfBounds = "ERR_OUT_OF_BOUNDS";
        public const string CellAlreadyTargeted = "ERR_CELL_ALREADY_TARGETED";
        public const string InvalidPlacement = "ERR_INVALID_PLACEMENT";
        public const string FleetMismatch = "ERR_FLEET_MISMATCH";
        public const string ShotCountInvalid = "ERR_SHOT_COUNT_INVALID";
        public const string AlreadyInQueue = "ERR_ALREADY_IN_QUEUE";
        public const string RateLimited = "ERR_RATE_LIMITED";
        public const string InsufficientFunds = "ERR_INSUFFICIENT_FUNDS";
        public const string ItemAlreadyOwned = "ERR_ITEM_ALREADY_OWNED";
        public const string PurchaseInvalid = "ERR_PURCHASE_INVALID";
        public const string Internal = "ERR_INTERNAL";

        /// <summary>Reasons carried as the parameter of <see cref="InvalidPlacement"/>.</summary>
        public static class PlacementReason
        {
            public const string Overlap = "overlap";
            public const string OutOfBounds = "oob";
            public const string Diagonal = "diagonal";
            public const string Adjacency = "adjacency";
            public const string DuplicateShip = "duplicate";
        }

        /// <summary>Builds <c>CODE|p1|p2</c>. Parameters are stringified with invariant culture.</summary>
        public static string Format(string code, params object[] parameters)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("Code is required.", nameof(code));
            if (parameters == null || parameters.Length == 0) return code;

            StringBuilder builder = new StringBuilder(code);
            for (int i = 0; i < parameters.Length; i++)
            {
                builder.Append('|');
                object? value = parameters[i];
                builder.Append(value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>Returns the bare code of an <c>ERR_X|p1</c> string.</summary>
        public static string CodeOf(string formatted)
        {
            if (string.IsNullOrEmpty(formatted)) return string.Empty;
            int pipe = formatted.IndexOf('|');
            return pipe < 0 ? formatted : formatted.Substring(0, pipe);
        }
    }
}
