---
name: scenario-unity
description: Use when an MCP server named scenario-unity (Scenario for Unity, http://127.0.0.1:8791/mcp) is connected, to generate images, video, 3D models, PBR materials, skyboxes or audio with Scenario models, to edit, retexture, remesh, rig, animate or split 3D models, and to place the results in an open Unity scene, or to inspect and edit that scene.
---

# Scenario for Unity: agent guide

The Unity Editor serves this MCP server while the user has **Agents can use Scenario and this project** on in the Scenario window. Every tool runs inside the editor: generated files land in the project's `Assets/`, scene edits are Undo steps, and paid calls go through the same price checks and approval rules as the window.

## Start

1. `scenario_status`: account, the Scenario project that is billed, whether spending is on, your per-call limit and the session budget left, render pipeline, 2D or 3D, the active scene. If it says signed out, ask the user to sign in from the Scenario window; nothing else works until then.
2. `scene_info`: hierarchy paths, components, selection. Use those paths as `target` and `path` everywhere.

## Money

Costs are in Creative Units (CU).

- `estimate` is free and returns the exact price of a request. Call it before `generate` or `workflow_run` when the user cares about cost or the request is large (video, 3D, many images).
- `generate`, `workflow_run` and `prompt_spark` are paid. Pass `max_cu` when the user gave a budget: above it nothing is sent.
- Above the per-call limit or the session budget, the call waits up to two minutes for the user's **Approve** in Unity. Tell the user you are waiting. "Not approved" means nothing was sent; do not retry unless the user asks.
- **Unknown outcome.** If a paid call returns "The outcome is unknown", the request may have run and been charged. Never call it again for the same request. Call `jobs` with its `local_id` a minute later.
- If spending is off (Settings or `SCENARIO_NO_SPEND=1`), paid tools refuse before sending. Pricing, browsing, imports, uploads and scene tools still work.
- `upload_reference`, `import_asset`, `models_find`, `motions_find`, `model_schema` and `estimate` are free.

## Pick a model

- `models_find` with `output_type` (image, video, model3d, motion, audio, material, skybox) lists the catalog, including the team's own trained models.
- `models_find` with `recommend_for_prompt` returns Scenario's ranked picks for a prompt, with cost and speed notes. Prefer it when the user did not name a model.
- `model_schema` gives the inputs: names, options, ranges, defaults, which inputs are files. Send only parameters the schema lists; unknown names are refused before pricing.

## Generate and place

`generate` prices the request, sends it once, waits, imports the files and places the result:

| output_type | Without `target` | With `target` |
|---|---|---|
| image | texture (sprite in a 2D project) on a new quad or sprite | set on its SpriteRenderer, Image, RawImage or renderer |
| material | material for the active pipeline with its maps, on a new sphere | set on the target's renderers and its children's |
| skybox | `Skybox/Panoramic` material set as the scene skybox | same |
| model3d | GLB imported as a prefab, instance at the Scene view pivot | instance placed next to the target |
| motion | animated GLB with an Animator Controller (one state per clip), instance at the pivot; it plays in Play mode | instance placed next to the target |
| audio | AudioClip on a new AudioSource | on the target's AudioSource (added if missing) |
| video | VideoClip on a new screen quad | on the target's VideoPlayer (added if missing) |

The result has `files` (Unity asset paths), `placed` (hierarchy paths), `asset_ids` (Scenario ids, reusable as inputs), `job_id`, `quoted_cu` and `charged_cu`. `place: false` imports without touching the scene. If `next` says the job is still running, call `jobs` with `local_id` and `wait_seconds`.

File inputs (reference images, a start frame, a 3D model, audio) take a Scenario asset id or a Unity asset path; Unity files are uploaded once, free.

3D results, what to expect:

- One model per result: the OBJ, MTL and PBR-map copies some models add are skipped. FBX, Blender and VOX results arrive as GLB.
- A rig that returns walk and run files (Meshy Rigging, Tripo with `includeRiggedModel`) is one model whose controller holds every clip. Rigs written in centimeters are placed at 1% scale.
- Gaussian splats (TripoSplat, HY World, Marble) are saved as `.ply` or `.spz` and not placed: tell the user Unity needs a Gaussian splat renderer package.
- Meshy 7.1 defaults to a 30,000-triangle remesh at the same price; send `shouldRemesh: false` for the full mesh (about a million triangles).

## Chain steps

Feed one result into the next model through its file input:

- Image to 3D: `generate` an image, then a model3d model (for example Meshy 7.1 Image to 3D) with the image's asset id.
- Image to material or skybox: PATINA Image to Maps, HY World Image to Skybox.
- Image to video: an img2video model with the image as start frame.
- Clean up: background removal, upscale, vectorize, pixelate.
- 3D texture: retexture (Meshy 7 Retexture, Tripo 3.0 Texturing, Tencent Texture Edit, Trellis 2, YVO3D), UV unwrap (Tripo Smart UV, Meshy, Tencent).
- 3D shape: edit with a prompt (Claude Opus 5.5 3D, Astra 3D; prompt only, no reference image), retopology (Tripo Retopology v3, Hunyuan Polygen), remesh to a polycount (Meshy Remesh), refine from an image (UltraShape).
- Rig and animate: rig a character (Uthana, Meshy, Cartwheel, Tripo), rig a creature (Tripo Rigging 2.5, `rigType`), a library motion (Meshy Animation: `actionId`, for example 30 Casual Walk, 106 Confident Walk, 4 Attack, 22 FunnyDancing 01; `motions_find` searches all 656 by name or category), a motion from a prompt (Cartwheel, Uthana) or from a video (Cartwheel, Uthana).
- Parts and style: split into parts (Tripo Segmentation v2, Hunyuan 3D Part, Hitem3D Split, Rodin Bang), stylize (Tripo Stylization: LEGO, voxel, Voronoi; Hitem3D Multicolor).
- Inputs Scenario needs although the schema says optional: Cartwheel motions need `characterModel` (a 3D model) or `characterFile` (an image, much dearer); YVO3D needs `referenceImage` unless `retexture` is false. The tool says so before pricing.
- Pass a GLB asset id even to models that read FBX only (Tencent Texture Edit): the plugin sends Scenario's FBX copy of it, free.
- Video: upscale, add sound, restyle. Audio: voice change, voice isolation.

`models_find` with a `capability` (img23d, img2video, 3d23d, video23d, video2audio, audio2audio) finds alternatives.

## Workflows

`workflows_find` (`scope`: team or featured) returns ids, descriptions and inputs. `workflow_run` takes `inputs` by name, with the same money rules as `generate`; outputs import by their kind.

## Scene tools

Off when the user turns off **Agents can edit the scene**; the read tools (`scene_info`, `object_get`, `assets_find`, `console_logs`, `capture_view`) keep working.

- `object_create` (empty, primitive or prefab), `object_set` (name, active, parent, transform, tag, layer, `height` in meters, `ground` to sit it on the ground below), `object_delete`.
- `component_add`, `component_set`: serialized paths (`m_Intensity`) or public properties (`intensity`); vectors as `[x,y,z]`, colors as `[r,g,b,a]` or `#RRGGBB`, references as asset or hierarchy paths. `object_get` with `properties: true` lists what can be set.
- `material_assign`, `skybox_set`, `select`, `scene_save` (an untitled scene needs a path).
- `assets_find` with `label: "Scenario"` lists every generated asset in the project.

Each edit is one Undo step, so the user can revert anything you did.

## Walkable worlds

From one prompt to a world a player can walk in:

1. `terrain_create`: ground with a collider. Shape it with a heightmap image you generate first (a top-down grayscale heightmap, white is high), level `flat_areas` where buildings go, and paint `layers` with generated materials (the first is the base; others by `min_height` or `min_slope`). `edge_falloff` makes an island or plateau.
2. Generate the set pieces, then `object_set` with `height` (meters) and `ground: true` to size each one and sit it on the terrain. Typical heights: a person 1.8, a door 2.2, a house 6 to 9, a pine 8 to 14, a rock 0.5 to 2.
3. `scatter` repeats a model on the ground (`on`: the terrain), with a `height` range, `keep_clear` zones around paths and buildings, and colliders.
4. A character: generate it, rig it, animate idle, walk, run and jump (`motions_find` for library ids), then `make_playable` with those results. It builds the controller, sizes the character, matches speeds to the clips and sets up the follow camera.
5. `play` with `action: "walk"` and waypoints (`run`, `jump`, `wait`) to playtest: it reports the points reached, falls and jumps. `play` with `action: "stop"` before editing again; edits made in Play mode are lost. `record: true` (or `action: "record"`) writes PNG frames of the main camera at a fixed frame rate.
6. `scene_save`.

## Check your work

- `capture_view` renders the Scene view or a camera and returns the image. Look at it after placing results or editing the scene. With `upload: true` it also returns a Scenario asset id, so a capture can be the reference for the next generation.
- `console_logs` with `level: "error"` after imports or component changes.

## Good habits

- One request per intent. Ask before batches or before anything above the user's stated budget.
- Reuse `asset_ids` from earlier results instead of uploading the same file again.
- `jobs` lists recent requests from this Unity project; check it before resending anything after an error.
- Report what you made with its Unity paths and the placed objects.
