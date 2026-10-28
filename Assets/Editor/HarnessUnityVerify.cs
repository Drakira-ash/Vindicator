using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Editor-side compile verification for the .agent harness.
/// Writes raw evidence to .agent/.local/unity-verify-editor.json;
/// agent.py unity-verify --adopt-editor stamps it with the harness signature.
/// </summary>
[InitializeOnLoad]
public static class HarnessUnityVerify
{
    private const string MenuPath = "Tools/Harness/Verify Compilation";
    private const string PendingRelative = ".agent/.local/unity-verify.pending.json";
    private const string EvidenceRelative = ".agent/.local/unity-verify-editor.json";
    private const long PendingMaxAgeSeconds = 600;

    private static readonly List<MessageRecord> ErrorRecords = new List<MessageRecord>();
    private static readonly List<MessageRecord> WarningRecords = new List<MessageRecord>();
    private static readonly List<string> Assemblies = new List<string>();
    private static readonly List<string> NotRequired = new List<string>();
    private static bool running;
    private static double startedAt;

    static HarnessUnityVerify()
    {
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        CompilationPipeline.assemblyCompilationNotRequired += OnAssemblyCompilationNotRequired;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
        EditorApplication.delayCall += OnEditorReady;
    }

    [MenuItem(MenuPath)]
    public static void Run()
    {
        if (running || EditorApplication.isCompiling)
        {
            Debug.LogWarning("[Harness] 已有编译在进行，忽略验证请求。");
            return;
        }
        if (EvidenceDirectory() == null)
        {
            Debug.LogError("[Harness] 无法定位项目根目录，编译验证未启动。");
            return;
        }
        Reset();
        running = true;
        startedAt = EditorApplication.timeSinceStartup;
        WritePending();
        Debug.Log("[Harness] 已请求脚本编译验证，结果将写入 " + EvidenceRelative);
        CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
    }

    private static void Reset()
    {
        ErrorRecords.Clear();
        WarningRecords.Clear();
        Assemblies.Clear();
        NotRequired.Clear();
    }

    private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
    {
        if (!running || messages == null) return;
        string assemblyName = string.IsNullOrEmpty(assemblyPath)
            ? "unknown"
            : Path.GetFileNameWithoutExtension(assemblyPath);
        if (!Assemblies.Contains(assemblyName)) Assemblies.Add(assemblyName);
        foreach (CompilerMessage message in messages)
        {
            MessageRecord record = new MessageRecord();
            record.message = message.message ?? string.Empty;
            record.file = message.file ?? string.Empty;
            record.line = message.line;
            record.column = message.column;
            record.assembly = assemblyName;
            if (message.type == CompilerMessageType.Error) ErrorRecords.Add(record);
            else if (message.type == CompilerMessageType.Warning) WarningRecords.Add(record);
        }
    }

    private static void OnAssemblyCompilationNotRequired(string assemblyPath)
    {
        if (!running) return;
        string name = string.IsNullOrEmpty(assemblyPath)
            ? "unknown"
            : Path.GetFileNameWithoutExtension(assemblyPath);
        if (!NotRequired.Contains(name)) NotRequired.Add(name);
    }

    private static void OnCompilationFinished(object context)
    {
        if (!running) return;
        running = false;
        bool failed = ErrorRecords.Count > 0 || EditorUtility.scriptCompilationFailed;
        WriteEvidence(failed ? "fail" : "pass", "CompilationPipeline.compilationFinished", false);
        TryDeletePending();
    }

    private static void OnEditorReady()
    {
        string pending = Absolute(PendingRelative);
        if (pending == null || !File.Exists(pending)) return;
        PendingDto dto = null;
        try
        {
            dto = JsonUtility.FromJson<PendingDto>(File.ReadAllText(pending, Encoding.UTF8));
        }
        catch (Exception)
        {
            dto = null;
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (dto == null || dto.requested_at_unix <= 0 || now - dto.requested_at_unix > PendingMaxAgeSeconds)
        {
            TryDeletePending();
            Debug.LogWarning("[Harness] 丢弃无效或过期的编译验证请求。");
            return;
        }
        if (EditorApplication.isCompiling)
        {
            // 成功的编译会触发 domain reload；仍在编译时交给 compilationFinished。
            return;
        }
        bool failed = EditorUtility.scriptCompilationFailed;
        WriteEvidence(failed ? "fail" : "pass", "domain reload fallback", true);
        TryDeletePending();
    }

    private static void WriteEvidence(string result, string source, bool reloadFallback)
    {
        EvidenceDto dto = new EvidenceDto();
        dto.schema_version = 1;
        dto.kind = "unity-editor-compile-evidence";
        dto.compiled_at = UtcNowIso();
        dto.result = result;
        dto.forced = true;
        dto.source = source;
        dto.unity_version = Application.unityVersion;
        dto.editor_pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        dto.editor_instance = Application.productName + "@" + SystemInfo.deviceName;
        dto.project_path = ProjectRoot();
        dto.duration_seconds = reloadFallback
            ? 0.0
            : Math.Round(EditorApplication.timeSinceStartup - startedAt, 2);
        dto.error_count = ErrorRecords.Count;
        dto.warning_count = WarningRecords.Count;
        dto.errors = ErrorRecords.GetRange(0, Math.Min(ErrorRecords.Count, 100)).ToArray();
        dto.warnings = WarningRecords.GetRange(0, Math.Min(WarningRecords.Count, 50)).ToArray();
        dto.assemblies_compiled = Assemblies.ToArray();
        dto.assemblies_not_recompiled = NotRequired.ToArray();
        dto.note = reloadFallback
            ? "domain reload 后补写：编译成功才会重载程序域，因此判定为 pass。"
            : "由 CompilationPipeline.RequestScriptCompilation(CleanBuildCache) 强制全量重编译。";
        WriteTextAtomic(Absolute(EvidenceRelative), JsonUtility.ToJson(dto, true));
        Debug.Log("[Harness] 编译验证: " + result
                  + "（errors=" + dto.error_count + ", warnings=" + dto.warning_count
                  + ", 未重编译=" + dto.assemblies_not_recompiled.Length + "）");
        if (result != "pass")
        {
            for (int i = 0; i < Math.Min(ErrorRecords.Count, 10); i++)
            {
                MessageRecord record = ErrorRecords[i];
                Debug.LogError("[Harness] " + record.file + "(" + record.line + "): " + record.message);
            }
        }
    }

    private static void WritePending()
    {
        PendingDto dto = new PendingDto();
        dto.schema_version = 1;
        dto.requested_at = UtcNowIso();
        dto.requested_at_unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        dto.requested_by = "HarnessUnityVerify.Run";
        WriteTextAtomic(Absolute(PendingRelative), JsonUtility.ToJson(dto, true));
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

    private static string EvidenceDirectory()
    {
        string root = ProjectRoot();
        return root == null ? null : Path.Combine(root, ".agent", ".local");
    }

    private static string Absolute(string relative)
    {
        string root = ProjectRoot();
        if (root == null) return null;
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    [Serializable]
    private class MessageRecord
    {
        public string message;
        public string file;
        public int line;
        public int column;
        public string assembly;
    }

    [Serializable]
    private class EvidenceDto
    {
        public int schema_version;
        public string kind;
        public string compiled_at;
        public string result;
        public bool forced;
        public string source;
        public string unity_version;
        public int editor_pid;
        public string editor_instance;
        public string project_path;
        public double duration_seconds;
        public int error_count;
        public int warning_count;
        public MessageRecord[] errors;
        public MessageRecord[] warnings;
        public string[] assemblies_compiled;
        public string[] assemblies_not_recompiled;
        public string note;
    }

    [Serializable]
    private class PendingDto
    {
        public int schema_version;
        public string requested_at;
        public long requested_at_unix;
        public string requested_by;
    }
}
