#if UNITY_EDITOR
using UnityEngine;
using System.IO;

namespace Kelo.AnimationTextureBaker.Editor
{
    public static class TextureCombiner
    {
        public static Texture2D Combine(Texture2D[] textures, string suffix, string baseName, string folderPath)
        {
            if (textures == null || textures.Length == 0) return null;

            int w = textures[0].width;
            TextureFormat f = textures[0].format;
            int totalHeight = 0;

            for (int i = 0; i < textures.Length; i++)
                totalHeight += textures[i].height + 1; // Extra pixel space for duplicate first row

            var output = new Texture2D(w, totalHeight, f, false);
            int currentY = 0;

            for (int i = 0; i < textures.Length; i++)
            {
                var t = textures[i];
                int h = t.height;
                Graphics.CopyTexture(t, 0, 0, 0, 0, w, h, output, 0, 0, 0, currentY);
                currentY += h;

                // Duplicate first row
                Graphics.CopyTexture(t, 0, 0, 0, 0, w, 1, output, 0, 0, 0, currentY);
                currentY += 1;
            }

            string path = Path.Combine(folderPath, $"{baseName}.tex2D.Combined_{suffix}.asset");
            AssetDatabaseService.CreateAsset(output, path);

            return output;
        }
    }
}
#endif
