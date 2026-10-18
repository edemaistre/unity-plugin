using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scenario.Editor.Core
{
    /// <summary>
    /// An output type is a preset over the whole catalog: which capabilities it
    /// shows, its quick picks, presets, and how results land in Unity. It never
    /// limits which model can be used: "All models" reaches every one.
    /// </summary>
    public sealed class OutputType
    {
        public string Id;
        public string Title;
        public string Icon;
        public string Folder;
        public string[] Capabilities;
        /// <summary>Quick picks, the first is the default (chosen 2026-10-18 from recommend and the catalog).</summary>
        public string[] QuickPicks;
        public string Placeholder;
        public string Hint;
        /// <summary>Extra filter for types that are a family of models (Material, Skybox).</summary>
        public System.Func<ModelInfo, bool> Family;
        public string DownloadFormat = "";

        public bool Accepts(ModelInfo model)
        {
            if (Family != null) return Family(model);
            if (Capabilities == null || Capabilities.Length == 0) return true;
            return model.Capabilities.Any(c => Capabilities.Contains(c));
        }
    }

    /// <summary>A typed step: something an asset of a kind can become.</summary>
    public sealed class AssetStep
    {
        public string Id;
        public string Title;
        public string Kind;
        public string OutputType;
        public string Capability;
        public string DefaultModel;
        public string Hint;
    }

    public static class OutputTypes
    {
        public const string Image = "image";
        public const string Video = "video";
        public const string Model3D = "model3d";
        public const string Audio = "audio";
        public const string Material = "material";
        public const string Skybox = "skybox";
        public const string All = "all";

        public static readonly List<OutputType> List = new List<OutputType>
        {
            new OutputType
            {
                Id = Image, Title = "Image", Icon = "image", Folder = "Image", Capabilities = new[] { "txt2img", "img2img" },
                QuickPicks = new[]
                {
                    "model_openai-gpt-image-2-5-sunburst", "model_openai-gpt-image-2-5-flare", "model_google-gemini-nano-banana-2-1",
                    "model_bytedance-seedream-5-0-pro", "model_retrodiffusion-plus",
                },
                Placeholder = "A stylized wooden treasure chest, game prop, clean background",
                Hint = "Sprites, icons, textures, concept art. Lands on the selected sprite, image or renderer.",
            },
            new OutputType
            {
                Id = Video, Title = "Video", Icon = "video", Folder = "Video", Capabilities = new[] { "txt2video", "img2video", "video2video" },
                QuickPicks = new[] { "model_minimax-h3", "model_google-omni-1-1-flash", "model_alibaba-wan-3-0", "model_bytedance-seedance-2-5" },
                Placeholder = "Slow dolly shot through a misty pine forest at dawn",
                Hint = "Clips for cinematics, trailers and backgrounds. Lands on the selected VideoPlayer or a new screen.",
            },
            new OutputType
            {
                Id = Model3D, Title = "3D", Icon = "cube", Folder = "Models", Capabilities = new[] { "txt23d", "img23d", "3d23d" }, DownloadFormat = "glb",
                QuickPicks = new[]
                {
                    "model_meshy-7-1-txt23d", "model_meshy-7-1-img23d", "model_rodin-hyper3d-v2-5-text-to-3d", "model_hunyuan-3d-pro-3-1-i23d",
                    "model_tripo-p2-text-to-3d", "model_meshy-t2-5-smart-topology",
                },
                Placeholder = "A stylized wooden treasure chest, game-ready prop",
                Hint = "Textured GLB models, imported as prefabs and placed at the Scene view pivot.",
            },
            new OutputType
            {
                Id = Audio, Title = "Audio", Icon = "audio", Folder = "Audio", Capabilities = new[] { "txt2audio", "audio2audio", "video2audio" },
                QuickPicks = new[]
                {
                    "model_mirelo-sfx-2-0-text-to-audio", "model_elevenlabs-sound-effects-v2", "model_sonilo-v1-1-text-to-sound-effects",
                    "model_elevenlabs-music-v2-5", "model_google-gemini-3-8-flash-tts",
                },
                Placeholder = "Short metallic sword swing with a whoosh",
                Hint = "Sound effects, music and voice. Lands on the selected AudioSource or a new one.",
            },
            new OutputType
            {
                Id = Material, Title = "Material", Icon = "material", Folder = "Materials", Capabilities = new[] { "txt2img", "img2img" },
                QuickPicks = new[] { "model_patina-material", "model_patina", "model_patina-material-extract" },
                Family = m => m.Id.StartsWith("model_patina"),
                Placeholder = "Weathered copper plates with green patina",
                Hint = "Tileable PBR materials for your render pipeline, set on the selected renderers.",
            },
            new OutputType
            {
                Id = Skybox, Title = "Skybox", Icon = "skybox", Folder = "Skyboxes", Capabilities = new[] { "txt2img", "img2img" },
                QuickPicks = new[] { "model_scenario-skybox-gpt", "model_scenario-skybox-flux", "model_hunyuan-world-image-to-skybox" },
                Family = m => m.Id.Contains("skybox") && !m.Capabilities.Contains("img23d"),
                Placeholder = "Alien desert at golden hour, twin moons, soft haze",
                Hint = "360 panoramas, set as the scene's skybox.",
            },
            new OutputType
            {
                Id = All, Title = "All models", Icon = "grid", Folder = "Generated", Capabilities = new string[0], QuickPicks = new string[0],
                Placeholder = "Describe what you want", Hint = "Every model in the catalog. Results land by their kind.",
            },
        };

        public static OutputType Get(string id) => List.FirstOrDefault(t => t.Id == id) ?? List[0];

        /// <summary>The output type a model naturally belongs to.</summary>
        public static string TypeFor(ModelInfo model)
        {
            if (model == null) return Image;
            if (model.Id.StartsWith("model_patina")) return Material;
            if (model.Id.Contains("skybox") && !model.Capabilities.Contains("img23d")) return Skybox;
            var caps = model.Capabilities;
            if (caps.Any(c => c.EndsWith("23d"))) return Model3D;
            if (caps.Any(c => c.EndsWith("2video"))) return Video;
            if (caps.Any(c => c.EndsWith("2audio"))) return Audio;
            if (caps.Any(c => c.EndsWith("2img") || c == "img2img")) return Image;
            return All;
        }

        /// <summary>The output type for a downloaded asset kind (image, video, audio, 3d).</summary>
        public static string TypeForKind(string kind)
        {
            switch (kind)
            {
                case "video": return Video;
                case "audio": return Audio;
                case "3d": return Model3D;
                case "image": return Image;
                default: return All;
            }
        }

        /// <summary>Values set on top of the schema defaults, per model.</summary>
        public static JObject Presets(string modelId)
        {
            switch (modelId)
            {
                case "model_rodin-hyper3d-v2-5-text-to-3d":
                case "model_rodin-hyper3d-v2-5":
                    return new JObject { ["material"] = "PBR" };
                case "model_hunyuan-3d-pro-3-1-i23d":
                    return new JObject { ["enablePbr"] = true };
                case "model_sonilo-v1-1-text-to-sound-effects":
                    return new JObject { ["audioFormat"] = "wav", ["duration"] = 2 };
                case "model_scenario-skybox-flux":
                    return new JObject { ["numOutputs"] = 1 };
                default:
                    return new JObject();
            }
        }

        /// <summary>Field values Unity can import (it has no FLAC or AAC importer).</summary>
        public static readonly Dictionary<string, JToken[]> Restrict = new Dictionary<string, JToken[]>
        {
            { "audioFormat", new JToken[] { "wav", "mp3", "ogg" } },
            { "outputFormat", new JToken[] { "mp4", "png", "jpg", "jpeg", "webp", "wav", "mp3", "ogg", "glb" } },
        };

        /// <summary>Typed steps per asset kind. Defaults checked against the live catalog on 2026-10-18.</summary>
        public static readonly List<AssetStep> Steps = new List<AssetStep>
        {
            new AssetStep { Id = "edit", Title = "Edit with a prompt", Kind = "image", OutputType = Image, Capability = "img2img", DefaultModel = "model_openai-gpt-image-2-5-sunburst" },
            new AssetStep { Id = "remove-bg", Title = "Remove background", Kind = "image", OutputType = Image, Capability = "img2img", DefaultModel = "model_birefnet-background-removal" },
            new AssetStep { Id = "upscale", Title = "Upscale", Kind = "image", OutputType = Image, Capability = "img2img", DefaultModel = "model_clarity-pro-upscaler" },
            new AssetStep { Id = "to-3d", Title = "Make a 3D model", Kind = "image", OutputType = Model3D, Capability = "img23d", DefaultModel = "model_meshy-7-1-img23d" },
            new AssetStep { Id = "animate", Title = "Animate to video", Kind = "image", OutputType = Video, Capability = "img2video", DefaultModel = "model_minimax-h3" },
            new AssetStep { Id = "to-material", Title = "Make a PBR material", Kind = "image", OutputType = Material, Capability = "img2img", DefaultModel = "model_patina" },
            new AssetStep { Id = "to-skybox", Title = "Make a skybox", Kind = "image", OutputType = Skybox, Capability = "img2img", DefaultModel = "model_hunyuan-world-image-to-skybox" },
            new AssetStep { Id = "vectorize", Title = "Vectorize", Kind = "image", OutputType = Image, Capability = "img2img", DefaultModel = "model_sc-vectorizer" },
            new AssetStep { Id = "pixelate", Title = "Pixelate", Kind = "image", OutputType = Image, Capability = "img2img", DefaultModel = "model_sc-pixelate" },
            new AssetStep { Id = "retexture", Title = "Retexture", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_meshy-7-retexture" },
            new AssetStep { Id = "retopo", Title = "Retopology", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_tripo-retopology-v3" },
            new AssetStep { Id = "uv", Title = "UV unwrap", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_tripo-smart-uv" },
            new AssetStep { Id = "rig", Title = "Rig a character", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_meshy-rigging" },
            new AssetStep { Id = "edit-3d", Title = "Edit with a prompt", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_scenario-claude-opus-5-5-3d" },
            new AssetStep { Id = "parts", Title = "Split into parts", Kind = "3d", OutputType = Model3D, Capability = "3d23d", DefaultModel = "model_tripo-segmentation-v2" },
            new AssetStep { Id = "upscale-video", Title = "Upscale", Kind = "video", OutputType = Video, Capability = "video2video", DefaultModel = "model_kandinsky-6-vsr-video-upscale" },
            new AssetStep { Id = "add-sound", Title = "Add sound", Kind = "video", OutputType = Audio, Capability = "video2audio", DefaultModel = "model_mirelo-sfx-2-0-video-to-audio" },
            new AssetStep { Id = "restyle", Title = "Restyle", Kind = "video", OutputType = Video, Capability = "video2video", DefaultModel = "model_bytedance-seedance-2-5" },
            new AssetStep { Id = "voice-change", Title = "Change the voice", Kind = "audio", OutputType = Audio, Capability = "audio2audio", DefaultModel = "model_elevenlabs-voice-changer" },
            new AssetStep { Id = "isolate", Title = "Isolate the voice", Kind = "audio", OutputType = Audio, Capability = "audio2audio", DefaultModel = "model_elevenlabs-voice-isolator" },
        };

        public static IEnumerable<AssetStep> StepsFor(string kind) => Steps.Where(s => s.Kind == kind);

        public static AssetStep Step(string id, string kind = null) => Steps.FirstOrDefault(s => s.Id == id && (kind == null || s.Kind == kind));
    }
}
