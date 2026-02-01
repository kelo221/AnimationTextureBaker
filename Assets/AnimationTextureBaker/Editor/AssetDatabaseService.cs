#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using UnityEngine;

namespace Kelo.AnimationTextureBaker.Editor
{
    public static class AssetDatabaseService
    {
        public static string FixPath(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Untitled";
            
            // Replace invalid characters with an underscore
            string invalidChars = new string(Path.GetInvalidPathChars());
            string validPath = System.Text.RegularExpressions.Regex.Replace(s, $"[{System.Text.RegularExpressions.Regex.Escape(invalidChars)}]", "_");
            
            // Combine multiple path separators into a single one
            string separator = Path.DirectorySeparatorChar.ToString();
            string doubleSeparator = separator + separator;
            while (validPath.Contains(doubleSeparator))
            {
                validPath = validPath.Replace(doubleSeparator, separator);
            }
            
            return validPath.Trim();
        }

        public static string EnsureFolder(string parent, string folderName)
        {
            string path = Path.Combine(parent, folderName);
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
            return path;
        }

        public static void CreateAsset(Object asset, string path)
        {
            AssetDatabase.CreateAsset(asset, path);
        }

        public static void SaveAndRefresh()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
#endif
