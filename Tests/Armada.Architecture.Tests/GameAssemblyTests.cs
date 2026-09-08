#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Armada.Architecture.Tests
{
    /// <summary>
    /// Structural rules for the Unity layer. Like the Core rules, these read the repository as text
    /// so they run in CI without a licence, and each of them exists because the equivalent mistake
    /// cost time on a previous project.
    /// </summary>
    [TestFixture]
    public sealed class GameAssemblyTests
    {
        /// <summary>
        /// A static field that can be reassigned, or a static event. Not <c>const</c>, and not
        /// <c>static readonly</c>: those cannot be repointed at a destroyed object, which is the
        /// failure this guards against.
        /// </summary>
        /// <remarks>
        /// Deliberately not anchored to the start of a line. An earlier version was, and it silently
        /// passed a probe declaring a static on the same line as its class: a rule that depends on
        /// formatting is not a rule. The trailing <c>= or ;</c> is what separates a field from a
        /// property or a method, both of which continue with <c>{</c> or <c>(</c>.
        /// </remarks>
        private static readonly Regex MutableStatic = new Regex(
            @"(?<![\w.])(?:(?:public|private|internal|protected)\s+)?static\s+(?!readonly\b)(?!const\b)(?:event\s+)?[\w<>,\.\?\[\]]+\s+\w+\s*(?:=|;)",
            RegexOptions.Multiline);

        [Test]
        public void Game_HasNoUnregisteredMutableStatics()
        {
            // With "Enter Play Mode without domain reload" on - which this project turns on from
            // day one - a mutable static survives between play sessions. A field left pointing at a
            // destroyed object is the null reference family that cost the two previous projects
            // real time, and it only ever reproduced on someone else's machine.
            //
            // The rule: any file declaring a reassignable static must also register a reset with
            // StaticStateRegistry. StaticStateRegistry itself is exempt for the obvious reason.
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string name = Path.GetFileName(file);
                if (string.Equals(name, "StaticStateRegistry.cs", StringComparison.Ordinal)) continue;

                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));

                MatchCollection matches = MutableStatic.Matches(code);
                if (matches.Count == 0) continue;

                if (code.Contains("StaticStateRegistry.Register")) continue;

                List<string> declarations = new List<string>();
                foreach (Match match in matches) declarations.Add(match.Value.Trim());

                violations.Add(RepositoryLayout.RelativeToRoot(file) + ": " + string.Join(" | ", declarations));
            }

            Assert.That(violations, Is.Empty,
                "Mutable statics with no reset registered in StaticStateRegistry:\n" + string.Join("\n", violations));
        }

        [Test]
        public void StaticStateRegistry_ResetsOnSubsystemRegistration()
        {
            // The reset has to run even when the domain is NOT reloaded. Any other
            // RuntimeInitializeLoadType would leave exactly the bug this is meant to catch.
            string source = File.ReadAllText(
                Path.Combine(RepositoryLayout.GameSourceDirectory, "Infrastructure", "StaticStateRegistry.cs"));

            Assert.That(source, Does.Contain("RuntimeInitializeLoadType.SubsystemRegistration"));
        }

        [Test]
        public void Game_UsesUiToolkit_NeverUguiOrTextMeshPro()
        {
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));

                if (Regex.IsMatch(code, @"\busing\s+UnityEngine\s*\.\s*UI\b")
                    || Regex.IsMatch(code, @"\busing\s+TMPro\b")
                    || Regex.IsMatch(code, @"\bTextMeshPro"))
                {
                    violations.Add(RepositoryLayout.RelativeToRoot(file));
                }
            }

            Assert.That(violations, Is.Empty, "uGUI or TextMeshPro in the UI layer:\n" + string.Join("\n", violations));
        }

        [Test]
        public void Game_DoesNotUsePanelRenderer_BecauseItIsUnity65Plus()
        {
            // PanelRenderer does not exist in the 6.3 LTS this project is pinned to. Referencing it
            // would compile on a colleague's newer Editor and break the build for everyone else.
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));
                if (code.Contains("PanelRenderer")) violations.Add(RepositoryLayout.RelativeToRoot(file));
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void UiDocument_IsTouchedOnlyByTheFacade()
        {
            // The whole point of UiPanelHost is that the eventual migration to PanelRenderer edits
            // one file instead of every screen. That only holds if nothing else reaches for
            // UIDocument directly.
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                if (string.Equals(Path.GetFileName(file), "UiPanelHost.cs", StringComparison.Ordinal)) continue;

                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));
                if (Regex.IsMatch(code, @"\bUIDocument\b")) violations.Add(RepositoryLayout.RelativeToRoot(file));
            }

            Assert.That(violations, Is.Empty,
                "UIDocument must only be referenced by UiPanelHost:\n" + string.Join("\n", violations));
        }

        [Test]
        public void Colours_AreDeclaredOnlyInTheThemeStyleSheet()
        {
            // Colours live in :root variables in Theme.uss and nowhere else. This is what makes the
            // cosmetic themes and the colourblind palettes possible at all: a screen that hardcodes
            // a colour silently opts out of both.
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.StyleSheetFiles)
            {
                if (string.Equals(Path.GetFileName(file), "Theme.uss", StringComparison.Ordinal)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int comment = line.IndexOf("/*", StringComparison.Ordinal);
                    if (comment >= 0) line = line.Substring(0, comment);

                    if (Regex.IsMatch(line, @"#[0-9a-fA-F]{3,8}\b") || Regex.IsMatch(line, @"\brgba?\s*\("))
                    {
                        violations.Add(RepositoryLayout.RelativeToRoot(file) + ":" + (i + 1) + " " + lines[i].Trim());
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "Literal colours outside Theme.uss:\n" + string.Join("\n", violations));
        }

        [Test]
        public void Colours_AreNotHardcodedInGameCode()
        {
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));

                if (Regex.IsMatch(code, @"\bnew\s+Color\s*\(") || Regex.IsMatch(code, @"\bColor\s*\.\s*(red|green|blue|white|black|yellow|cyan|magenta)\b"))
                {
                    violations.Add(RepositoryLayout.RelativeToRoot(file));
                }
            }

            Assert.That(violations, Is.Empty,
                "Colours belong in USS variables, not in C#:\n" + string.Join("\n", violations));
        }

        [Test]
        public void Uxml_IsWellFormedXml()
        {
            // A UXML that does not parse produces no visual tree at all, and Unity says nothing
            // about it: the only symptom is every screen reporting itself missing at runtime.
            //
            // The specific trap that cost an evening here is that XML forbids a double hyphen
            // inside a comment, so writing a BEM-style modifier class name in a comment silently
            // breaks the whole document. Parsing the file is a one-line check that catches it, and
            // anything else malformed, before the Editor is even opened.
            List<string> violations = new List<string>();

            string uiDirectory = RepositoryLayout.UiDirectory;
            if (!Directory.Exists(uiDirectory)) return;

            foreach (string file in Directory.GetFiles(uiDirectory, "*.uxml", SearchOption.AllDirectories))
            {
                try
                {
                    System.Xml.Linq.XDocument.Load(file);
                }
                catch (System.Xml.XmlException exception)
                {
                    violations.Add(RepositoryLayout.RelativeToRoot(file) + ": " + exception.Message);
                }
            }

            Assert.That(violations, Is.Empty, "Malformed UXML:\n" + string.Join("\n", violations));
        }

        [Test]
        public void EveryElementTheScreensLookUp_ExistsInTheMarkup()
        {
            // The element names are a contract between the markup and the controllers, and nothing
            // enforced it: a name that stops matching degrades to a logged warning at runtime and a
            // control that quietly does nothing. That is how a missing back button ships.
            string markup = File.ReadAllText(Path.Combine(RepositoryLayout.UiDirectory, "Main.uxml"));

            HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(markup, @"name\s*=\s*""([^""]+)"""))
            {
                declared.Add(match.Groups[1].Value);
            }

            Regex lookups = new Regex(
                @"(?:Q<[^>]+>|WireButton|SetText)\(\s*(?:\w+\s*,\s*)?""([a-z][a-z0-9]*(?:-[a-z0-9]+)+)""");

            List<string> missing = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));
                if (!code.Contains("VisualElement", StringComparison.Ordinal)) continue;

                foreach (Match match in lookups.Matches(File.ReadAllText(file)))
                {
                    string name = match.Groups[1].Value;
                    if (!declared.Contains(name)) missing.Add(RepositoryLayout.RelativeToRoot(file) + ": " + name);
                }
            }

            Assert.That(missing, Is.Empty,
                "Element names looked up in code but absent from Main.uxml:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Uxml_DeclaresEveryPanelTheRouterExpects()
        {
            // The element names are a contract between the markup and the screen controllers, and
            // nothing else checks it until the game is running. Renaming one here without updating
            // its controller is a silent break.
            string markup = File.ReadAllText(Path.Combine(RepositoryLayout.UiDirectory, "Main.uxml"));

            string[] required =
            {
                "screen-home", "screen-mode", "screen-placement",
                "screen-match", "screen-summary", "screen-settings"
            };

            List<string> missing = new List<string>();
            foreach (string name in required)
            {
                if (!markup.Contains("name=\"" + name + "\"", StringComparison.Ordinal)) missing.Add(name);
            }

            Assert.That(missing, Is.Empty, "Panels missing from Main.uxml: " + string.Join(", ", missing));
        }

        [Test]
        public void StyleSheets_AreWiredByTheBootstrap_AndAnyUxmlReferenceCarriesItsGuid()
        {
            // Runtime styling comes from serialized asset references that ProjectBootstrap assigns,
            // because a reference is either wired or visibly null. A hand-written <Style src> is
            // not: the relative form imports without complaint and applies nothing, and the project
            // database form needs the fileID and GUID query string that only the Editor writes.
            // Both failures are silent and look identical on screen.
            //
            // A reference is still allowed, and useful - it is what makes UI Builder and the edit
            // mode preview show the real styling - but only in the form the Editor generates. Add
            // it from UI Builder rather than by hand.
            string uiDirectory = RepositoryLayout.UiDirectory;
            if (!Directory.Exists(uiDirectory)) return;

            List<string> violations = new List<string>();

            foreach (string file in Directory.GetFiles(uiDirectory, "*.uxml", SearchOption.AllDirectories))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"<Style\s+src\s*=\s*""([^""]+)"""))
                {
                    if (!match.Groups[1].Value.Contains("guid=", StringComparison.Ordinal))
                    {
                        violations.Add(RepositoryLayout.RelativeToRoot(file) + ": " + match.Groups[1].Value);
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "Style references that will not resolve:\n" + string.Join("\n", violations));

            // The other half of the rule: the bootstrap must actually wire every sheet, or it never
            // applies at runtime no matter what the markup says.
            string bootstrap = File.ReadAllText(
                Path.Combine(RepositoryLayout.GameSourceDirectory, "Editor", "ProjectBootstrap.cs"));

            foreach (string sheet in Directory.GetFiles(uiDirectory, "*.uss", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(sheet);
                Assert.That(bootstrap, Does.Contain(name),
                    name + " exists but ProjectBootstrap never assigns it, so it will never apply.");
            }
        }

        [Test]
        public void UiPanelHost_RunsInEditMode_SoTheGameViewPreviewIsStyled()
        {
            // UIDocument renders in edit mode but no ordinary Awake runs there, so without this the
            // preview shows raw markup: every panel at once, in Unity's default grey.
            string source = File.ReadAllText(
                Path.Combine(RepositoryLayout.GameSourceDirectory, "Infrastructure", "UiPanelHost.cs"));

            Assert.That(source, Does.Contain("[ExecuteAlways]"));
        }

        [Test]
        public void Uxml_CarriesNoPlayerFacingText()
        {
            // Every string a player reads comes from UiText by key. A `text=` attribute in the
            // markup is a string that can never be localized.
            List<string> violations = new List<string>();

            string uiDirectory = RepositoryLayout.UiDirectory;
            if (!Directory.Exists(uiDirectory)) return;

            foreach (string file in Directory.GetFiles(uiDirectory, "*.uxml", SearchOption.AllDirectories))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\stext\s*=\s*""([^""]+)"""))
                {
                    violations.Add(RepositoryLayout.RelativeToRoot(file) + ": text=\"" + match.Groups[1].Value + "\"");
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void Screens_AreHiddenByDefaultInStyle()
        {
            // The router reveals one panel by adding `screen--active`. If the default stopped being
            // hidden, every screen would render at once and nobody would notice until a screenshot.
            string screens = File.ReadAllText(Path.Combine(RepositoryLayout.UiDirectory, "Screens.uss"));

            Assert.That(Regex.IsMatch(screens, @"\.screen\s*\{[^}]*display\s*:\s*none"), Is.True,
                ".screen must default to display: none");
            Assert.That(Regex.IsMatch(screens, @"\.screen--active\s*\{[^}]*display\s*:\s*flex"), Is.True,
                ".screen--active must set display: flex");
        }

        [Test]
        public void Game_ReferencesCore_AndCoreDoesNotReferenceGame()
        {
            string gameAsmdef = File.ReadAllText(Path.Combine(RepositoryLayout.GameSourceDirectory, "Armada.Game.asmdef"));
            string coreAsmdef = File.ReadAllText(RepositoryLayout.CoreAssemblyDefinition);

            Assert.That(gameAsmdef, Does.Contain("Armada.Core"));
            Assert.That(coreAsmdef, Does.Not.Contain("Armada.Game"));
        }

        [Test]
        public void TheOnlyHardcodedPlayerFacingStrings_LiveInBootTextsAndTheTemporaryStringTable()
        {
            // Everything a player reads goes through UiText by key. The two exceptions are declared
            // and bounded: BootTexts, which runs before Localization exists, and EmbeddedStrings,
            // the stand-in table that Unity Localization replaces in M3.
            string[] allowed = { "BootTexts.cs", "UiText.cs" };
            List<string> violations = new List<string>();

            foreach (string file in RepositoryLayout.GameRuntimeSourceFiles)
            {
                string name = Path.GetFileName(file);
                if (Array.IndexOf(allowed, name) >= 0) continue;

                // A dictionary of strings in a screen is the shape a hardcoded copy table takes.
                string code = SourceText.StripCommentsAndLiterals(File.ReadAllText(file));
                if (Regex.IsMatch(code, @"Dictionary\s*<\s*string\s*,\s*string\s*>"))
                {
                    violations.Add(RepositoryLayout.RelativeToRoot(file));
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }
    }
}
