using System;
using System.IO;

namespace JMDParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string sourceDir = null;
            string targetDir = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-i" && i + 1 < args.Length)
                {
                    sourceDir = args[++i];
                }
                else if (args[i] == "-o" && i + 1 < args.Length)
                {
                    targetDir = args[++i];
                }
            }

            if (string.IsNullOrEmpty(sourceDir))
            {
                sourceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            }
            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataRaw");
            }

            sourceDir = Path.GetFullPath(sourceDir);
            targetDir = Path.GetFullPath(targetDir);

            if (!Directory.Exists(sourceDir))
            {
                return;
            }

            var jmdFiles = Directory.GetFiles(sourceDir, "*.jmd", SearchOption.AllDirectories);
            Array.Sort(jmdFiles);

            bool movieCopied = false;

            foreach (var jmdPath in jmdFiles)
            {
                string relJmd = GetRelativePath(sourceDir, jmdPath);

                if (!movieCopied && string.Compare(relJmd, "movie", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    string sourceMovieDir = Path.Combine(sourceDir, "movie");
                    string targetMovieDir = Path.Combine(targetDir, "movie");
                    if (Directory.Exists(sourceMovieDir))
                    {
                        Console.WriteLine("Data\\movie");
                        CopyDirectory(sourceMovieDir, targetMovieDir);
                    }
                    movieCopied = true;
                }

                try
                {
                    Console.WriteLine(Path.Combine("Data", relJmd));
                    string subpath = Path.GetDirectoryName(relJmd) ?? "";
                    string name = Path.GetFileNameWithoutExtension(relJmd);

                    string targetName = name;
                    if (targetName.EndsWith("_tex", StringComparison.OrdinalIgnoreCase))
                    {
                        targetName = targetName.Substring(0, targetName.Length - 4);
                    }

                    string parentDirName = Path.GetFileName(subpath);

                    bool isSoundBgm = relJmd.Replace('\\', '/').StartsWith("sound/bgm", StringComparison.OrdinalIgnoreCase);
                    bool shouldUnpackToParent = string.Equals(targetName, parentDirName, StringComparison.OrdinalIgnoreCase) || isSoundBgm;

                    string targetOutputDir;
                    if (shouldUnpackToParent)
                    {
                        targetOutputDir = Path.Combine(targetDir, subpath);
                    }
                    else
                    {
                        targetOutputDir = Path.Combine(targetDir, subpath, targetName);
                    }

                    using (var parser = new JmdLogic())
                    {
                        parser.Open(jmdPath);
                        parser.ExtractAll(targetOutputDir);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed to process JMD {Path.GetFileName(jmdPath)}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            if (!movieCopied)
            {
                string sourceMovieDir = Path.Combine(sourceDir, "movie");
                string targetMovieDir = Path.Combine(targetDir, "movie");
                if (Directory.Exists(sourceMovieDir))
                {
                    Console.WriteLine("Data\\movie");
                    CopyDirectory(sourceMovieDir, targetMovieDir);
                }
            }
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            var uri1 = new Uri(relativeTo.EndsWith("\\") || relativeTo.EndsWith("/") ? relativeTo : relativeTo + "\\");
            var uri2 = new Uri(path);
            var relativeUri = uri1.MakeRelativeUri(uri2);
            string relPath = Uri.UnescapeDataString(relativeUri.ToString());
            return relPath.Replace('/', '\\');
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string dest = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string folder in Directory.GetDirectories(source))
            {
                string dest = Path.Combine(target, Path.GetFileName(folder));
                CopyDirectory(folder, dest);
            }
        }
    }
}
