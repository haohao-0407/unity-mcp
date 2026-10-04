using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>Supported Meshy creation parameters, split by API phase. No credentials.</summary>
    public sealed class MeshyModelOptions
    {
        public string ModelType;
        public string GeometryResolution;
        public bool? ShouldRemesh;
        public string Topology;
        public int? DecimationMode;
        public int? TargetPolycount;
        public string PoseMode;
        public bool? EnablePbr;
        public string TextureResolution = "4k";
        public string TextureModel;
        public string TexturePrompt;
        public string TextureImageUrl;
        public bool? RemoveLighting;
        public bool? Moderation;
        public string[] TargetFormats;
        public bool? AlphaThumbnail;
        public bool? AutoSize;
        public string OriginAt;
        public string PreviewTaskId;
        public string InputTaskId;
        public bool? ImageEnhancement;
        public bool? SavePreRemeshedModel;
        public bool? MultiViewThumbnails;

        public static MeshyModelOptions FromParams(ToolParams p)
        {
            return new MeshyModelOptions
            {
                ModelType = p.Get("model_type"),
                GeometryResolution = p.Get("geometry_resolution"),
                ShouldRemesh = OptionalBool(p, "should_remesh"),
                Topology = p.Get("topology"),
                DecimationMode = OptionalInt(p, "decimation_mode"),
                TargetPolycount = OptionalInt(p, "target_polycount"),
                PoseMode = p.Get("pose_mode"),
                EnablePbr = OptionalBool(p, "enable_pbr"),
                TextureResolution = p.Get("texture_resolution", "4k"),
                TextureModel = p.Get("texture_model"),
                TexturePrompt = p.Get("texture_prompt"),
                TextureImageUrl = p.Get("texture_image_url"),
                RemoveLighting = OptionalBool(p, "remove_lighting"),
                Moderation = OptionalBool(p, "moderation"),
                TargetFormats = ReadFormats(p),
                AlphaThumbnail = OptionalBool(p, "alpha_thumbnail"),
                AutoSize = OptionalBool(p, "auto_size"),
                OriginAt = p.Get("origin_at"),
                PreviewTaskId = p.Get("preview_task_id"),
                InputTaskId = p.Get("input_task_id"),
                ImageEnhancement = OptionalBool(p, "image_enhancement"),
                SavePreRemeshedModel = OptionalBool(p, "save_pre_remeshed_model"),
                MultiViewThumbnails = OptionalBool(p, "multi_view_thumbnails"),
            };
        }

        private static bool? OptionalBool(ToolParams p, string key)
        {
            var token = p.GetRaw(key);
            if (token == null || token.Type == JTokenType.Null) return null;
            if (bool.TryParse(token.ToString(), out bool value)) return value;
            throw new ArgumentException(key + " must be a boolean.");
        }

        private static int? OptionalInt(ToolParams p, string key)
        {
            var token = p.GetRaw(key);
            if (token == null || token.Type == JTokenType.Null) return null;
            if (int.TryParse(token.ToString(), out int value)) return value;
            throw new ArgumentException(key + " must be an integer.");
        }

        private static string[] ReadFormats(ToolParams p)
        {
            var token = p.GetRaw("target_formats");
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JArray a) || a.Count == 0 || a.Any(x => x.Type != JTokenType.String))
                throw new ArgumentException("target_formats must be a non-empty string array.");
            return a.Values<string>().ToArray();
        }

        public string GeometryModel(ModelGenRequest req)
            => string.IsNullOrEmpty(req.Model)
                ? (ModelType == "smart-topology" ? "meshy-t2" : MeshyAdapter.DefaultModel) : req.Model;

        public string RefineModel(ModelGenRequest req)
            => TextureModel ?? (GeometryModel(req) == "meshy-t2" ? "meshy-7.1" : GeometryModel(req));

        public void Validate(ModelGenRequest req)
        {
            // Normalize before checking policy so aliases/case cannot bypass image requirements.
            req.Model = req.Model?.Trim().ToLowerInvariant();
            req.Mode = req.Mode?.Trim().ToLowerInvariant();
            ModelType = ModelType?.Trim().ToLowerInvariant();
            TextureModel = TextureModel?.Trim().ToLowerInvariant();
            bool image = req.Mode == "image";
            bool refine = !string.IsNullOrEmpty(PreviewTaskId);
            string model = GeometryModel(req);
            bool smart = model == "meshy-t2" || ModelType == "smart-topology";
            if (IsDisabledLegacyModel(model) || IsDisabledLegacyModel(ModelType) || IsDisabledLegacyModel(TextureModel))
                Fail("Meshy T1 / lowpoly is deprecated and disabled in this fork. Use model=meshy-t2, model_type=smart-topology and mode=image with a reference image.");
            bool needsImage = model == "meshy-t2" || model == "meshy-7.1" || model == "latest" || model == "meshy-7";
            if (needsImage && (!image || (string.IsNullOrWhiteSpace(req.ImagePath)
                && string.IsNullOrWhiteSpace(req.ImageUrl) && string.IsNullOrWhiteSpace(InputTaskId))))
                Fail($"Meshy model '{model}' requires a reference image in this fork. Generate and inspect an image first, then use mode=image with image_path, image_url or a completed image-generation input_task_id. Text-to-3D fallback is disabled.");
            if (InputTaskId != null && string.IsNullOrWhiteSpace(InputTaskId))
                Fail("input_task_id must identify a completed image-generation task; it cannot be empty.");
            Choice(ModelType, "model_type", "standard", "smart-topology");
            Choice(GeometryResolution, "geometry_resolution", "standard", "2k", "4k");
            Choice(Topology, "topology", "triangle", "quad");
            Choice(PoseMode, "pose_mode", "", "a-pose", "t-pose");
            Choice(TextureResolution, "texture_resolution", "2k", "4k", "8k");
            Choice(OriginAt, "origin_at", "bottom", "center");
            Choice(TextureModel, "texture_model", "meshy-6-lite", "meshy-6", "meshy-7.1", "latest", "meshy-7");
            Choice(req.Format, "format", "glb", "fbx", "obj", "stl", "usdz", "3mf");
            if (req.Format == "3mf" && TargetFormats == null) TargetFormats = new[] { "3mf" };
            if (req.Mode != "text" && !image) Fail("mode must be text or image.");
            if ((req.Prompt?.Length ?? 0) > 800 || (TexturePrompt?.Length ?? 0) > 800)
                Fail("prompt and texture_prompt must not exceed 800 characters.");
            if (smart && (model != "meshy-t2" || ModelType == "standard"))
                Fail("Smart Topology requires model=meshy-t2 and model_type=smart-topology.");
            if (smart && Topology == "quad") Fail("meshy-t2 only supports triangle topology.");
            if (TargetPolycount.HasValue && (TargetPolycount < 100 || TargetPolycount > (smart ? 15000 : 300000)))
                Fail("target_polycount must be 100..15000 for T2 or 100..300000 for standard models.");
            if (DecimationMode.HasValue && (DecimationMode < 1 || DecimationMode > 4))
                Fail("decimation_mode must be 1..4.");
            if (GeometryResolution != null && GeometryResolution != "standard" && model != "meshy-7.1" && model != "latest")
                Fail("Ultra geometry_resolution requires meshy-7.1 or latest.");
            if (!smart && (TargetPolycount.HasValue || DecimationMode.HasValue || Topology != null) && ShouldRemesh != true)
                Fail("Standard topology/face controls require should_remesh=true.");
            if (smart && (ShouldRemesh.HasValue || DecimationMode.HasValue))
                Fail("T2 does not use should_remesh or decimation_mode; use target_polycount.");
            if (OriginAt != null && AutoSize != true) Fail("origin_at requires auto_size=true.");
            if (TexturePrompt != null && TextureImageUrl != null) Fail("Use only one of texture_prompt or texture_image_url.");
            if (req.Texture)
            {
                string textureModel = image ? model : RefineModel(req);
                if (textureModel == "meshy-t2" && !image) Fail("texture_model must be a texture-capable model, e.g. meshy-7.1.");
                if (textureModel == "meshy-6-lite" && TextureResolution != "2k")
                    Fail("meshy-6-lite requires texture_resolution=2k; use texture_model=meshy-7.1 in text mode for 4k.");
                if (RemoveLighting.HasValue && textureModel != "meshy-6") Fail("remove_lighting requires meshy-6 texturing.");
            }
            else if (EnablePbr.HasValue || TexturePrompt != null || TextureImageUrl != null || TextureModel != null || RemoveLighting.HasValue)
                Fail("Texturing options require texture=true.");
            if (image && TextureModel != null) Fail("texture_model override is supported only in text mode.");
            if (refine && (image || !req.Texture)) Fail("preview_task_id requires text mode and texture=true.");
            if (refine && (ModelType != null || GeometryResolution != null || ShouldRemesh.HasValue || Topology != null || TargetPolycount.HasValue || DecimationMode.HasValue || PoseMode != null))
                Fail("preview_task_id reuses geometry; omit geometry generation options.");
            if (!image && (InputTaskId != null || ImageEnhancement.HasValue || SavePreRemeshedModel.HasValue || MultiViewThumbnails.HasValue))
                Fail("input_task_id, image_enhancement, save_pre_remeshed_model and multi_view_thumbnails require image mode.");
            if (InputTaskId != null && (req.ImageUrl != null || req.ImagePath != null))
                Fail("Use input_task_id or image_url/image_path, not both.");
            if (ImageEnhancement.HasValue && model != "meshy-6" && model != "meshy-7.1" && model != "latest")
                Fail("image_enhancement requires meshy-6, meshy-7.1 or latest.");
            if (TargetFormats != null)
            {
                if (TargetFormats.Length == 0) Fail("target_formats cannot be empty.");
                foreach (string f in TargetFormats) Choice(f, "target_formats", "glb", "fbx", "obj", "stl", "usdz", "3mf");
                if (!TargetFormats.Contains(req.Format)) Fail("target_formats must include the format selected for download.");
            }
        }

        private static bool IsDisabledLegacyModel(string value)
            => value == "meshy-t1" || value == "t1" || value == "lowpoly";

        private static void Fail(string message) => throw new ArgumentException(message);
        private static void Choice(string value, string name, params string[] choices)
        {
            if (value != null && !choices.Contains(value)) Fail(name + " must be one of: " + string.Join(", ", choices));
        }
        private static void Put(JObject body, string name, object value)
        {
            if (value != null) body[name] = JToken.FromObject(value);
        }
        public void ApplyShared(JObject body)
        {
            Put(body, "moderation", Moderation);
            Put(body, "target_formats", TargetFormats);
            Put(body, "alpha_thumbnail", AlphaThumbnail);
            Put(body, "auto_size", AutoSize);
            Put(body, "origin_at", OriginAt);
        }
        public void ApplyGeometry(JObject body, ModelGenRequest req, bool image)
        {
            Put(body, "model_type", ModelType ?? (GeometryModel(req) == "meshy-t2" ? "smart-topology" : null));
            Put(body, "geometry_resolution", GeometryResolution);
            Put(body, "should_remesh", ShouldRemesh);
            Put(body, "topology", Topology);
            Put(body, "decimation_mode", DecimationMode);
            Put(body, "target_polycount", TargetPolycount);
            Put(body, "pose_mode", PoseMode);
            if (image)
            {
                Put(body, "image_enhancement", ImageEnhancement);
                Put(body, "save_pre_remeshed_model", SavePreRemeshedModel);
                Put(body, "multi_view_thumbnails", MultiViewThumbnails);
            }
        }
        public void ApplyTexture(JObject body)
        {
            Put(body, "enable_pbr", EnablePbr);
            Put(body, "texture_resolution", TextureResolution ?? "4k");
            Put(body, "texture_prompt", TexturePrompt);
            Put(body, "texture_image_url", TextureImageUrl);
            Put(body, "remove_lighting", RemoveLighting);
        }
    }
}
