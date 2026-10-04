using System;
using System.Collections;
using System.IO;
using System.Reflection;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.AssetGen
{
    // Runs after the synchronous fixtures. The reload bootstrap exists only in the test assembly.
    public class ZZZMeshyDomainReloadTests
    {
        const string Prefix = "MeshyDomainReloadTest.";
        static readonly string DomainMarker = Guid.NewGuid().ToString("N");

        [InitializeOnLoadMethod]
        static void InstallTestTransportAfterReload()
        {
            if (!SessionState.GetBool(Prefix + "active", false)) return;
            SessionState.SetString(Prefix + "bootstrap", DomainMarker);
            string journal = SessionState.GetString(Prefix + "journal", "");
            typeof(AssetGenJobManager).GetField("_journalDirectoryForTests", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, journal);
            var keys = new EncryptedFileKeyStore(Path.Combine(journal, "keys"));
            keys.Set("meshy", "fake-reload-key");
            SecureKeyStore.OverrideForTests(keys);
            AssetGenJobManager.TransportOverrideForTests = new FakeHttpTransport { Handler = request => {
                if (request.Method == "POST") {
                    SessionState.SetInt(Prefix + "posts", SessionState.GetInt(Prefix + "posts", 0) + 1);
                    return new HttpResult { Status = 200, IsSuccess = true, Text = "{\"result\":\"real-reload-remote\"}" };
                }
                if (request.Url.Contains("cdn.example.com")) return new HttpResult { Status = 200, IsSuccess = true, Body = new byte[] {1,2,3} };
                return new HttpResult { Status = 200, IsSuccess = true, Text = SessionState.GetBool(Prefix + "finish", false)
                    ? "{\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"glb\":\"https://cdn.example.com/a.glb\"}}"
                    : "{\"status\":\"IN_PROGRESS\",\"progress\":50}" };
            } };
            AssetGenJobManager.ImportOverrideForTests = (job, path) => { job.AssetPath = path; return job; };
            AssetGenJobManager.PollIntervalSeconds = 0;
        }

        [UnityTest]
        public IEnumerator RealDomainReload_ResumesExistingRemoteTask()
        {
            if (!SessionState.GetBool(Prefix + "active", false))
            {
                AssetGenJobManager.ResetForTests();
                SessionState.SetString(Prefix + "journal", AssetGenJobManager.JournalDirectory);
                SessionState.SetString(Prefix + "before", DomainMarker);
                SessionState.SetBool(Prefix + "active", true);
                SessionState.SetBool(Prefix + "finish", false);
                SessionState.SetInt(Prefix + "posts", 0);
                InstallTestTransportAfterReload();
                var job = AssetGenJobManager.StartModelGeneration(new ModelGenRequest {
                    Provider = "meshy", Model = "meshy-t2", Mode = "image", ImageUrl = "https://example.com/ref.png",
                    OutputFolder = "Assets/Generated/__meshy_real_reload", Name = "reload"
                });
                AssetGenJobManager.TryAdvanceForTests(job.JobId);
                Assert.AreEqual("real-reload-remote", job.ProviderTaskId);
                SessionState.SetString(Prefix + "job", job.JobId);
                EditorUtility.RequestScriptReload();
            }
            yield return new WaitForDomainReload();
            try
            {
                Assert.AreNotEqual(SessionState.GetString(Prefix + "before", ""), DomainMarker, "An actual domain reload must have occurred.");
                string id = SessionState.GetString(Prefix + "job", "");
                for (int i = 0; i < 60 && AssetGenJobManager.GetJob(id) == null; i++) yield return null;
                var restored = AssetGenJobManager.GetJob(id);
                Assert.IsNotNull(restored, "id=" + id + "; index=" + SessionState.GetString("MCPForUnity.AssetGen.JobIndex", "")
                    + "; journal=" + AssetGenJobManager.JournalDirectory
                    + "; files=" + (Directory.Exists(AssetGenJobManager.JournalDirectory) ? string.Join(",", Directory.GetFiles(AssetGenJobManager.JournalDirectory, "*.json")) : "missing")
                    + "; bootstrap=" + SessionState.GetString(Prefix + "bootstrap", "") + "; domain=" + DomainMarker
                    + "; record=" + SessionState.GetString("MCPForUnity.AssetGen.Job." + id, "missing"));
                Assert.AreEqual("real-reload-remote", restored.ProviderTaskId);
                SessionState.SetBool(Prefix + "finish", true);
                for (int i = 0; i < 40 && restored.State != AssetGenJobState.Done; i++) AssetGenJobManager.TryAdvanceForTests(id);
                Assert.AreEqual(AssetGenJobState.Done, restored.State, restored.Error);
                Assert.AreEqual(1, SessionState.GetInt(Prefix + "posts", 0), "Reload must not submit another paid generation.");
            }
            finally
            {
                SessionState.SetBool(Prefix + "active", false);
                AssetGenJobManager.ResetForTests();
                SecureKeyStore.ResetForTests();
                string dir = Path.Combine(Application.dataPath, "Generated/__meshy_real_reload");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta");
            }
        }
    }
}
