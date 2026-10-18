using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scenario.Editor.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scenario.Editor.Agent
{
    /// <summary>A tool refused the call; the message goes back to the agent as is.</summary>
    public sealed class ToolError : Exception
    {
        public ToolError(string message) : base(message) { }
    }

    /// <summary>
    /// The agent's tools: the same action layer as the window (ScenarioService),
    /// plus scene tools. Every scene edit is one named Undo step.
    /// </summary>
    public static class AgentTools
    {
        public sealed class Context
        {
            public string Client = "Agent";
        }

        sealed class Tool
        {
            public string Name;
            public string Description;
            public JObject Schema;
            public bool EditsScene;
            public bool ReadOnly;
            public Func<JObject, Context, Task<JObject>> Run;
        }

        public const string Instructions =
            "Scenario for Unity: generate images, video, 3D models, audio, PBR materials and skyboxes with Scenario models and put them in the open Unity project. " +
            "Flow: scenario_status, then models_find (add recommend_for_prompt to get Scenario's ranked picks), model_schema, estimate (free), generate (paid: it waits, imports and places the result, and returns Unity asset paths and scene object paths). " +
            "Then capture_view to look at the scene. Inputs that take files accept Scenario asset ids or Unity asset paths (Assets/...), uploaded for you. " +
            "Money rules: a paid call is never retried by the bridge. If generate reports an unknown outcome, call jobs instead of generating again. Calls above the user's limit wait for approval in Unity. " +
            "Scene objects are addressed by hierarchy path (Root/Child). Each scene edit is one Undo step the user can revert.";

        static ScenarioService S => ScenarioService.Instance;
        // Built on first use: Build() reads static helpers declared further down.
        static List<Tool> _tools;
        static List<Tool> Tools => _tools ?? (_tools = Build());

        public static JArray List()
        {
            return new JArray(Tools.Select(t =>
            {
                var tool = new JObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.Schema };
                var annotations = new JObject();
                if (t.ReadOnly) annotations["readOnlyHint"] = true;
                if (t.EditsScene) annotations["destructiveHint"] = t.Name == "object_delete";
                if (annotations.HasValues) tool["annotations"] = annotations;
                return tool;
            }));
        }

        public static IEnumerable<string> Names => Tools.Select(t => t.Name);

        public static async Task<JObject> Call(string name, JObject arguments, Context context)
        {
            var tool = Tools.FirstOrDefault(t => t.Name == name);
            if (tool == null) return Error($"Unknown tool {name}. Call tools/list.");
            if (tool.EditsScene && !ScenarioUserSettings.instance.AgentCanEditScene)
                return Error("Scene edits are turned off in Scenario > Agent. Ask the user to turn them on.");
            var group = -1;
            if (tool.EditsScene)
            {
                Undo.IncrementCurrentGroup();
                group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Scenario agent: " + name);
            }
            try
            {
                return await tool.Run(arguments ?? new JObject(), context);
            }
            catch (ToolError e)
            {
                return Error(e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Error($"Unity error in {name}: {e.Message}");
            }
            finally
            {
                if (group >= 0) Undo.CollapseUndoOperations(group);
            }
        }

        // --- Results --------------------------------------------------------------------

        static JObject Ok(JToken data) => new JObject
        {
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = data.ToString(Formatting.Indented) }),
        };

        static JObject Error(string message) => new JObject
        {
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = message }),
            ["isError"] = true,
        };

        static JObject Image(byte[] png, JToken data) => new JObject
        {
            ["content"] = new JArray(
                new JObject { ["type"] = "image", ["data"] = System.Convert.ToBase64String(png), ["mimeType"] = "image/png" },
                new JObject { ["type"] = "text", ["text"] = data.ToString(Formatting.Indented) }),
        };

        // --- Schema helpers -----------------------------------------------------------------

        static JObject Schema(string[] required, params (string name, JObject schema)[] properties)
        {
            var props = new JObject();
            foreach (var (name, schema) in properties) props[name] = schema;
            var result = new JObject { ["type"] = "object", ["properties"] = props };
            if (required != null && required.Length > 0) result["required"] = new JArray(required);
            return result;
        }

        static readonly string[] None = new string[0];
        static JObject Str(string description, params string[] options)
        {
            var schema = new JObject { ["type"] = "string", ["description"] = description };
            if (options.Length > 0) schema["enum"] = new JArray(options);
            return schema;
        }
        static JObject Num(string description) => new JObject { ["type"] = "number", ["description"] = description };
        static JObject Int(string description) => new JObject { ["type"] = "integer", ["description"] = description };
        static JObject Bool(string description) => new JObject { ["type"] = "boolean", ["description"] = description };
        static JObject Obj(string description) => new JObject { ["type"] = "object", ["description"] = description };
        static JObject Arr(string itemType, string description) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = itemType }, ["description"] = description };
        static JObject Vec(string description) => new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3, ["description"] = description };

        static string S_(JObject args, string key, string fallback = "") => Json.Str(args, key, fallback);
        static bool B_(JObject args, string key, bool fallback) => args[key] == null ? fallback : Json.Truthy(args[key]);
        static int I_(JObject args, string key, int fallback) => args[key] == null ? fallback : (int)Math.Round(Json.ToDouble(args[key], fallback));

        static string Need(JObject args, string key)
        {
            var value = Json.Str(args, key);
            if (value.Length == 0) throw new ToolError($"{key} is required.");
            return value;
        }

        static readonly string[] Types = OutputTypes.List.Select(t => t.Id).ToArray();

        // --- Registry ---------------------------------------------------------------------------

        static List<Tool> Build()
        {
            return new List<Tool>
            {
                new Tool
                {
                    Name = "scenario_status", ReadOnly = true,
                    Description = "Who is signed in, which Scenario project receives generations, spending state and limits, the Unity render pipeline and scene. Call this first.",
                    Schema = Schema(None), Run = Status,
                },
                new Tool
                {
                    Name = "models_find", ReadOnly = true,
                    Description = "Find Scenario models. Search the catalog by text, output type and capability, or pass recommend_for_prompt to get Scenario's ranked recommendation with cost and speed notes.",
                    Schema = Schema(None,
                        ("query", Str("Words to match: name, provider, style.")),
                        ("output_type", Str("image, video, model3d, audio, material, skybox or all.", Types)),
                        ("capability", Str("txt2img, img2img, txt2video, img2video, video2video, txt23d, img23d, 3d23d, txt2audio, audio2audio, video2audio.")),
                        ("recommend_for_prompt", Str("A prompt: returns Scenario's ranked picks for it (free).")),
                        ("include_legacy", Bool("Include deprecated models.")),
                        ("limit", Int("Max results, default 15."))),
                    Run = ModelsFind,
                },
                new Tool
                {
                    Name = "model_schema", ReadOnly = true,
                    Description = "The inputs a model takes: names, types, options, ranges, defaults, which inputs are files (and their kind), and the presets the plugin applies.",
                    Schema = Schema(new[] { "model_id" }, ("model_id", Str("Model id, e.g. model_openai-gpt-image-2-5-sunburst."))),
                    Run = ModelSchema,
                },
                new Tool
                {
                    Name = "estimate", ReadOnly = true,
                    Description = "Free price check in Creative Units for a model or workflow run with these parameters. Nothing is generated. The quote is valid 10 minutes.",
                    Schema = Schema(None,
                        ("model_id", Str("Model id.")),
                        ("workflow_id", Str("Workflow id, instead of model_id.")),
                        ("parameters", Obj("Model parameters (or workflow inputs). File inputs take Scenario asset ids or Unity asset paths."))),
                    Run = Estimate,
                },
                new Tool
                {
                    Name = "generate",
                    Description = "Paid. Generates with a Scenario model, waits, imports the result into Assets and places it in the scene (on target when given). Returns Unity asset paths and scene object paths. Above the user's limit it waits for approval in Unity. Never call it again for the same request after an unknown outcome: call jobs.",
                    Schema = Schema(new[] { "model_id" },
                        ("model_id", Str("Model id.")),
                        ("parameters", Obj("Model parameters from model_schema. File inputs take Scenario asset ids or Unity asset paths.")),
                        ("output_type", Str("How the result lands in Unity; defaults to the model's type.", Types)),
                        ("place", Bool("Place the result in the scene (default true).")),
                        ("target", Str("Hierarchy path of an object to apply the result to (sprite, material, audio source, video player).")),
                        ("folder", Str("Assets folder for the files (default Assets/Scenario/<Type>/<date>).")),
                        ("max_cu", Num("Refuse without sending if the price is above this.")),
                        ("wait", Bool("Wait for the result (default true).")),
                        ("timeout_seconds", Int("How long to wait, default 240."))),
                    Run = (a, c) => Generate(a, c, workflow: false),
                },
                new Tool
                {
                    Name = "workflow_run",
                    Description = "Paid. Runs a Scenario workflow (see workflows_find) with its inputs, waits and imports the outputs. Same approval and money rules as generate.",
                    Schema = Schema(new[] { "workflow_id" },
                        ("workflow_id", Str("Workflow id.")),
                        ("inputs", Obj("Workflow inputs by name. File inputs take Scenario asset ids or Unity asset paths.")),
                        ("max_cu", Num("Refuse without sending if the price is above this.")),
                        ("wait", Bool("Wait for the outputs (default true).")),
                        ("timeout_seconds", Int("How long to wait, default 300."))),
                    Run = (a, c) => Generate(a, c, workflow: true),
                },
                new Tool
                {
                    Name = "workflows_find", ReadOnly = true,
                    Description = "Lists Scenario workflows: the team's own, or featured public ones, with their inputs.",
                    Schema = Schema(None, ("query", Str("Words to match.")), ("scope", Str("team or featured (default featured).", "team", "featured"))),
                    Run = WorkflowsFind,
                },
                new Tool
                {
                    Name = "jobs", ReadOnly = true,
                    Description = "Recent requests from this Unity project with state, cost, files and placed objects. Give local_id or job_id to get one, and wait_seconds to wait for it to finish.",
                    Schema = Schema(None, ("local_id", Str("Ledger id from generate.")), ("job_id", Str("Scenario job id.")),
                        ("wait_seconds", Int("Wait up to this long for the job to finish.")), ("limit", Int("Rows to list, default 10."))),
                    Run = Jobs,
                },
                new Tool
                {
                    Name = "import_asset",
                    Description = "Imports existing Scenario assets (by id) into the Unity project, optionally placing them. Free.",
                    Schema = Schema(new[] { "asset_ids" }, ("asset_ids", Arr("string", "Scenario asset ids.")),
                        ("output_type", Str("How it lands; defaults to the asset's kind.", Types)), ("place", Bool("Place in the scene (default false).")),
                        ("target", Str("Hierarchy path to apply it to."))),
                    Run = ImportAsset,
                },
                new Tool
                {
                    Name = "upload_reference",
                    Description = "Uploads a reference to Scenario and returns its asset id (free): a Unity asset path, a scene object (exported as GLB), a capture of the Scene view or main camera, or a file path.",
                    Schema = Schema(None, ("asset_path", Str("Unity asset path, e.g. Assets/Art/hero.png.")), ("object_path", Str("Scene object to export as GLB.")),
                        ("capture", Str("scene_view or main_camera.", "scene_view", "main_camera")), ("file_path", Str("Absolute path of a file."))),
                    Run = Upload,
                },
                new Tool
                {
                    Name = "prompt_spark",
                    Description = "Paid (a few CU). Rewrites a prompt for a given model with Scenario's Prompt Spark. Returns the improved prompt; nothing is generated.",
                    Schema = Schema(new[] { "model_id", "prompt" }, ("model_id", Str("Model the prompt is for.")), ("prompt", Str("The prompt to improve."))),
                    Run = PromptSpark,
                },
                new Tool
                {
                    Name = "scene_info", ReadOnly = true,
                    Description = "Open scenes, the hierarchy (paths and components), the selection, render pipeline and 2D or 3D mode.",
                    Schema = Schema(None, ("depth", Int("Hierarchy depth, default 3.")), ("max_objects", Int("Default 150."))),
                    Run = SceneInfo,
                },
                new Tool
                {
                    Name = "object_get", ReadOnly = true,
                    Description = "One scene object: transform, components, and with properties=true their editable serialized properties.",
                    Schema = Schema(new[] { "path" }, ("path", Str("Hierarchy path.")), ("properties", Bool("Include component properties."))),
                    Run = ObjectGet,
                },
                new Tool
                {
                    Name = "object_create", EditsScene = true,
                    Description = "Creates an empty object, a primitive or a prefab instance in the active scene.",
                    Schema = Schema(null, ("name", Str("Object name.")),
                        ("primitive", Str("cube, sphere, capsule, cylinder, plane or quad. Omit for an empty object.", "cube", "sphere", "capsule", "cylinder", "plane", "quad")),
                        ("prefab", Str("Asset path of a prefab or model to instantiate.")), ("parent", Str("Parent hierarchy path.")),
                        ("position", Vec("World position [x, y, z].")), ("rotation", Vec("Euler angles in degrees.")), ("scale", Vec("Local scale."))),
                    Run = ObjectCreate,
                },
                new Tool
                {
                    Name = "object_set", EditsScene = true,
                    Description = "Changes an object's name, active state, parent, transform, tag or layer.",
                    Schema = Schema(new[] { "path" }, ("path", Str("Hierarchy path.")), ("name", Str("New name.")), ("active", Bool("Active state.")),
                        ("parent", Str("New parent path, or empty for the scene root.")), ("position", Vec("Position.")), ("rotation", Vec("Euler angles.")),
                        ("scale", Vec("Local scale.")), ("local", Bool("Position and rotation in parent space (default false).")), ("tag", Str("Tag.")), ("layer", Str("Layer name."))),
                    Run = ObjectSet,
                },
                new Tool
                {
                    Name = "component_add", EditsScene = true,
                    Description = "Adds a component by type name (Light, Rigidbody, AudioSource, BoxCollider, or any script).",
                    Schema = Schema(new[] { "path", "type" }, ("path", Str("Hierarchy path.")), ("type", Str("Component type name."))),
                    Run = ComponentAdd,
                },
                new Tool
                {
                    Name = "component_set", EditsScene = true,
                    Description = "Sets a component property: a serialized property path (m_Intensity, m_Color) or a public C# property (intensity, color). Values: numbers, booleans, strings, enums by name, [x,y,z] vectors, colors as [r,g,b,a] or #RRGGBB, asset paths or hierarchy paths for references.",
                    Schema = Schema(new[] { "path", "type", "property", "value" }, ("path", Str("Hierarchy path.")), ("type", Str("Component type name.")),
                        ("property", Str("Property name or serialized path.")), ("value", new JObject { ["description"] = "New value." })),
                    Run = ComponentSet,
                },
                new Tool
                {
                    Name = "object_delete", EditsScene = true,
                    Description = "Deletes a scene object (undoable).",
                    Schema = Schema(new[] { "path" }, ("path", Str("Hierarchy path."))),
                    Run = ObjectDelete,
                },
                new Tool
                {
                    Name = "select",
                    Description = "Selects objects or assets in the editor and frames them in the Scene view.",
                    Schema = Schema(new[] { "paths" }, ("paths", Arr("string", "Hierarchy paths or asset paths."))),
                    Run = Select,
                },
                new Tool
                {
                    Name = "material_assign", EditsScene = true,
                    Description = "Sets a material asset on an object's renderer.",
                    Schema = Schema(new[] { "path", "material" }, ("path", Str("Hierarchy path.")), ("material", Str("Material asset path.")),
                        ("index", Int("Material slot, default 0.")), ("children", Bool("Also on child renderers."))),
                    Run = MaterialAssign,
                },
                new Tool
                {
                    Name = "skybox_set", EditsScene = true,
                    Description = "Sets the scene skybox from a panorama texture or a skybox material asset.",
                    Schema = Schema(None, ("texture", Str("Panorama texture asset path.")), ("material", Str("Skybox material asset path."))),
                    Run = SkyboxSet,
                },
                new Tool
                {
                    Name = "assets_find", ReadOnly = true,
                    Description = "Finds project assets by name, type (Texture2D, Material, Prefab, AudioClip, VideoClip, Model, Scene), folder or label (Scenario marks every generated asset).",
                    Schema = Schema(None, ("query", Str("Name words.")), ("type", Str("Asset type.")), ("folder", Str("Folder, e.g. Assets/Scenario.")),
                        ("label", Str("Asset label, e.g. Scenario.")), ("limit", Int("Default 50."))),
                    Run = AssetsFind,
                },
                new Tool
                {
                    Name = "scene_save", EditsScene = true,
                    Description = "Saves the open scenes. An untitled scene needs a path (Assets/Scenes/Name.unity).",
                    Schema = Schema(None, ("path", Str("Path for an untitled active scene."))),
                    Run = SceneSave,
                },
                new Tool
                {
                    Name = "console_logs", ReadOnly = true,
                    Description = "Recent Unity console messages since the bridge started.",
                    Schema = Schema(None, ("limit", Int("Default 30.")), ("level", Str("all, warning or error.", "all", "warning", "error"))),
                    Run = ConsoleLogs,
                },
                new Tool
                {
                    Name = "capture_view", ReadOnly = true,
                    Description = "Renders the Scene view, the main camera or a given camera and returns the image, so you can see the result. Optionally uploads it to Scenario as a reference.",
                    Schema = Schema(None, ("source", Str("scene_view, main_camera, or a camera's hierarchy path (default main_camera, then scene_view).")),
                        ("max_size", Int("Longest side in pixels, default 768.")), ("upload", Bool("Also upload to Scenario and return the asset id."))),
                    Run = CaptureView,
                },
            };
        }

        // --- Scenario tools ---------------------------------------------------------------------

        static void RequireReady()
        {
            if (!S.IsSignedIn) throw new ToolError("Not signed in to Scenario. Ask the user to sign in: Window > Scenario.");
            if (!S.Ready) throw new ToolError("No Scenario project picked. Ask the user to pick one in Window > Scenario.");
        }

        static Task<JObject> Status(JObject args, Context context)
        {
            var user = ScenarioUserSettings.instance;
            var scene = SceneManager.GetActiveScene();
            var data = new JObject
            {
                ["signed_in"] = S.IsSignedIn,
                ["ready"] = S.Ready,
                ["account"] = S.AccountLabel,
                ["team"] = user.TeamName,
                ["project"] = user.ProjectName,
                ["plan"] = user.Plan,
                ["spending"] = S.Client.SpendingDisabled ? "off" : "on",
                ["spent_today_cu"] = Math.Round(S.SpentToday(), 2),
                ["agent_limits"] = new JObject
                {
                    ["auto_approve_cu_per_call"] = user.AgentAutoApproveCu,
                    ["session_budget_cu"] = user.AgentSessionBudgetCu,
                    ["session_approved_cu"] = Math.Round(AgentServer.SessionSpent, 2),
                    ["scene_edits"] = user.AgentCanEditScene,
                },
                ["unity"] = new JObject
                {
                    ["version"] = Application.unityVersion,
                    ["render_pipeline"] = MaterialBuilder.Current.ToString(),
                    ["mode"] = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D ? "2D" : "3D",
                    ["active_scene"] = scene.path.Length > 0 ? scene.path : scene.name,
                },
                ["catalog_models"] = S.Catalog.Models.Count,
                ["output_root"] = ScenarioProjectSettings.instance.OutputRoot,
                ["output_types"] = new JArray(OutputTypes.List.Select(t => t.Id)),
            };
            if (!S.IsSignedIn) data["next"] = "Ask the user to sign in: Window > Scenario.";
            else if (!S.Ready) data["next"] = "Ask the user to pick a Scenario project in Window > Scenario.";
            return Task.FromResult(Ok(data));
        }

        static string CapabilityFor(string type, bool hasImage = false)
        {
            switch (type)
            {
                case OutputTypes.Video: return hasImage ? "img2video" : "txt2video";
                case OutputTypes.Model3D: return hasImage ? "img23d" : "txt23d";
                case OutputTypes.Audio: return "txt2audio";
                default: return hasImage ? "img2img" : "txt2img";
            }
        }

        static async Task<JObject> ModelsFind(JObject args, Context context)
        {
            RequireReady();
            var limit = Math.Max(1, Math.Min(50, I_(args, "limit", 15)));
            var type = S_(args, "output_type");
            var capability = S_(args, "capability");
            var prompt = S_(args, "recommend_for_prompt");
            if (prompt.Length > 0)
            {
                var cap = capability.Length > 0 ? capability : CapabilityFor(type);
                var result = await S.Recommend(cap, prompt, Math.Min(limit, 5));
                if (!result.Ok) throw new ToolError(result.Error.Message);
                return Ok(new JObject { ["capability"] = cap, ["rationale"] = result.Value["rationale"], ["ranked"] = result.Value["ranked"] });
            }
            if (S.Catalog.IsEmpty) await S.RefreshCatalog(false);
            var models = S.Catalog.Search(new Catalog.Query
            {
                Text = S_(args, "query"),
                OutputType = type,
                Capability = capability,
                ShowLegacy = B_(args, "include_legacy", false),
                Favorites = ScenarioUserSettings.instance.Favorites,
            }).Take(limit);
            var list = new JArray(models.Select(m => new JObject
            {
                ["id"] = m.Id,
                ["name"] = m.Name,
                ["provider"] = m.Provider,
                ["output_type"] = OutputTypes.TypeFor(m),
                ["capabilities"] = new JArray(m.Capabilities),
                ["description"] = m.Description,
                ["featured"] = m.IsFeatured,
                ["team_model"] = m.IsPrivate,
                ["legacy"] = m.IsLegacy,
            }));
            var data = new JObject { ["models"] = list };
            if (type.Length > 0) data["quick_picks"] = new JArray(OutputTypes.Get(type).QuickPicks);
            if (list.Count == 0) data["hint"] = S.Catalog.IsEmpty ? "The catalog is empty: " + S.CatalogError : "Nothing matched. Try fewer words, or recommend_for_prompt.";
            return Ok(data);
        }

        static async Task<List<Field>> FieldsFor(string modelId)
        {
            var schema = await S.Schema(modelId);
            if (!schema.Ok) throw new ToolError(schema.Error.Message);
            return SchemaForm.FieldsFromSchema(schema.Value, OutputTypes.Restrict);
        }

        static async Task<JObject> ModelSchema(JObject args, Context context)
        {
            RequireReady();
            var id = Need(args, "model_id");
            var schema = await S.Schema(id);
            if (!schema.Ok) throw new ToolError(schema.Error.Message);
            var fields = SchemaForm.FieldsFromSchema(schema.Value, OutputTypes.Restrict);
            var model = S.Model(id);
            return Ok(new JObject
            {
                ["model_id"] = id,
                ["name"] = model?.Name ?? id,
                ["output_type"] = OutputTypes.TypeFor(model),
                ["prompt_field"] = SchemaForm.PromptName(fields),
                ["prompt_spark"] = Json.Truthy(schema.Value["prompt_spark"]),
                ["presets"] = OutputTypes.Presets(id),
                ["fields"] = new JArray(fields.Select(FieldJson)),
            });
        }

        static JObject FieldJson(Field f)
        {
            var o = new JObject { ["name"] = f.Name, ["type"] = f.Type.ToString().ToLowerInvariant(), ["required"] = f.Required };
            if (f.Label != f.Name) o["label"] = f.Label;
            if (f.Description.Length > 0) o["description"] = f.Description;
            if (f.Default != null) o["default"] = f.Default;
            if (f.Options.Count > 0) o["options"] = new JArray(f.Options);
            if (f.Min.HasValue) o["min"] = f.Min.Value;
            if (f.Max.HasValue) o["max"] = f.Max.Value;
            if (f.IsFile) o["file_kind"] = f.Kind;
            if (f.MaxItems > 0) o["max_items"] = f.MaxItems;
            if (f.CostImpact) o["changes_price"] = true;
            return o;
        }

        /// <summary>Validates parameters against the schema and uploads Unity paths given for file inputs.</summary>
        static async Task<GenerateRequest> BuildRequest(JObject args, Context context, bool workflow)
        {
            RequireReady();
            var parameters = (args[workflow ? "inputs" : "parameters"] as JObject ?? args["parameters"] as JObject ?? new JObject()).DeepClone() as JObject;
            List<Field> fields;
            string id, name, type;
            JObject values;
            if (workflow)
            {
                id = Need(args, "workflow_id");
                var wf = await S.Client.GetWorkflow(id);
                if (!wf.Ok) throw new ToolError(wf.Error.Message);
                var definition = wf.Value["workflow"] as JObject ?? wf.Value;
                fields = SchemaForm.FieldsFromSchema(definition);
                name = Json.Str(definition, "name", id);
                type = OutputTypes.All;
                values = SchemaForm.InitialValues(fields);
            }
            else
            {
                id = Need(args, "model_id");
                fields = await FieldsFor(id);
                var model = S.Model(id);
                name = model?.Name ?? id;
                type = S_(args, "output_type");
                if (type.Length == 0) type = model != null ? OutputTypes.TypeFor(model) : OutputTypes.All;
                values = SchemaForm.InitialValues(fields, OutputTypes.Presets(id));
            }
            var unknown = parameters.Properties().Select(p => p.Name).Where(n => fields.All(f => f.Name != n)).ToList();
            if (unknown.Count > 0)
                throw new ToolError($"Unknown {(workflow ? "inputs" : "parameters")}: {string.Join(", ", unknown)}. Accepted: {string.Join(", ", fields.Select(f => f.Name))}.");
            foreach (var property in parameters.Properties()) values[property.Name] = property.Value;
            foreach (var field in fields.Where(f => f.IsFile && values[f.Name] != null)) values[field.Name] = await UploadPaths(values[field.Name]);
            var problems = SchemaForm.Validate(fields, values);
            if (problems.Count > 0) throw new ToolError(string.Join(" ", problems));
            var canPlace = ScenarioUserSettings.instance.AgentCanEditScene;
            var targetPath = S_(args, "target");
            var target = targetPath.Length > 0 ? Find(targetPath) : null;
            var folder = S_(args, "folder").TrimEnd('/');
            if (folder.Length > 0 && !folder.StartsWith("Assets/") && folder != "Assets") throw new ToolError("folder must be inside Assets.");
            var promptName = SchemaForm.PromptName(fields);
            return new GenerateRequest
            {
                OutputType = type,
                ModelId = workflow ? "" : id,
                WorkflowId = workflow ? id : "",
                ModelName = name,
                Payload = SchemaForm.Payload(fields, values),
                Prompt = promptName.Length > 0 ? Json.Str(values, promptName) : name,
                Place = !workflow && canPlace && B_(args, "place", true),
                Target = canPlace ? target : null,
                Origin = "agent: " + context.Client,
                Folder = folder,
            };
        }

        /// <summary>File inputs: Unity asset paths and absolute paths become Scenario asset ids.</summary>
        static async Task<JToken> UploadPaths(JToken value)
        {
            async Task<JToken> One(JToken item)
            {
                var text = item?.ToString() ?? "";
                if (text.StartsWith("asset_")) return text;
                if (text.StartsWith("Assets/") || text.StartsWith("Packages/"))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(text);
                    if (asset == null) throw new ToolError($"No asset at {text}.");
                    var uploaded = await S.Upload(asset);
                    if (!uploaded.Ok) throw new ToolError(uploaded.Error.Message);
                    return uploaded.Value;
                }
                if (Path.IsPathRooted(text) && File.Exists(text))
                {
                    var uploaded = await S.UploadPath(text);
                    if (!uploaded.Ok) throw new ToolError(uploaded.Error.Message);
                    return uploaded.Value;
                }
                return item;
            }
            if (value is JArray array)
            {
                var result = new JArray();
                foreach (var item in array) result.Add(await One(item));
                return result;
            }
            return await One(value);
        }

        static string ApprovalText(double cu)
        {
            var user = ScenarioUserSettings.instance;
            return cu <= user.AgentAutoApproveCu && AgentServer.SessionSpent + cu <= user.AgentSessionBudgetCu
                ? "automatic (within the user's limits)"
                : "the user approves it in Unity (above the auto-approve limit or the session budget)";
        }

        static async Task<JObject> Estimate(JObject args, Context context)
        {
            var workflow = S_(args, "workflow_id").Length > 0;
            if (workflow && args["inputs"] == null && args["parameters"] != null) args["inputs"] = args["parameters"];
            var request = await BuildRequest(args, context, workflow);
            var quote = await S.Estimate(request);
            if (!quote.Ok) throw new ToolError(quote.Error.Message);
            return Ok(new JObject
            {
                ["cu"] = quote.Value.Cu,
                [workflow ? "workflow_id" : "model_id"] = workflow ? request.WorkflowId : request.ModelId,
                ["parameters"] = request.Payload,
                ["quote_valid_seconds"] = (int)QuoteBook.MaxAgeSeconds,
                ["approval"] = ApprovalText(quote.Value.Cu),
                ["spending"] = S.Client.SpendingDisabled ? "off: generate will be refused" : "on",
            });
        }

        static async Task<JObject> Generate(JObject args, Context context, bool workflow)
        {
            var request = await BuildRequest(args, context, workflow);
            var block = S.BlockReason(request);
            if (block.Length > 0) throw new ToolError(block + " Nothing was sent.");
            var quote = S.QuoteFor(request);
            if (quote == null)
            {
                var priced = await S.Estimate(request);
                if (!priced.Ok) throw new ToolError(priced.Error.Message + " Nothing was sent.");
                quote = priced.Value;
            }
            var max = args["max_cu"] != null ? Json.ToDouble(args["max_cu"], -1) : -1;
            if (max >= 0 && quote.Cu > max) throw new ToolError($"This costs {quote.Cu} CU, above max_cu {max}. Nothing was sent.");
            var label = request.Prompt.Length > 60 ? request.Prompt.Substring(0, 60) + "…" : request.Prompt;
            if (!await AgentServer.RequestSpend(context.Client, workflow ? "workflow_run" : "generate", $"run {request.ModelName}: \"{label}\"", quote.Cu))
                throw new ToolError($"Not approved in Unity ({quote.Cu} CU). Nothing was sent.");
            var result = await S.Generate(request);
            if (!result.Ok)
            {
                if (result.Error.Ambiguous)
                    throw new ToolError(result.Error.Message + " The outcome is unknown: it may have run. Do not call generate again for this request; call jobs in a minute.");
                throw new ToolError(result.Error.Message);
            }
            AgentServer.AddSpent(quote.Cu);
            var row = result.Value;
            if (B_(args, "wait", true)) await WaitFor(row, I_(args, "timeout_seconds", workflow ? 300 : 240));
            var data = RowJson(row);
            if (!row.IsFinal) data["next"] = $"Still {row.State}. Call jobs with local_id {row.LocalId} and wait_seconds to get the result.";
            return Ok(data);
        }

        static bool Done(JobRow row)
        {
            if (row.IsUnresolved) return false;
            if (!row.IsFinal) return false;
            return row.State != JobState.Success || row.Imported || row.Error.StartsWith("Import failed");
        }

        static async Task WaitFor(JobRow row, int seconds)
        {
            var until = Json.Now() + Math.Max(0, Math.Min(900, seconds));
            while (!Done(row) && Json.Now() < until) await Task.Delay(1000);
        }

        static JObject RowJson(JobRow row)
        {
            var data = new JObject
            {
                ["local_id"] = row.LocalId,
                ["job_id"] = row.JobId,
                ["state"] = row.State,
                ["model"] = row.WorkflowId.Length > 0 ? row.WorkflowId : row.ModelId,
                ["prompt"] = row.Prompt,
                ["quoted_cu"] = row.QuotedCu,
                ["created"] = DateTimeOffset.FromUnixTimeSeconds((long)row.CreatedAt).ToString("u"),
            };
            if (row.ChargedCu.HasValue) data["charged_cu"] = row.ChargedCu.Value;
            if (row.AssetIds.Count > 0) data["asset_ids"] = new JArray(row.AssetIds);
            if (row.Files.Count > 0) data["files"] = new JArray(row.Files);
            if (row.Placed.Count > 0) data["placed"] = new JArray(row.Placed);
            if (row.Error.Length > 0) data["error"] = row.Error;
            if (row.Progress > 0 && row.IsActive) data["progress"] = row.Progress;
            return data;
        }

        static async Task<JObject> Jobs(JObject args, Context context)
        {
            var localId = S_(args, "local_id");
            var jobId = S_(args, "job_id");
            if (localId.Length > 0 || jobId.Length > 0)
            {
                var row = localId.Length > 0 ? S.Ledger.Find(localId) : S.Ledger.FindByJob(jobId);
                if (row == null) throw new ToolError("No such job in this Unity project.");
                var wait = I_(args, "wait_seconds", 0);
                if (wait > 0) await WaitFor(row, wait);
                return Ok(RowJson(row));
            }
            var limit = Math.Max(1, Math.Min(50, I_(args, "limit", 10)));
            return Ok(new JObject { ["jobs"] = new JArray(S.Ledger.Jobs.Where(r => r.State != JobState.Dismissed).Take(limit).Select(RowJson)) });
        }

        static async Task<JObject> WorkflowsFind(JObject args, Context context)
        {
            RequireReady();
            var team = S_(args, "scope") == "team";
            var result = await S.Client.ListWorkflows(team ? "private" : "public", 60);
            if (!result.Ok) throw new ToolError(result.Error.Message);
            var words = S_(args, "query").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = (result.Value["workflows"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(w => words.All(word => (Json.Str(w, "name") + " " + Json.Str(w, "description")).ToLowerInvariant().Contains(word)))
                .Select(w => new JObject
                {
                    ["id"] = Json.Str(w, "id"),
                    ["name"] = Json.Str(w, "name"),
                    ["description"] = Json.Str(w, "shortDescription", Json.Str(w, "description")),
                    ["inputs"] = new JArray(SchemaForm.FieldsFromSchema(w).Select(FieldJson)),
                    ["output_kinds"] = w["outputAssetKinds"],
                });
            return Ok(new JObject { ["workflows"] = new JArray(list) });
        }

        static async Task<JObject> ImportAsset(JObject args, Context context)
        {
            RequireReady();
            var ids = (args["asset_ids"] as JArray)?.Select(a => a.ToString()).Where(a => a.StartsWith("asset_")).ToList() ?? new List<string>();
            if (ids.Count == 0 && S_(args, "asset_id").Length > 0) ids.Add(S_(args, "asset_id"));
            if (ids.Count == 0) throw new ToolError("asset_ids is required.");
            var type = S_(args, "output_type");
            if (type.Length == 0)
            {
                var first = await S.Client.GetAsset(ids[0]);
                if (!first.Ok) throw new ToolError(first.Error.Message);
                type = OutputTypes.TypeForKind(Files.KindOf(first.Value));
            }
            var canPlace = ScenarioUserSettings.instance.AgentCanEditScene;
            var target = S_(args, "target").Length > 0 ? Find(S_(args, "target")) : null;
            var result = await S.ImportAssets(ids, type, "", canPlace && B_(args, "place", false), canPlace ? target : null);
            if (!result.Ok) throw new ToolError(result.Error.Message);
            return Ok(new JObject
            {
                ["files"] = new JArray(result.Value.Select(f => f.Path)),
                ["selection"] = new JArray(Selection.gameObjects.Where(g => !EditorUtility.IsPersistent(g)).Select(PathOf)),
            });
        }

        static async Task<JObject> Upload(JObject args, Context context)
        {
            RequireReady();
            Result<string> result;
            var assetPath = S_(args, "asset_path");
            var objectPath = S_(args, "object_path");
            var capture = S_(args, "capture");
            var filePath = S_(args, "file_path");
            if (assetPath.Length > 0)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null) throw new ToolError($"No asset at {assetPath}.");
                result = await S.Upload(asset);
            }
            else if (objectPath.Length > 0) result = await Uploader.UploadObject(S.Client, Find(objectPath));
            else if (capture.Length > 0)
            {
                var path = capture == "scene_view" ? Capture.SceneView() : Capture.MainCamera();
                if (path == null) throw new ToolError(capture == "scene_view" ? "No Scene view is open." : "No camera is tagged MainCamera.");
                result = await S.UploadPath(path);
            }
            else if (filePath.Length > 0)
            {
                if (!File.Exists(filePath)) throw new ToolError($"No file at {filePath}.");
                result = await S.UploadPath(filePath);
            }
            else throw new ToolError("Give asset_path, object_path, capture or file_path.");
            if (!result.Ok) throw new ToolError(result.Error.Message);
            return Ok(new JObject { ["asset_id"] = result.Value });
        }

        static async Task<JObject> PromptSpark(JObject args, Context context)
        {
            RequireReady();
            var modelId = Need(args, "model_id");
            var prompt = Need(args, "prompt");
            var price = await S.PromptSparkPrice(modelId);
            if (!price.Ok) throw new ToolError(price.Error.Message);
            if (!await AgentServer.RequestSpend(context.Client, "prompt_spark", "improve a prompt with Prompt Spark", price.Value))
                throw new ToolError("Not approved in Unity. Nothing was sent.");
            var result = await S.ImprovePrompt(modelId, S.Model(modelId)?.Name ?? modelId, prompt, "agent: " + context.Client);
            if (!result.Ok) throw new ToolError(result.Error.Message);
            AgentServer.AddSpent(price.Value);
            return Ok(new JObject { ["prompt"] = result.Value, ["cu"] = price.Value });
        }

        // --- Scene tools ----------------------------------------------------------------------

        public static GameObject Find(string path)
        {
            var go = FindOrNull(path);
            if (go == null) throw new ToolError($"No object at \"{path}\". Call scene_info for paths.");
            return go;
        }

        public static GameObject FindOrNull(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Trim('/').Split('/');
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0]) continue;
                    var current = root.transform;
                    for (var p = 1; p < parts.Length && current != null; p++)
                    {
                        Transform next = null;
                        foreach (Transform child in current)
                            if (child.name == parts[p])
                            {
                                next = child;
                                break;
                            }
                        current = next;
                    }
                    if (current != null) return current.gameObject;
                }
            }
            return null;
        }

        public static string PathOf(GameObject go) => Placer.PathOf(go);

        static Vector3? Vec3(JToken token)
        {
            if (token is JArray a && a.Count >= 3) return new Vector3((float)Json.ToDouble(a[0]), (float)Json.ToDouble(a[1]), (float)Json.ToDouble(a[2]));
            if (token is JObject o) return new Vector3((float)Json.Num(o, "x"), (float)Json.Num(o, "y"), (float)Json.Num(o, "z"));
            return null;
        }

        static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));

        static Task<JObject> SceneInfo(JObject args, Context context)
        {
            var depth = Math.Max(1, Math.Min(8, I_(args, "depth", 3)));
            var max = Math.Max(10, Math.Min(1000, I_(args, "max_objects", 150)));
            var scenes = new JArray();
            var objects = new JArray();
            var count = 0;
            void Walk(Transform t, int level)
            {
                if (count >= max) return;
                count++;
                var entry = new JObject
                {
                    ["path"] = PathOf(t.gameObject),
                    ["components"] = new JArray(t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)),
                };
                if (!t.gameObject.activeSelf) entry["active"] = false;
                if (t.childCount > 0 && level >= depth) entry["children"] = t.childCount;
                objects.Add(entry);
                if (level < depth)
                    foreach (Transform child in t) Walk(child, level + 1);
            }
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(new JObject
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["loaded"] = scene.isLoaded,
                    ["dirty"] = scene.isDirty,
                    ["active"] = scene == SceneManager.GetActiveScene(),
                });
                if (scene.isLoaded)
                    foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, 1);
            }
            var data = new JObject
            {
                ["scenes"] = scenes,
                ["objects"] = objects,
                ["selection"] = new JArray(Selection.gameObjects.Where(g => !EditorUtility.IsPersistent(g)).Select(PathOf)),
                ["render_pipeline"] = MaterialBuilder.Current.ToString(),
                ["mode"] = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D ? "2D" : "3D",
            };
            if (count >= max) data["truncated"] = true;
            var view = SceneView.lastActiveSceneView;
            if (view != null) data["scene_view_pivot"] = V(view.pivot);
            return Task.FromResult(Ok(data));
        }

        static Task<JObject> ObjectGet(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            var t = go.transform;
            var components = new JArray();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var entry = new JObject { ["type"] = component.GetType().Name };
                if (component is Behaviour behaviour) entry["enabled"] = behaviour.enabled;
                if (B_(args, "properties", false) && !(component is Transform)) entry["properties"] = Properties(component);
                components.Add(entry);
            }
            return Task.FromResult(Ok(new JObject
            {
                ["path"] = PathOf(go),
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["position"] = V(t.position),
                ["rotation"] = V(t.eulerAngles),
                ["scale"] = V(t.localScale),
                ["children"] = new JArray(t.Cast<Transform>().Select(c => c.name)),
                ["components"] = components,
            }));
        }

        static JObject Properties(Component component)
        {
            var result = new JObject();
            var so = new SerializedObject(component);
            var it = so.GetIterator();
            var count = 0;
            if (!it.NextVisible(true)) return result;
            do
            {
                if (it.name == "m_Script") continue;
                result[it.propertyPath] = ValueOf(it);
                if (++count >= 40) break;
            }
            while (it.NextVisible(false));
            return result;
        }

        static JToken ValueOf(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue;
                case SerializedPropertyType.Boolean: return p.boolValue;
                case SerializedPropertyType.Float: return Math.Round(p.doubleValue, 4);
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : (JToken)p.intValue;
                case SerializedPropertyType.Vector2: return new JArray(p.vector2Value.x, p.vector2Value.y);
                case SerializedPropertyType.Vector3: return V(p.vector3Value);
                case SerializedPropertyType.Vector4: return new JArray(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
                case SerializedPropertyType.ObjectReference:
                    var o = p.objectReferenceValue;
                    if (o == null) return null;
                    var path = AssetDatabase.GetAssetPath(o);
                    return path.Length > 0 ? path : o is GameObject g ? PathOf(g) : o is Component c ? PathOf(c.gameObject) : o.name;
                default: return $"({p.propertyType})";
            }
        }

        static Task<JObject> ObjectCreate(JObject args, Context context)
        {
            GameObject go;
            var prefabPath = S_(args, "prefab");
            var primitive = S_(args, "primitive");
            if (prefabPath.Length > 0)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) throw new ToolError($"No prefab or model at {prefabPath}.");
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else if (primitive.Length > 0)
            {
                if (!Enum.TryParse(primitive, true, out PrimitiveType type)) throw new ToolError("primitive must be cube, sphere, capsule, cylinder, plane or quad.");
                go = GameObject.CreatePrimitive(type);
            }
            else go = new GameObject();
            go.name = S_(args, "name", go.name.Length > 0 ? go.name : "GameObject");
            Undo.RegisterCreatedObjectUndo(go, "Scenario agent: object_create");
            var parentPath = S_(args, "parent");
            if (parentPath.Length > 0) Undo.SetTransformParent(go.transform, Find(parentPath).transform, "Scenario agent: parent");
            var position = Vec3(args["position"]);
            if (position.HasValue) go.transform.position = position.Value;
            else if (parentPath.Length == 0 && prefabPath.Length + primitive.Length > 0) go.transform.position = Placer.Pivot();
            var rotation = Vec3(args["rotation"]);
            if (rotation.HasValue) go.transform.eulerAngles = rotation.Value;
            var scale = Vec3(args["scale"]);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go) }));
        }

        static Task<JObject> ObjectSet(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            Undo.RecordObject(go, "Scenario agent: object_set");
            Undo.RecordObject(go.transform, "Scenario agent: object_set");
            if (args["name"] != null) go.name = S_(args, "name");
            if (args["active"] != null) go.SetActive(Json.Truthy(args["active"]));
            if (args["tag"] != null) go.tag = S_(args, "tag");
            if (args["layer"] != null)
            {
                var layer = LayerMask.NameToLayer(S_(args, "layer"));
                if (layer < 0) throw new ToolError($"No layer named {S_(args, "layer")}.");
                go.layer = layer;
            }
            if (args["parent"] != null)
            {
                var parentPath = S_(args, "parent");
                Undo.SetTransformParent(go.transform, parentPath.Length == 0 ? null : Find(parentPath).transform, "Scenario agent: parent");
            }
            var local = B_(args, "local", false);
            var position = Vec3(args["position"]);
            if (position.HasValue)
            {
                if (local) go.transform.localPosition = position.Value;
                else go.transform.position = position.Value;
            }
            var rotation = Vec3(args["rotation"]);
            if (rotation.HasValue)
            {
                if (local) go.transform.localEulerAngles = rotation.Value;
                else go.transform.eulerAngles = rotation.Value;
            }
            var scale = Vec3(args["scale"]);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go) }));
        }

        static Type ComponentType(string name)
        {
            var types = TypeCache.GetTypesDerivedFrom<Component>();
            var exact = types.FirstOrDefault(t => t.FullName == name);
            if (exact != null) return exact;
            var matches = types.Where(t => t.Name == name && !t.IsAbstract).ToList();
            return matches.FirstOrDefault(t => t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) ?? matches.FirstOrDefault();
        }

        static Task<JObject> ComponentAdd(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            var type = ComponentType(Need(args, "type"));
            if (type == null) throw new ToolError($"No component type named {S_(args, "type")}.");
            var existing = go.GetComponent(type);
            if (existing != null && Attribute.IsDefined(type, typeof(DisallowMultipleComponent)))
                return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go), ["type"] = type.Name, ["note"] = "Already present." }));
            var component = Undo.AddComponent(go, type);
            if (component == null) throw new ToolError($"Unity refused to add {type.Name} to {go.name}.");
            return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go), ["type"] = type.Name }));
        }

        static Task<JObject> ComponentSet(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            var typeName = Need(args, "type");
            var type = ComponentType(typeName);
            var component = type != null ? go.GetComponent(type) : go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
            if (component == null) throw new ToolError($"{go.name} has no {typeName}. Add it with component_add.");
            var property = Need(args, "property");
            var value = args["value"];
            var so = new SerializedObject(component);
            var prop = so.FindProperty(property) ?? so.FindProperty("m_" + char.ToUpperInvariant(property[0]) + property.Substring(1));
            if (prop != null)
            {
                SetSerialized(prop, value);
                so.ApplyModifiedProperties();
                return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go), ["property"] = prop.propertyPath, ["value"] = ValueOf(prop) }));
            }
            var member = component.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (member != null && member.CanWrite)
            {
                Undo.RecordObject(component, "Scenario agent: component_set");
                member.SetValue(component, Convert(value, member.PropertyType));
                EditorUtility.SetDirty(component);
                return Task.FromResult(Ok(new JObject { ["path"] = PathOf(go), ["property"] = member.Name, ["value"] = JToken.FromObject(member.GetValue(component)?.ToString() ?? "") }));
            }
            throw new ToolError($"{type?.Name ?? typeName} has no property {property}. Call object_get with properties=true to list them.");
        }

        static void SetSerialized(SerializedProperty prop, JToken value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.longValue = (long)Math.Round(Json.ToDouble(value));
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Json.Truthy(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.doubleValue = Json.ToDouble(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? "";
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = ToColor(value);
                    break;
                case SerializedPropertyType.Enum:
                    var text = value?.ToString() ?? "";
                    var index = Array.FindIndex(prop.enumNames, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) index = Array.FindIndex(prop.enumDisplayNames, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                    if (index < 0 && int.TryParse(text, out var number)) index = number;
                    if (index < 0) throw new ToolError($"{prop.name} takes one of: {string.Join(", ", prop.enumDisplayNames)}.");
                    prop.enumValueIndex = index;
                    break;
                case SerializedPropertyType.Vector2:
                    var v2 = value as JArray ?? throw new ToolError("Give [x, y].");
                    prop.vector2Value = new Vector2((float)Json.ToDouble(v2[0]), (float)Json.ToDouble(v2[1]));
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = Vec3(value) ?? throw new ToolError("Give [x, y, z].");
                    break;
                case SerializedPropertyType.Vector4:
                    var v4 = value as JArray ?? throw new ToolError("Give [x, y, z, w].");
                    prop.vector4Value = new Vector4((float)Json.ToDouble(v4[0]), (float)Json.ToDouble(v4[1]), (float)Json.ToDouble(v4[2]), (float)Json.ToDouble(v4[3]));
                    break;
                case SerializedPropertyType.Quaternion:
                    prop.quaternionValue = Quaternion.Euler(Vec3(value) ?? throw new ToolError("Give euler angles [x, y, z]."));
                    break;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = Reference(value?.ToString() ?? "", prop.type);
                    break;
                default:
                    throw new ToolError($"{prop.name} is a {prop.propertyType}; set its child properties instead (object_get lists them).");
            }
        }

        static UnityEngine.Object Reference(string text, string pptrType)
        {
            if (text.Length == 0) return null;
            var wanted = pptrType.StartsWith("PPtr<$") ? pptrType.Substring(6).TrimEnd('>') : "";
            if (text.StartsWith("Assets/") || text.StartsWith("Packages/"))
            {
                var all = AssetDatabase.LoadAllAssetsAtPath(text);
                var match = all.FirstOrDefault(a => a != null && a.GetType().Name == wanted) ?? AssetDatabase.LoadMainAssetAtPath(text);
                if (match == null) throw new ToolError($"No asset at {text}.");
                return match;
            }
            var go = Find(text);
            if (wanted.Length == 0 || wanted == "GameObject") return go;
            var component = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == wanted);
            return component != null ? component : (UnityEngine.Object)go;
        }

        static Color ToColor(JToken value)
        {
            if (value is JArray a && a.Count >= 3)
                return new Color((float)Json.ToDouble(a[0]), (float)Json.ToDouble(a[1]), (float)Json.ToDouble(a[2]), a.Count > 3 ? (float)Json.ToDouble(a[3]) : 1f);
            var text = value?.ToString() ?? "";
            if (!text.StartsWith("#")) text = "#" + text;
            if (ColorUtility.TryParseHtmlString(text, out var color)) return color;
            throw new ToolError("Colors are [r, g, b, a] from 0 to 1, or #RRGGBB.");
        }

        static object Convert(JToken value, Type type)
        {
            if (type == typeof(float)) return (float)Json.ToDouble(value);
            if (type == typeof(double)) return Json.ToDouble(value);
            if (type == typeof(int)) return (int)Math.Round(Json.ToDouble(value));
            if (type == typeof(bool)) return Json.Truthy(value);
            if (type == typeof(string)) return value?.ToString() ?? "";
            if (type == typeof(Vector3)) return Vec3(value) ?? throw new ToolError("Give [x, y, z].");
            if (type == typeof(Color)) return ToColor(value);
            if (type.IsEnum) return Enum.Parse(type, value?.ToString() ?? "", true);
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return Reference(value?.ToString() ?? "", "PPtr<$" + type.Name + ">");
            return value?.ToObject(type);
        }

        static Task<JObject> ObjectDelete(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            var path = PathOf(go);
            Undo.DestroyObjectImmediate(go);
            return Task.FromResult(Ok(new JObject { ["deleted"] = path }));
        }

        static Task<JObject> Select(JObject args, Context context)
        {
            var paths = (args["paths"] as JArray)?.Select(p => p.ToString()).ToList() ?? new List<string>();
            var objects = new List<UnityEngine.Object>();
            foreach (var path in paths)
            {
                if (path.StartsWith("Assets/") || path.StartsWith("Packages/"))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset != null) objects.Add(asset);
                }
                else
                {
                    var go = FindOrNull(path);
                    if (go != null) objects.Add(go);
                }
            }
            Selection.objects = objects.ToArray();
            if (objects.OfType<GameObject>().Any() && SceneView.lastActiveSceneView != null && !Application.isBatchMode) SceneView.lastActiveSceneView.FrameSelected();
            return Task.FromResult(Ok(new JObject { ["selected"] = objects.Count }));
        }

        static Task<JObject> MaterialAssign(JObject args, Context context)
        {
            var go = Find(Need(args, "path"));
            var material = AssetDatabase.LoadAssetAtPath<Material>(Need(args, "material"));
            if (material == null) throw new ToolError($"No material at {S_(args, "material")}.");
            var renderers = B_(args, "children", false) ? go.GetComponentsInChildren<Renderer>() : go.GetComponents<Renderer>();
            if (renderers.Length == 0) throw new ToolError($"{go.name} has no renderer.");
            var index = Math.Max(0, I_(args, "index", 0));
            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Scenario agent: material_assign");
                var materials = renderer.sharedMaterials;
                if (index >= materials.Length) Array.Resize(ref materials, index + 1);
                materials[index] = material;
                renderer.sharedMaterials = materials;
            }
            return Task.FromResult(Ok(new JObject { ["renderers"] = renderers.Length }));
        }

        static Task<JObject> SkyboxSet(JObject args, Context context)
        {
            var texture = S_(args, "texture");
            var materialPath = S_(args, "material");
            if (texture.Length > 0)
            {
                var (material, message) = SkyboxBuilder.Apply(texture, true);
                if (material == null) throw new ToolError(message.Length > 0 ? message : "Could not build a skybox from that texture.");
                return Task.FromResult(Ok(new JObject { ["material"] = AssetDatabase.GetAssetPath(material), ["note"] = message }));
            }
            if (materialPath.Length > 0)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) throw new ToolError($"No material at {materialPath}.");
                RenderSettings.skybox = material;
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                return Task.FromResult(Ok(new JObject { ["material"] = materialPath }));
            }
            throw new ToolError("Give texture or material.");
        }

        static Task<JObject> AssetsFind(JObject args, Context context)
        {
            var filter = S_(args, "query");
            var type = S_(args, "type");
            if (type.Length > 0) filter += " t:" + type;
            var label = S_(args, "label");
            if (label.Length > 0) filter += " l:" + label;
            var folder = S_(args, "folder").TrimEnd('/');
            var guids = folder.Length > 0 ? AssetDatabase.FindAssets(filter.Trim(), new[] { folder }) : AssetDatabase.FindAssets(filter.Trim());
            var limit = Math.Max(1, Math.Min(200, I_(args, "limit", 50)));
            var list = new JArray(guids.Take(limit).Select(AssetDatabase.GUIDToAssetPath).Select(p => new JObject
            {
                ["path"] = p,
                ["type"] = AssetDatabase.GetMainAssetTypeAtPath(p)?.Name ?? "",
            }));
            return Task.FromResult(Ok(new JObject { ["assets"] = list, ["total"] = guids.Length }));
        }

        static Task<JObject> SceneSave(JObject args, Context context)
        {
            var scene = SceneManager.GetActiveScene();
            var path = S_(args, "path");
            if (scene.path.Length == 0)
            {
                if (path.Length == 0) throw new ToolError("The active scene is untitled. Give path, e.g. Assets/Scenes/Level.unity.");
                if (!path.StartsWith("Assets/") || !path.EndsWith(".unity")) throw new ToolError("path must be Assets/....unity.");
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
                if (!EditorSceneManager.SaveScene(scene, path)) throw new ToolError("Unity could not save the scene.");
            }
            else if (!EditorSceneManager.SaveOpenScenes()) throw new ToolError("Unity could not save the scenes.");
            return Task.FromResult(Ok(new JObject { ["saved"] = SceneManager.GetActiveScene().path }));
        }

        static Task<JObject> ConsoleLogs(JObject args, Context context)
        {
            var entries = AgentLogs.Recent(Math.Min(200, I_(args, "limit", 30)), S_(args, "level", "all"));
            return Task.FromResult(Ok(new JObject
            {
                ["messages"] = new JArray(entries.Select(e => new JObject { ["type"] = e.Type, ["message"] = e.Message, ["where"] = e.Where })),
            }));
        }

        static async Task<JObject> CaptureView(JObject args, Context context)
        {
            var source = S_(args, "source", "main_camera");
            var size = Math.Max(128, Math.Min(2048, I_(args, "max_size", 768)));
            string path;
            if (source == "scene_view") path = Capture.SceneView(size);
            else if (source == "main_camera") path = Capture.MainCamera(size) ?? Capture.SceneView(size);
            else
            {
                var camera = Find(source).GetComponent<Camera>();
                if (camera == null) throw new ToolError($"{source} has no Camera.");
                path = Capture.Render(camera, 16, 9, size, "camera");
            }
            if (path == null) throw new ToolError("Nothing to capture: no camera tagged MainCamera and no Scene view.");
            var png = File.ReadAllBytes(path);
            var data = new JObject { ["file"] = path };
            if (B_(args, "upload", false))
            {
                RequireReady();
                var uploaded = await S.UploadPath(path);
                if (!uploaded.Ok) throw new ToolError(uploaded.Error.Message);
                data["asset_id"] = uploaded.Value;
            }
            return Image(png, data);
        }
    }
}
