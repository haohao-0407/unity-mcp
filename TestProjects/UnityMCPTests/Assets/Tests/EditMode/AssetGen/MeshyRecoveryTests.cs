using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class MeshyRecoveryTests
    {
        const string Env = "MCPFORUNITY_MESHY_API_KEY";
        const string Secret = "meshy_recovery_test_secret";
        const string Folder = "Assets/Generated/__meshy_recovery";
        string _previousKey;
        FakeHttpTransport _http;

        [SetUp]
        public void SetUp()
        {
            _previousKey = Environment.GetEnvironmentVariable(Env);
            Environment.SetEnvironmentVariable(Env, Secret);
            AssetGenJobManager.ResetForTests();
            _http = new FakeHttpTransport { Handler = spec => {
                if (spec.Method == "POST") return Json("{\"result\":\"remote-1\"}");
                if (spec.Url.Contains("cdn.example.com")) return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] {1,2,3} };
                return Json("{\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"glb\":\"https://cdn.example.com/model.glb?signature=temporary\"}}");
            } };
            AssetGenJobManager.TransportOverrideForTests = _http;
            AssetGenJobManager.ImportOverrideForTests = (job, path) => { job.AssetPath = path; return job; };
            AssetGenJobManager.PollIntervalSeconds = 0;
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable(Env, _previousKey);
            string dir = Path.Combine(Application.dataPath, "Generated/__meshy_recovery");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta");
        }

        static HttpResult Json(string text) => new HttpResult { Status = 200, IsSuccess = true, Text = text };
        static ModelGenRequest Request() => new ModelGenRequest {
            Provider = "meshy", Model = "meshy-t2", Mode = "image", ImageUrl = "https://example.com/reference.png",
            OutputFolder = Folder, Name = "recovered", Meshy = new MeshyModelOptions { EnablePbr = true }
        };
        static void Pump(string id) {
            int n = 0;
            while (!AssetGenJobManager.TryAdvanceForTests(id) && n++ < 40) {}
            Assert.Less(n, 40);
        }
        int Posts => _http.RecordedRequests.Count(x => x.Method == "POST");
        static JObject Call(JObject args) => JObject.Parse(JsonConvert.SerializeObject(GenerateModel.HandleCommand(args)));

        [TestCase(false)]
        [TestCase(true)]
        public void ReloadImmediatelyAfterSubmit_RetainsId_AndNeverResubmits(bool restartEditor)
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId); // response received, AwaitSubmit not consumed
            Assert.AreEqual("remote-1", job.ProviderTaskId);
            Assert.AreEqual(1, Posts);
            AssetGenJobManager.SimulateReloadForTests(restartEditor);
            Pump(job.JobId);
            var restored = AssetGenJobManager.GetJob(job.JobId);
            Assert.AreEqual(AssetGenJobState.Done, restored.State);
            Assert.AreEqual(1, Posts);
            StringAssert.EndsWith("recovered.glb", restored.AssetPath);
            var journal = File.ReadAllText(Path.Combine(AssetGenJobManager.JournalDirectory, job.JobId + ".json"));
            StringAssert.DoesNotContain(Secret, journal);
            StringAssert.DoesNotContain("signature=temporary", journal); // reacquire signed URLs by polling
            StringAssert.DoesNotContain("reference.png", journal); // no original image needed for recovery
        }

        [Test]
        public void DownloadedFile_ReloadContinuesImport_WithoutDownloadOrDuplicateFile()
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            for (int i = 0; i < 15 && job.State != AssetGenJobState.Importing; i++) AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.IsNotNull(job.DownloadedPath);
            int count = _http.RecordedRequests.Count;
            AssetGenJobManager.SimulateReloadForTests(true);
            Pump(job.JobId);
            Assert.AreEqual(count, _http.RecordedRequests.Count);
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(Application.dataPath, "Generated/__meshy_recovery"), "*.glb").Length);
        }

        [Test]
        public void RefineId_IsCheckpointedBeforePollCompletes_AndResumesRefineEndpoint()
        {
            var normal = _http.Handler;
            _http.Handler = spec => spec.Method == "POST" ? Json("{\"result\":\"" + (Posts == 1 ? "preview-1" : "refine-1") + "\"}") : normal(spec);
            var req = Request(); req.Mode = "text"; req.Model = "meshy-6"; req.ImageUrl = null; req.Prompt = "a tower";
            var job = AssetGenJobManager.StartModelGeneration(req);
            for (int i = 0; i < 15 && job.ProviderTaskId != "refine-1"; i++) AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.AreEqual("preview-1", job.ProviderRootTaskId);
            Assert.AreEqual("refine-1", job.ProviderTaskId);
            AssetGenJobManager.SimulateReloadForTests(true);
            Pump(job.JobId);
            Assert.AreEqual(2, Posts);
            Assert.IsTrue(_http.RecordedRequests.Any(x => x.Method == "GET" && x.Url.EndsWith("/text-to-3d/refine-1")));
            Assert.AreEqual(AssetGenJobState.Done, AssetGenJobManager.GetJob(job.JobId).State);
        }

        sealed class PendingPost : IHttpTransport
        {
            public int Posts;
            public Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct) {
                Posts++;
                return new TaskCompletionSource<HttpResult>().Task;
            }
        }

        [Test]
        public void ReloadDuringUnknownSubmission_NeverRetriesChargeablePost()
        {
            var transport = new PendingPost();
            AssetGenJobManager.TransportOverrideForTests = transport;
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.IsTrue(job.MeshyCheckpoint.SubmissionPending);
            AssetGenJobManager.SimulateReloadForTests(true);
            var restored = AssetGenJobManager.GetJob(job.JobId);
            Assert.AreEqual(AssetGenJobState.Failed, restored.State);
            StringAssert.Contains("NOT resubmitted", restored.Error);
            Assert.IsFalse(restored.CanResume);
            Assert.Throws<ArgumentException>(() => AssetGenJobManager.ResumeMeshyJob(job.JobId));
            Assert.AreEqual(1, transport.Posts);
        }

        [Test]
        public void Timeout_PreservesRemoteId_AndExplicitResumeDoesNotRegenerate()
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            job.DeadlineUnixMs = 1;
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.AreEqual(AssetGenJobState.Failed, job.State);
            Assert.IsTrue(job.CanResume);
            AssetGenJobManager.ResumeMeshyJob(job.JobId);
            Pump(job.JobId);
            Assert.AreEqual(AssetGenJobState.Done, job.State);
            Assert.AreEqual(1, Posts);
        }

        [Test]
        public void Cancel_IsDurable_AndNotAutoResumed()
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.IsTrue(AssetGenJobManager.Cancel(job.JobId));
            AssetGenJobManager.SimulateReloadForTests(true);
            Assert.AreEqual(AssetGenJobState.Canceled, AssetGenJobManager.GetJob(job.JobId).State);
            Assert.AreEqual(1, _http.RecordedRequests.Count);
        }

        [TestCase("image")]
        [TestCase("text")]
        public void ManualRemoteIdRecovery_OnlyPollsAndImports_AndIsIdempotent(string mode)
        {
            var args = new JObject { ["action"] = "resume", ["provider_task_id"] = "existing-task", ["mode"] = mode, ["output_folder"] = Folder };
            var result = Call(args);
            string id = (string)result["data"]["job_id"];
            Assert.IsNotNull(id, result.ToString());
            Pump(id);
            var again = Call(args);
            Assert.AreEqual(id, (string)again["data"]["job_id"]);
            Assert.AreEqual("done", (string)again["data"]["state"]);
            Assert.AreEqual(0, Posts);
        }

        [Test]
        public void StatusAndListJobs_ExposeBothIdsAndRecoveryState()
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            var status = Call(new JObject { ["action"] = "status", ["job_id"] = job.JobId });
            Assert.AreEqual("remote-1", (string)status["data"]["provider_task_id"]);
            Assert.AreEqual(true, (bool)status["data"]["resumable"]);
            var list = Call(new JObject { ["action"] = "list_jobs" });
            Assert.AreEqual(job.JobId, (string)list["data"]["jobs"][0]["job_id"]);
            StringAssert.DoesNotContain(Secret, status.ToString() + list.ToString());
        }

        [Test]
        public void MalformedJournal_DoesNotPreventOtherJobsFromRecovering()
        {
            var job = AssetGenJobManager.StartModelGeneration(Request());
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            File.WriteAllText(Path.Combine(AssetGenJobManager.JournalDirectory, "bad.json"), "invalid json");
            AssetGenJobManager.SimulateReloadForTests(true);
            Pump(job.JobId);
            Assert.AreEqual(AssetGenJobState.Done, AssetGenJobManager.GetJob(job.JobId).State);
        }

        [Test]
        public void PreviewReload_SubmitsOnlyMissingRefine_WithPersistedTextureOptions()
        {
            var normal = _http.Handler;
            _http.Handler = spec => spec.Method == "POST" ? Json("{\"result\":\"" + (Posts == 1 ? "preview-1" : "refine-1") + "\"}") : normal(spec);
            var req = Request(); req.Mode = "text"; req.Model = "meshy-6"; req.ImageUrl = null; req.Prompt = "a tower";
            req.Meshy.TexturePrompt = "weathered stone";
            var job = AssetGenJobManager.StartModelGeneration(req);
            AssetGenJobManager.TryAdvanceForTests(job.JobId);
            AssetGenJobManager.SimulateReloadForTests(true);
            Pump(job.JobId);
            Assert.AreEqual(2, Posts);
            var post = _http.RecordedRequests.Last(x => x.Method == "POST");
            var body = JObject.Parse(System.Text.Encoding.UTF8.GetString(post.Body));
            Assert.AreEqual("refine", (string)body["mode"]);
            Assert.AreEqual("preview-1", (string)body["preview_task_id"]);
            Assert.AreEqual("4k", (string)body["texture_resolution"]);
            Assert.AreEqual("weathered stone", (string)body["texture_prompt"]);
        }

        sealed class PendingRefine : IHttpTransport
        {
            public int Posts;
            public IHttpTransport Inner;
            public Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct) {
                if (spec.Method == "POST" && ++Posts == 2) return new TaskCompletionSource<HttpResult>().Task;
                return Inner.SendAsync(spec, ct);
            }
        }

        [Test]
        public void ReloadDuringRefineSubmission_KeepsPreviewId_AndNeverRetriesRefinePost()
        {
            var transport = new PendingRefine { Inner = _http };
            AssetGenJobManager.TransportOverrideForTests = transport;
            var req = Request(); req.Mode = "text"; req.Model = "meshy-6"; req.ImageUrl = null; req.Prompt = "a tower";
            var job = AssetGenJobManager.StartModelGeneration(req);
            for (int i = 0; i < 15 && transport.Posts < 2; i++) AssetGenJobManager.TryAdvanceForTests(job.JobId);
            Assert.AreEqual(2, transport.Posts);
            AssetGenJobManager.SimulateReloadForTests(true);
            var restored = AssetGenJobManager.GetJob(job.JobId);
            Assert.AreEqual("remote-1", restored.ProviderRootTaskId);
            Assert.IsTrue(restored.MeshyCheckpoint.SubmissionPending);
            Assert.IsFalse(restored.CanResume);
            Assert.AreEqual(AssetGenJobState.Failed, restored.State);
            Assert.AreEqual(2, transport.Posts);
        }
    }
}
