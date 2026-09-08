#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Armada.Game
{
    public enum ScreenId
    {
        Home,
        ModeSelect,
        Placement,
        Match,
        Summary,
        Settings
    }

    /// <summary>
    /// One panel of the single persistent scene. Screens are plain C# objects, not MonoBehaviours:
    /// there is one scene and one document, so there is nothing for a component to hang off.
    /// </summary>
    public abstract class ScreenController
    {
        /// <summary>Name of this screen's element inside the document.</summary>
        public abstract string RootName { get; }

        protected VisualElement? Root { get; private set; }

        protected ScreenRouter Router { get; private set; } = null!;

        /// <summary>
        /// Wires the screen to a freshly built tree. Called again on every rebuild, so it must be
        /// idempotent: query elements fresh, and never assume a handler from last time survived.
        /// </summary>
        /// <summary>Returns false when this screen's element is not in the document.</summary>
        public bool Bind(VisualElement appRoot, ScreenRouter router)
        {
            Router = router;
            Root = appRoot.Q<VisualElement>(RootName);

            if (Root == null) return false;

            OnBind(Root);
            return true;
        }

        protected abstract void OnBind(VisualElement root);

        public virtual void OnShow()
        {
        }

        public virtual void OnHide()
        {
        }

        /// <summary>
        /// Toggles the modifier class rather than writing an inline style. Inline styles win over
        /// every stylesheet rule, so using one here would mean the panel's visibility lived in two
        /// places at once - and would undo the USS default that keeps the edit-mode preview from
        /// showing all six panels stacked.
        /// </summary>
        internal void SetVisible(bool visible)
        {
            Root?.EnableInClassList("screen--active", visible);
        }

        /// <summary>Sets a label's text from a Localization key. Null-safe, so a renamed element degrades to a warning.</summary>
        protected static void SetText(VisualElement root, string elementName, string localizationKey)
        {
            Label? label = root.Q<Label>(elementName);
            if (label == null)
            {
                Debug.LogWarning("[UI] Label '" + elementName + "' not found.");
                return;
            }

            label.text = UiText.Get(localizationKey);
        }

        /// <summary>
        /// Wires a button, replacing whatever was there before. Assigning <c>clicked</c> through
        /// this helper is what keeps rebinding idempotent: registering twice would fire twice.
        /// </summary>
        protected static Button? WireButton(VisualElement root, string elementName, string localizationKey, Action onClick)
        {
            Button? button = root.Q<Button>(elementName);
            if (button == null)
            {
                Debug.LogWarning("[UI] Button '" + elementName + "' not found.");
                return null;
            }

            button.text = UiText.Get(localizationKey);
            button.clickable = new Clickable(onClick);
            return button;
        }
    }

    /// <summary>
    /// Shows exactly one panel at a time inside the single document. There is no scene loading in
    /// this game: switching screens is toggling display, which is what keeps "two taps to be
    /// shooting" achievable.
    /// </summary>
    public sealed class ScreenRouter
    {
        private readonly Dictionary<ScreenId, ScreenController> _screens = new Dictionary<ScreenId, ScreenController>();
        private ScreenId _current = ScreenId.Home;
        private bool _bound;

        public ScreenId Current
        {
            get { return _current; }
        }

        public void Register(ScreenId id, ScreenController screen)
        {
            _screens[id] = screen;
        }

        public T Get<T>(ScreenId id) where T : ScreenController
        {
            return (T)_screens[id];
        }

        /// <summary>Rebinds every screen to a new tree and restores the current one.</summary>
        public void Bind(VisualElement appRoot)
        {
            List<string> missing = new List<string>();

            foreach (KeyValuePair<ScreenId, ScreenController> entry in _screens)
            {
                if (!entry.Value.Bind(appRoot, this)) missing.Add(entry.Value.RootName);
            }

            // One message naming everything that is missing, rather than one per screen. Six
            // identical-looking errors say "the UI is broken"; this one says which part.
            if (missing.Count > 0)
            {
                Debug.LogError(
                    "[UI] " + missing.Count + " panel(s) missing from the document: " + string.Join(", ", missing) +
                    ". Either the UXML did not load, or these element names no longer match Main.uxml.");
            }

            _bound = true;
            Apply(_current, notify: false);
        }

        public void Show(ScreenId id)
        {
            if (!_screens.ContainsKey(id))
            {
                Debug.LogError("[UI] No screen registered for " + id);
                return;
            }

            if (_bound && _current == id) return;

            Apply(id, notify: true);
        }

        private void Apply(ScreenId id, bool notify)
        {
            ScreenId previous = _current;
            _current = id;

            foreach (KeyValuePair<ScreenId, ScreenController> entry in _screens)
            {
                entry.Value.SetVisible(entry.Key == id);
            }

            if (!notify) return;

            if (_screens.TryGetValue(previous, out ScreenController? old) && previous != id) old.OnHide();
            _screens[id].OnShow();
        }
    }
}
