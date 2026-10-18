using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Scenario.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Scenario.Editor.Tests
{
    /// <summary>
    /// Import and placement per output type, on real files made by the test (PNG, WAV,
    /// GLB) and a tiny MP4 fixture, served by the fake Scenario. Everything lands in a
    /// temporary Assets folder that the test removes, and every placed object is undone.
    /// </summary>
    public class ImportTests
    {
        FakeMcp _server;
        McpClient _client;
        string _folder;
        string _projectSettings;
        readonly Dictionary<string, (JObject asset, byte[] bytes)> _assets = new Dictionary<string, (JObject, byte[])>();
        readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _projectSettings = EditorJsonUtility.ToJson(ScenarioProjectSettings.instance);
            ScenarioProjectSettings.instance.ImagesAsSprites = false;
            ScenarioProjectSettings.instance.AddCollider = true;
            _folder = "Assets/ScenarioTestTmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _server = new FakeMcp();
            _server.Tools["asset_get"] = (args, i) => FakeMcp.Result(new JObject { ["asset"] = _assets[(string)args["asset_id"]].asset });
            _server.Tools["asset_download"] = (args, i) => FakeMcp.Result(new JObject { ["url"] = "https://cdn.cloud.scenario.com/assets/" + (string)args["asset_id"] });
            _server.DownloadContent = url => _assets[url.Substring(url.LastIndexOf('/') + 1)].bytes;
            _client = new McpClient(_server, new ApiKeyAuth("key_test", "secret_test"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made.Where(g => g != null)) UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.DeleteAsset(_folder);
            EditorJsonUtility.FromJsonOverwrite(_projectSettings, ScenarioProjectSettings.instance);
        }

        // --- Test files ------------------------------------------------------------------

        void Serve(string id, string kind, string mime, byte[] bytes, string type = "")
        {
            var asset = new JObject { ["id"] = id, ["mimeType"] = mime, ["kind"] = kind, ["metadata"] = new JObject { ["kind"] = kind } };
            if (type.Length > 0) asset["metadata"]["type"] = type;
            _assets[id] = (asset, bytes);
        }

        static byte[] Png(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, width * height).ToArray());
            texture.Apply();
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            return bytes;
        }

        /// <summary>A quarter second of 440 Hz, 16-bit mono PCM.</summary>
        static byte[] Wav()
        {
            const int rate = 8000;
            var samples = rate / 4;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);
                for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
                return stream.ToArray();
            }
        }

        /// <summary>A binary glTF with one triangle.</summary>
        static byte[] Glb()
        {
            var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            var bin = new byte[positions.Length * 4];
            Buffer.BlockCopy(positions, 0, bin, 0, bin.Length);
            var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0,\"name\":\"Triangle\"}]," +
                       "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]," +
                       "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]}]," +
                       "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}],\"buffers\":[{\"byteLength\":36}]}";
            while (json.Length % 4 != 0) json += " ";
            var jsonBytes = Encoding.ASCII.GetBytes(json);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x46546C67); // "glTF"
                writer.Write(2);
                writer.Write(12 + 8 + jsonBytes.Length + 8 + bin.Length);
                writer.Write(jsonBytes.Length);
                writer.Write(0x4E4F534A); // JSON
                writer.Write(jsonBytes);
                writer.Write(bin.Length);
                writer.Write(0x004E4942); // BIN
                writer.Write(bin);
                return stream.ToArray();
            }
        }

        List<ImportedFile> Import(string outputType, string prompt, params string[] ids)
        {
            var result = Run.Sync(Importer.Import(_client, ids, new ImportContext
            {
                OutputType = outputType,
                Prompt = prompt,
                ModelId = "model_test",
                ModelName = "Test model",
                JobId = "job_test",
                Folder = _folder,
                Cu = 4,
            }));
            Assert.IsTrue(result.Ok, result.Ok ? "" : result.Error.Message);
            return result.Value;
        }

        PlaceResult Place(string outputType, List<ImportedFile> files, string prompt, GameObject target, bool create)
        {
            Undo.IncrementCurrentGroup();
            var placed = Placer.Place(outputType, files, prompt, target, create);
            _made.AddRange(placed.Objects);
            return placed;
        }

        // --- Per type ------------------------------------------------------------------------

        [Test]
        public void Image_IsImportedWithProvenance_AndPlacedAsAnUndoableQuad()
        {
            Serve("asset_img00001", "image", "image/png", Png(16, 8, Color.red));
            var files = Import(OutputTypes.Image, "Stone golem", "asset_img00001");
            Assert.AreEqual(1, files.Count);
            var file = files[0];
            StringAssert.StartsWith(_folder + "/stone-golem", file.Path);
            StringAssert.EndsWith(".png", file.Path);
            Assert.AreEqual("image", file.Kind);
            Assert.IsFalse(File.Exists(Path.Combine(Files.Downloads, "asset_img00001.download")), "the download is moved, not copied");
            Assert.GreaterOrEqual(((TextureImporter)AssetImporter.GetAtPath(file.Path)).maxTextureSize, 4096);

            var info = Provenance.Read(file.Path);
            Assert.AreEqual("asset_img00001", info.AssetId);
            Assert.AreEqual("job_test", info.JobId);
            Assert.AreEqual("model_test", info.ModelId);
            Assert.AreEqual("Stone golem", info.Prompt);
            Assert.AreEqual(4, info.Cu);

            var placed = Place(OutputTypes.Image, files, "Stone golem", null, create: true);
            Assert.AreEqual(1, placed.Objects.Count);
            var quad = placed.Objects[0];
            if (EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D)
                Assert.IsNotNull(quad.GetComponent<SpriteRenderer>().sprite);
            else
            {
                Assert.AreEqual(2f * quad.transform.localScale.y, quad.transform.localScale.x, 1e-4, "the quad keeps the image's 2:1 aspect");
                Assert.IsNotNull(quad.GetComponent<Renderer>().sharedMaterial.mainTexture);
            }
            Undo.PerformUndo();
            Assert.IsTrue(quad == null, "one Undo removes the placed image");
        }

        [Test]
        public void Download_FromAnUnexpectedHost_IsRefused()
        {
            Serve("asset_img00002", "image", "image/png", Png(4, 4, Color.blue));
            _server.Tools["asset_download"] = (args, i) => FakeMcp.Result(new JObject { ["url"] = "https://evil.example/asset_img00002" });
            var result = Run.Sync(Importer.Import(_client, new[] { "asset_img00002" }, new ImportContext { Folder = _folder, Prompt = "x" }));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("unexpected host", result.Error.Message);
            Assert.IsFalse(_server.Requests.Any(r => r.Method == "GET"), "nothing was downloaded");
            Assert.IsFalse(Directory.Exists(_folder) && Directory.GetFiles(_folder).Any(f => !f.EndsWith(".meta")));
        }

        [Test]
        public void PbrMaps_GetTheirImportSettings_AndBuildOneMaterialOnTheTarget()
        {
            Serve("asset_alb00001", "image", "image/png", Png(8, 8, Color.gray), "texture-albedo");
            Serve("asset_nrm00001", "image", "image/png", Png(8, 8, new Color(0.5f, 0.5f, 1f)), "texture-normal");
            Serve("asset_rgh00001", "image", "image/png", Png(8, 8, Color.white), "texture-roughness");
            Serve("asset_mtl00001", "image", "image/png", Png(8, 8, Color.black), "texture-metallic");
            var files = Import(OutputTypes.Material, "Mossy stone", "asset_alb00001", "asset_nrm00001", "asset_rgh00001", "asset_mtl00001");
            CollectionAssert.AreEquivalent(new[] { "albedo", "normal", "roughness", "metallic" }, files.Select(f => f.Role));
            TextureImporter Importer_(string role) => (TextureImporter)AssetImporter.GetAtPath(files.First(f => f.Role == role).Path);
            Assert.AreEqual(TextureImporterType.NormalMap, Importer_("normal").textureType);
            Assert.IsFalse(Importer_("roughness").sRGBTexture);
            Assert.IsFalse(Importer_("metallic").sRGBTexture);
            Assert.IsTrue(Importer_("albedo").sRGBTexture);
            Assert.AreEqual("albedo", Importer.Main(files).Role);

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _made.Add(target);
            var placed = Place(OutputTypes.Material, files, "Mossy stone", target, create: false);
            var material = target.GetComponent<Renderer>().sharedMaterial;
            Assert.IsTrue(placed.Asset != null, "the returned material is alive");
            Assert.AreEqual(placed.Asset.GetInstanceID(), material.GetInstanceID());
            StringAssert.StartsWith(_folder + "/", AssetDatabase.GetAssetPath(material));
            Assert.IsNotNull(material.mainTexture, "albedo");
            var normalSlot = material.HasProperty("_BumpMap") ? "_BumpMap" : "_NormalMap";
            Assert.IsNotNull(material.GetTexture(normalSlot), "normal map");
            if (MaterialBuilder.Current != Pipeline.HighDefinition)
                Assert.IsNotNull(material.GetTexture("_MetallicGlossMap"), "packed metallic and smoothness");

            // A finished job's files come back with their roles from provenance.
            var again = Importer.FromPaths(files.Select(f => f.Path));
            CollectionAssert.AreEquivalent(files.Select(f => f.Role), again.Select(f => f.Role));
            CollectionAssert.AreEquivalent(files.Select(f => f.AssetId), again.Select(f => f.AssetId));
        }

        [Test]
        public void Panorama_BecomesTheSkybox_AndCanBeUndone()
        {
            Serve("asset_sky00001", "image", "image/png", Png(64, 32, Color.cyan), "skybox-base-360");
            var files = Import(OutputTypes.Skybox, "Sunset over dunes", "asset_sky00001");
            Assert.AreEqual("panorama", files[0].Role);
            var previous = RenderSettings.skybox;
            try
            {
                var placed = Place(OutputTypes.Skybox, files, "Sunset over dunes", null, create: true);
                var material = (Material)placed.Asset;
                Assert.IsNotNull(material, placed.Message);
                Assert.AreEqual("Skybox/Panoramic", material.shader.name);
                Assert.IsNotNull(material.GetTexture("_MainTex"));
                if (MaterialBuilder.Current != Pipeline.HighDefinition) Assert.AreEqual(material.GetInstanceID(), RenderSettings.skybox.GetInstanceID());
            }
            finally
            {
                RenderSettings.skybox = previous;
            }
        }

        [Test]
        public void Model_IsDownloadedAsGlb_AndPlacedWithACollider()
        {
            Serve("asset_mdl00001", "3d", "model/gltf-binary", Glb());
            var files = Import(OutputTypes.Model3D, "Triangle", "asset_mdl00001");
            var download = _server.ToolArguments[_server.ToolCalls.IndexOf("asset_download")];
            Assert.AreEqual("glb", (string)download["format"]);
            StringAssert.EndsWith(".glb", files[0].Path);
            Assert.AreEqual("3d", files[0].Kind);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(files[0].Path), "glTFast imports the model");

            var placed = Place(OutputTypes.Model3D, files, "Triangle", null, create: true);
            Assert.AreEqual(1, placed.Objects.Count, placed.Message);
            var instance = placed.Objects[0];
            Assert.IsNotNull(instance.GetComponentInChildren<MeshFilter>());
            Assert.IsNotNull(instance.GetComponentInChildren<MeshCollider>(), "AddCollider is on");
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(instance));
            Undo.PerformUndo();
            Assert.IsTrue(instance == null, "one Undo removes the model");
        }

        [Test]
        public void Audio_GetsAnAudioSource()
        {
            Serve("asset_aud00001", "audio", "audio/wav", Wav());
            var files = Import(OutputTypes.Audio, "Door creak", "asset_aud00001");
            StringAssert.EndsWith(".wav", files[0].Path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(files[0].Path);
            Assert.IsNotNull(clip);

            var placed = Place(OutputTypes.Audio, files, "Door creak", null, create: true);
            var source = placed.Objects.Single().GetComponent<AudioSource>();
            Assert.AreSame(clip, source.clip);
            Assert.IsFalse(source.playOnAwake);
        }

        [Test]
        public void Video_GetsAVideoPlayerOnAQuad()
        {
            Serve("asset_vid00001", "video", "video/mp4", File.ReadAllBytes(Path.Combine(Fixtures.Folder, "tiny-video.mp4.bytes")));
            var files = Import(OutputTypes.Video, "Waves", "asset_vid00001");
            StringAssert.EndsWith(".mp4", files[0].Path);
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(files[0].Path);
            Assert.IsNotNull(clip, "Unity imports the MP4");

            var placed = Place(OutputTypes.Video, files, "Waves", null, create: true);
            var player = placed.Objects.Single().GetComponent<VideoPlayer>();
            Assert.AreSame(clip, player.clip);
            Assert.IsTrue(player.isLooping);
            Assert.AreEqual(VideoRenderMode.MaterialOverride, player.renderMode);
        }

        [Test]
        public void KindOfPath_ReadsTheExtension()
        {
            Assert.AreEqual("3d", Importer.KindOfPath("Assets/a.glb"));
            Assert.AreEqual("3d", Importer.KindOfPath("Assets/a.fbx"));
            Assert.AreEqual("audio", Importer.KindOfPath("Assets/a.wav"));
            Assert.AreEqual("video", Importer.KindOfPath("Assets/a.mp4"));
            Assert.AreEqual("image", Importer.KindOfPath("Assets/a.png"));
        }
    }
}
