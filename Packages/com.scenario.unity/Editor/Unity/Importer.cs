using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Scenario.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace Scenario.Editor
{
    public sealed class ImportedFile
    {
        public string Path = "";
        public string AssetId = "";
        public string Role = "main";
        public string Kind = "";
    }

    /// <summary>What a set of Scenario assets is, so it lands in the right folder with its provenance.</summary>
    public sealed class ImportContext
    {
        public string OutputType = OutputTypes.Image;
        public string Prompt = "";
        public string ModelId = "";
        public string ModelName = "";
        public string JobId = "";
        public JObject Parameters = new JObject();
        /// <summary>Overrides the dated type folder.</summary>
        public string Folder = "";
        public double Cu;
    }

    /// <summary>
    /// Downloads Scenario assets into the project and configures their import.
    /// Must be started on the main thread: AssetDatabase calls happen after each await.
    /// </summary>
    public static class Importer
    {
        static readonly HashSet<string> UnityImageFormats = new HashSet<string> { "png", "jpg", "jpeg", "tga", "psd", "exr", "hdr", "tif", "tiff", "bmp", "gif" };

        public static async Task<Result<List<ImportedFile>>> Import(McpClient client, IList<string> assetIds, ImportContext context, CancellationToken cancel = default)
        {
            var settings = ScenarioProjectSettings.instance;
            var typeFolder = OutputTypes.Get(context.OutputType).Folder;
            var folder = string.IsNullOrEmpty(context.Folder)
                ? Files.OutputFolder(settings.OutputRoot, typeFolder, DateTime.Now, settings.DatedFolders)
                : context.Folder;
            var downloads = new List<(string temp, string ext, JObject asset, string assetId)>();
            foreach (var assetId in assetIds)
            {
                var asset = await client.GetAsset(assetId, cancel);
                if (!asset.Ok) return asset.Cast<List<ImportedFile>>();
                var kind = Files.KindOf(asset.Value);
                var mime = Json.Str(asset.Value, "mimeType");
                var format = "";
                if (kind == "3d") format = "glb";
                else if (kind == "image" && !UnityImageFormats.Contains(Files.ExtensionForMime(mime))) format = "png";
                var url = await client.DownloadUrl(assetId, format, cancel);
                if (!url.Ok) return url.Cast<List<ImportedFile>>();
                if (!Files.IsScenarioCdn(url.Value))
                    return Result<List<ImportedFile>>.Fail(ErrorKind.Local, "Refused to download from an unexpected host.");
                var temp = System.IO.Path.GetFullPath(System.IO.Path.Combine(Files.Downloads, assetId + ".download"));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(temp));
                var reply = await client.Transport.DownloadAsync(url.Value, temp, 600, null, cancel);
                if (!reply.IsSuccess)
                    return Result<List<ImportedFile>>.Fail(reply.Error == "canceled" ? ErrorKind.Canceled : ErrorKind.Transport,
                        $"Downloading {assetId} failed ({(reply.Answered ? "HTTP " + reply.Status : reply.Error)}).");
                var ext = Files.SniffFile(temp);
                if (ext.Length == 0) ext = Files.ExtensionForMime(mime);
                if (ext.Length == 0) ext = kind == "3d" ? "glb" : "bin";
                downloads.Add((temp, ext, asset.Value, assetId));
            }

            Directory.CreateDirectory(folder);
            var imported = new List<ImportedFile>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (temp, ext, asset, assetId) in downloads)
                {
                    var role = Files.RoleFor(asset);
                    var path = AssetDatabase.GenerateUniqueAssetPath(Files.BasePath(folder, context.Prompt, assetId, role) + "." + ext);
                    File.Move(temp, path);
                    imported.Add(new ImportedFile { Path = path, AssetId = assetId, Role = role, Kind = Files.KindOf(asset) });
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            foreach (var file in imported)
            {
                AssetDatabase.ImportAsset(file.Path, ImportAssetOptions.ForceSynchronousImport);
                Configure(file, context.OutputType, settings);
                Provenance.Write(file.Path, new ProvenanceInfo
                {
                    AssetId = file.AssetId,
                    JobId = context.JobId,
                    ModelId = context.ModelId,
                    ModelName = context.ModelName,
                    Prompt = context.Prompt,
                    OutputType = context.OutputType,
                    Role = file.Role,
                    Parameters = context.Parameters ?? new JObject(),
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    TeamId = client.TeamId,
                    ProjectId = client.ProjectId,
                    Cu = context.Cu,
                });
            }
            return Result<List<ImportedFile>>.Success(imported);
        }

        static void Configure(ImportedFile file, string outputType, ScenarioProjectSettings settings)
        {
            if (!(AssetImporter.GetAtPath(file.Path) is TextureImporter texture)) return;
            var changed = false;
            switch (file.Role)
            {
                case "normal":
                    texture.textureType = TextureImporterType.NormalMap;
                    changed = true;
                    break;
                case "roughness":
                case "metallic":
                case "ao":
                case "height":
                    texture.sRGBTexture = false;
                    changed = true;
                    break;
            }
            if (outputType == OutputTypes.Image && (settings.ImagesAsSprites || EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D))
            {
                texture.textureType = TextureImporterType.Sprite;
                texture.spriteImportMode = SpriteImportMode.Single;
                texture.alphaIsTransparency = true;
                texture.spritePixelsPerUnit = Mathf.Max(1, settings.PixelsPerUnit);
                changed = true;
            }
            if (outputType == OutputTypes.Image && texture.filterMode != settings.ImageFilter)
            {
                texture.filterMode = settings.ImageFilter;
                changed = true;
            }
            if (texture.maxTextureSize < 4096)
            {
                texture.maxTextureSize = 4096;
                changed = true;
            }
            if (changed) texture.SaveAndReimport();
        }

        /// <summary>Imported files of a finished job, rebuilt from their paths and provenance.</summary>
        public static List<ImportedFile> FromPaths(IEnumerable<string> paths)
        {
            var list = new List<ImportedFile>();
            foreach (var path in paths ?? new string[0])
            {
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) continue;
                var info = Provenance.Read(path);
                list.Add(new ImportedFile
                {
                    Path = path,
                    AssetId = info?.AssetId ?? "",
                    Role = string.IsNullOrEmpty(info?.Role) ? "main" : info.Role,
                    Kind = KindOfPath(path),
                });
            }
            return list;
        }

        public static string KindOfPath(string path)
        {
            var ext = System.IO.Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
            if (ext == "glb" || ext == "gltf" || ext == "fbx" || ext == "obj" || ext == "prefab") return "3d";
            return Files.UploadTypeFor(path)?.Kind ?? "image";
        }

        /// <summary>The main file of a result set: the panorama, the albedo, the model, or the first file.</summary>
        public static ImportedFile Main(List<ImportedFile> files)
        {
            return files.FirstOrDefault(f => f.Role == "panorama")
                ?? files.FirstOrDefault(f => f.Kind == "3d")
                ?? files.FirstOrDefault(f => f.Role == "main")
                ?? files.FirstOrDefault(f => f.Role == "albedo")
                ?? files.FirstOrDefault();
        }
    }
}
