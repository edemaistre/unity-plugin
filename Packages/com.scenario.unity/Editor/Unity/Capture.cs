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

        /// <summary>
        /// The open scenes from a temporary camera, three-quarter view on everything that renders, for when
        /// there is no Main Camera and no Scene view (a new empty scene, the Scene view closed, batch mode).
        /// A temporary light is added only when the scenes have none. Both are removed after the render.
        /// </summary>
        public static string Overview(int maxSize = 1536)
        {
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var bounds = new Bounds(Vector3.zero, Vector3.one);
            var first = true;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (first) bounds = renderer.bounds; else bounds.Encapsulate(renderer.bounds);
                first = false;
            }
            var made = new System.Collections.Generic.List<GameObject>();
            try
            {
                var cameraObject = new GameObject("Scenario Overview Camera") { hideFlags = HideFlags.HideAndDontSave };
                made.Add(cameraObject);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.fieldOfView = 40;
                var radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
                var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
                var direction = Quaternion.Euler(25, -35, 0) * Vector3.back;
                cameraObject.transform.position = bounds.center + direction * distance;
                cameraObject.transform.LookAt(bounds.center);
                camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2);
                camera.farClipPlane = distance + radius * 4;
                if (UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length == 0)
                {
                    var lightObject = new GameObject("Scenario Overview Light") { hideFlags = HideFlags.HideAndDontSave };
                    made.Add(lightObject);
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
                }
                return Render(camera, 16, 9, maxSize, "overview");
            }
            finally
            {
                foreach (var go in made) UnityEngine.Object.DestroyImmediate(go);
            }
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
