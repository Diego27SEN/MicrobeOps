#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Armada.Game
{
    /// <summary>
    /// Single place where every piece of mutable static state in <c>Armada.Game</c> declares
    /// itself, and the single place where it all gets reset.
    /// <para>
    /// With "Enter Play Mode without domain reload" turned on - which this project does turn on,
    /// from day one, on purpose - statics survive between play sessions in the Editor. A field left
    /// holding a destroyed object is the null reference family that cost the two previous projects
    /// real time, and it only ever showed up on someone else's machine.
    /// </para>
    /// <para>
    /// The rule is: no mutable static in <c>Armada.Game</c> that is not registered here. The
    /// architecture test <c>Game_HasNoUnregisteredMutableStatics</c> fails the build otherwise, so
    /// this is enforced rather than remembered.
    /// </para>
    /// </summary>
    public static class StaticStateRegistry
    {
        private static readonly List<Action> Resets = new List<Action>();

        /// <summary>
        /// Registers a reset action. Call it from a static constructor, not from instance code:
        /// registration itself must happen once per domain, not once per object.
        /// </summary>
        public static void Register(Action reset)
        {
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            Resets.Add(reset);
        }

        /// <summary>How many resets are registered. Exposed for the architecture test and for logs.</summary>
        public static int RegisteredCount
        {
            get { return Resets.Count; }
        }

        /// <summary>
        /// Runs before any scene loads, on every entry into play mode, whether or not the domain
        /// was reloaded. That is exactly the point: the reset has to happen in both cases so the
        /// bug cannot hide behind a domain reload during development and then appear in a build.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetAll()
        {
            for (int i = 0; i < Resets.Count; i++)
            {
                try
                {
                    Resets[i]();
                }
                catch (Exception exception)
                {
                    // One misbehaving reset must not stop the rest, or the first failure would
                    // cascade into a much harder bug to read.
                    Debug.LogError("[StaticStateRegistry] A reset threw: " + exception);
                }
            }
        }
    }
}
