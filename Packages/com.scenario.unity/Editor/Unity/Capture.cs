using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Scenario.Editor
{
    /// <summary>Renders a camera (Scene view, Main Camera) to a PNG to use as a reference.</summary>
    public static class Capture
    {
        public const string Folder = "Library/Scenario/captures";

        public static string SceneView(int maxSize = 1536)
        {
            var view = UnityEditor.SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) return null;
            var size = view.position.size;
            return Render(view.camera, size.x, size.y, maxSize, "scene-view");
        }

        public static string MainCamera(int maxSize = 1536)
        {
            var camera = Camera.main;
            if (camera == null) return null;
            return Render(camera, 16, 9, maxSize, "main-camera");
        }

        public static string Render(Camera camera, float aspectWidth, float aspectHeight, int maxSize, string label)
        {
            var aspect = aspectHeight > 0 ? aspectWidth / aspectHeight : 1;
            var width = aspect >= 1 ? maxSize : Mathf.RoundToInt(maxSize * aspect);
            var height = aspect >= 1 ? Mathf.RoundToInt(maxSize / aspect) : maxSize;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Folder);
                var path = Path.GetFullPath(Path.Combine(Folder, $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
                File.WriteAllBytes(path, image.EncodeToPNG());
                return path;
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        /// <summary>A PNG copy of any texture asset (PSD, TGA, EXR...), for uploading.</summary>
        public static string TextureToPng(Texture texture, int maxSize = 2048)
        {
            if (texture == null) return null;
            var scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(texture.width, texture.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var active = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Folder);
                var path = Path.GetFullPath(Path.Combine(Folder, $"{Core.Files.Slug(texture.name)}-{DateTime.Now:HHmmss}.png"));
                File.WriteAllBytes(path, image.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
