using System.IO;

namespace TodoWidget.Services;

public static class SoundGenerator
{
    public static void EnsureDefaultTracksExist(string musicFolder)
    {
        try
        {
            if (Directory.Exists(musicFolder))
            {
                // Purge all downloaded audio files to free up disk space as audio streams directly from GitHub
                foreach (var file in Directory.GetFiles(musicFolder, "*.*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
