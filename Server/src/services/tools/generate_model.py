"""
Defines the generate_model tool for AI 3D model generation in Unity.

Thin pass-through: this tool carries NO API keys and NO file bytes. The C# side
reads the user's provider key from the OS secure store, performs the provider
HTTPS call, downloads the result, and imports it into the Unity project.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="asset_gen",
    description=(
        "Generate 3D models with AI providers (Tripo, Meshy, fal) and import them "
        "into the Unity project. Bring-your-own-key: provider keys live in the editor's "
        "secure store and never cross the bridge.\n\n"
        "ACTIONS:\n"
        "- generate: Submit a generation job (text->3D or image->3D). Returns { job_id } "
        "immediately; poll with the status action. Params: provider, mode (text|image), "
        "prompt, image_path|image_url, format (glb|fbx|obj|usdz), target_size, texture, "
        "tier, model, name, output_folder. Meshy also supports geometry/topology, PBR, "
        "texture guidance, output and sizing options; texture_resolution defaults to 4k. "
        "Meshy T2, 7.1 and latest require mode=image and image_path/image_url/input_task_id. Generate and inspect a reference image first; text fallback is disabled. T1/lowpoly is disabled. Use preview_task_id with text-capable geometry models to texture an existing preview.\n"
        "- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight job by job_id.\n"
        "- list_providers: List configured 3D providers and capabilities (no key values).\n"
        "- list_models: Search/paginate live fal 3D models and bundled Tripo/Meshy models.\n"
        "- refresh_models: Refresh fal discovery. Repeat list_models while catalogs[].refreshing is true."
    ),
    annotations=ToolAnnotations(
        title="Generate Model",
        destructiveHint=False,
    ),
)
async def generate_model(
    ctx: Context,
    action: Annotated[Literal["generate", "status", "cancel", "list_providers", "list_models", "refresh_models"],
                      "Action to perform."],

    provider: Annotated[str, "Provider id (tripo, meshy, fal). fal supports GLB output."] | None = None,
    mode: Annotated[str, "Generation mode: text or image. Meshy T2/7.1/latest require image mode and a reference image."] | None = None,
    prompt: Annotated[str, "Text prompt for text->3D."] | None = None,
    image_path: Annotated[str, "Path to a source image for image->3D."] | None = None,
    image_url: Annotated[str, "URL of a source image for image->3D."] | None = None,
    format: Annotated[str, "Output model format: glb, fbx, obj, usdz; Meshy also supports stl and 3mf (may require extra Unity importers)."] | None = None,
    target_size: Annotated[float, "Normalize the largest dimension to this size (meters)."] | None = None,
    texture: Annotated[bool, "Whether to generate textures for the model."] | None = None,
    tier: Annotated[str, "Provider quality/cost tier."] | None = None,
    model: Annotated[str, "Provider model id/version (e.g. Tripo v3.1, Meshy meshy-6). "
                     "Omit for the GUI-selected default."] | None = None,
    model_type: Annotated[str, "Meshy geometry family: standard or smart-topology. Inferred for meshy-t2."] | None = None,
    geometry_resolution: Annotated[str, "Meshy geometry resolution: standard, 2k or 4k (7.1/latest only)."] | None = None,
    should_remesh: Annotated[bool, "Meshy standard-model remeshing switch."] | None = None,
    topology: Annotated[str, "Meshy topology: triangle or quad. T2 requires triangle."] | None = None,
    decimation_mode: Annotated[int, "Meshy adaptive decimation level 1..4; overrides target_polycount."] | None = None,
    target_polycount: Annotated[int, "Meshy face target: 100..15000 for T2; 100..300000 with standard remeshing."] | None = None,
    pose_mode: Annotated[str, "Meshy pose: empty string, a-pose, or t-pose."] | None = None,
    enable_pbr: Annotated[bool, "Generate Meshy PBR maps in addition to base color."] | None = None,
    texture_resolution: Annotated[str, "Meshy texture resolution: 2k, 4k (default), or 8k. 6-lite requires 2k."] | None = None,
    texture_model: Annotated[str, "Meshy text refine model override for text-capable geometry models; image mode cannot override."] | None = None,
    texture_prompt: Annotated[str, "Meshy texturing guidance, max 800 characters; exclusive with texture_image_url."] | None = None,
    texture_image_url: Annotated[str, "Meshy texture reference image URL or data URI; exclusive with texture_prompt."] | None = None,
    remove_lighting: Annotated[bool, "Meshy 6 only: remove baked lighting from base color."] | None = None,
    moderation: Annotated[bool, "Meshy input moderation for geometry and texturing."] | None = None,
    target_formats: Annotated[list[str], "Meshy cloud output formats: glb, fbx, obj, stl, usdz, 3mf. Must include format selected for download."] | None = None,
    alpha_thumbnail: Annotated[bool, "Request Meshy transparent preview thumbnail."] | None = None,
    auto_size: Annotated[bool, "Meshy real-world size estimation. Omit target_size to preserve the cloud size."] | None = None,
    origin_at: Annotated[str, "Meshy origin: bottom or center; requires auto_size=true."] | None = None,
    preview_task_id: Annotated[str, "Meshy text mode: refine an existing successful preview without generating geometry again."] | None = None,
    input_task_id: Annotated[str, "Meshy image mode: source task from Text/Image to Image; exclusive with image_url/image_path."] | None = None,
    image_enhancement: Annotated[bool, "Meshy image mode: enhance source image (6/7.1/latest only)."] | None = None,
    save_pre_remeshed_model: Annotated[bool, "Meshy image mode: also save pre-remesh GLB in cloud results."] | None = None,
    multi_view_thumbnails: Annotated[bool, "Meshy image mode: request four cardinal-view thumbnails."] | None = None,
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    job_id: Annotated[str, "Job id for status/cancel."] | None = None,
    search: Annotated[str, "Filter list_models by name, id or use case."] | None = None,
    limit: Annotated[int, "Model page size (1..200; default 50)."] | None = None,
    offset: Annotated[int, "Model page offset (default 0)."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "action": action.lower(),
        "provider": provider,
        "mode": mode,
        "prompt": prompt,
        "imagePath": image_path,
        "imageUrl": image_url,
        "format": format,
        "targetSize": target_size,
        "texture": texture,
        "tier": tier,
        "model": model,
        "model_type": model_type,
        "geometry_resolution": geometry_resolution,
        "should_remesh": should_remesh,
        "topology": topology,
        "decimation_mode": decimation_mode,
        "target_polycount": target_polycount,
        "pose_mode": pose_mode,
        "enable_pbr": enable_pbr,
        "texture_resolution": texture_resolution,
        "texture_model": texture_model,
        "texture_prompt": texture_prompt,
        "texture_image_url": texture_image_url,
        "remove_lighting": remove_lighting,
        "moderation": moderation,
        "target_formats": target_formats,
        "alpha_thumbnail": alpha_thumbnail,
        "auto_size": auto_size,
        "origin_at": origin_at,
        "preview_task_id": preview_task_id,
        "input_task_id": input_task_id,
        "image_enhancement": image_enhancement,
        "save_pre_remeshed_model": save_pre_remeshed_model,
        "multi_view_thumbnails": multi_view_thumbnails,
        "name": name,
        "outputFolder": output_folder,
        "jobId": job_id,
        "search": search,
        "limit": limit,
        "offset": offset,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "generate_model",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
