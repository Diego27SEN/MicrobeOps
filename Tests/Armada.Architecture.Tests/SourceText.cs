#nullable enable

using System.Text;

namespace Armada.Architecture.Tests
{
    /// <summary>
    /// Strips comments and string literals from C# source so scans match real code and not
    /// prose. Without this, a doc comment explaining why <c>DateTime.UtcNow</c> is banned would
    /// itself trip the ban.
    /// </summary>
    internal static class SourceText
    {
        public static string StripCommentsAndLiterals(string source)
        {
            StringBuilder output = new StringBuilder(source.Length);

            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inVerbatimString = false;
            bool inChar = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                        output.Append(c);
                    }
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    else if (c == '\n')
                    {
                        output.Append(c);
                    }
                    continue;
                }

                if (inVerbatimString)
                {
                    if (c == '"' && next == '"') { i++; continue; }
                    if (c == '"') inVerbatimString = false;
                    continue;
                }

                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (inChar)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
                if (c == '@' && next == '"') { inVerbatimString = true; i++; continue; }
                if (c == '"') { inString = true; continue; }
                if (c == '\'') { inChar = true; continue; }

                output.Append(c);
            }

            return output.ToString();
        }
    }
}
