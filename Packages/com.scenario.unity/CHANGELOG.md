# Changelog

## 0.1.0 (2026-10-19)

First release.

### Features

- Scenario window (Window > Scenario, Alt+Shift+S): Create, Library, Jobs, Workflows, Agent and Settings, a command palette (Ctrl/Cmd+K), toasts, light and dark editor themes, from 320 px wide.
- Sign in with Scenario in the browser (OAuth with PKCE) and a team and project picker, or an API key and secret, or `SCENARIO_API_KEY` and `SCENARIO_API_SECRET`. Secrets stay in the OS keychain.
- Every model in the catalog and the team's own models, with forms built from each model's live schema. Output types Image, Video, 3D, Motion, Audio, Material, Skybox and All models, each with quick picks, Suggest (Scenario's ranking for the prompt) and a model browser.
- Price before every paid call, single-use quotes, paid calls sent once, Unknown outcomes blocked until matched, a confirmation threshold and a spending switch (`SCENARIO_NO_SPEND=1`).
- Import and placement per type: sprites and textures, prefabs from GLB through glTFast, pipeline-correct PBR materials, panoramic skyboxes, audio sources, video players. One Undo step each. Provenance in the asset's `.meta`, the `Scenario` label and an Inspector header.
- Typed next steps on images, 3D models, videos and audio, from result cards, the Project window and the Inspector.
- Every 3D operation on Scenario as a step on a model, in four groups: Texture (retexture, UV unwrap), Shape (edit with a prompt, retopology, remesh to a polycount, refine from an image), Rig and animate (rig a character or a creature, animate from Meshy's motion library, from a prompt or from a video) and Parts and style (split into parts, stylize as LEGO, voxels or Voronoi).
- Motion output type: character animation from a prompt or a video (Cartwheel, Uthana, Meshy), with a searchable picker for Meshy's 656 library motions.
- 3D results import whole: a rig set (rigged mesh plus walk and run files) gets one Animator controller with every clip, played on the rigged mesh; models stored in another format (FBX, `.blend`, `.vox`) are converted to GLB on download; models without a material get the pipeline's default one; rigs written in centimeters are placed at meter scale; every model gets a plain scene root, so previews show it standing and the files of one rig share a hierarchy.
- Inputs a model needs beyond its schema are asked for before pricing (a character for Cartwheel motions, a reference image for YVO3D retexture), and GLB inputs are sent as an FBX copy to models that refuse GLB.
- Library of the Scenario project's assets, and the list of Scenario assets in the Unity project.
- Workflows: team and featured workflows, their inputs as a form, priced and run like any model.
- Prompt Spark to improve a prompt (priced first).
- Agent bridge: a local MCP server (`127.0.0.1`, bearer token) with Scenario tools (including `motions_find` for the motion library), scene tools and a capture of the Scene view, a camera or, when there is neither, an overview of the open scenes, one-click setup for Claude Code and setup lines for Codex, Cursor and VS Code, per-call and session spending limits with approvals in the window.
- `Scenario.ScenarioApi` for editor scripts and CI.
