using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Scenario.Editor.Core;
using Scenario.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Scenario.Editor.Tests
{
    /// <summary>
    /// The window on a scripted Scenario: every view and overlay builds without an
    /// error in the console, and browsing never sends a paid call.
    /// </summary>
    public class UiTests
    {
        FakeMcp _server;
        ScenarioService _s;
        ScenarioWindow _window;
        string _settings;

        [SetUp]
        public void SetUp()
        {
            // Editor windows need a graphics device: CI runs with -nographics skip these.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("The window tests need a graphics device (run without -nographics).");
            _settings = EditorJsonUtility.ToJson(ScenarioUserSettings.instance);
            _server = new FakeMcp();
            _server.Tools["teams_list"] = (args, i) => FakeMcp.Fixture("teams.sse.txt");
            _server.Tools["models_list"] = (args, i) => FakeMcp.Fixture("models_page.sse.txt");
            _server.Tools["model_schema_get"] = (args, i) => FakeMcp.Fixture("schema_flare.sse.txt");
            _server.Tools["model_run"] = (args, i) => FakeMcp.Fixture("estimate.sse.txt");
            _server.Tools["recommend"] = (args, i) => FakeMcp.Fixture("recommend_3d.sse.txt");
            _server.Tools["workflows_list"] = (args, i) => FakeMcp.Fixture("workflows.sse.txt");
            _server.Tools["search"] = (args, i) => FakeMcp.Result(new JObject { ["assets"] = new JArray(), ["estimatedTotalHits"] = 0, ["offset"] = 0 });
            _server.Tools["jobs_list"] = (args, i) => FakeMcp.Fixture("jobs.sse.txt");
            _s = ScenarioService.UseForTests(_server, TempDir.Make("ui"));
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings == null) return;
            if (_window != null) _window.Close();
            ScenarioService.ResetForTests();
            EditorJsonUtility.FromJsonOverwrite(_settings, ScenarioUserSettings.instance);
        }

        static IEnumerator Frames(int count = 4)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        VisualElement Q(string name) => _window.rootVisualElement.Q(name);

        void NoPaidCall()
        {
            for (var i = 0; i < _server.ToolCalls.Count; i++)
                if (_server.ToolCalls[i] == "model_run" || _server.ToolCalls[i] == "workflow_run" || _server.ToolCalls[i] == "prompt_spark")
                    Assert.AreEqual(true, (bool?)_server.ToolArguments[i]["dry_run"], $"{_server.ToolCalls[i]} without dry_run");
        }

        [UnityTest]
        public IEnumerator SignedOut_ShowsSignIn_AndTheKeyForm()
        {
            _window = ScenarioWindow.Open();
            yield return Frames();
            Assert.IsNotNull(Q("sign-in-browser") ?? Q("use-api-key"), "the sign-in view is up");
            var useKey = Q("use-api-key");
            Assert.IsNotNull(useKey);
            var mouseUp = new Event { type = EventType.MouseUp, mousePosition = useKey.worldBound.center, button = 0, clickCount = 1 };
            using (var click = ClickEvent.GetPooled(mouseUp))
            {
                click.target = useKey;
                useKey.SendEvent(click);
            }
            yield return Frames();
            Assert.IsTrue(_window.rootVisualElement.Query<TextField>().ToList().Count >= 2, "key and secret fields");
            Assert.AreEqual(0, _server.ToolCalls.Count, "nothing is sent before sign-in");
        }

        T Q<T>(string name) where T : VisualElement => _window.rootVisualElement.Q<T>(name);

        [UnityTest]
        public IEnumerator SignedIn_EveryViewAndOverlayBuilds_WithoutSpending()
        {
            Assert.IsTrue(Run.Sync(_s.SignInWithApiKey("key_test", "secret_test")), _s.SignInError);
            ScenarioUserSettings.instance.View = "create";
            _window = ScenarioWindow.Open();
            _window.position = new Rect(40, 40, 460, 780);
            yield return Frames(8);

            foreach (var type in OutputTypes.List.Select(t => t.Id))
            {
                _window.Create.SelectType(type, true);
                yield return Frames(2);
            }
            _window.Create.SelectType(OutputTypes.Image, true);
            _window.Create.SetPrompt("A lantern on a mossy rock");
            yield return Frames(4);

            foreach (var id in new[] { "library", "jobs", "workflows", "agent", "settings", "create" })
            {
                _window.Show(id);
                yield return Frames(4);
                Assert.IsNotNull(_window.ViewForTests(id).panel, $"{id} is shown");
            }

            _window.OpenPalette();
            yield return Frames(2);
            Assert.IsTrue(_window.HasOverlay, "the command palette opens");
            var search = Q<TextField>("palette-search");
            Assert.IsNotNull(search);
            search.value = "sky";
            yield return Frames(2);
            _window.CloseOverlay();
            Assert.IsFalse(_window.HasOverlay);

            _window.OpenOverlay(new ModelBrowser(_window, OutputTypes.Image, "", _ => { }));
            yield return Frames(4);
            Assert.IsNotNull(Q<TextField>("model-search"), "the model browser opens");
            _window.CloseOverlay();

            _window.Toast("Imported.", "success");
            _window.Toast("Price unavailable.", "error");
            yield return Frames(2);

            // Wide layout.
            _window.position = new Rect(40, 40, 1040, 720);
            yield return Frames(6);
            Assert.IsTrue(_window.rootVisualElement.ClassListContains("sc-wide") || _window.rootVisualElement.resolvedStyle.width < 560);

            NoPaidCall();
        }
    }
}
