# Changelog

## 0.1.0 (2026-10-18)

First release.

### Features

- Scenario window (Window > Scenario, Alt+Shift+S): Create, Library, Jobs, Workflows, Agent and Settings, a command palette (Ctrl/Cmd+K), toasts, light and dark editor themes, from 320 px wide.
- Sign in with Scenario in the browser (OAuth with PKCE) and a team and project picker, or an API key and secret, or `SCENARIO_API_KEY` and `SCENARIO_API_SECRET`. Secrets stay in the OS keychain.
- Every model in the catalog and the team's own models, with forms built from each model's live schema. Output types Image, Video, 3D, Audio, Material, Skybox and All models, each with quick picks, Suggest (Scenario's ranking for the prompt) and a model browser.
- Price before every paid call, single-use quotes, paid calls sent once, Unknown outcomes blocked until matched, a confirmation threshold and a spending switch (`SCENARIO_NO_SPEND=1`).
- Import and placement per type: sprites and textures, prefabs from GLB through glTFast, pipeline-correct PBR materials, panoramic skyboxes, audio sources, video players. One Undo step each. Provenance in the asset's `.meta`, the `Scenario` label and an Inspector header.
- Typed next steps on images, 3D models, videos and audio, from result cards, the Project window and the Inspector.
- Library of the Scenario project's assets, and the list of Scenario assets in the Unity project.
- Workflows: team and featured workflows, their inputs as a form, priced and run like any model.
- Prompt Spark to improve a prompt (priced first).
- Agent bridge: a local MCP server (`127.0.0.1`, bearer token) with Scenario tools, scene tools and a Scene view capture, one-click setup for Claude Code and setup lines for Codex, Cursor and VS Code, per-call and session spending limits with approvals in the window.
- `Scenario.ScenarioApi` for editor scripts and CI.
