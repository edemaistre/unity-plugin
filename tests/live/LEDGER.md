# Live test ledger

Paid runs against the Scenario test project, one section per run. Each paid call is sent once; an unknown outcome is checked in Jobs, never resent. Quoted = the free price check, charged = what the job reported.

## 2026-10-18 22:33 acceptance run (2 failure(s))

| Case | Model or workflow | Job | Quoted CU | Charged CU | Result |
|---|---|---|---|---|---|
| image | `model_openai-gpt-image-2-5-sunburst` | `job_6XaiWsdbs3FmX7ru4LuGk8XC` | 11 | 11 | ok: a-stylized-wooden-treasure-chest-game-2xyp6f.png; placed Stylized Wooden Treasure |
| material | `model_patina-material` | `job_YHUjjPRfKfSLbR6esyBRVrk8` | 17 | 17 | FAIL target has no built material |
| skybox | `model_scenario-skybox-flux` | `job_z4UoZ7zzQzESas62UHLoweQq` | 14 | 14 | FAIL state running  |
| model3d | `model_rodin-hyper3d-v2-5-text-to-3d` | `job_E6NWNc7R4aiKVwNVc1RwA3nm` | 60 | 60 | ok: a-stylized-wooden-treasure-chest-game-83pjsb.glb; placed Stylized Wooden Treasure |
| audio | `model_sonilo-v1-1-text-to-sound-effects` | `job_ugTwprxJi913ZvJur3Hhht59` | 1 | 1 | ok: short-metallic-sword-swing-with-a-whoosh-xke5uv.wav; placed Short Metallic Sword |
| video | `model_alibaba-wan-3-0` | `job_VcSyDUVuyva9EfBV6kFziDND` | 100 | 100 | ok: slow-dolly-shot-through-a-misty-pine-xztmce.mp4; placed Slow Dolly Shot |
| step remove-bg (uploaded texture) | `model_birefnet-background-removal` | `job_DN8VPjsnbhfSTavjaiLcJyJY` | 2 | 2 | ok: acceptance-source-nyzw6m.png; placed Acceptance Source |
| workflow True 4-Pose | `wflow_true-4-pose` | `job_B5Fw2mwZn1vehMFTtk2whsYW` | 16 | 0 | ok: workflow-cwbxdc.png; placed Workflow |
| prompt spark | `model_openai-gpt-image-2-5-sunburst` | `` | 2.75 | 2.75 | ok |

## 2026-10-18 22:37 acceptance run of material, resumed job_z4UoZ7zzQzESas62UHLoweQq (pass)

| Case | Model or workflow | Job | Quoted CU | Charged CU | Result |
|---|---|---|---|---|---|
| material | `model_patina-material` | `job_y1Q2LNVDy8LjrTH2UkUTG2QH` | 17 | 17 | ok: weathered-copper-plates-with-green-fzttxc-base.png, weathered-copper-plates-with-green-k1kwue-albedo.png, weathered-copper-plates-with-green-pxjjde-normal.png, weathered-copper-plates-with-green-esvkho-roughness.png, weathered-copper-plates-with-green-wdazhk-metallic.png, weathered-copper-plates-with-green-ghjeid-height.png; placed Material Target |
| resumed skybox (after restart) | `model_scenario-skybox-flux` | `job_z4UoZ7zzQzESas62UHLoweQq` | 14 | 14 | ok: alien-desert-at-golden-hour-twin-moons-uoui69-panorama.png |
