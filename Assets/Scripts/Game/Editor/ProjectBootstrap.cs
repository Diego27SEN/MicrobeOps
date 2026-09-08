#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Armada.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Armada.Editor
{
    /// <summary>
    /// Single entry point for every generated project asset: scene wiring, panel settings,
    /// locales, string tables, Remote Config fragments and content-derived ScriptableObjects.
    /// <para>
    /// It exists to solve the "last mile" problem inherited from the previous project, where the
    /// proof of concept could not be finished remotely because scene wiring and panel settings
    /// demanded a human with the Editor open. Everything here must be idempotent: running it twice
    /// produces the same project, and CI runs it headless on every build:
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod Armada.Editor.ProjectBootstrap.SetupAll
    /// </code>
    /// <para>
    /// Steps are registered rather than called inline so each milestone can add its own without
    /// touching this method, and so a failing step names itself in the log.
    /// </para>
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string MenuPath = "Armada/Project/Setup All";

        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string PanelSettingsPath = "Assets/UI/ArmadaPanelSettings.asset";
        private const string MainUxmlPath = "Assets/UI/Main.uxml";
        private const string ThemeUssPath = "Assets/UI/Theme.uss";
        private const string ScreensUssPath = "Assets/UI/Screens.uss";

        /// <summary>A named, idempotent setup step.</summary>
        private readonly struct Step
        {
            public readonly string Name;
            public readonly Action Run;

            public Step(string name, Action run)
            {
                Name = name;
                Run = run;
            }
        }

        private static IReadOnlyList<Step> BuildSteps()
        {
            return new List<Step>
            {
                new Step("Editor settings", ConfigureEditorSettings),
                new Step("Panel settings", EnsurePanelSettings),
                new Step("Main scene", EnsureMainScene),
                new Step("Build settings", EnsureBuildSettings)
            };
        }

        [MenuItem(MenuPath)]
        public static void SetupAll()
        {
            bool batchMode = Application.isBatchMode;
            IReadOnlyList<Step> steps = BuildSteps();

            Debug.Log("[ProjectBootstrap] Running " + steps.Count + " setup step(s).");

            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    Step step = steps[i];
                    Debug.Log("[ProjectBootstrap] Step " + (i + 1) + "/" + steps.Count + ": " + step.Name);
                    step.Run();
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[ProjectBootstrap] Done.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[ProjectBootstrap] Failed: " + exception);

                // In batch mode a swallowed exception would let CI publish a half-generated
                // project, so make the process exit non-zero.
                if (batchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>
        /// Turns on "Enter Play Mode without domain reload" from day one.
        /// <para>
        /// This looks like it makes life harder, and it does - deliberately. It is the setting that
        /// makes stale-static bugs appear on a developer's machine in week one instead of in a
        /// production crash report. The counterpart is <see cref="StaticStateRegistry"/> and the
        /// architecture test that enforces it.
        /// </para>
        /// </summary>
        private static void ConfigureEditorSettings()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        private static void EnsurePanelSettings()
        {
            EnsureFolder("Assets/UI");

            PanelSettings? settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            // Scale with screen size against a phone-shaped reference, matching on height so the
            // board keeps its proportions on everything from 16:9 to 20:9.
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1.0f;
            settings.clearColor = true;
            settings.colorClearValue = new Color(0.047f, 0.078f, 0.121f, 1f);

            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// Builds the one persistent scene: a camera, an EventSystem-free UI document and the
        /// game root. Written from scratch every run so the scene file is a pure function of this
        /// code - nobody ever hand-edits the YAML, which is the whole point.
        /// </summary>
        private static void EnsureMainScene()
        {
            EnsureFolder("Assets/Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.047f, 0.078f, 0.121f, 1f);
            camera.orthographic = true;

            GameObject uiObject = new GameObject("UI");
            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainUxmlPath);

            if (document.panelSettings == null) throw new InvalidOperationException("PanelSettings missing at " + PanelSettingsPath);
            if (document.visualTreeAsset == null) throw new InvalidOperationException("Main.uxml missing at " + MainUxmlPath);

            UiPanelHost host = uiObject.AddComponent<UiPanelHost>();
            AssignStyleSheets(host);

            uiObject.AddComponent<GameRoot>();

            EditorSceneManager.SaveScene(scene, MainScenePath);
        }

        /// <summary>
        /// Wires the style sheets onto the host through the serialized field.
        /// <para>
        /// This is the generated-asset rule doing its job: the wiring lives in code, is idempotent,
        /// and survives a regenerated scene. Nobody drags a .uss onto a component by hand, and
        /// nobody hand-edits the YAML that results.
        /// </para>
        /// </summary>
        private static void AssignStyleSheets(UiPanelHost host)
        {
            string[] paths = { ThemeUssPath, ScreensUssPath };

            SerializedObject serialized = new SerializedObject(host);
            SerializedProperty sheets = serialized.FindProperty("_styleSheets");
            sheets.arraySize = paths.Length;

            for (int i = 0; i < paths.Length; i++)
            {
                StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(paths[i]);
                if (sheet == null) throw new InvalidOperationException("Style sheet missing at " + paths[i]);

                sheets.GetArrayElementAtIndex(i).objectReferenceValue = sheet;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
