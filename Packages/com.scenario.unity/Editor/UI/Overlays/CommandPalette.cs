using System;
using System.Collections.Generic;
using System.Linq;
using Scenario.Editor.Agent;
using Scenario.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scenario.Editor.UI
{
    /// <summary>
    /// Ctrl/Cmd+K: every action, model, step on the selection and recent job,
    /// filtered as you type.
    /// </summary>
    public sealed class CommandPalette : VisualElement
    {
        sealed class Command
        {
            public string Title;
            public string Detail = "";
            public string Icon = "arrow-right";
            public string Group;
            public Action Run;
        }

        readonly ScenarioWindow _window;
        readonly TextField _search;
        readonly ListView _list;
        readonly List<Command> _all;
        List<Command> _items = new List<Command>();

        ScenarioService S => ScenarioService.Instance;

        public CommandPalette(ScenarioWindow window)
        {
            _window = window;
            AddToClassList("sc-sheet");
            style.height = Length.Percent(80);
            name = "command-palette";
            style.maxHeight = Length.Percent(70);
            var header = new VisualElement();
            header.AddToClassList("sc-sheet-header");
            _search = Ui.Search("Type a command, a model or a step", _ => Filter());
            _search.name = "palette-search";
            _search.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            header.Add(_search);
            Add(header);
            _list = new ListView
            {
                fixedItemHeight = 34,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.AddToClassList("sc-fill");
            _list.style.minHeight = 200;
            _list.itemsChosen += chosen =>
            {
                var command = chosen.OfType<Command>().FirstOrDefault();
                if (command != null) Run(command);
            };
            Add(_list);
            var footer = new VisualElement();
            footer.AddToClassList("sc-sheet-footer");
            footer.Add(new Label("↑↓ move   ↩ run   Esc close"));
            Add(footer);
            _all = BuildCommands();
            Filter();
            RegisterCallback<AttachToPanelEvent>(_ => schedule.Execute(() => _search.Focus()).StartingIn(30));
        }

        List<Command> BuildCommands()
        {
            var list = new List<Command>();
            foreach (var type in OutputTypes.List.Where(t => t.Id != OutputTypes.All))
            {
                var t = type;
                list.Add(new Command
                {
                    Title = $"Generate {(t.Id == OutputTypes.Model3D ? "a 3D model" : t.Title.ToLowerInvariant())}",
                    Detail = t.Hint, Icon = t.Icon, Group = "Create",
                    Run = () =>
                    {
                        _window.Show("create");
                        _window.Create.SelectType(t.Id);
                        _window.Create.FocusPrompt();
                    },
                });
            }

            var selected = Selection.activeObject;
            var kind = InputSlot.KindOf(selected);
            if (selected != null && kind.Length > 0)
            {
                var name = selected.name;
                list.Add(new Command
                {
                    Title = $"Use {name} as input", Detail = "Attach the selection to the current model", Icon = "upload", Group = "Selection",
                    Run = () =>
                    {
                        _window.Show("create");
                        _window.Create.AttachObjects(Selection.objects);
                    },
                });
                foreach (var step in OutputTypes.StepsFor(kind))
                {
                    var s = step;
                    list.Add(new Command
                    {
                        Title = $"{s.Title}: {name}", Detail = "Opens Create with the model and the asset; you see the price before sending", Icon = "wand", Group = "Selection",
                        Run = () => _window.Create.StartStep(s, new SlotItem { Source = selected, Kind = kind, Label = name }),
                    });
                }
            }

            list.Add(new Command { Title = "Library", Detail = "Your Scenario assets and the ones in this project", Icon = "library", Group = "Go to", Run = () => _window.Show("library") });
            list.Add(new Command { Title = "Jobs", Detail = "Everything sent from this project", Icon = "jobs", Group = "Go to", Run = () => _window.Show("jobs") });
            list.Add(new Command { Title = "Workflows", Detail = "Run team and featured workflows", Icon = "workflows", Group = "Go to", Run = () => _window.Show("workflows") });
            list.Add(new Command { Title = "Agent", Detail = "Let Claude Code, Codex or Cursor use Scenario and this project", Icon = "agent", Group = "Go to", Run = () => _window.Show("agent") });
            list.Add(new Command { Title = "Settings", Icon = "settings", Group = "Go to", Run = () => _window.Show("settings") });
            list.Add(new Command { Title = "Refresh the model catalog", Icon = "refresh", Group = "Actions", Run = () => _ = S.RefreshCatalog(true) });
            if (S.Ledger.Unresolved().Count > 0)
                list.Add(new Command { Title = "Look for unknown requests on Scenario", Icon = "search", Group = "Actions", Run = () => _ = S.CheckUnknown() });
            list.Add(new Command
            {
                Title = S.Client.SpendingDisabled ? "Turn spending on" : "Turn spending off", Detail = "Stops every paid call from this editor", Icon = "lock", Group = "Actions",
                Run = () => S.SetSpendingDisabled(!ScenarioUserSettings.instance.SpendingDisabled),
            });
            list.Add(new Command
            {
                Title = AgentServer.Running ? "Turn the agent bridge off" : "Turn the agent bridge on", Icon = "agent", Group = "Actions",
                Run = () => AgentServer.SetEnabled(!AgentServer.Running),
            });
            if (S.Method == AuthMethod.OAuth)
                foreach (var team in S.Teams)
                    foreach (var project in team.Projects)
                    {
                        var t = team;
                        var p = project;
                        if (p.Id == S.Client.ProjectId) continue;
                        list.Add(new Command { Title = $"Switch to {p.Name}", Detail = t.Name, Icon = "layers", Group = "Workspace", Run = () => S.SelectWorkspace(t.Id, p.Id) });
                    }
            list.Add(new Command { Title = "Open Scenario on the web", Icon = "external", Group = "Actions", Run = () => Application.OpenURL(S.HomeLink) });
            if (!S.FromEnvironment) list.Add(new Command { Title = "Sign out", Icon = "logout", Group = "Account", Run = S.SignOut });

            foreach (var row in S.Ledger.Jobs.Where(r => r.OutputType != "prompt" && r.State != JobState.Dismissed).Take(12))
            {
                var r = row;
                list.Add(new Command
                {
                    Title = Files.ObjectName(r.Prompt, r.ModelName), Detail = $"{r.ModelName} · {r.State} · {Ui.Ago(r.CreatedAt)}", Icon = "jobs", Group = "Recent",
                    Run = () =>
                    {
                        var asset = r.Files.Count > 0 ? AssetDatabase.LoadMainAssetAtPath(r.Files[0]) : null;
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                        else _window.Show("jobs");
                    },
                });
            }
            return list;
        }

        IEnumerable<Command> ModelCommands(string[] words)
        {
            if (words.Length == 0) return Enumerable.Empty<Command>();
            return S.Catalog.Search(new Catalog.Query { Text = string.Join(" ", words), Favorites = ScenarioUserSettings.instance.Favorites })
                .Take(12)
                .Select(m => new Command
                {
                    Title = m.Name, Detail = $"{m.Provider} · {m.CapabilityText}", Icon = OutputTypes.Get(OutputTypes.TypeFor(m)).Icon, Group = "Models",
                    Run = () =>
                    {
                        _window.Show("create");
                        _window.Create.SelectType(OutputTypes.TypeFor(m));
                        _window.Create.SelectModel(m.Id);
                    },
                });
        }

        void Filter()
        {
            var words = (_search.value ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(Command command, int score)>();
            foreach (var command in _all)
            {
                var title = command.Title.ToLowerInvariant();
                var rest = (command.Detail + " " + command.Group).ToLowerInvariant();
                var score = 1;
                foreach (var word in words)
                {
                    if (title.StartsWith(word)) score += 6;
                    else if (title.Contains(" " + word)) score += 4;
                    else if (title.Contains(word)) score += 3;
                    else if (rest.Contains(word)) score += 1;
                    else
                    {
                        score = 0;
                        break;
                    }
                }
                if (score > 0) scored.Add((command, score));
            }
            _items = scored.OrderByDescending(s => s.score).Select(s => s.command).Take(40).ToList();
            _items.AddRange(ModelCommands(words));
            _list.itemsSource = _items;
            _list.Rebuild();
            if (_items.Count > 0) _list.SetSelectionWithoutNotify(new[] { 0 });
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("sc-list-row");
            var icon = new VisualElement { name = "icon" };
            icon.AddToClassList("sc-icon");
            icon.style.marginRight = 10;
            row.Add(icon);
            var title = new Label { name = "title" };
            title.AddToClassList("sc-ellipsis");
            title.style.flexShrink = 1;
            row.Add(title);
            var detail = new Label { name = "detail" };
            detail.AddToClassList("sc-faint");
            detail.AddToClassList("sc-small");
            detail.AddToClassList("sc-ellipsis");
            detail.style.marginLeft = 8;
            detail.style.flexShrink = 1;
            detail.style.flexGrow = 1;
            row.Add(detail);
            var group = new Label { name = "group" };
            group.AddToClassList("sc-faint");
            group.AddToClassList("sc-small");
            row.Add(group);
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _items.Count) return;
            var command = _items[index];
            Ui.SetIcon(row.Q("icon"), command.Icon);
            row.Q<Label>("title").text = command.Title;
            row.Q<Label>("detail").text = command.Detail;
            row.Q<Label>("group").text = command.Group;
        }

        void Run(Command command)
        {
            _window.CloseOverlay();
            command.Run();
        }

        void OnKey(KeyDownEvent e)
        {
            if (_items.Count == 0) return;
            var index = Mathf.Max(0, _list.selectedIndex);
            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    index = Mathf.Min(_items.Count - 1, index + 1);
                    break;
                case KeyCode.UpArrow:
                    index = Mathf.Max(0, index - 1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    e.StopPropagation();
                    Run(_items[index]);
                    return;
                default:
                    return;
            }
            e.StopPropagation();
            _list.SetSelection(index);
            _list.ScrollToItem(index);
        }
    }
}
