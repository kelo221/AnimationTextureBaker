using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kelo.AnimationTextureBaker
{
    public class BakerEngine
    {
        public struct VertInfo
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector3 tangent;
            public Vector3 velocity;
        }


        public static int GetFrameCount(AnimationClip clip, int frameRate)
        {
            if (clip == null) return 0;
            return Mathf.NextPowerOfTwo((int)(clip.length * frameRate));
        }



        public static Texture2D ConvertRTToTexture2D(RenderTexture rt)
        {
            TextureFormat format = TextureFormat.RGBAHalf;

            switch (rt.format)
            {
                case RenderTextureFormat.ARGBFloat:
                    format = TextureFormat.RGBAFloat;
                    break;
                case RenderTextureFormat.ARGBHalf:
                    format = TextureFormat.RGBAHalf;
                    break;
                case RenderTextureFormat.ARGBInt:
                    format = TextureFormat.RGBA32;
                    break;
                case RenderTextureFormat.ARGB32:
                    format = TextureFormat.ARGB32;
                    break;
            }

            var tex2d = new Texture2D(rt.width, rt.height, format, false);
            var rect = Rect.MinMaxRect(0f, 0f, tex2d.width, tex2d.height);
            RenderTexture.active = rt;
            tex2d.ReadPixels(rect, 0, 0);
            RenderTexture.active = null;
            return tex2d;
        }
    }
}
