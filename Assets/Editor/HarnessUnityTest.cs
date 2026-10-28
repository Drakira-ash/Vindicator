using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// Editor-side one-click EditMode test runner for the .agent harness.
/// Writes raw evidence to .agent/.local/unity-test-editor.json;
/// agent.py unity-test --adopt-editor stamps it with the harness signature.
/// </summary>
[InitializeOnLoad]
public static class HarnessUnityTest
{
    private const string MenuPath = "Tools/Harness/Run EditMode Tests";
    private const string PendingRelative = ".agent/.local/unity-test.pending.json";
    private const string EvidenceRelative = ".agent/.local/unity-test-editor.json";

    private static readonly List<FailureRecord> Failures = new List<FailureRecord>();
    private static TestRunnerApi api;
    private static Callbacks callbacks;
    private static bool running;
    private static double startedAt;
    private static string startedIso;
    private static int total;
    private static int passed;
    private static int failed;
    private static int skipped;
    private static int inconclusive;

    static HarnessUnityTest()
    {
        EditorApplication.delayCall += OnEditorReady;
    }

    [MenuItem(MenuPath)]
    public static void Run()
    {
        if (running)
        {
            Debug.LogWarning("[Harness] 已有测试运行在进行，忽略请求。");
            return;
        }
        if (EditorApplication.isCompiling)
        {
            Debug.LogWarning("[Harness] 正在编译，等编译结束后再运行测试。");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Harness] 请先退出 Play Mode 再运行 EditMode 测试。");
            return;
        }
        if (ProjectRoot() == null)
        {
            Debug.LogError("[Harness] 无法定位项目根目录，测试未启动。");
            return;
        }

        Reset();
        running = true;
        startedAt = EditorApplication.timeSinceStartup;
        startedIso = UtcNowIso();
        WriteTextAtomic(Absolute(PendingRelative), "{\"requested_at\":\"" + startedIso + "\"}");

        callbacks = new Callbacks();
        api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(callbacks);
        ExecutionSettings settings = new ExecutionSettings(new Filter[]
        {
            new Filter { testMode = TestMode.EditMode }
        });
        Debug.Log("[Harness] 开始运行 EditMode 测试，结果将写入 " + EvidenceRelative);
        api.Execute(settings);
    }

    private static void Reset()
    {
        total = 0;
        passed = 0;
        failed = 0;
        skipped = 0;
        inconclusive = 0;
        Failures.Clear();
    }

    private static void OnRunFinished(ITestResultAdaptor result)
    {
        if (!running) return;
        running = false;
        try
        {
            if (result != null) Accumulate(result);
            string state = failed > 0 ? "fail" : (total > 0 ? "pass" : "empty");
            WriteEvidence(state);
        }
        finally
        {
            if (api != null && callbacks != null) api.UnregisterCallbacks(callbacks);
            api = null;
            callbacks = null;
            TryDeletePending();
        }
    }

    private static void Accumulate(ITestResultAdaptor node)
    {
        if (node == null) return;
        if (node.HasChildren && node.Children != null)
        {
            foreach (ITestResultAdaptor child in node.Children) Accumulate(child);
            return;
        }
        total++;
        string state = node.ResultState ?? string.Empty;
        if (state.StartsWith("Passed", StringComparison.Ordinal)) passed++;
        else if (state.StartsWith("Failed", StringComparison.Ordinal))
        {
            failed++;
            FailureRecord record = new FailureRecord();
            record.name = node.FullName ?? node.Name ?? "unknown";
            record.message = node.Message ?? string.Empty;
            record.stack_trace = node.StackTrace ?? string.Empty;
            Failures.Add(record);
        }
        else if (state.StartsWith("Skipped", StringComparison.Ordinal)) skipped++;
        else inconclusive++;
    }

    private static void WriteEvidence(string result)
    {
        EvidenceDto dto = new EvidenceDto();
        dto.schema_version = 1;
        dto.kind = "unity-test-editor-evidence";
        dto.started_at = startedIso;
        dto.finished_at = UtcNowIso();
        dto.result = result;
        dto.test_mode = "EditMode";
        dto.total = total;
        dto.passed = passed;
        dto.failed = failed;
        dto.skipped = skipped;
        dto.inconclusive = inconclusive;
        dto.duration_seconds = Math.Round(EditorApplication.timeSinceStartup - startedAt, 2);
        dto.unity_version = Application.unityVersion;
        dto.editor_pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        dto.editor_instance = Application.productName + "@" + SystemInfo.deviceName;
        dto.project_path = ProjectRoot();
        dto.failures = Failures.ToArray();
        dto.note = result == "empty"
            ? "没有可运行的 EditMode 测试；请检查测试程序集与 defineConstraints。"
            : "由 TestRunnerApi 在编辑器内运行 EditMode 测试。";
        WriteTextAtomic(Absolute(EvidenceRelative), JsonUtility.ToJson(dto, true));
        Debug.Log("[Harness] EditMode 测试: " + result
                  + "（total=" + total + ", passed=" + passed + ", failed=" + failed
                  + ", skipped=" + skipped + ", inconclusive=" + inconclusive + "）");
        if (failed > 0)
        {
            for (int i = 0; i < Math.Min(Failures.Count, 10); i++)
            {
                Debug.LogError("[Harness] 失败: " + Failures[i].name + " — " + Failures[i].message);
            }
        }
    }

    private static void OnEditorReady()
    {
        if (!File.Exists(Absolute(PendingRelative))) return;
        TryDeletePending();
        Debug.LogWarning("[Harness] 上一次 EditMode 测试运行被域重载中断，结果不可用，未写入证据。");
    }

    private static void TryDeletePending()
    {
        string path = Absolute(PendingRelative);
        if (path == null || !File.Exists(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // 删除失败不影响证据文件。
        }
    }

    private static void WriteTextAtomic(string path, string content)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    private static string UtcNowIso()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string ProjectRoot()
    {
        DirectoryInfo parent = Directory.GetParent(Application.dataPath);
        return parent == null ? null : parent.FullName;
    }

    private static string Absolute(string relative)
    {
        string root = ProjectRoot();
        if (root == null) return null;
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void RunFinished(ITestResultAdaptor result) { OnRunFinished(result); }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
    }

    [Serializable]
    private class FailureRecord
    {
        public string name;
        public string message;
        public string stack_trace;
    }

    [Serializable]
    private class EvidenceDto
    {
        public int schema_version;
        public string kind;
        public string started_at;
        public string finished_at;
        public string result;
        public string test_mode;
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public double duration_seconds;
        public string unity_version;
        public int editor_pid;
        public string editor_instance;
        public string project_path;
        public FailureRecord[] failures;
        public string note;
    }
}
