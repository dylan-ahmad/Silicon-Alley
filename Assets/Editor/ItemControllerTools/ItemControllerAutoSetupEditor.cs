#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace BAModTemplate.Editor.ItemControllerTools
{
    [CustomEditor(typeof(ItemController), true)]
    public sealed class ItemControllerAutoSetupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Big Ambitions Mod Tools", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Rebuilds ItemController auto-managed references for mod prefabs. " +
                    "Unlike the game source button, this preserves the current itemName.",
                    MessageType.Info);

                if (GUILayout.Button("Auto Configure ItemController References"))
                    AutoConfigure((ItemController)target);
            }
        }

        private static void AutoConfigure(ItemController controller)
        {
            Undo.RecordObject(controller, "Auto Configure ItemController References");

            var serialized = new SerializedObject(controller);
            var transform = controller.transform;

            SetObjectArray(serialized.FindProperty("renderers"), GetRenderers(controller));
            SetObjectArray(serialized.FindProperty("navMeshTargets"), GetNavMeshTargets(transform));
            SetObjectArray(serialized.FindProperty("attachmentPoints"),
                controller.GetComponentsInChildren<AttachmentPoint>(true));
            SetObjectArray(serialized.FindProperty("colliders"), GetColliders(controller));
            SetObjectArray(serialized.FindProperty("navMeshObstacles"),
                controller.GetComponentsInChildren<NavMeshObstacle>(true));
            serialized.FindProperty("screenVideoController").objectReferenceValue =
                controller.GetComponent<ScreenVideoController>();

            SetGroundIndicators(serialized.FindProperty("groundIndicators"), GetGroundIndicators(transform));

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        }

        private static IEnumerable<Renderer> GetRenderers(ItemController controller)
        {
            var serialized = new SerializedObject(controller);
            var checkChildren = serialized.FindProperty("checkRenderersInChildren")?.boolValue ?? false;
            var depth = serialized.FindProperty("checkRenderersDepth")?.intValue ?? -1;

            if (checkChildren)
            {
                var renderers = depth < 1
                    ? controller.GetComponentsInChildren<Renderer>(false)
                    : GetComponentsInChildrenWithDepth<Renderer>(controller.transform, depth).ToArray();

                return renderers.Where(IsItemRenderer);
            }

            return controller.TryGetComponent<Renderer>(out var renderer)
                ? new[] { renderer }
                : Array.Empty<Renderer>();
        }

        private static bool IsItemRenderer(Renderer renderer)
        {
            return renderer.gameObject.CompareTag("Untagged")
                   && (renderer.transform.parent == null
                       || !renderer.transform.parent.gameObject.CompareTag("DirectionIndicator"));
        }

        private static IEnumerable<Transform> GetNavMeshTargets(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.CompareTag("NavMeshTarget"));
        }

        private static IEnumerable<Collider> GetColliders(ItemController controller)
        {
            return controller.GetComponentsInChildren<Collider>(true)
                .Where(c => !c.CompareTag("GroundIndicator") && !c.CompareTag("AttachmentPointIndicator"));
        }

        private static IEnumerable<GroundIndicator> GetGroundIndicators(Transform root)
        {
            foreach (Transform child in root)
            {
                if (!child.CompareTag("GroundIndicator"))
                    continue;

                child.TryGetComponent<Renderer>(out var renderer);
                yield return new GroundIndicator
                {
                    transform = child,
                    renderer = renderer
                };
            }
        }

        private static IEnumerable<T> GetComponentsInChildrenWithDepth<T>(Transform root, int maxDepth)
            where T : Component
        {
            var results = new List<T>();
            AddChildren(root, 0);
            return results;

            void AddChildren(Transform parent, int currentDepth)
            {
                if (currentDepth > maxDepth)
                    return;

                if (parent.TryGetComponent<T>(out var component))
                    results.Add(component);

                foreach (Transform child in parent)
                    AddChildren(child, currentDepth + 1);
            }
        }

        private static void SetObjectArray(SerializedProperty property, IEnumerable<Object> values)
        {
            var objects = values.Where(value => value != null).ToArray();
            property.arraySize = objects.Length;
            for (var i = 0; i < objects.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
        }

        private static void SetGroundIndicators(SerializedProperty property, IEnumerable<GroundIndicator> values)
        {
            var indicators = values.ToArray();
            property.arraySize = indicators.Length;
            for (var i = 0; i < indicators.Length; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("transform").objectReferenceValue = indicators[i].transform;
                element.FindPropertyRelative("renderer").objectReferenceValue = indicators[i].renderer;
            }
        }
    }
}