using System;
using System.IO;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class GenerateModelHandlerTests
    {
        private string _dir;
        private EncryptedFileKeyStore _store;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable("MCPFORUNITY_TRIPO_API_KEY", null);
            _dir = Path.Combine(Path.GetTempPath(), "mcp_handler_" + Guid.NewGuid().ToString("N"));
            _store = new EncryptedFileKeyStore(_dir);
            SecureKeyStore.OverrideForTests(_store);
            AssetGenJobManager.TransportOverrideForTests = new FakeHttpTransport();
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            SecureKeyStore.ResetForTests();
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
            try { Directory.Delete(Path.Combine(ProjectRoot(), "Assets/Generated/__assetgen_model_handler"), true); } catch { }
        }

        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(GenerateModel.HandleCommand(p)));

        private static string ProjectRoot()
        {
            string dp = Application.dataPath.Replace('\\', '/');
            return dp.Substring(0, dp.Length - "Assets".Length);
        }

        private static string WriteProjectFile(string rel, byte[] bytes)
        {
            string abs = Path.Combine(ProjectRoot(), rel).Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, bytes);
            return rel;
        }

        [Test]
        public void Generate_WithKey_ReturnsPendingJobId_AndStatusReportsState()
        {
            _store.Set("tripo", "handlerkey123");

            JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "tripo", ["mode"] = "text", ["prompt"] = "a tree" });
            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
            string jobId = (string)gen["data"]["job_id"];
            Assert.IsFalse(string.IsNullOrEmpty(jobId));

            JObject st = Call(new JObject { ["action"] = "status", ["job_id"] = jobId });
            Assert.AreEqual("pending", (string)st["_mcp_status"]);
            Assert.IsNotNull(st["data"]["state"]);
        }

        [Test]
        public void Generate_NoKey_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "tripo", ["mode"] = "text", ["prompt"] = "a tree" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("No API key", (string)resp["error"]);
        }

        [Test]
        public void Generate_UnimplementedProvider_ReturnsError()
        {
            _store.Set("hunyuan", "k");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "hunyuan", ["mode"] = "text", ["prompt"] = "a tree" });
            Assert.AreEqual(false, (bool)resp["success"]);
        }

        [Test]
        public void Generate_ImageMode_MissingFile_ReturnsError()
        {
            // Use a provider that supports local images (Meshy) so we exercise the file-existence
            // check; Tripo short-circuits on any local path (covered separately).
            _store.Set("meshy", "k");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "meshy", ["mode"] = "image", ["imagePath"] = "Assets/does_not_exist_zzz.png" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("not found", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void Generate_ImageMode_PathOutsideAssets_ReturnsError()
        {
            _store.Set("meshy", "k");
            string tmp = Path.Combine(Path.GetTempPath(), "mcp_imgin_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tmp, new byte[] { 137, 80, 78, 71 });
            try
            {
                JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "meshy", ["mode"] = "image", ["imagePath"] = tmp });
                Assert.AreEqual(false, (bool)resp["success"]);
                StringAssert.Contains("Assets", (string)resp["error"]);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Test]
        public void Generate_ImageMode_ProjectLocalPath_Accepted_ReturnsPending()
        {
            _store.Set("meshy", "k");
            string rel = WriteProjectFile("Assets/Generated/__assetgen_model_handler/ref.png", new byte[] { 137, 80, 78, 71 });

            JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "meshy", ["mode"] = "image", ["imagePath"] = rel });

            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
        }

        [Test]
        public void Generate_ImageMode_Tripo_LocalPath_ReturnsErrorSynchronously()
        {
            _store.Set("tripo", "k");
            string tmp = Path.Combine(Path.GetTempPath(), "mcp_tripoimg_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tmp, new byte[] { 137, 80, 78, 71 });
            try
            {
                JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "tripo", ["mode"] = "image", ["imagePath"] = tmp });
                Assert.AreEqual(false, (bool)resp["success"]);                 // synchronous error, not a fake 'pending'
                StringAssert.Contains("Tripo", (string)resp["error"]);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Test]
        public void Generate_TextMode_RequiresPrompt()
        {
            _store.Set("tripo", "k");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "tripo", ["mode"] = "text" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("prompt", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void ListProviders_IncludesTripoModel()
        {
            JObject resp = Call(new JObject { ["action"] = "list_providers" });
            Assert.AreEqual(true, (bool)resp["success"]);
            string s = resp.ToString();
            StringAssert.Contains("tripo", s);
            StringAssert.Contains("model", s);
        }

        [Test]
        public void UnknownAction_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "frobnicate" });
            Assert.AreEqual(false, (bool)resp["success"]);
        }

        [TestCase("{ 'target_polycount': 16000, 'model': 'meshy-t2' }")]
        [TestCase("{ 'enable_pbr': 'invalid' }")]
        [TestCase("{ 'target_polycount': 'invalid' }")]
        [TestCase("{ 'target_formats': [] }")]
        [TestCase("{ 'texture_resolution': '16k' }")]
        public void Meshy_InvalidOptions_ReturnSynchronousError(string json)
        {
            _store.Set("meshy", "k");
            var p = JObject.Parse(json);
            p["action"] = "generate";
            p["provider"] = "meshy";
            p["prompt"] = "tower";
            var result = Call(p);
            Assert.AreEqual(false, (bool)result["success"]);
            Assert.IsNull(result["_mcp_status"]);
        }

        [Test]
        public void Meshy_ExistingPreview_DoesNotRequirePrompt()
        {
            _store.Set("meshy", "k");
            var result = Call(new JObject { ["action"] = "generate", ["provider"] = "meshy", ["preview_task_id"] = "existing" });
            Assert.AreEqual("pending", (string)result["_mcp_status"]);
        }

        [Test]
        public void Meshy_ImageTask_DoesNotRequireLocalImage()
        {
            _store.Set("meshy", "k");
            var result = Call(new JObject { ["action"] = "generate", ["provider"] = "meshy", ["mode"] = "image", ["input_task_id"] = "image" });
            Assert.AreEqual("pending", (string)result["_mcp_status"]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Meshy_AutoSize_PreservesCloudSizeUnlessExplicitlyOverridden(bool overrideSize)
        {
            _store.Set("meshy", "k");
            var p = new JObject { ["action"] = "generate", ["provider"] = "meshy", ["prompt"] = "tower", ["auto_size"] = true };
            if (overrideSize) p["target_size"] = 2f;
            var response = Call(p);
            string id = (string)response["data"]["job_id"];
            Assert.AreEqual(overrideSize ? 2f : 0f, AssetGenJobManager.GetJob(id).TargetSize);
        }

        [Test]
        public void Meshy_HandlerOptions_ReachPreviewAndDefault4kRefine()
        {
            _store.Set("meshy", "k");
            var fake = new FakeHttpTransport { Handler = request => new HttpResult {
                Status = 200, IsSuccess = true, Text = request.Method == "POST"
                    ? "{\"result\":\"task\"}" : "{\"status\":\"SUCCEEDED\",\"progress\":100}" } };
            AssetGenJobManager.TransportOverrideForTests = fake;
            AssetGenJobManager.PollIntervalSeconds = 0;
            var p = new JObject { ["action"] = "generate", ["provider"] = "meshy", ["prompt"] = "tower",
                ["model_type"] = "smart-topology", ["target_polycount"] = 4000, ["enable_pbr"] = true };
            var response = Call(p);
            string id = (string)response["data"]["job_id"];
            for (int i = 0; i < 20 && fake.RecordedRequests.Count < 3; i++) AssetGenJobManager.TryAdvanceForTests(id);
            Assert.GreaterOrEqual(fake.RecordedRequests.Count, 3);
            var preview = JObject.Parse(System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body));
            var refine = JObject.Parse(System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[2].Body));
            Assert.AreEqual("meshy-t2", (string)preview["ai_model"]);
            Assert.AreEqual(4000, (int)preview["target_polycount"]);
            Assert.AreEqual("4k", (string)refine["texture_resolution"]);
            Assert.AreEqual(true, (bool)refine["enable_pbr"]);
        }
    }
}
