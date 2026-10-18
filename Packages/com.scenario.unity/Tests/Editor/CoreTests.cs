using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Scenario.Editor.Core;

namespace Scenario.Editor.Tests
{
    public class SseAndJsonTests
    {
        [Test]
        public void TheLastMatchingMessageWins()
        {
            var body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\n"
                + "data: {\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"a\":1}}\n\n"
                + "data: not json\n\n"
                + "data: {\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"a\":2}}\n\n";
            var parsed = Sse.Parse(body, "text/event-stream", 7);
            Assert.IsTrue(parsed.Ok);
            Assert.AreEqual(2, (int)parsed.Value["result"]["a"]);
        }

        [Test]
        public void AMissingReplyIsAProtocolError()
        {
            var parsed = Sse.Parse("data: {\"id\":3,\"result\":{}}\n\n", "text/event-stream", 4);
            Assert.AreEqual(ErrorKind.Protocol, parsed.Error.Kind);
            Assert.IsTrue(Sse.Parse("", "", null).Ok, "a notification may have an empty body");
        }

        [Test]
        public void CanonicalJsonIgnoresKeyOrderAndWholeFloats()
        {
            var a = JObject.Parse("{\"b\":2.0,\"a\":{\"y\":1,\"x\":[1,2]}}");
            var b = JObject.Parse("{\"a\":{\"x\":[1,2],\"y\":1},\"b\":2}");
            Assert.AreEqual(Json.Canonical(a), Json.Canonical(b));
            Assert.AreNotEqual(Json.Canonical(a), Json.Canonical(JObject.Parse("{\"a\":{\"x\":[2,1],\"y\":1},\"b\":2}")));
        }

        [Test]
        public void TruthyAcceptsSchemaShapes()
        {
            Assert.IsTrue(Json.Truthy(JToken.Parse("{\"always\":true}")));
            Assert.IsTrue(Json.Truthy("true"));
            Assert.IsFalse(Json.Truthy("no"));
            Assert.IsFalse(Json.Truthy(null));
        }

        [Test]
        public void IsoDates()
        {
            Assert.AreEqual(1759569052.353, Json.IsoToUnix("2025-10-04T09:10:52.353Z"), 0.001);
            Assert.AreEqual(-1, Json.IsoToUnix("never"));
        }
    }

    public class QuoteTests
    {
        [Test]
        public void AQuoteIsSingleUseAndBoundToTheRequest()
        {
            var book = new QuoteBook();
            var fp = QuoteBook.Fingerprint("m", new JObject { ["prompt"] = "a", ["n"] = 1 }, "team/proj");
            Assert.AreEqual(fp, QuoteBook.Fingerprint("m", new JObject { ["n"] = 1.0, ["prompt"] = "a" }, "team/proj"));
            Assert.AreNotEqual(fp, QuoteBook.Fingerprint("m", new JObject { ["prompt"] = "b", ["n"] = 1 }, "team/proj"));
            Assert.AreNotEqual(fp, QuoteBook.Fingerprint("m", new JObject { ["prompt"] = "a", ["n"] = 1 }, "other/proj"));
            book.Store(fp, 11, 0, 100);
            Assert.IsNotNull(book.Usable(fp, 200));
            Assert.IsNull(book.Usable(fp, 100 + QuoteBook.MaxAgeSeconds + 1), "stale");
            Assert.IsNotNull(book.Consume(fp, 200));
            Assert.IsNull(book.Consume(fp, 200), "consumed");
        }
    }

    public class LedgerTests
    {
        string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(TempDir.Make("ledger"), "jobs.json");

        [Test]
        public void AnInterruptedSubmissionBecomesUnknownAndBlocks()
        {
            var ledger = new JobLedger(_path);
            var row = ledger.AddIntent("image", "m", "Model", new JObject { ["prompt"] = "chest" }, "fp1", 11, 1000);
            Assert.AreEqual(JobState.Submitting, row.State);
            Assert.AreEqual("chest", row.Prompt);
            var reloaded = new JobLedger(_path);
            reloaded.Load();
            Assert.AreEqual(JobState.Unknown, reloaded.Jobs[0].State);
            Assert.IsTrue(reloaded.Blocks("fp1"));
            Assert.IsFalse(reloaded.Blocks("fp2"));
            reloaded.Dismiss(reloaded.Jobs[0].LocalId, 1001);
            Assert.IsFalse(reloaded.Blocks("fp1"));
        }

        [Test]
        public void ReconcileLinksOnlyAUniqueMatch()
        {
            var ledger = new JobLedger(_path);
            var created = Json.IsoToUnix("2026-10-02T21:22:13.168Z");
            var row = ledger.AddIntent("video", "model_bytedance-seedance-2-5", "Seedance", new JObject(), "fp", 444, created - 2);
            ledger.MarkUnknown(row.LocalId, "lost", created);
            var jobs = (JArray)Fixtures.Structured("jobs.sse.txt")["jobs"];
            var linked = ledger.Reconcile(jobs, created + 10);
            var matches = jobs.Count(j => (string)j["modelId"] == "model_bytedance-seedance-2-5"
                && System.Math.Abs(Json.IsoToUnix((string)j["createdAt"]) - created) < JobLedger.ReconcileWindowSeconds);
            if (matches == 1)
            {
                Assert.AreEqual(1, linked);
                Assert.AreEqual("job_test00000000000000000002", ledger.Jobs[0].JobId);
                Assert.AreEqual(JobState.Success, ledger.Jobs[0].State);
            }
            else
            {
                Assert.AreEqual(0, linked, "two candidates: the user decides");
            }
        }

        [Test]
        public void ApplyJobReadsAssetsCostAndModeration()
        {
            var ledger = new JobLedger(_path);
            var row = ledger.AddIntent("image", "m", "M", new JObject(), "fp", 14, 1);
            ledger.MarkSubmitted(row.LocalId, "job_test00000000000000000001", "queued", 2);
            var job = (JObject)Fixtures.Structured("job.sse.txt")["job"];
            ledger.ApplyJob(job, 3);
            Assert.AreEqual(JobState.Success, row.State);
            Assert.AreEqual(14, row.ChargedCu);
            Assert.AreEqual("asset_test00000000000000000001", row.AssetIds.Single());
            var blocked = ledger.AddIntent("image", "m", "M", new JObject(), "fp2", 14, 4);
            ledger.MarkSubmitted(blocked.LocalId, "job_2", "queued", 5);
            ledger.ApplyJob(new JObject { ["jobId"] = "job_2", ["status"] = "failure", ["error"] = "Image flagged by moderation" }, 6);
            Assert.AreEqual(JobState.Blocked, blocked.State);
        }

        [Test]
        public void CancelRequestedHoldsUntilScenarioReportsIt()
        {
            var ledger = new JobLedger(_path);
            var row = ledger.AddIntent("video", "m", "M", new JObject(), "fp", 1, 1);
            ledger.MarkSubmitted(row.LocalId, "job_c", "running", 2);
            ledger.MarkCancelRequested(row.LocalId, 3);
            ledger.ApplyJob(new JObject { ["jobId"] = "job_c", ["status"] = "in-progress" }, 4);
            Assert.AreEqual(JobState.CancelRequested, row.State);
            ledger.ApplyJob(new JObject { ["jobId"] = "job_c", ["status"] = "canceled" }, 5);
            Assert.AreEqual(JobState.Canceled, row.State);
        }

        [Test]
        public void AnUnreadableLedgerIsSetAside()
        {
            File.WriteAllText(_path, "{not json");
            var ledger = new JobLedger(_path);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("job ledger could not be read"));
            ledger.Load();
            Assert.AreEqual(0, ledger.Jobs.Count);
            Assert.IsFalse(File.Exists(_path));
            Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(_path), "jobs.json.unreadable-*").Length);
        }
    }

    public class SchemaFormTests
    {
        [Test]
        public void FlareSchemaPutsThePromptFirst()
        {
            var fields = SchemaForm.FieldsFromSchema(Fixtures.Structured("schema_flare.sse.txt"), OutputTypes.Restrict);
            Assert.AreEqual("prompt", fields[0].Name);
            Assert.AreEqual(FieldType.Prompt, fields[0].Type);
            Assert.AreEqual("prompt", SchemaForm.PromptName(fields));
            var image = SchemaForm.FileInputFor(fields, "image");
            Assert.IsNotNull(image, "Flare takes reference images");
            var values = SchemaForm.InitialValues(fields);
            Assert.Contains("Write a prompt.", SchemaForm.Validate(fields, values));
            values["prompt"] = "  a chest  ";
            Assert.IsEmpty(SchemaForm.Validate(fields, values));
            Assert.AreEqual("a chest", (string)SchemaForm.Payload(fields, values)["prompt"]);
        }

        [Test]
        public void PatinaSchemaHasItsFields()
        {
            var fields = SchemaForm.FieldsFromSchema(Fixtures.Structured("schema_patina.sse.txt"));
            Assert.AreEqual("prompt", fields[0].Name);
            Assert.IsTrue(fields.Any(f => f.IsFile));
        }

        [Test]
        public void WorkflowInputsDefinitionBecomesTheForm()
        {
            // The shape workflow_get returned on 2026-10-18.
            var workflow = JObject.Parse(@"{""id"":""wflow_x"",""inputs_definition"":[
                {""kind"":""image"",""label"":""Character Reference"",""name"":""image1"",""required"":{""always"":true},""type"":""file""},
                {""kind"":""image"",""label"":""Style"",""name"":""image2"",""required"":{""always"":false},""type"":""file_array""},
                {""label"":""Tagline"",""name"":""text1"",""prompt"":true,""promptSpark"":true,""required"":{""always"":false},""type"":""string""}]}");
            var fields = SchemaForm.FieldsFromSchema(workflow);
            Assert.AreEqual(3, fields.Count);
            var image = SchemaForm.FileInputFor(fields, "image");
            Assert.AreEqual("image1", image.Name);
            Assert.AreEqual(FieldType.File, image.Type);
            Assert.IsTrue(image.Required);
            Assert.IsFalse(fields.Single(f => f.Name == "image2").Required);
            Assert.IsTrue(fields.Single(f => f.Name == "text1").PromptSpark);
            var values = SchemaForm.InitialValues(fields);
            Assert.Contains("Add a character reference.", SchemaForm.Validate(fields, values));
            values["image1"] = "asset_a";
            values["image2"] = "asset_b";
            var payload = SchemaForm.Payload(fields, values);
            Assert.AreEqual(JTokenType.String, payload["image1"].Type, "a single file input is sent as one id");
            Assert.AreEqual(JTokenType.Array, payload["image2"].Type);
        }

        [Test]
        public void ValidationAndPayloadTyping()
        {
            var schema = JObject.Parse(@"{""parameters"":[
                {""name"":""prompt"",""type"":""string"",""required"":true,""prompt"":true},
                {""name"":""numOutputs"",""type"":""number"",""min"":1,""max"":4,""step"":1,""default"":1},
                {""name"":""guidance"",""type"":""number"",""min"":0,""max"":10},
                {""name"":""audioFormat"",""type"":""string"",""allowed_values"":[""flac"",""wav"",""mp3""],""default"":""flac""},
                {""name"":""loop"",""type"":""boolean""},
                {""name"":""images"",""type"":""file_array"",""kind"":""image"",""max_length"":2}]}");
            var fields = SchemaForm.FieldsFromSchema(schema, OutputTypes.Restrict);
            var audio = fields.Single(f => f.Name == "audioFormat");
            CollectionAssert.AreEqual(new[] { "wav", "mp3" }, audio.Options.Select(o => (string)o));
            Assert.AreEqual("wav", (string)audio.Default, "an unimportable default is replaced");
            Assert.AreEqual(FieldType.Integer, fields.Single(f => f.Name == "numOutputs").Type);
            var values = new JObject { ["prompt"] = "x", ["numOutputs"] = 9, ["guidance"] = 2.5, ["loop"] = "true", ["images"] = new JArray("a", "b", "c") };
            var problems = SchemaForm.Validate(fields, values);
            Assert.That(problems, Has.Some.Contains("at most 4"));
            Assert.That(problems, Has.Some.Contains("at most 2 items"));
            values["numOutputs"] = 2.0;
            values["images"] = "asset_1";
            var payload = SchemaForm.Payload(fields, values);
            Assert.AreEqual(JTokenType.Integer, payload["numOutputs"].Type);
            Assert.AreEqual(true, (bool)payload["loop"]);
            Assert.AreEqual(1, ((JArray)payload["images"]).Count);
        }

        [Test]
        public void Humanize()
        {
            Assert.AreEqual("Aspect ratio", SchemaForm.Humanize("aspectRatio"));
            Assert.AreEqual("Outputs", SchemaForm.Humanize("numOutputs"));
            Assert.AreEqual("Enable PBR", SchemaForm.Humanize("enablePbr"));
            Assert.AreEqual("Texture prompt", SchemaForm.Humanize("texture_prompt"));
        }
    }

    public class FilesTests
    {
        [Test]
        public void SlugsAndNames()
        {
            Assert.AreEqual("a-stylized-wooden-treasure-chest", Files.Slug("A stylized wooden treasure chest, game prop!", 36));
            Assert.AreEqual("abcdefghij", Files.Slug("abcdefghijklmnop", 10));
            Assert.AreEqual("scenario", Files.Slug("!!!"));
            Assert.AreEqual("Stylized Wooden Treasure", Files.ObjectName("A stylized wooden treasure chest"));
            Assert.AreEqual("Assets/Scenario/Image/chest-000001-normal", Files.BasePath("Assets/Scenario/Image/", "Chest", "asset_test00000000000000000001", "normal"));
            Assert.AreEqual("Assets/Scenario/Models/2026-10-18", Files.OutputFolder("", "Models", new System.DateTime(2026, 10, 18)));
        }

        [Test]
        public void SniffsCommonFormats()
        {
            Assert.AreEqual("png", Files.SniffExtension(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
            Assert.AreEqual("jpg", Files.SniffExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
            Assert.AreEqual("glb", Files.SniffExtension(Encoding.ASCII.GetBytes("glTF\x02\0\0\0")));
            Assert.AreEqual("wav", Files.SniffExtension(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ")));
            Assert.AreEqual("webp", Files.SniffExtension(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")));
            Assert.AreEqual("mp4", Files.SniffExtension(Encoding.ASCII.GetBytes("\0\0\0 ftypisom")));
            Assert.AreEqual("mp3", Files.SniffExtension(Encoding.ASCII.GetBytes("ID3\x04")));
            Assert.AreEqual("", Files.SniffExtension(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void RolesFromMetadata()
        {
            Assert.AreEqual("roughness", Files.RoleFor(JObject.Parse("{\"metadata\":{\"type\":\"texture-smoothness\"}}")));
            Assert.AreEqual("panorama", Files.RoleFor(JObject.Parse("{\"metadata\":{\"type\":\"skybox-base-360\"}}")));
            Assert.AreEqual("main", Files.RoleFor(new JObject()));
        }

        [Test]
        public void OnlyScenarioCdnHostsAreDownloaded()
        {
            Assert.IsTrue(Files.IsScenarioCdn("https://cdn.cloud.scenario.com/assets/x.png?sig=1"));
            Assert.IsFalse(Files.IsScenarioCdn("http://cdn.cloud.scenario.com/x"));
            Assert.IsFalse(Files.IsScenarioCdn("https://cdn.cloud.scenario.com.evil.io/x"));
            Assert.IsFalse(Files.IsScenarioCdn("https://example.com/x"));
        }

        [Test]
        public void UploadTypes()
        {
            Assert.AreEqual("image", Files.UploadTypeFor("a/b.PNG").Kind);
            Assert.AreEqual("model/gltf-binary", Files.UploadTypeFor("x.glb").Mime);
            Assert.IsNull(Files.UploadTypeFor("x.psd"));
        }
    }

    public class CatalogTests
    {
        Catalog CatalogFromFixture()
        {
            var catalog = new Catalog("");
            foreach (var row in (JArray)Fixtures.Structured("models_page.sse.txt")["models"])
                catalog.Models.Add(ModelInfo.FromJson((JObject)row));
            return catalog;
        }

        [Test]
        public void ModelFlags()
        {
            var catalog = CatalogFromFixture();
            var legacy = catalog.Find("model_bytedance-seedance-2-0");
            Assert.IsTrue(legacy.IsLegacy);
            Assert.AreEqual("model_bytedance-seedance-2-5", legacy.ReplacementId);
            Assert.AreEqual("Bytedance", legacy.Provider);
            var gemini = catalog.Find("model_google-gemini-3-1-flash");
            Assert.IsTrue(gemini.IsFeatured);
            Assert.IsFalse(gemini.IsLegacy);
            StringAssert.Contains("Text to image", gemini.CapabilityText);
        }

        [Test]
        public void SearchHidesLegacyAndRanksNames()
        {
            var catalog = CatalogFromFixture();
            var videos = catalog.Search(new Catalog.Query { OutputType = OutputTypes.Video });
            Assert.IsFalse(videos.Any(m => m.IsLegacy));
            var withLegacy = catalog.Search(new Catalog.Query { OutputType = OutputTypes.Video, ShowLegacy = true });
            Assert.IsTrue(withLegacy.Any(m => m.Id == "model_bytedance-seedance-2-0"));
            var gemini = catalog.Search(new Catalog.Query { Text = "gemini" });
            Assert.AreEqual("model_google-gemini-3-1-flash", gemini.First().Id);
            Assert.IsEmpty(catalog.Search(new Catalog.Query { Text = "gemini zzz" }));
            var skyboxes = catalog.Search(new Catalog.Query { OutputType = OutputTypes.Skybox, ShowLegacy = true });
            Assert.IsFalse(skyboxes.Any(m => m.Id == "model_hunyuan-world-skybox-to-splat"), "a splat is not a skybox");
        }

        [Test]
        public void CacheRoundTrip()
        {
            var path = Path.Combine(TempDir.Make("catalog"), "catalog.json");
            var catalog = CatalogFromFixture();
            var saved = new Catalog(path) { Models = catalog.Models, FetchedAt = 500, Scope = "t/p" };
            saved.Save();
            var loaded = new Catalog(path);
            loaded.Load();
            Assert.AreEqual(catalog.Models.Count, loaded.Models.Count);
            Assert.IsTrue(loaded.Find("model_bytedance-seedance-2-0").IsLegacy);
            Assert.IsFalse(loaded.IsStale(600));
            Assert.IsTrue(loaded.IsStale(500 + Catalog.MaxAgeSeconds + 1));
        }

        [Test]
        public void RefreshPagesAndKeepsPrivateModels()
        {
            var server = new FakeMcp();
            server.Tools["models_list"] = (args, i) =>
            {
                var privacy = (string)args["filters"]["privacy"];
                if (privacy == "private")
                    return FakeMcp.Result(new JObject { ["models"] = new JArray(new JObject { ["id"] = "model_mine", ["name"] = "Mine", ["privacy"] = "private", ["capabilities"] = new JArray("txt2img"), ["type"] = "flux.1-lora" }) });
                if (args["page_token"] == null) return FakeMcp.Fixture("models_page.sse.txt");
                return FakeMcp.Result(new JObject { ["models"] = new JArray(new JObject { ["id"] = "model_page2", ["name"] = "Second page", ["capabilities"] = new JArray("txt2img") }) });
            };
            var client = new McpClient(server, new ApiKeyAuth("k", "s"));
            var catalog = new Catalog("");
            var result = Run.Sync(catalog.Refresh(client, "scope", 10));
            Assert.IsTrue(result.Ok, result.Error?.Message);
            Assert.IsNotNull(catalog.Find("model_page2"));
            Assert.IsTrue(catalog.Find("model_mine").IsLora);
            Assert.IsTrue(catalog.Search(new Catalog.Query { TeamOnly = true }).Single().IsPrivate);
        }
    }

    public class OutputTypeTests
    {
        [Test]
        public void ModelsLandInTheirType()
        {
            ModelInfo M(string id, params string[] caps) => new ModelInfo { Id = id, Capabilities = caps.ToList() };
            Assert.AreEqual(OutputTypes.Material, OutputTypes.TypeFor(M("model_patina-material", "txt2img")));
            Assert.AreEqual(OutputTypes.Skybox, OutputTypes.TypeFor(M("model_scenario-skybox-gpt", "txt2img")));
            Assert.AreEqual(OutputTypes.Model3D, OutputTypes.TypeFor(M("model_hunyuan-world-skybox-to-splat", "img23d")));
            Assert.AreEqual(OutputTypes.Video, OutputTypes.TypeFor(M("v", "img2video")));
            Assert.AreEqual(OutputTypes.Audio, OutputTypes.TypeFor(M("a", "txt2audio")));
            Assert.AreEqual(OutputTypes.Image, OutputTypes.TypeFor(M("i", "txt2img")));
        }

        [Test]
        public void EveryTypeHasADefaultAndSteps()
        {
            foreach (var type in OutputTypes.List.Where(t => t.Id != OutputTypes.All))
                Assert.IsNotEmpty(type.QuickPicks, type.Id);
            Assert.AreEqual(9, OutputTypes.StepsFor("image").Count());
            Assert.AreEqual("model_meshy-rigging", OutputTypes.Step("rig").DefaultModel);
            Assert.AreEqual("PBR", (string)OutputTypes.Presets("model_rodin-hyper3d-v2-5")["material"]);
        }
    }
}
