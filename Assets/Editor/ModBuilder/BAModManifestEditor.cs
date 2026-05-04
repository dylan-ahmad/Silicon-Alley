#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace BAModTemplate.Editor
{
    [CustomEditor(typeof(BAModManifest))]
    public sealed class BAModManifestEditor : UnityEditor.Editor
    {
        // Validation hits the disk (every asmdef in the project) plus reflects over every
        // assembly in the domain, so it's far too expensive to run on every OnInspectorGUI
        // repaint. We cache per-editor-instance and invalidate only on project / compilation
        // changes or when the user explicitly asks for a re-run.
        private IReadOnlyList<ValidationIssue>? _issues;
        private DiscoveredMod? _discovered;
        private bool _cacheMissing;

        private void OnEnable()
        {
            EditorApplication.projectChanged += Invalidate;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Invalidate;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
        }

        private void OnCompilationFinished(object _) => Invalidate();

        private void Invalidate()
        {
            _issues = null;
            _discovered = null;
            _cacheMissing = false;
            Repaint();
        }

        private void EnsureValidated()
        {
            if (_issues != null || _cacheMissing) return;

            var manifest = (BAModManifest)target;
            _discovered = ModDiscovery.DiscoverFor(manifest);
            if (_discovered == null)
            {
                _cacheMissing = true;
                return;
            }

            var allMods = ModDiscovery.DiscoverAll();
            _issues = ModValidator.Validate(_discovered, allMods);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Re-validate", EditorStyles.miniButton, GUILayout.Width(90)))
                    Invalidate();
            }

            EnsureValidated();

            if (_cacheMissing)
            {
                EditorGUILayout.HelpBox(
                    "Manifest is not discoverable. Check that it lives under Assets/Mods/<ModId>/.",
                    MessageType.Warning);
                return;
            }

            var issues = _issues!;
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("All checks pass.", MessageType.Info);
            }
            else
            {
                foreach (var issue in issues)
                {
                    var msgType = issue.Severity switch
                    {
                        Severity.Error => MessageType.Error,
                        Severity.Warning => MessageType.Warning,
                        _ => MessageType.Info,
                    };

                    EditorGUILayout.HelpBox(issue.Message, msgType);
                    if (issue.QuickFix != null)
                    {
                        var label = string.IsNullOrEmpty(issue.QuickFixLabel) ? "Fix" : issue.QuickFixLabel;
                        if (GUILayout.Button(label!))
                        {
                            issue.QuickFix.Invoke();
                            Invalidate();
                        }
                    }
                }
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Open Mod Builder Window"))
            {
                ModBuilderWindow.ShowWindow();
            }
        }
    }
}
