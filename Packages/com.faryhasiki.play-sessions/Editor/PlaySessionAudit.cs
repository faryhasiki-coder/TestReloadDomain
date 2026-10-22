using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Faryhasiki.PlaySessions.Editor
{
    [Serializable]
    public sealed class AuditReport
    {
        public string generatedUtc;
        public string unityVersion;
        public bool fastPlay;
        public bool requiresEditorRestartAfterPlay;
        public string[] scannedAssemblies;
        public string[] modules;
        public List<SessionDiagnostic> diagnostics = new List<SessionDiagnostic>();
        public string limitations = "Metadata audit only; reset/disposal correctness, arbitrary async writes, native state and unreviewed dependencies are not proven safe.";
        public bool HasErrors => diagnostics.Any(d => d.severity == "Error");
    }

    public static class PlaySessionAudit
    {
        public static AuditReport Run(bool scanFields = true)
        {
            var settings = PlaySessionsSettings.instance;
            var report = new AuditReport { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, fastPlay = UnitySessionBridge.FastPlay };
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToArray();
            SessionPlan plan;
            try { plan = SessionCatalog.Build(assemblies); report.modules = plan.Resolve().Select(m => m.Id).ToArray(); }
            catch (Exception ex)
            {
                report.diagnostics.Add(new SessionDiagnostic("Error", "PS001", "Session plan", ex.GetBaseException().Message));
                plan = new SessionPlan(); report.modules = Array.Empty<string>();
            }
            if (report.fastPlay && (Application.unityVersion != settings.verifiedUnityVersion || string.IsNullOrWhiteSpace(settings.verificationEvidence)))
                report.diagnostics.Add(new SessionDiagnostic("Error", "PS301", Application.unityVersion, "Unity version has not been verified. Run the migration tests, then record the version and evidence in Monitor."));
            if (UnitySessionBridge.SceneReloadDisabled)
                report.diagnostics.Add(new SessionDiagnostic("Error", "PS302", "Scene Reload", "This version requires Scene Reload enabled."));

            // Include Assets runtime assemblies even if they forgot to opt in. Skip Editor/test assemblies.
            var projectNames = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Where(a => a.sourceFiles.Any(f => Path.GetFullPath(f).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.name).ToHashSet();
            var targets = assemblies.Where(a => projectNames.Contains(a.GetName().Name) ||
                a.IsDefined(typeof(PlaySessionAssemblyAttribute), false)).ToArray();
            report.scannedAssemblies = targets.Select(a => a.GetName().Name).OrderBy(x => x).ToArray();
            if (scanFields) report.diagnostics.AddRange(SessionAudit.Scan(targets, plan));
            var declarations = assemblies.SelectMany(a => a.GetCustomAttributes<PlaySessionCompatibilityAttribute>()).ToArray();
            var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(p => p.name != "com.faryhasiki.play-sessions" && p.source != PackageSource.BuiltIn)
                .Select(p => new InstalledDependency { Kind = DependencyKind.Package, Name = p.name, Version = p.version }).ToList();
            // User-imported DLLs + explicitly declared managed assemblies. Engine/framework DLLs are not blanket-scanned.
            var dllNames = PluginImporter.GetAllImporters().Where(p => !p.isNativePlugin && p.assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                .Select(p => Path.GetFileNameWithoutExtension(p.assetPath)).ToHashSet();
            foreach (var policy in declarations.Where(p => p.Kind == DependencyKind.ManagedAssembly)) dllNames.Add(policy.Name);
            foreach (var assembly in assemblies.Where(a => dllNames.Contains(a.GetName().Name)))
            {
                string hash = null;
                try { using (var stream = File.OpenRead(assembly.Location)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
                catch (Exception ex) { report.diagnostics.Add(new SessionDiagnostic("Error", "PS210", assembly.FullName, "Cannot fingerprint DLL: " + ex.Message)); }
                installed.Add(new InstalledDependency { Kind = DependencyKind.ManagedAssembly, Name = assembly.GetName().Name, Version = assembly.GetName().Version.ToString(), Sha256 = hash });
            }
            foreach (var importer in PluginImporter.GetAllImporters().Where(p => p.isNativePlugin && p.GetCompatibleWithEditor()))
                report.diagnostics.Add(new SessionDiagnostic(settings.requireDependencyCoverage ? "Error" : "Warning", "PS211", importer.assetPath,
                    "Native plugin: audit cannot prove process-state cleanup. Verify shutdown/reinit or restart the Editor."));
            report.diagnostics.AddRange(CompatibilityCheck.Evaluate(declarations, installed, plan, report.fastPlay, settings.requireDependencyCoverage));
            report.requiresEditorRestartAfterPlay = declarations.Any(p => p.Handling == DependencyHandling.EditorRestartRequired && installed.Any(d => d.Kind == p.Kind && d.Name == p.Name));
            if (UnityEditor.SessionState.GetBool(PlaySessionEditor.RestartRequiredKey, false))
                report.diagnostics.Add(new SessionDiagnostic("Error", "PS212", "Editor process", "A dependency used during Play requires an Editor restart. Script/Domain Reload cannot clear this requirement."));
            return report;
        }

        public static string Export(AuditReport report)
        {
            const string path = "outputs/play-sessions/audit.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return Path.GetFullPath(path);
        }

        // Offline Unity batch command. Dependencies must already be resolved and imported.
        public static void RunBatch()
        {
            var report = Run();
            Debug.Log("[Play Sessions] Audit: " + Export(report));
            bool failed = report.HasErrors || PlaySessionsSettings.instance.warningsBlockFastPlay && report.diagnostics.Any(d => d.severity == "Warning");
            if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
            else if (failed) Debug.LogError("[Play Sessions] Audit failed. See exported report.");
        }
    }
}
