using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoExport
{
    /// <summary>
    /// Remembers the export dialog's values per Revit project, so a project only needs setting up once.
    /// Stored as key=value text under %AppData%\RevitGeoExport\projects.
    /// </summary>
    internal static class SettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitGeoExport", "projects");

        public static Dictionary<string, string> Load(string project)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string path = PathFor(project);
                if (!File.Exists(path))
                    return values;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split > 0)
                        values[line.Substring(0, split)] = line.Substring(split + 1);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Remembering is a convenience; start from the defaults if the file can't be read.
            }

            return values;
        }

        public static void Save(string project, Dictionary<string, string> values)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var lines = new List<string> { "project=" + project.Replace('\n', ' ') };
                lines.AddRange(values.Select(v => v.Key + "=" + (v.Value ?? "").Replace('\r', ' ').Replace('\n', ' ')));
                File.WriteAllLines(PathFor(project), lines, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        // Project paths (including cloud "Autodesk Docs://" ones) can't be file names; hash them.
        private static string PathFor(string project)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(project ?? ""));
                return Path.Combine(Folder, string.Concat(hash.Take(10).Select(b => b.ToString("x2"))) + ".txt");
            }
        }
    }
}
