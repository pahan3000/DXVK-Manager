using System.IO;

namespace DxvkInjector.Dxvk
{
    /// <summary>
    /// Validates a user-picked "DXVK extracted here" folder instead of downloading
    /// anything. The user is expected to have already grabbed a DXVK (or dxvk-gplasync)
    /// release themselves and extracted it somewhere; we just need to confirm the
    /// folder looks right and hand back the correct architecture subfolder.
    /// Expected layout: {folder}/x32/*.dll and/or {folder}/x64/*.dll — exactly what
    /// the official DXVK release archives extract to.
    /// </summary>
    public static class DxvkLocalSource
    {
        public static bool TryValidate(string folderPath, out string error)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                error = "No DXVK folder is set. Open DXVK Injector settings and pick the folder you extracted DXVK into.";
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                error = $"DXVK folder does not exist: {folderPath}";
                return false;
            }

            var x32 = Path.Combine(folderPath, "x32");
            var x64 = Path.Combine(folderPath, "x64");

            if (!Directory.Exists(x32) && !Directory.Exists(x64))
            {
                error = "That folder doesn't look like an extracted DXVK build — expected an x32 and/or x64 " +
                        "subfolder containing the DLLs. Point this at the folder produced by extracting the " +
                        "DXVK (or dxvk-gplasync) release archive.";
                return false;
            }

            error = null;
            return true;
        }

        public static string GetArchDir(string dxvkFolderPath, GameArchitecture arch)
        {
            var sub = arch == GameArchitecture.X64 ? "x64" : "x32";
            return Path.Combine(dxvkFolderPath, sub);
        }

        /// <summary>Best-effort display label for the configured build, derived from the folder name (e.g. "dxvk-2.4").</summary>
        public static string GetDisplayLabel(string dxvkFolderPath)
        {
            if (string.IsNullOrEmpty(dxvkFolderPath)) return null;
            var trimmed = dxvkFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed);
        }
    }
}
