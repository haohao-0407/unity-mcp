# Meshy generation parameters

The `generate_model` MCP tool and `unity-mcp asset-gen generate-model` CLI expose
current Text-to-3D preview/refine and Image-to-3D creation parameters. Provider
credentials remain in the Unity secure store. These additions do not implement
cloud task listing/deletion, SSE, rigging or animation endpoints. Local Meshy job recovery and attaching known remote task IDs are supported.

## Defaults and model selection

- `model` maps to Meshy `ai_model`. The catalog includes Meshy 6 (existing default),
  6 Lite, 7.1, T2 and `latest`; a saved GUI model selection still takes precedence.
- `model="meshy-t2"` automatically sends `model_type="smart-topology"`.
  Selecting `model_type="smart-topology"` without `model` chooses T2.
- **Fork policy: T2, 7.1, `latest` and the legacy `meshy-7` alias require image mode
  and a non-empty `image_path`, `image_url` or completed image-generation `input_task_id`.**
  Generate and inspect the reference image first, then submit it with `mode="image"`.
  Text mode (including a `preview_task_id`) is rejected for these geometry models;
  texture reference images alone do not satisfy the geometry input requirement.
  GUI-selected models and inferred T2 selections obey the same rule. No model or
  text-to-3D fallback is performed. This is a fork policy, not an API capability claim.
- **T1 is disabled**, including `meshy-t1`, `t1` and the deprecated `lowpoly` entry.
  Explicit IDs and saved selections are rejected before any generation request.
- Text generation uses preview followed by refine when `texture=true` (default).
  `texture=false` produces geometry only. Image generation uses one API task.
- **Texturing defaults to `texture_resolution="4k"`**, sent explicitly to Meshy.
  Set `2k` or `8k` to override it. Meshy 6 Lite needs explicit `2k`, or a
  `texture_model="meshy-7.1"` override in text mode. It is not silently downgraded.
- `texture_model` independently selects the text refine model for text-capable
  geometry models such as Meshy 6. This texture-only override does not generate
  geometry. Image-to-3D does not accept an independent texture model.
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
  "mode": "image",
  "model": "meshy-t2",
  "image_path": "Assets/Generated/References/lighthouse.png",
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
  "model": "meshy-6",
  "mode": "text",
  "preview_task_id": "YOUR_SUCCEEDED_PREVIEW_TASK_ID",
  "texture_model": "meshy-7.1",
  "texture_prompt": "Weathered stone and painted red metal",
  "texture_resolution": "8k",
  "enable_pbr": true
}
```

CLI (repeat `--target-formats` for multiple cloud outputs):

```powershell
unity-mcp asset-gen generate-model --provider meshy --model meshy-t2 --mode image --image-path Assets/Generated/References/lighthouse.png --target-polycount 4000 --enable-pbr --texture-resolution 4k --target-formats glb
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


## Script reloads and Meshy task recovery

Meshy task records are saved atomically under `Library/MCPForUnity/AssetGenJobs/`
and mirrored to Unity SessionState. They contain local/remote task IDs, the
image/text endpoint, preview/refine state, the import destination and any completed
download path. API keys are never included; recovery reads the secure store again.
Records survive script reloads and editor restarts while Library is retained.
Deleting Library removes the disk history; keep remote IDs separately if needed.

An acknowledged Meshy generation automatically resumes polling after a reload.
The original local `job_id` continues working. If the file was already downloaded,
recovery imports that same file instead of generating or downloading another copy.
Image tasks use `/openapi/v1/image-to-3d`; text preview/refine tasks use their own
`/openapi/v2/text-to-3d` endpoint. Preview and refine IDs are saved separately.
Text workflows may still submit their *not-yet-created* refine stage once; existing
geometry/refine tasks are not recreated. T2/7.1 image-only policy remains unchanged.

The status response (including failure/cancellation) exposes `provider_task_id`,
`provider_root_task_id`, `resumable` and `submission_unknown`. Discover prior jobs:

```json
{"action":"list_jobs","limit":20}
```

Resume after a local timeout, download failure, missing key, or local cancellation:

```json
{"action":"resume","job_id":"LOCAL_JOB_ID"}
```

If an old plugin version lost the ID, copy the confirmed remote task ID from Meshy
and attach it explicitly. `mode` selects the endpoint; it is required for this form:

```json
{"action":"resume","provider":"meshy","provider_task_id":"REMOTE_TASK_ID","mode":"image","format":"glb","output_folder":"Assets/Generated/Models"}
```

Attaching a remote ID never sends a generation/refine POST. For text tasks, attach
the refine ID for a textured result; attaching a preview ID imports its geometry.
Repeated attachment of the same tracked task reuses the local job. Remote task
expiry/deletion or missing output formats remain provider errors.

```powershell
unity-mcp asset-gen list-model-jobs
unity-mcp asset-gen resume-model --job-id LOCAL_JOB_ID
unity-mcp asset-gen resume-model --provider-task-id REMOTE_TASK_ID --mode image
```

Canceled and failed jobs are not automatically restarted. Local cancel does not
cancel a provider's remote paid task. Automatic recovery keeps the original local
timeout deadline; explicit resume grants a fresh polling window.

There is an unavoidable ambiguous case: the editor may reload after a POST is sent
but before its response ID arrives. The journal records this submission intent,
stops with an actionable message, and never blindly repeats the paid POST. Confirm
the task in Meshy and attach its ID. Existing old-version tasks without stored IDs
cannot be reconstructed from local history alone.

Automatic recovery currently covers **Meshy 3D models**. Other asset providers keep
their existing reload behavior. Import pipelines now target the generated file or
extracted directory with `ImportAsset`, avoiding a whole-project `Refresh`; this
does not suppress legitimate recompilation caused by script/package changes.
