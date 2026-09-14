using System.IO;
using System.Text;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>The plugin's own files under BepInEx/config, written so a crash mid-write never loses what was there.</summary>
internal static class JsonFile
{
    /// <summary>Written beside the old file and swapped in.</summary>
    public static void Write(string path, string text)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, Encoding.UTF8);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }
}
