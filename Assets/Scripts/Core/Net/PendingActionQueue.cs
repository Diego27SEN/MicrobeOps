#nullable enable

using System;
using System.Collections.Generic;

namespace Armada.Core
{
    /// <summary>
    /// The kinds of action that may wait for the network to come back.
    /// <para>
    /// This list is short on purpose, and adding to it is a decision. Anything that changes the
    /// outcome of a match or moves currency is NOT here: those must fail in front of the player,
    /// now, rather than be replayed minutes later against a world that moved on.
    /// </para>
    /// </summary>
    public enum PendingActionKind : byte
    {
        Analytics = 0,
        EquipCosmetic = 1,
        SetPlayerName = 2,
        RegisterPushToken = 3,
        SettingsChanged = 4
    }

    /// <summary>One deferred call, with the identity that makes replaying it idempotent.</summary>
    public readonly struct PendingAction
    {
        /// <summary>The <c>clientRequestId</c>. Replaying it must not create a duplicate server-side.</summary>
        public readonly string Id;

        public readonly PendingActionKind Kind;

        /// <summary>What the action applies to - a cosmetic slot, an event name. Empty when it has no target.</summary>
        public readonly string Target;

        public readonly string PayloadJson;
        public readonly long EnqueuedAtUnixMs;

        public PendingAction(string id, PendingActionKind kind, string target, string payloadJson, long enqueuedAtUnixMs)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A pending action needs a client request id.", nameof(id));

            Id = id;
            Kind = kind;
            Target = target ?? string.Empty;
            PayloadJson = payloadJson ?? string.Empty;
            EnqueuedAtUnixMs = enqueuedAtUnixMs;
        }
    }

    /// <summary>Why an action was refused. Machine-readable so the caller can log or surface it.</summary>
    public static class PendingActionRejection
    {
        public const string NotQueueable = "not_queueable";
        public const string QueueFull = "queue_full";
    }

    /// <summary>
    /// Holds the non-critical calls made while offline and replays them when the network returns.
    /// <para>
    /// Two rules carry the weight. Nothing that decides a match or moves money can be queued, so a
    /// lost connection during a shot is an error the player sees rather than a move that lands
    /// three minutes late. And for state that is "the latest value wins" - an equipped cosmetic, a
    /// push token - a newer action replaces the older one instead of stacking, because replaying
    /// four cosmetic changes to reach the fourth is four times the work for the same result.
    /// </para>
    /// <para>
    /// Analytics is the exception to that: each event is its own fact, so they accumulate. When the
    /// queue is full the oldest is dropped, because a telemetry event losing its place is a smaller
    /// loss than the queue growing without bound on a phone that has been offline for a day.
    /// </para>
    /// </summary>
    public sealed class PendingActionQueue
    {
        public const int DefaultCapacity = 128;

        private readonly List<PendingAction> _actions = new List<PendingAction>();
        private readonly int _capacity;

        public PendingActionQueue(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count
        {
            get { return _actions.Count; }
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>How many actions were dropped to make room. Worth an alert if it ever moves much.</summary>
        public int DroppedCount { get; private set; }

        /// <summary>
        /// Queues an action, or explains why it will not be queued. Returns false rather than
        /// throwing: being offline is a normal state, not an exceptional one.
        /// </summary>
        public bool TryEnqueue(in PendingAction action, out string rejection)
        {
            rejection = string.Empty;

            if (!IsQueueable(action.Kind))
            {
                rejection = PendingActionRejection.NotQueueable;
                return false;
            }

            if (CoalescesByTarget(action.Kind))
            {
                for (int i = 0; i < _actions.Count; i++)
                {
                    if (_actions[i].Kind != action.Kind) continue;
                    if (!string.Equals(_actions[i].Target, action.Target, StringComparison.Ordinal)) continue;

                    _actions[i] = action;
                    return true;
                }
            }

            if (_actions.Count >= _capacity)
            {
                // Dropping the oldest telemetry beats letting the queue grow without bound on a
                // phone that has been offline all day.
                if (action.Kind != PendingActionKind.Analytics)
                {
                    rejection = PendingActionRejection.QueueFull;
                    return false;
                }

                _actions.RemoveAt(0);
                DroppedCount++;
            }

            _actions.Add(action);
            return true;
        }

        /// <summary>
        /// Hands over everything queued, in the order it was made, and empties the queue.
        /// <para>
        /// Order matters: a name change followed by a cosmetic change has to replay that way round,
        /// and the caller is expected to stop on the first hard failure rather than plough on.
        /// </para>
        /// </summary>
        public IReadOnlyList<PendingAction> Drain()
        {
            PendingAction[] drained = _actions.ToArray();
            _actions.Clear();
            return drained;
        }

        /// <summary>Puts actions back at the front after a failed replay, keeping their order.</summary>
        public void Requeue(IReadOnlyList<PendingAction> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));

            for (int i = actions.Count - 1; i >= 0; i--)
            {
                if (_actions.Count >= _capacity)
                {
                    DroppedCount++;
                    continue;
                }

                _actions.Insert(0, actions[i]);
            }
        }

        public void Clear()
        {
            _actions.Clear();
        }

        /// <summary>
        /// Whether a kind may wait for the network at all. Anything that decides a match, moves
        /// currency or validates a purchase is deliberately absent: replaying it later against a
        /// world that moved on is worse than failing now.
        /// </summary>
        public static bool IsQueueable(PendingActionKind kind)
        {
            switch (kind)
            {
                case PendingActionKind.Analytics:
                case PendingActionKind.EquipCosmetic:
                case PendingActionKind.SetPlayerName:
                case PendingActionKind.RegisterPushToken:
                case PendingActionKind.SettingsChanged:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True for kinds where only the latest value matters, so a newer action replaces the
        /// older one for the same target instead of stacking behind it.
        /// </summary>
        public static bool CoalescesByTarget(PendingActionKind kind)
        {
            return kind == PendingActionKind.EquipCosmetic
                || kind == PendingActionKind.SetPlayerName
                || kind == PendingActionKind.RegisterPushToken
                || kind == PendingActionKind.SettingsChanged;
        }
    }
}
