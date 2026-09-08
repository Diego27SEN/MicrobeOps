#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Armada.Architecture.Tests
{
    /// <summary>
    /// Locates the repository from wherever the test runner happens to execute, so these tests
    /// work the same on a developer machine and on a CI runner.
    /// </summary>
    internal static class RepositoryLayout
    {
        private static string? _root;

        public static string Root
        {
            get { return _root ??= FindRoot(); }
        }

        public static string CoreSourceDirectory
        {
            get { return Path.Combine(Root, "Assets", "Scripts", "Core"); }
        }

        public static string CoreTestsSourceDirectory
        {
            get { return Path.Combine(Root, "Assets", "Scripts", "Core.Tests"); }
        }

        public static string CoreAssemblyDefinition
        {
            get { return Path.Combine(CoreSourceDirectory, "Armada.Core.asmdef"); }
        }

        public static string CoreCompilerResponseFile
        {
            get { return Path.Combine(CoreSourceDirectory, "csc.rsp"); }
        }

        public static IReadOnlyList<string> CoreSourceFiles
        {
            get { return Directory.GetFiles(CoreSourceDirectory, "*.cs", SearchOption.AllDirectories); }
        }

        public static string GameSourceDirectory
        {
            get { return Path.Combine(Root, "Assets", "Scripts", "Game"); }
        }

        public static string UiDirectory
        {
            get { return Path.Combine(Root, "Assets", "UI"); }
        }

        /// <summary>Runtime sources of Armada.Game, excluding the editor-only assembly.</summary>
        public static IReadOnlyList<string> GameRuntimeSourceFiles
        {
            get
            {
                List<string> files = new List<string>();
                string editorDirectory = Path.Combine(GameSourceDirectory, "Editor");

                foreach (string file in Directory.GetFiles(GameSourceDirectory, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.StartsWith(editorDirectory, StringComparison.OrdinalIgnoreCase)) continue;
                    files.Add(file);
                }

                return files;
            }
        }

        public static IReadOnlyList<string> StyleSheetFiles
        {
            get
            {
                return Directory.Exists(UiDirectory)
                    ? Directory.GetFiles(UiDirectory, "*.uss", SearchOption.AllDirectories)
                    : Array.Empty<string>();
            }
        }

        public static string RelativeToRoot(string absolutePath)
        {
            return Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');
        }

        private static string FindRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                bool looksLikeTheProject =
                    Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"));

                if (looksLikeTheProject) return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity project root above " + AppContext.BaseDirectory);
        }
    }
}
