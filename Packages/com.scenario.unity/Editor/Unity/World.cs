using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Scenario.Editor
{
    /// <summary>
    /// Builds walkable ground and dresses it: a Terrain from a heightmap image (or gentle
    /// noise) with ground materials painted by height and slope, models scattered on the
    /// ground with colliders, and helpers that size a model and sit it on the ground.
    /// Every object created is one Undo step for the caller to group.
    /// </summary>
    public static class World
    {
        public const string DefaultFolder = "Assets/Scenario/World";
        /// <summary>Where terrain data and layers are saved when a spec names no folder (tests point it elsewhere).</summary>
        internal static string Root = DefaultFolder;

        // --- Terrain ---------------------------------------------------------------------

        public sealed class Layer
        {
            /// <summary>A material (its base and normal maps are used) or a texture.</summary>
            public string Source = "";
            public string Normal = "";
            /// <summary>Meters covered by one tile of the texture.</summary>
            public float Tile = 8f;
            /// <summary>Where this layer shows, as a share of the terrain's height (0 to 1) and in degrees of slope.</summary>
            public float MinHeight, MaxHeight = 1f, MinSlope, MaxSlope = 90f;
        }

        public sealed class Flat
        {
            public Vector2 Center;
            public float Radius = 10f;
            /// <summary>World height of the flat area; NaN keeps the ground height at the center.</summary>
            public float Height = float.NaN;
        }

        public sealed class TerrainSpec
        {
            public string Name = "Terrain";
            /// <summary>A grayscale image asset (white is high). Empty: rolling noise.</summary>
            public string Heightmap = "";
            public Vector3 Size = new Vector3(200, 30, 200);
            public Vector3 Center = Vector3.zero;
            public int Resolution = 513;
            public int Smoothing = 2;
            /// <summary>Share of the width over which the edges fall to the bottom (0: none). Makes an island or a plateau.</summary>
            public float EdgeFalloff;
            public int Seed = 1;
            public List<Flat> Flats = new List<Flat>();
            public List<Layer> Layers = new List<Layer>();
            public string Folder = "";
        }

        public sealed class TerrainResult
        {
            public Terrain Terrain;
            public float MinHeight, MaxHeight;
            public List<string> Notes = new List<string>();
        }

        public static TerrainResult CreateTerrain(TerrainSpec spec)
        {
            var resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(spec.Resolution, 33, 4097) - 1) + 1;
            var size = new Vector3(Mathf.Max(1, spec.Size.x), Mathf.Max(0.1f, spec.Size.y), Mathf.Max(1, spec.Size.z));
            var result = new TerrainResult();
            var heights = spec.Heightmap.Length > 0 ? ReadHeightmap(spec.Heightmap, resolution) : Noise(resolution, spec.Seed);
            for (var i = 0; i < spec.Smoothing; i++) Blur(heights);
            if (spec.EdgeFalloff > 0) Falloff(heights, Mathf.Clamp01(spec.EdgeFalloff));

            var origin = spec.Center - new Vector3(size.x / 2, 0, size.z / 2);
            foreach (var flat in spec.Flats) Flatten(heights, flat, origin, size);

            var folder = Folder(spec.Folder, spec.Name);
            var data = new TerrainData { heightmapResolution = resolution };
            data.size = size;
            data.SetHeights(0, 0, heights);
            AssetDatabase.CreateAsset(data, Unique(folder + "/" + Core.Files.Slug(spec.Name) + ".asset"));

            if (spec.Layers.Count > 0)
            {
                data.alphamapResolution = Mathf.Clamp(resolution - 1, 16, 1024);
                data.terrainLayers = spec.Layers.Select((l, i) => MakeLayer(l, folder, spec.Name, i, result.Notes)).ToArray();
                Paint(data, spec.Layers);
            }

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = spec.Name;
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            var template = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
            if (template != null) terrain.materialTemplate = template;
            Undo.RegisterCreatedObjectUndo(go, "Create terrain");
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();

            result.Terrain = terrain;
            var all = heights.Cast<float>().ToArray();
            result.MinHeight = origin.y + all.Min() * size.y;
            result.MaxHeight = origin.y + all.Max() * size.y;
            return result;
        }

        static float[,] ReadHeightmap(string assetPath, int resolution)
        {
            var source = LoadPixels(assetPath);
            try
            {
                var heights = new float[resolution, resolution];
                // Terrain heights are [row = z, column = x]; image row 0 is the bottom, which is -z.
                for (var z = 0; z < resolution; z++)
                for (var x = 0; x < resolution; x++)
                {
                    var c = source.GetPixelBilinear(x / (float)(resolution - 1), z / (float)(resolution - 1));
                    heights[z, x] = Mathf.Clamp01(c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f);
                }
                return heights;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// The image's own pixels: PNG and JPEG files are decoded from disk (16-bit PNGs keep their
        /// precision, and the imported texture's compression never reaches the heights); other
        /// formats are copied from the imported texture.
        /// </summary>
        static Texture2D LoadPixels(string assetPath)
        {
            var extension = Path.GetExtension(assetPath).ToLowerInvariant();
            if ((extension == ".png" || extension == ".jpg" || extension == ".jpeg") && File.Exists(assetPath))
            {
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA64, false, true);
                if (decoded.LoadImage(File.ReadAllBytes(assetPath))) return decoded;
                Object.DestroyImmediate(decoded);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null) throw new ArgumentException($"No image at {assetPath}.");
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static float[,] Noise(int resolution, int seed)
        {
            var random = new System.Random(seed);
            var ox = (float)random.NextDouble() * 1000f;
            var oz = (float)random.NextDouble() * 1000f;
            var heights = new float[resolution, resolution];
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                float u = (float)x / resolution, v = (float)z / resolution, value = 0, amplitude = 0.5f, frequency = 3f;
                for (var octave = 0; octave < 4; octave++)
                {
                    value += amplitude * Mathf.PerlinNoise(ox + u * frequency, oz + v * frequency);
                    amplitude *= 0.5f;
                    frequency *= 2f;
                }
                heights[z, x] = Mathf.Clamp01(value * 0.6f + 0.1f);
            }
            return heights;
        }

        static void Blur(float[,] heights)
        {
            var n = heights.GetLength(0);
            var copy = (float[,])heights.Clone();
            for (var z = 1; z < n - 1; z++)
            for (var x = 1; x < n - 1; x++)
            {
                var sum = 0f;
                for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                    sum += copy[z + dz, x + dx];
                heights[z, x] = sum / 9f;
            }
        }

        static void Falloff(float[,] heights, float share)
        {
            var n = heights.GetLength(0);
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var edge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(z, n - 1 - z)) / (float)(n - 1);
                var t = Mathf.Clamp01(edge / share);
                heights[z, x] *= t * t * (3 - 2 * t);
            }
        }

        static void Flatten(float[,] heights, Flat flat, Vector3 origin, Vector3 size)
        {
            var n = heights.GetLength(0);
            float Column(float worldX) => (worldX - origin.x) / size.x * (n - 1);
            float Row(float worldZ) => (worldZ - origin.z) / size.z * (n - 1);
            int cx = Mathf.Clamp(Mathf.RoundToInt(Column(flat.Center.x)), 0, n - 1), cz = Mathf.Clamp(Mathf.RoundToInt(Row(flat.Center.y)), 0, n - 1);
            var level = float.IsNaN(flat.Height) ? heights[cz, cx] : Mathf.Clamp01((flat.Height - origin.y) / size.y);
            var blend = flat.Radius * 0.6f;
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var worldX = origin.x + x / (float)(n - 1) * size.x;
                var worldZ = origin.z + z / (float)(n - 1) * size.z;
                var distance = Vector2.Distance(new Vector2(worldX, worldZ), flat.Center);
                if (distance >= flat.Radius + blend) continue;
                var t = distance <= flat.Radius ? 1f : 1f - (distance - flat.Radius) / blend;
                t = t * t * (3 - 2 * t);
                heights[z, x] = Mathf.Lerp(heights[z, x], level, t);
            }
        }

        static TerrainLayer MakeLayer(Layer layer, string folder, string name, int index, List<string> notes)
        {
            var terrainLayer = new TerrainLayer { tileSize = new Vector2(layer.Tile, layer.Tile) };
            var material = AssetDatabase.LoadAssetAtPath<Material>(layer.Source);
            if (material != null)
            {
                terrainLayer.diffuseTexture = FirstTexture(material, "_BaseMap", "_MainTex", "_BaseColorMap");
                terrainLayer.normalMapTexture = FirstTexture(material, "_BumpMap", "_NormalMap");
            }
            else
            {
                terrainLayer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(layer.Source);
                if (terrainLayer.diffuseTexture == null) notes.Add($"Layer {index + 1}: no material or texture at {layer.Source}.");
            }
            if (layer.Normal.Length > 0) terrainLayer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(layer.Normal);
            AssetDatabase.CreateAsset(terrainLayer, Unique($"{folder}/{Core.Files.Slug(name)}-layer-{index + 1}.terrainlayer"));
            return terrainLayer;
        }

        static Texture2D FirstTexture(Material material, params string[] names)
        {
            foreach (var property in names)
                if (material.HasProperty(property) && material.GetTexture(property) is Texture2D texture)
                    return texture;
            return material.mainTexture as Texture2D;
        }

        /// <summary>Each cell takes the last layer whose height and slope rules it meets, with soft edges; the first layer is the base.</summary>
        static void Paint(TerrainData data, List<Layer> layers)
        {
            var n = data.alphamapResolution;
            var maps = new float[n, n, layers.Count];
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1), v = z / (float)(n - 1);
                var height = data.GetInterpolatedHeight(u, v) / data.size.y;
                var slope = data.GetSteepness(u, v);
                var remaining = 1f;
                for (var i = layers.Count - 1; i >= 1 && remaining > 0; i--)
                {
                    var l = layers[i];
                    var weight = Mathf.Min(Band(height, l.MinHeight, l.MaxHeight, 0.03f, 1f), Band(slope, l.MinSlope, l.MaxSlope, 4f, 90f)) * remaining;
                    maps[z, x, i] = weight;
                    remaining -= weight;
                }
                maps[z, x, 0] += remaining;
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>1 inside [min, max], 0 outside, with a ramp of width soft; a bound at 0 or at top is open.</summary>
        static float Band(float value, float min, float max, float soft, float top)
        {
            var low = min <= 0 ? 1f : Mathf.Clamp01((value - min) / soft + 0.5f);
            var high = max >= top ? 1f : Mathf.Clamp01((max - value) / soft + 0.5f);
            return Mathf.Min(low, high);
        }

        // --- Scatter ---------------------------------------------------------------------

        public sealed class ScatterSpec
        {
            /// <summary>A prefab or model asset path, or a scene object to copy.</summary>
            public string Source = "";
            public int Count = 10;
            public Vector2 Center;
            public Vector2 Area = new Vector2(50, 50);
            /// <summary>Ground object: only hits on its colliders count. Empty: any collider outside the scattered group.</summary>
            public string On = "";
            /// <summary>Height of each copy in meters, picked between the two. Zero keeps the source's size.</summary>
            public Vector2 Height;
            public Vector2 Scale = Vector2.one;
            public bool RandomYaw = true;
            /// <summary>Most degrees a copy leans to follow the ground.</summary>
            public float Tilt;
            public float MaxSlope = 35f;
            /// <summary>Meters between copies; 0 picks 80% of a copy's footprint.</summary>
            public float MinDistance;
            public List<(Vector2 center, float radius)> KeepClear = new List<(Vector2, float)>();
            /// <summary>mesh, box or none.</summary>
            public string Collider = "mesh";
            public int Seed = 1;
            public string Group = "";
        }

        public sealed class ScatterResult
        {
            public GameObject Group;
            public int Placed;
            public int SkippedSlope, SkippedClear, SkippedNoGround, SkippedCrowded;
        }

        public static ScatterResult Scatter(ScatterSpec spec)
        {
            var template = Template(spec.Source, out var owned);
            try
            {
                var result = new ScatterResult();
                var groupName = spec.Group.Length > 0 ? spec.Group : template.name + " (scattered)";
                var group = new GameObject(groupName);
                Undo.RegisterCreatedObjectUndo(group, "Scatter");
                result.Group = group;
                var ground = spec.On.Length > 0 ? Placer.FindByPath(spec.On) : null;
                if (spec.On.Length > 0 && ground == null) throw new ArgumentException($"No object at \"{spec.On}\".");
                var groundColliders = ground != null ? new HashSet<Collider>(ground.GetComponentsInChildren<Collider>()) : null;
                Physics.SyncTransforms();

                var random = new System.Random(spec.Seed);
                float Range(Vector2 r) => r.y > r.x ? Mathf.Lerp(r.x, r.y, (float)random.NextDouble()) : r.x;
                var baseBounds = BoundsOf(template);
                var footprint = Mathf.Max(baseBounds.size.x, baseBounds.size.z);
                var placed = new List<(Vector2 at, float radius)>();
                var attempts = Mathf.Max(30, spec.Count * 30);
                for (var attempt = 0; attempt < attempts && result.Placed < spec.Count; attempt++)
                {
                    var at = spec.Center + new Vector2(((float)random.NextDouble() - 0.5f) * spec.Area.x, ((float)random.NextDouble() - 0.5f) * spec.Area.y);
                    var yaw = spec.RandomYaw ? (float)random.NextDouble() * 360f : template.transform.eulerAngles.y;
                    var height = Range(spec.Height);
                    var factor = height > 0 && baseBounds.size.y > 0 ? height / baseBounds.size.y : Range(spec.Scale);
                    var radius = footprint * factor * 0.5f;
                    var spacing = spec.MinDistance > 0 ? spec.MinDistance : radius * 1.6f;
                    if (spec.KeepClear.Any(k => Vector2.Distance(at, k.center) < k.radius + radius))
                    {
                        result.SkippedClear++;
                        continue;
                    }
                    if (placed.Any(p => Vector2.Distance(at, p.at) < Mathf.Max(spacing, p.radius + radius) * (spec.MinDistance > 0 ? 1f : 0.8f)))
                    {
                        result.SkippedCrowded++;
                        continue;
                    }
                    if (!GroundHit(new Vector3(at.x, 0, at.y), groundColliders, group.transform, out var hit))
                    {
                        result.SkippedNoGround++;
                        continue;
                    }
                    var slope = Vector3.Angle(hit.normal, Vector3.up);
                    if (slope > spec.MaxSlope)
                    {
                        result.SkippedSlope++;
                        continue;
                    }

                    var copy = Instance(spec.Source, template);
                    Undo.RegisterCreatedObjectUndo(copy, "Scatter");
                    copy.name = template.name + " " + (result.Placed + 1);
                    copy.transform.SetParent(group.transform, true);
                    var lean = spec.Tilt > 0 ? Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, hit.normal), spec.Tilt) : Quaternion.identity;
                    copy.transform.rotation = lean * Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(template.transform.eulerAngles.x, 0, template.transform.eulerAngles.z);
                    copy.transform.localScale = template.transform.lossyScale * factor;
                    // Sink a little so no edge floats on a slope.
                    var sink = 0.03f * Mathf.Max(height, baseBounds.size.y * factor) + radius * Mathf.Tan(Mathf.Min(slope, 60f) * Mathf.Deg2Rad) * 0.5f;
                    SitOn(copy, hit.point, sink);
                    AddColliders(copy, spec.Collider);
                    placed.Add((at, radius));
                    result.Placed++;
                }
                Physics.SyncTransforms();
                return result;
            }
            finally
            {
                if (owned) Object.DestroyImmediate(template);
            }
        }

        /// <summary>The object copies are made from: a scene object as is, or a hidden instance of an asset.</summary>
        static GameObject Template(string source, out bool owned)
        {
            owned = false;
            if (source.StartsWith("Assets/") || source.StartsWith("Packages/"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                if (asset == null) throw new ArgumentException($"No prefab or model at {source}.");
                owned = true;
                var template = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                template.hideFlags = HideFlags.HideAndDontSave;
                return template;
            }
            var go = Placer.FindByPath(source);
            if (go == null) throw new ArgumentException($"No object at \"{source}\".");
            return go;
        }

        static GameObject Instance(string source, GameObject template)
        {
            if (source.StartsWith("Assets/") || source.StartsWith("Packages/"))
                return (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source));
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(template);
            if (prefab != null && PrefabUtility.GetOutermostPrefabInstanceRoot(template) == template)
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            return Object.Instantiate(template);
        }

        static void AddColliders(GameObject go, string kind)
        {
            if (kind == "none") return;
            if (go.GetComponentInChildren<Collider>() != null) return;
            if (kind == "box")
            {
                var bounds = BoundsOf(go);
                var box = Undo.AddComponent<BoxCollider>(go);
                box.center = go.transform.InverseTransformPoint(bounds.center);
                var scale = go.transform.lossyScale;
                box.size = new Vector3(bounds.size.x / Mathf.Abs(scale.x), bounds.size.y / Mathf.Abs(scale.y), bounds.size.z / Mathf.Abs(scale.z));
                return;
            }
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        // --- Ground and size ---------------------------------------------------------------

        /// <summary>World bounds of an object's renderers (colliders when it has none).</summary>
        public static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                return bounds;
            }
            var colliders = root.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                var bounds = colliders[0].bounds;
                foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
                return bounds;
            }
            return new Bounds(root.transform.position, Vector3.zero);
        }

        /// <summary>Scales an object uniformly so its bounds are this many meters tall.</summary>
        public static void FitHeight(GameObject go, float meters)
        {
            var bounds = BoundsOf(go);
            if (bounds.size.y <= 0 || meters <= 0) return;
            Undo.RecordObject(go.transform, "Set size");
            go.transform.localScale *= meters / bounds.size.y;
        }

        /// <summary>Sits an object on the ground under it, its lowest point at the ground. False when there is no ground below.</summary>
        public static bool Ground(GameObject go, float sink = 0f, GameObject on = null)
        {
            Physics.SyncTransforms();
            var colliders = on != null ? new HashSet<Collider>(on.GetComponentsInChildren<Collider>()) : null;
            var bounds = BoundsOf(go);
            if (!GroundHit(bounds.center, colliders, go.transform, out var hit)) return false;
            Undo.RecordObject(go.transform, "Put on the ground");
            SitOn(go, hit.point, sink);
            return true;
        }

        /// <summary>Ground height at a point, or NaN.</summary>
        public static float HeightAt(Vector3 point, GameObject on = null)
        {
            Physics.SyncTransforms();
            var colliders = on != null ? new HashSet<Collider>(on.GetComponentsInChildren<Collider>()) : null;
            return GroundHit(point, colliders, null, out var hit) ? hit.point.y : float.NaN;
        }

        static void SitOn(GameObject go, Vector3 point, float sink)
        {
            var bounds = BoundsOf(go);
            var offset = go.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            go.transform.position = point + offset - Vector3.up * sink;
        }

        /// <summary>The highest hit straight below (from far above) on the ground colliders, skipping anything under ignore.</summary>
        static bool GroundHit(Vector3 point, HashSet<Collider> only, Transform ignore, out RaycastHit best)
        {
            best = default;
            var found = false;
            var origin = new Vector3(point.x, 10000f, point.z);
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 20000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (only != null && !only.Contains(hit.collider)) continue;
                if (ignore != null && hit.transform.IsChildOf(ignore)) continue;
                if (hit.collider.GetComponentInParent<Scenario.ScenarioPlayer>() != null) continue;
                if (!found || hit.point.y > best.point.y)
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        // --- Files -----------------------------------------------------------------------

        static string Folder(string root, string name)
        {
            var folder = (root.Length > 0 ? root : Root).TrimEnd('/') + "/" + Core.Files.Slug(name);
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            return folder;
        }

        static string Unique(string path) => AssetDatabase.GenerateUniqueAssetPath(path);
    }
}
