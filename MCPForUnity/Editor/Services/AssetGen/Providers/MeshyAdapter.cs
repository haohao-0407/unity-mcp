using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>Non-secret state needed to resume an acknowledged Meshy task.</summary>
    public sealed class MeshyTaskCheckpoint
    {
        public string RootTaskId;
        public string RefineTaskId;
        public bool SubmissionPending;
        public bool IsImage;
        public bool WantTexture;
        public bool DirectRefine;
        public bool RefineSubmitted;
        public string Format;
        public string AiModel;
        public JObject RefineBody;
        public string CurrentTaskId => RefineTaskId ?? RootTaskId;
    }

    /// <summary>
    /// Meshy model provider. Text→3D posts a "preview" task to the v2 text-to-3d endpoint (geometry
    /// only); when textures are requested it then issues a "refine" task and surfaces the textured
    /// result. Image→3D posts to the v1 image-to-3d endpoint, which textures in a single call. Each
    /// task is polled at its OWN endpoint (text vs image). The bearer key is supplied per call and
    /// never logged; every error is run through <see cref="SecretRedactor"/>.
    /// </summary>
    public sealed class MeshyAdapter : IModelProviderAdapter
    {
        private const string TextEndpoint = "https://api.meshy.ai/openapi/v2/text-to-3d";
        private const string ImageEndpoint = "https://api.meshy.ai/openapi/v1/image-to-3d";
        // Default model; the catalog mirrors this and the drift-guard test pins the two together.
        internal const string DefaultModel = "meshy-6";

        public string Id => "meshy";

        // Stashed at submit so poll picks the right endpoint, model_urls entry, and texture flow.
        private string _format = "glb";
        private bool _isImage;
        private bool _wantTexture = true;
        private string _aiModel = DefaultModel; // stashed so the refine task reuses the submit model
        private JObject _refineBody;
        private bool _directRefine;
        private string _refineTaskId;
        private bool _refineSubmitted;
        private string _rootTaskId;
        private bool _submissionPending;
        // Called before POST and immediately after the response ID is received, not on a later tick.
        internal Action<MeshyTaskCheckpoint> Checkpoint;

        internal MeshyTaskCheckpoint Capture() => new MeshyTaskCheckpoint {
            RootTaskId = _rootTaskId, RefineTaskId = _refineTaskId, SubmissionPending = _submissionPending,
            IsImage = _isImage, WantTexture = _wantTexture, DirectRefine = _directRefine,
            RefineSubmitted = _refineSubmitted, Format = _format, AiModel = _aiModel,
            RefineBody = _isImage ? null : (JObject)_refineBody?.DeepClone()
        };

        internal void Restore(MeshyTaskCheckpoint state)
        {
            if (state == null || state.SubmissionPending || string.IsNullOrWhiteSpace(state.RootTaskId))
                throw new ArgumentException("Remote submission outcome is unknown. Supply the confirmed Meshy provider_task_id; do not resubmit generation.");
            _rootTaskId = state.RootTaskId; _refineTaskId = state.RefineTaskId;
            _isImage = state.IsImage; _wantTexture = state.WantTexture; _directRefine = state.DirectRefine;
            _refineSubmitted = state.RefineSubmitted; _format = state.Format ?? "glb";
            _aiModel = state.AiModel; _refineBody = (JObject)state.RefineBody?.DeepClone();
            _submissionPending = false;
        }

        public async Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            var options = req.Meshy ?? new MeshyModelOptions();
            options.Validate(req);
            _rootTaskId = null;
            _submissionPending = false;
            _refineSubmitted = false;
            _refineTaskId = null;
            _directRefine = !string.IsNullOrEmpty(options.PreviewTaskId);
            _format = string.IsNullOrEmpty(req.Format) ? "glb" : req.Format.TrimStart('.').ToLowerInvariant();
            _wantTexture = req.Texture;
            _aiModel = options.GeometryModel(req);
            _isImage = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase);
            _refineBody = new JObject { ["mode"] = "refine", ["ai_model"] = options.RefineModel(req) };
            options.ApplyShared(_refineBody);
            options.ApplyTexture(_refineBody);
            if (_directRefine)
            {
                _refineBody["preview_task_id"] = options.PreviewTaskId;
                return await PostTask(TextEndpoint, _refineBody, apiKey, http, ct, "refine");
            }

            JObject body;
            string url;
            if (_isImage)
            {
                // image→3D textures in a single call (no separate refine task). image_url accepts a
                // hosted URL or an inline base64 data URI (for a local image_path).
                url = ImageEndpoint;
                string imageRef = options.InputTaskId != null ? null : (!string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath));
                body = new JObject
                {
                    [options.InputTaskId != null ? "input_task_id" : "image_url"] = options.InputTaskId ?? imageRef,
                    ["ai_model"] = _aiModel,
                    ["should_texture"] = _wantTexture
                };
            }
            else
            {
                // text→3D preview is geometry only; texturing happens via a follow-up refine task.
                url = TextEndpoint;
                body = new JObject
                {
                    ["mode"] = "preview",
                    ["prompt"] = req.Prompt ?? string.Empty,
                    ["ai_model"] = _aiModel
                };
            }

            options.ApplyShared(body);
            options.ApplyGeometry(body, req, _isImage);
            if (_isImage && _wantTexture) options.ApplyTexture(body);
            return await PostTask(url, body, apiKey, http, ct, "submit");
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            if (http == null) throw new ArgumentNullException(nameof(http));

            bool refinePhase = _refineSubmitted;
            string pollId = refinePhase ? _refineTaskId : providerJobId;
            // image tasks live on the v1 image endpoint; preview/refine tasks on v2 text-to-3d.
            string statusBase = (_isImage && !refinePhase) ? ImageEndpoint : TextEndpoint;

            var spec = new HttpRequestSpec { Method = "GET", Url = statusBase + "/" + Uri.EscapeDataString(pollId) };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "poll");

            ProviderPollState state = MapState(json["status"]?.ToString());
            var result = new ProviderPollResult { State = state };

            // Two-phase (text + texture) splits progress across preview (0..0.5) and refine (0.5..1).
            bool twoPhase = !_isImage && _wantTexture && !_directRefine;
            float raw = 0f;
            JToken prog = json["progress"];
            if (prog != null && prog.Type != JTokenType.Null) raw = Mathf.Clamp01(prog.Value<float>() / 100f);
            result.Progress = !twoPhase ? raw : (refinePhase ? 0.5f + raw * 0.5f : raw * 0.5f);

            if (state == ProviderPollState.Succeeded)
            {
                // Preview just finished and textures were requested: start the refine task and keep
                // polling it; never surface the untextured preview result.
                if (twoPhase && !refinePhase)
                {
                    _refineBody["preview_task_id"] = providerJobId;
                    _refineTaskId = await PostTask(TextEndpoint, _refineBody, apiKey, http, ct, "refine");
                    _refineSubmitted = true;
                    result.State = ProviderPollState.Running;
                    result.Progress = 0.5f;
                    return result;
                }

                result.Progress = 1f;
                result.DownloadUrl = ExtractModelUrl(json["model_urls"] as JObject);
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    result.State = ProviderPollState.Failed;
                    result.Error = "Meshy reported success but no model URL was present in the response.";
                }
            }
            else if (state == ProviderPollState.Failed)
            {
                string err = json["task_error"]?["message"]?.ToString()
                             ?? json["message"]?.ToString()
                             ?? "Meshy task failed.";
                result.Error = SecretRedactor.Scrub(err, apiKey);
            }

            return result;
        }

        /// <summary>POST a task body and return its <c>result</c> task id (or null).</summary>
        private async Task<string> PostTask(string url, JObject body, string apiKey, IHttpTransport http, CancellationToken ct, string phase)
        {
            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = url,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            _submissionPending = true;
            Checkpoint?.Invoke(Capture()); // Durable intent before a potentially chargeable request.
            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, phase);
            string id = json["result"]?.ToString();
            if (string.IsNullOrEmpty(id))
                throw new Exception(SecretRedactor.Scrub(
                    $"Meshy {phase} returned no task id: " + ProviderHttp.Truncate(ProviderHttp.BodyText(res)), apiKey));
            if (_rootTaskId == null) _rootTaskId = id;
            else if (phase == "refine") { _refineTaskId = id; _refineSubmitted = true; }
            _submissionPending = false;
            Checkpoint?.Invoke(Capture());
            return id;
        }

        private string ExtractModelUrl(JObject urls)
        {
            if (urls == null) return null;
            string byFormat = urls[_format]?.ToString();
            if (!string.IsNullOrEmpty(byFormat)) return byFormat;
            return null; // Never save GLB/FBX fallback bytes with a different requested extension.
        }

        private static ProviderPollState MapState(string status)
        {
            switch ((status ?? string.Empty).ToUpperInvariant())
            {
                case "SUCCEEDED":
                    return ProviderPollState.Succeeded;
                case "FAILED":
                case "EXPIRED":
                case "CANCELED":
                case "CANCELLED":
                    return ProviderPollState.Failed;
                case "IN_PROGRESS":
                    return ProviderPollState.Running;
                case "PENDING":
                default:
                    return ProviderPollState.Queued;
            }
        }

        private static JObject ParseOk(HttpResult res, string apiKey, string phase)
        {
            string text = ProviderHttp.BodyText(res);

            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); } catch { /* non-JSON */ }
            }

            bool ok = res?.Ok == true;
            if (!ok)
            {
                string detail = json?["message"]?.ToString() ?? json?["error"]?.ToString() ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"Meshy {phase} failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
