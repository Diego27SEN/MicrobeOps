#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Armada.Core;
using Unity.Services.CloudCode;
using UnityEngine;

namespace Armada.Game
{
    /// <summary>What happened to a call, from the caller's point of view.</summary>
    public enum GatewayStatus
    {
        Ok = 0,

        /// <summary>The server answered with an <c>ERR_*</c> code. A decision, not a failure.</summary>
        Rejected = 1,

        /// <summary>The call never got an answer. Retries are already spent by the time this is returned.</summary>
        Offline = 2,

        /// <summary>This build cannot talk to this server: wrong protocol, or a kill switch.</summary>
        SessionEnded = 3
    }

    /// <summary>The outcome of one gateway call.</summary>
    public sealed class GatewayResult<T>
    {
        public GatewayStatus Status { get; private set; }
        public T? Data { get; private set; }

        /// <summary>Formatted <c>ERR_CODE|p1</c>, or null. Never shown to a player as-is.</summary>
        public string? Error { get; private set; }

        public bool IsOk
        {
            get { return Status == GatewayStatus.Ok; }
        }

        /// <summary>The player-facing message, resolved through Localization.</summary>
        public string LocalizedMessage
        {
            get { return Error != null ? UiText.Error(Error) : string.Empty; }
        }

        public static GatewayResult<T> Ok(T data)
        {
            return new GatewayResult<T> { Status = GatewayStatus.Ok, Data = data };
        }

        public static GatewayResult<T> Rejected(string error)
        {
            return new GatewayResult<T> { Status = GatewayStatus.Rejected, Error = error };
        }

        public static GatewayResult<T> Offline()
        {
            return new GatewayResult<T> { Status = GatewayStatus.Offline };
        }

        public static GatewayResult<T> SessionEnded(string error)
        {
            return new GatewayResult<T> { Status = GatewayStatus.SessionEnded, Error = error };
        }
    }

    /// <summary>
    /// The single door to Cloud Code. Nothing else in the client calls it directly.
    /// <para>
    /// The interesting decisions are not here: <see cref="RetryPolicy"/> decides what is worth
    /// retrying and how long to wait, and <see cref="PendingActionQueue"/> decides what may wait
    /// for the network at all. Both live in Core, where they are tested without a network. This
    /// class is the transport that carries them out, kept deliberately thin so there is little
    /// here that can be wrong and untestable at the same time.
    /// </para>
    /// <para>
    /// It never throws. An unexpected exception degrades to <see cref="GatewayStatus.Offline"/>
    /// and a warning, because a service that misbehaves must not take the game down with it.
    /// </para>
    /// </summary>
    public sealed class UgsGateway
    {
        private readonly RetryPolicy _retry;
        private readonly PendingActionQueue _queue;
        private readonly Func<string, Dictionary<string, object>, Task<string>> _invoke;

        /// <summary>Raised when the session cannot continue: forced update or maintenance.</summary>
        public event Action<string>? SessionEnded;

        public UgsGateway(
            RetryPolicy? retry = null,
            PendingActionQueue? queue = null,
            Func<string, Dictionary<string, object>, Task<string>>? invoke = null)
        {
            _retry = retry ?? new RetryPolicy();
            _queue = queue ?? new PendingActionQueue();

            // The transport is injectable so the gateway itself can be exercised without UGS.
            // Default is the real Cloud Code call.
            _invoke = invoke ?? InvokeCloudCode;
        }

        public PendingActionQueue OfflineQueue
        {
            get { return _queue; }
        }

        /// <summary>
        /// Calls a Cloud Code function, retrying only what is worth retrying.
        /// <para>
        /// The deserializer is passed in rather than pulled from a JSON library here: Core cannot
        /// depend on one, and keeping the choice at the call site means switching serializers later
        /// touches the callers rather than this class.
        /// </para>
        /// </summary>
        public async Task<GatewayResult<T>> CallAsync<T>(
            string functionName,
            Dictionary<string, object> arguments,
            Func<string, T> deserialize)
        {
            if (string.IsNullOrEmpty(functionName)) throw new ArgumentException("Function name is required.", nameof(functionName));
            if (deserialize == null) throw new ArgumentNullException(nameof(deserialize));

            int attempts = 0;
            string? lastError = null;

            while (true)
            {
                int delay = _retry.DelayMsBefore(attempts, lastError);
                if (delay > 0) await Task.Delay(delay);

                attempts++;

                try
                {
                    string raw = await _invoke(functionName, arguments);
                    T parsed = deserialize(raw);

                    return GatewayResult<T>.Ok(parsed);
                }
                catch (CloudCodeException exception)
                {
                    lastError = ExtractErrorCode(exception);

                    if (RetryPolicy.IsFatalForSession(lastError))
                    {
                        SessionEnded?.Invoke(lastError!);
                        return GatewayResult<T>.SessionEnded(lastError!);
                    }

                    if (lastError != null && !_retry.ShouldRetryError(lastError, attempts))
                    {
                        return GatewayResult<T>.Rejected(lastError);
                    }

                    if (!_retry.ShouldRetryTransport(attempts)) return GatewayResult<T>.Offline();
                }
                catch (Exception exception)
                {
                    // Anything unexpected is treated as a transport problem rather than allowed to
                    // escape. A misbehaving service must degrade the feature, not the game.
                    Debug.LogWarning("[UgsGateway] " + functionName + " failed: " + exception.Message);
                    lastError = null;

                    if (!_retry.ShouldRetryTransport(attempts)) return GatewayResult<T>.Offline();
                }
            }
        }

        /// <summary>
        /// Calls a function, or queues it for later when there is no network.
        /// <para>
        /// Only for the kinds the queue accepts. Anything that decides a match or moves currency is
        /// refused by <see cref="PendingActionQueue"/> itself, so this cannot be used to make a
        /// shot land late by mistake.
        /// </para>
        /// </summary>
        public async Task<GatewayResult<bool>> CallOrQueueAsync(
            string functionName,
            Dictionary<string, object> arguments,
            PendingAction action)
        {
            GatewayResult<bool> result = await CallAsync(functionName, arguments, _ => true);
            if (result.Status != GatewayStatus.Offline) return result;

            if (!_queue.TryEnqueue(action, out string rejection))
            {
                Debug.LogWarning("[UgsGateway] " + functionName + " was not queued: " + rejection);
            }

            return result;
        }

        /// <summary>
        /// Replays what was queued while offline, stopping at the first call that fails to reach
        /// the server and putting the rest back in order. Ploughing on would reorder the replay.
        /// </summary>
        public async Task ReplayQueuedAsync(Func<PendingAction, Task<GatewayResult<bool>>> send)
        {
            if (send == null) throw new ArgumentNullException(nameof(send));

            IReadOnlyList<PendingAction> pending = _queue.Drain();

            for (int i = 0; i < pending.Count; i++)
            {
                GatewayResult<bool> result = await send(pending[i]);
                if (result.Status != GatewayStatus.Offline) continue;

                List<PendingAction> remaining = new List<PendingAction>();
                for (int r = i; r < pending.Count; r++) remaining.Add(pending[r]);

                _queue.Requeue(remaining);
                return;
            }
        }

        private static async Task<string> InvokeCloudCode(string functionName, Dictionary<string, object> arguments)
        {
            return await CloudCodeService.Instance.CallEndpointAsync<string>(functionName, arguments);
        }

        /// <summary>
        /// Digs the <c>ERR_*</c> token out of a Cloud Code failure. The server never sends prose,
        /// so anything without that shape is a transport problem wearing an exception's clothes.
        /// </summary>
        private static string? ExtractErrorCode(CloudCodeException exception)
        {
            string message = exception.Message ?? string.Empty;

            int start = message.IndexOf("ERR_", StringComparison.Ordinal);
            if (start < 0) return null;

            int end = start;
            while (end < message.Length && (char.IsLetterOrDigit(message[end]) || message[end] == '_' || message[end] == '|' || message[end] == '.'))
            {
                end++;
            }

            return message.Substring(start, end - start);
        }
    }
}
