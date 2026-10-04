# Meshy generation parameters

The `generate_model` MCP tool and `unity-mcp asset-gen generate-model` CLI expose
current Text-to-3D preview/refine and Image-to-3D creation parameters. Provider
credentials remain in the Unity secure store. These additions do not implement
cloud task listing/deletion, SSE, task recovery, rigging or animation endpoints.

## Defaults and model selection

- `model` maps to Meshy `ai_model`. The catalog includes Meshy 6 (existing default),
  6 Lite, 7.1, T2 and `latest`; a saved GUI model selection still takes precedence.
- `model="meshy-t2"` automatically sends `model_type="smart-topology"`.
  Selecting `model_type="smart-topology"` without `model` chooses T2.
- Text generation uses preview followed by refine when `texture=true` (default).
  `texture=false` produces geometry only. Image generation uses one API task.
- **Texturing defaults to `texture_resolution="4k"`**, sent explicitly to Meshy.
  Set `2k` or `8k` to override it. Meshy 6 Lite needs explicit `2k`, or a
  `texture_model="meshy-7.1"` override in text mode. It is not silently downgraded.
- `texture_model` independently selects the text refine model. T2 defaults to
  Meshy 7.1 for texturing; other models keep the selected geometry model.
  Image-to-3D does not accept an independent texture model.
- `enable_pbr` is opt-in, retaining Meshy's default when omitted.

## Parameter reference

Existing `mode` (`text`/`image`), `prompt`, `image_url`/`image_path`, `model`,
`texture`, `format`, `target_size`, `name` and `output_folder` remain supported.
Use `preview_task_id` in text mode to texture an existing successful preview;
no prompt or geometry regeneration is needed. `input_task_id` in image mode
accepts an image-generation task instead of `image_url`/`image_path`.

| Parameters | Stage and behavior |
| --- | --- |
| `model_type` | Geometry: `standard` or `smart-topology` |
| `geometry_resolution` | Geometry: `standard`, `2k`, `4k`; Ultra requires 7.1/latest |
| `should_remesh`, `topology` | Standard geometry: enable remeshing, choose `triangle` or `quad` |
| `target_polycount` | T2: 100–15,000; standard remesh: 100–300,000 |
| `decimation_mode` | Standard remesh: level 1–4; takes precedence over target face count |
| `pose_mode` | Geometry: empty, `a-pose`, `t-pose` |
| `enable_pbr`, `texture_resolution` | Texture: PBR maps; `2k`, `4k` (default), `8k` |
| `texture_model` | Text refine model override |
| `texture_prompt`, `texture_image_url` | Texture guidance: choose one; URL supports a public image URL or data URI |
| `remove_lighting` | Meshy 6 texturing only |
| `moderation` | Input moderation, forwarded to both text stages |
| `target_formats` | Cloud formats: `glb`, `fbx`, `obj`, `stl`, `usdz`, `3mf` |
| `alpha_thumbnail` | Request a transparent thumbnail |
| `auto_size`, `origin_at` | Real-world size estimation; origin `bottom` or `center` |
| `image_enhancement` | Image mode, Meshy 6/7.1/latest only |
| `save_pre_remeshed_model` | Image mode: retain pre-remesh GLB in cloud results |
| `multi_view_thumbnails` | Image mode: request cardinal-view thumbnails |

Invalid parameter types, enum values and incompatible combinations return errors
before submitting a paid job. T2 rejects quad topology and remesh/decimation
controls; use `target_polycount` directly. Standard topology/face controls require
`should_remesh=true`. Geometry options are rejected for an existing preview.
Deprecated `ultra_mode`, `hd_texture`, `symmetry_mode`, `is_a_t_pose`, `art_style`
and `lowpoly` are not exposed; use the current replacements above.

`format` selects the one result downloaded into Unity. If supplied,
`target_formats` must contain that format. Selecting `format="3mf"` automatically
requests cloud 3MF output, since Meshy does not generate it by default. GLB requires
glTFast; STL/USDZ/3MF may need additional importers to become usable scene models.
Extra formats, thumbnails and pre-remesh output are requested in the cloud but
are not automatically downloaded by the existing single-model import workflow.

When `auto_size=true`, omitting `target_size` preserves cloud sizing; supplying a
positive `target_size` explicitly requests the existing Unity normalization.
The existing `tier` parameter does not control Meshy geometry quality.

## Examples

T2 game prop with PBR and default 4K textures:

```json
{
  "action": "generate",
  "provider": "meshy",
  "mode": "text",
  "model": "meshy-t2",
  "prompt": "A stylized stone lighthouse for a game",
  "target_polycount": 4000,
  "enable_pbr": true,
  "target_formats": ["glb"],
  "format": "glb"
}
```

Texture an existing preview without paying to regenerate geometry:

```json
{
  "action": "generate",
  "provider": "meshy",
  "preview_task_id": "YOUR_SUCCEEDED_PREVIEW_TASK_ID",
  "texture_model": "meshy-7.1",
  "texture_prompt": "Weathered stone and painted red metal",
  "texture_resolution": "8k",
  "enable_pbr": true
}
```

CLI (repeat `--target-formats` for multiple cloud outputs):

```powershell
unity-mcp asset-gen generate-model --provider meshy --model meshy-t2 --prompt "A stone lighthouse" --target-polycount 4000 --enable-pbr --texture-resolution 4k --target-formats glb
```

## Using this fork locally

Both sides must run this checkout: point the Unity package dependency to
`file:D:/UnityMCPFork/unity-mcp/MCPForUnity`, and start the Python server from
`D:/UnityMCPFork/unity-mcp/Server` (for example `uv run mcp-for-unity` with your
existing transport/port arguments). Restart the server and refresh the MCP tool
schema after updating. Updating only the C# package leaves the old Python schema
unable to accept the new tool arguments. This change does not automatically switch
other Unity projects or global MCP configuration to the fork.

## Sources

Parameter contract checked on 2026-10-04:
- https://docs.meshy.ai/en/api/text-to-3d
- https://docs.meshy.ai/en/api/image-to-3d
