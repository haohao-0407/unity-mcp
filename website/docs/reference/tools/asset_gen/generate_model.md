---
title: generate_model
sidebar_label: generate_model
description: "Generate 3D models with AI providers (Tripo, Meshy, fal) and import them into the Unity project."
---

# `generate_model`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.generate_model`

## Description

Generate 3D models with AI providers (Tripo, Meshy, fal) and import them into the Unity project. Bring-your-own-key: provider keys live in the editor's secure store and never cross the bridge.

ACTIONS:
- generate: Submit a generation job (text->3D or image->3D). Returns { job_id } immediately; poll with the status action. Params: provider, mode (text|image), prompt, image_path|image_url, format (glb|fbx|obj|usdz), target_size, texture, tier, model, name, output_folder. Meshy also supports geometry/topology, PBR, texture guidance, output and sizing options; texture_resolution defaults to 4k. Use model=meshy-t2 for Smart Topology. Use preview_task_id to texture an existing preview.
- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.
- cancel: Cancel an in-flight job by job_id.
- list_providers: List configured 3D providers and capabilities (no key values).
- list_models: Search/paginate live fal 3D models and bundled Tripo/Meshy models.
- refresh_models: Refresh fal discovery. Repeat list_models while catalogs[].refreshing is true.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['generate', 'status', 'cancel', 'list_providers', 'list_models', 'refresh_models']` | yes | Action to perform. |
| `provider` | `str \| None` | — | Provider id (tripo, meshy, fal). fal supports GLB output. |
| `mode` | `str \| None` | — | Generation mode: text or image. |
| `prompt` | `str \| None` | — | Text prompt for text->3D. |
| `image_path` | `str \| None` | — | Path to a source image for image->3D. |
| `image_url` | `str \| None` | — | URL of a source image for image->3D. |
| `format` | `str \| None` | — | Output model format: glb, fbx, obj, usdz; Meshy also supports stl and 3mf (may require extra Unity importers). |
| `target_size` | `float \| None` | — | Normalize the largest dimension to this size (meters). |
| `texture` | `bool \| None` | — | Whether to generate textures for the model. |
| `tier` | `str \| None` | — | Provider quality/cost tier. |
| `model` | `str \| None` | — | Provider model id/version (e.g. Tripo v3.1, Meshy meshy-6). Omit for the GUI-selected default. |
| `model_type` | `str \| None` | — | Meshy geometry family: standard or smart-topology. Inferred for meshy-t2. |
| `geometry_resolution` | `str \| None` | — | Meshy geometry resolution: standard, 2k or 4k (7.1/latest only). |
| `should_remesh` | `bool \| None` | — | Meshy standard-model remeshing switch. |
| `topology` | `str \| None` | — | Meshy topology: triangle or quad. T2 requires triangle. |
| `decimation_mode` | `int \| None` | — | Meshy adaptive decimation level 1..4; overrides target_polycount. |
| `target_polycount` | `int \| None` | — | Meshy face target: 100..15000 for T2; 100..300000 with standard remeshing. |
| `pose_mode` | `str \| None` | — | Meshy pose: empty string, a-pose, or t-pose. |
| `enable_pbr` | `bool \| None` | — | Generate Meshy PBR maps in addition to base color. |
| `texture_resolution` | `str \| None` | — | Meshy texture resolution: 2k, 4k (default), or 8k. 6-lite requires 2k. |
| `texture_model` | `str \| None` | — | Meshy text refine model override. T2 defaults to meshy-7.1 for texturing; image mode cannot override. |
| `texture_prompt` | `str \| None` | — | Meshy texturing guidance, max 800 characters; exclusive with texture_image_url. |
| `texture_image_url` | `str \| None` | — | Meshy texture reference image URL or data URI; exclusive with texture_prompt. |
| `remove_lighting` | `bool \| None` | — | Meshy 6 only: remove baked lighting from base color. |
| `moderation` | `bool \| None` | — | Meshy input moderation for geometry and texturing. |
| `target_formats` | `list[str] \| None` | — | Meshy cloud output formats: glb, fbx, obj, stl, usdz, 3mf. Must include format selected for download. |
| `alpha_thumbnail` | `bool \| None` | — | Request Meshy transparent preview thumbnail. |
| `auto_size` | `bool \| None` | — | Meshy real-world size estimation. Omit target_size to preserve the cloud size. |
| `origin_at` | `str \| None` | — | Meshy origin: bottom or center; requires auto_size=true. |
| `preview_task_id` | `str \| None` | — | Meshy text mode: refine an existing successful preview without generating geometry again. |
| `input_task_id` | `str \| None` | — | Meshy image mode: source task from Text/Image to Image; exclusive with image_url/image_path. |
| `image_enhancement` | `bool \| None` | — | Meshy image mode: enhance source image (6/7.1/latest only). |
| `save_pre_remeshed_model` | `bool \| None` | — | Meshy image mode: also save pre-remesh GLB in cloud results. |
| `multi_view_thumbnails` | `bool \| None` | — | Meshy image mode: request four cardinal-view thumbnails. |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `job_id` | `str \| None` | — | Job id for status/cancel. |
| `search` | `str \| None` | — | Filter list_models by name, id or use case. |
| `limit` | `int \| None` | — | Model page size (1..200; default 50). |
| `offset` | `int \| None` | — | Model page offset (default 0). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
```json
{"action": "list_models", "provider": "fal", "mode": "image"}
```

CLI: `unity-mcp asset-gen list-models --kind model --provider fal --refresh` (GLB output).
<!-- examples:end -->

