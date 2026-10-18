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

## 2026-10-18 23:56 3D acceptance run (pass)

| Case | Model or workflow | Job | Quoted CU | Charged CU | Result |
|---|---|---|---|---|---|
| 3D generate | `model_meshy-7-1-txt23d` | `job_BUuHxZ57Lw8Lh14yENw9WnDL` | 120 | 120 | ok: a-stylized-cartoon-knight-full-body-hdj4qe.glb |
| 3D rig | `model_uthana-character-rigging` | `job_yDMAvrPP6uFspD6gKLqP1WeJ` | 40 | 40 | ok: rig-the-adventurer-4ofobu.glb |
| 3D animate | `model_meshy-animation` | `job_rE6ES3dk2AjkeDw4p2EcreMJ` | 32 | 32 | ok: attack-4yuedx.glb |
| 3D agent texture edit (GLB to FBX) | `model_tencent-texture-edit` | `job_h6CqTHJTcyTFhPPgsv7tZqN1` | 68 | 68 | ok: silver-plate-armor-with-deep-blue-cloth-wz2g2o.glb |

## 2026-10-18 3D discovery runs (outside the plugin, through the Scenario MCP)

Before the 3D steps were built, one run per 3D operation was sent once from a script (`model_run` with `wait:false`, then `job_get`), to see what each model returns: files, formats, bones, clips, parts. Results fed the step defaults, presets and importer rules. Sources: the Rodin chest `asset_kQhwDw2rLdmpF9Ez7i83pjsb`, its image `asset_Yru9qdbSu93fXrWMUU2xYP6f`, and the Meshy character remeshed to 30k triangles `asset_Vv93WrrNrXKuQd4APofT6XxN`.

| Batch | Case | Model | Job | Quoted CU | Charged CU | Result |
|---|---|---|---|---|---|---|
| A | character | `model_meshy-7-1-txt23d` | `job_KWGyZisfGFSk4iHxfF7Vt9YU` | 120 | 120 | ok: 1 glb, 1 mtl, 1 obj, 4 png |
| A | remesh-quad | `model_meshy-remesh` | `job_URYwrA4G83GhrqtgDy2mzfxo` | 20 | 20 | ok: 1 glb, 1 obj |
| A | polygen | `model_tencent-smarttopology` | `job_PVDKwaeZGPieiD1gDN1kbcXR` | 113 | 113 | ok: 1 glb, 1 obj |
| A | stylize-voxel | `model_tripo-stylization` | `job_Utx9nE2huiyKHc6MApRsSnbR` | 30 | 30 | ok: 1 glb |
| A | texture-edit | `model_tencent-texture-edit` | `job_DSfRpnaFRhzWe54Hv4c5ekYc` | 68 | 68 | FAIL Unsupported file format: GLB. Must be FBX. |
| A | hy-part | `model_hunyuan-3d-part` | `job_b5GuJZ5nMeCN28FeJoQowjZT` | 30 | 30 | ok: 1 glb |
| A | hitem-split | `model_hitem-3d-split` | `job_yrdYsHCbv7hKjb3bhfUdcGcG` | 80 | 80 | ok: 1 glb |
| A | seg-v2 | `model_tripo-segmentation-v2` | `job_EUgEM3dJymB2P8oyEuaWJCLR` | 80 | 80 | ok: 1 glb |
| A | meshy-uv | `model_meshy-uv-unwrap` | `job_6UXpuZ5jHPaA9NWuZgf5QzCq` | 20 | 20 | ok: 1 glb |
| A | tripo-texturing | `model_tripo-v3-0-texturing` | `job_crVMKJHCkBr5hEP32S1fBXNh` | 15 | 15 | ok: 1 glb |
| A | multicolor | `model_hitem-3d-multicolor` | `job_8qek82z5AgTQxoxjMv7TdbXb` | 80 | 80 | ok: 1 glb |
| A | meshy-motion | `model_meshy-text-to-motion` | `job_g4k8RuLby2YWfzQnaWDB4G9u` | 40 | 40 | ok: 1 fbx |
| A | uthana-motion-nochar | `model_uthana-text-to-motion-3.0` | `job_fPa7oDEiGnFpBtkiVvro56ef` | 208 | 208 | ok: 1 glb |
| A | voxels | `model_scenario-voxels` | `job_1KFFG7LPvf3uphxNwSQ2NeSH` | 36 | 36 | ok: 1 vox |
| B | texture-edit-fbx | `model_tencent-texture-edit` | `job_Qg8hq9JqaYAD7mDnfDzPvRp5` | 68 | 68 | ok: 1 glb |
| B | opus-edit | `model_scenario-claude-opus-5-5-3d` | `job_BuAQgET2bHE2mcyyUxtX2pDG` | 75 | 75 | ok: 1 glb |
| B | triposplat | `model_vast-ai-triposplat` | `job_vbP2zZSXRDj4kpLRkwgb2c6w` | 25 | 25 | ok: 1 ply (labelled spz) |
| B | rodin-bang | `model_rodin-hyper3d-bang` | `job_wDxUtap3mTU66dsSsqhiYsVk` | 60 | 60 | ok: 1 glb |
| B | stylize-lego | `model_tripo-stylization` | `job_P7qKKUpDMrD9s73CAkrM9aEn` | 30 | 30 | ok: 1 glb |
| B | voxel-img | `model_scenario-voxels` | `job_VibKA2Mh9XaG6kgskCJhN7hX` | 37 | 37 | ok: 1 vox |
| C0 | character-30k | `model_meshy-remesh` | `job_fsw5tgXVdhaKrY8vpUSnCh5N` | 20 | 20 | ok: 1 glb, 1 obj |
| C | rig-meshy | `model_meshy-rigging` | `job_xDL3RXe8HG7K6DV6KXUTryP1` | 20 | 20 | ok: 5 glb |
| C | rig-uthana | `model_uthana-character-rigging` | `job_HgnCnz3U2wTsvehj8i4Mt9jZ` | 40 | 40 | ok: 1 glb |
| C | rig-cartwheel | `model_cartwheel-character-rigging` | `job_NGrfaVWmmPbamYKAoXWupyJU` | 30 | 30 | ok: 1 glb |
| C | rig-tripo-walk | `model_tripo-rigging-v1` | `job_AD78NbQPuJVzWLfBvVuzu4GX` | 70 | 70 | ok: 2 glb |
| C | anim-meshy | `model_meshy-animation` | `job_du9LcRMqpY1Dwz4VcwkfK5Ny` | 32 | 32 | ok: 1 glb |
| C | motion-cartwheel | `model_cartwheel-text-to-motion` | `job_nuEEcBktgTDmErhCQppUhynk` | 120 | 120 | ok: 1 glb |
| C | motion-uthana | `model_uthana-text-to-motion-3.0` | `job_zT1XUsURumczFQSjdVsw5FUQ` | 228 | 228 | ok: 1 glb |
| C | creature | `model_meshy-7-1-txt23d` | `job_YsM8NfbfeZToTPx7XA1grQRy` | 120 | 120 | ok: 1 glb, 1 mtl, 1 obj, 4 png |

Not sent: HY World image to splat (quoted 1600 CU) and YVO3D Retexture (price check refused without a reference image); both are priced by the contract check.
