#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    /// <summary>
    /// The only place in the project that touches <see cref="UIDocument"/> directly.
    /// <para>
    /// Unity 6.3 LTS has <c>UIDocument</c>; <c>PanelRenderer</c> arrives in 6.5. Sooner or later
    /// this project will jump an LTS and want the newer one, and when that happens the migration
    /// should touch this file and nothing else - not the sixteen screens. That is the whole reason
    /// this class exists; it is not indirection for its own sake.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <c>ExecuteAlways</c> is what makes the Game view usable while editing. <c>UIDocument</c>
    /// renders in edit mode, but no ordinary <c>Awake</c> runs there, so with the style sheets
    /// attached from code the preview was showing raw unstyled markup: every panel at once, in
    /// Unity's default grey. Attaching them in edit mode too costs nothing - the work is idempotent
    /// and touches only the panel - and it restores a preview worth looking at. Only Home shows,
    /// because that is the one panel the markup marks active; the router takes over at runtime.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiPanelHost : MonoBehaviour
    {
        /// <summary>
        /// How many scheduler ticks to wait for the tree before giving up and saying so. Generous
        /// enough to cover any component ordering, small enough that a genuinely broken document
        /// reports itself instead of retrying in silence forever.
        /// </summary>
        private const int MaxReadyAttempts = 30;

        /// <summary>
        /// Style sheets applied to the root every time the tree is rebuilt. Assigned by
        /// <c>ProjectBootstrap</c>, never by hand.
        /// <para>
        /// They are attached from here instead of through a <c>&lt;Style src&gt;</c> element in the
        /// UXML on purpose. That attribute only resolves reliably with the full
        /// <c>project://database/...?fileID=...&amp;guid=...</c> URI the Editor generates; a
        /// hand-written relative path imports without complaint and then silently applies nothing,
        /// which is exactly how this project spent an evening looking at default grey buttons.
        /// An asset reference cannot fail that way: it is either assigned or visibly null.
        /// </para>
        /// </summary>
        [SerializeField]
        private StyleSheet[] _styleSheets = Array.Empty<StyleSheet>();

        private UIDocument? _document;
        private Action? _rebuilt;
        private int _readyAttempts;
        private bool _waitingForTree;

        /// <summary>
        /// The live root, or null when the panel is gone. Never cache it across an await: with
        /// <c>UIDocument</c> the tree can be rebuilt while you are suspended, and the element you
        /// were holding is then detached and silently does nothing.
        /// </summary>
        public VisualElement? Root
        {
            get { return _document != null ? _document.rootVisualElement : null; }
        }

        /// <summary>
        /// The guard to write after every await in UI code:
        /// <c>if (!host.IsAlive) return;</c>
        /// </summary>
        public bool IsAlive
        {
            get { return this != null && _document != null && _document.rootVisualElement != null; }
        }

        /// <summary>
        /// True once the visual tree has actually been cloned into the root.
        /// <para>
        /// <see cref="IsAlive"/> is not enough to bind against. <c>UIDocument</c> hands out a
        /// non-null <c>rootVisualElement</c> well before it clones the UXML into it, so a screen
        /// that queries too early finds nothing, logs that its panel is missing, and leaves the
        /// UI unwired. Which is exactly what happened the first time this ran.
        /// </para>
        /// </summary>
        public bool IsReady
        {
            get { return IsAlive && _document!.rootVisualElement.childCount > 0; }
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _readyAttempts = 0;
            _waitingForTree = false;
            FireWhenReady();
        }

        /// <summary>
        /// Fires the rebuild callbacks as soon as the tree exists.
        /// <para>
        /// Whether <c>UIDocument.OnEnable</c> has already run when this one does depends on the
        /// order the components sit in on the GameObject - which is a fact about a serialized
        /// scene file, not something the code should be betting on. Retrying on the scheduler
        /// removes the bet entirely.
        /// </para>
        /// </summary>
        private void FireWhenReady()
        {
            if (!IsAlive) return;

            if (IsReady)
            {
                _waitingForTree = false;
                ApplyStyleSheets();
                _rebuilt?.Invoke();
                return;
            }

            if (_waitingForTree) return;

            if (++_readyAttempts > MaxReadyAttempts)
            {
                Debug.LogError(
                    "[UiPanelHost] The UI document never produced a visual tree. Check that the UIDocument " +
                    "has both a PanelSettings and a Source Asset assigned, and that the UXML imports cleanly.");
                return;
            }

            _waitingForTree = true;
            _document!.rootVisualElement.schedule.Execute(() =>
            {
                _waitingForTree = false;
                FireWhenReady();
            });
        }

        /// <summary>
        /// Registers a rewiring callback. It fires as soon as the tree is available, and again on
        /// every rebuild.
        /// <para>
        /// The callback MUST be idempotent. Running twice is the normal case, not the error case,
        /// and a callback that registers an event handler without clearing the previous one will
        /// fire twice per click.
        /// </para>
        /// </summary>
        public void RegisterRebuildCallback(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _rebuilt -= callback;
            _rebuilt += callback;

            if (IsReady)
            {
                callback();
                return;
            }

            // Registration can land after OnEnable has already come and gone - GameRoot registers
            // from Awake, and nothing guarantees the two orders line up. Kicking the wait off from
            // here too means a late registration still gets wired instead of waiting for a rebuild
            // that never comes.
            FireWhenReady();
        }

        public void UnregisterRebuildCallback(Action callback)
        {
            _rebuilt -= callback;
        }

        /// <summary>
        /// Attaches the style sheets to the freshly built root. Idempotent: the tree is rebuilt
        /// more than once and adding the same sheet twice would apply every rule twice.
        /// </summary>
        private void ApplyStyleSheets()
        {
            VisualElement root = _document!.rootVisualElement;

            if (_styleSheets.Length == 0)
            {
                // Warn once per enable, and only while playing: in edit mode a freshly added host
                // legitimately has nothing wired yet, and a console full of warnings during asset
                // work is noise nobody reads.
                if (Application.isPlaying)
                {
                    Debug.LogWarning(
                        "[UiPanelHost] No style sheets assigned. The UI will render with Unity's default look. " +
                        "Run Armada > Project > Setup All to wire them.");
                }

                return;
            }

            for (int i = 0; i < _styleSheets.Length; i++)
            {
                StyleSheet sheet = _styleSheets[i];
                if (sheet == null) continue;
                if (root.styleSheets.Contains(sheet)) continue;

                root.styleSheets.Add(sheet);
            }
        }

        private void OnDisable()
        {
            // Handlers are kept: a disabled host is expected to come back, and dropping them here
            // would leave the screens unwired after the next enable.
        }
    }
}
