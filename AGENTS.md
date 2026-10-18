# unity-plugin: agent instructions

Root `AGENTS.md` is the repository rulebook; `.claude/CLAUDE.md` links here.
[README.md](README.md) describes the plugin for users.

## What this repository is

A Unity 6 editor package, C# only, in `Packages/com.scenario.unity`. It gives a
person (the Scenario window), an AI agent (a local MCP server in the editor) and
editor scripts (`Scenario.ScenarioApi`) every Scenario capability through the
Scenario MCP server (`https://mcp.scenario.com/mcp?toolsets=full`: JSON-RPC 2.0
over HTTP, SSE replies, OAuth bearer or Basic auth with an API key and secret),
and imports the results as wired Unity assets. The repository root is a Unity
6.3 URP project that hosts the package and its tests. `Assets/ScenarioDev/` holds
dev-only tools (screenshots, the live contract check, the paid acceptance run)
that never ship.

## Code map

In `Packages/com.scenario.unity/Editor/` (assembly `Scenario.Editor`):

- `Core/` (no UI, unit-tested):
  - `McpClient.cs`: Scenario MCP calls, sessions, error kinds, retry policy
    (reads and price checks only), estimate and submit.
  - `Sse.cs`, `Json.cs`, `ScenarioResult.cs`: replies, JSON helpers, results and errors.
  - `Auth.cs`, `OAuth.cs`, `CredentialStore.cs`: API key, OAuth (PKCE, dynamic
    client registration, loopback redirect), environment, OS keychain.
  - `Quote.cs`: canonical JSON, fingerprints, single-use quotes.
  - `JobLedger.cs`: durable job list (`Library/Scenario/jobs.json`), states,
    reconciles Unknown rows with `jobs_list`.
  - `SchemaForm.cs`: model schema or workflow `inputs_definition` to form
    fields, validation, payload.
  - `Catalog.cs`, `OutputTypes.cs`: the model catalog, output types, quick
    picks, presets and typed steps.
  - `Files.cs`, `Transport.cs`: names, roles, sniffing, CDN allowlist, HTTP.
- `Services/ScenarioService.cs`: the action layer shared by the window, the
  agent bridge and the C# API: account, workspace, estimate, generate, poll,
  import, place.
- `Unity/`: importer, placer, material and skybox builders, uploader, capture,
  provenance and the Inspector header, context menus, main-thread dispatcher,
  settings, `ScenarioApi`.
- `UI/`: `ScenarioWindow` (shell), `Views/`, `Overlays/` (model browser,
  command palette), `Components/`, `Styles/Scenario.uss`, `Icons/` (built by
  `tools/build_icons.py`).
- `Agent/`: `AgentServer` (HTTP on 127.0.0.1, token, Origin and Host checks,
  JSON-RPC), `AgentTools` (tool registry), `ClientSetup` (setup lines).

Tests: `Packages/com.scenario.unity/Tests/Editor/` (assembly
`Scenario.Editor.Tests`), against the scripted server `FakeMcp` in
`TestSupport.cs` and recorded replies in `Fixtures/`.

## Spending rules

Every change keeps them.

1. A price check is `model_run` or `workflow_run` with `dry_run: true`; if it
   returns a job id, automatic price checks stop (`EstimateAlarm`).
2. Generate needs an unused quote whose fingerprint matches the request now; it
   consumes it.
3. A paid call (`model_run` without dry run, `workflow_run`, `prompt_spark`,
   uploads) is sent once. A transport failure on it marks the job Unknown, which
   blocks the same fingerprint until reconciled.
4. The intent row is written to the ledger before the paid request.
5. `SCENARIO_NO_SPEND=1` and the Settings switch refuse paid calls before any
   request.
6. Secrets never go under `Assets/`, `ProjectSettings/` or `Packages/`.

## Commands

Unity 6000.3.21f1 (the version in `ProjectSettings/ProjectVersion.txt`).

```sh
# EditMode tests; keep graphics on so the window tests run (no window is shown in batch mode)
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml

# Free live contract check (test key in SCENARIO_API_KEY and SCENARIO_API_SECRET)
SCENARIO_NO_SPEND=1 SCENARIO_UNITY_SERVICE=1 Unity -batchmode -projectPath . -executeMethod ScenarioDev.ContractCheck.Run

# PAID acceptance run: never without explicit authorization for the project and budget
SCENARIO_ACCEPT=1 SCENARIO_UNITY_SERVICE=1 Unity -batchmode -projectPath . -executeMethod ScenarioDev.Acceptance.Run

# Icons from SVG
uv run -q --with resvg-py tools/build_icons.py
```

`SCENARIO_UNITY_SERVICE=1` lets the service poll and import in batch mode;
`SCENARIO_UNITY_AGENT=1` starts the agent bridge in batch mode, with
`SCENARIO_UNITY_AGENT_TOKEN` as its token.

CI (`.github/workflows/ci.yml`) runs the EditMode tests with GameCI when the
Unity license secrets are set (`UNITY_LICENSE`, `UNITY_EMAIL`,
`UNITY_PASSWORD`), and the commit and title checks on every pull request.

## Rules

- Commit the `.meta` file Unity writes next to every new file in the package.
- Tests restore what they change: user settings, project settings, scenes,
  environment variables, `RenderSettings.skybox`. Files they import go to a
  temporary `Assets/` folder they remove.
- The window tests skip without a graphics device; run them before any UI change.
- No em dashes, in code, docs, commit messages and PR titles.
- No credentials, session IDs, personal paths, account or project records, or
  spend anecdotes in code, docs, commits or PRs. Recorded fixtures carry fake
  ids. The exception is `tests/live/LEDGER.md`: the acceptance run records each
  authorized paid run there (job id, CU).
- Fresh code: do not copy code from the Scenario Blender Plugin (GPL) into this
  MIT package.

## Commits and pull requests

- Branch from `main`; PRs are squash-merged, so the PR title becomes the commit
  header and the changelog line. Titles follow Conventional Commits with a scope
  from `commitlint.config.mjs`. `fix` only for behavior users of a released
  version saw; tooling and CI corrections are `chore`, `ci`, `build` or `docs`.
- Fill `.github/pull_request_template.md`; tick only what you ran.
- Screenshots of a visible change go in the PR description, never in a commit
  (the README image in `docs/images/` is the exception).
- Name the contributing agent in the PR and keep its co-author trailer on
  commits and in the squash message.

## Releases

release-please keeps a release PR open on `main` (`release-please-config.json`).
Merging it tags `unity-plugin-vX.Y.Z` and drafts the GitHub release. Never edit
versions or `CHANGELOG.md` by hand: release-please bumps
`Packages/com.scenario.unity/package.json`, `McpClient.Version` and the README
(`x-release-please` markers), and writes the changelog from the squashed PR
titles. Users pin a release with `#unity-plugin-vX.Y.Z` on the git URL.
