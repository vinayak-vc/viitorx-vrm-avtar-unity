using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Editor {
    /// <summary>
    /// Draws a component's serialized fields as collapsible sections, as declared by
    /// <see cref="InspectorGroupAttribute"/>, instead of one flat list. The fields themselves are drawn
    /// with <c>PropertyField</c>, so tooltips, ranges and the masked PIN drawer all behave exactly as
    /// before; only the layout changes, and nothing is serialized differently.
    ///
    /// Which sections are open is remembered for the editor session (<see cref="SessionState"/>), per
    /// component type, so it survives selecting something else and coming back. A visible field that no
    /// section claims is drawn under "Other" rather than silently dropped from the Inspector.
    /// </summary>
    public abstract class GroupedInspectorEditor : UnityEditor.Editor {
        private const string OtherTitle = "Other";

        private List<InspectorFieldGroup> groups;

        public override void OnInspectorGUI() {
            if (groups == null) {
                groups = InspectorGrouping.Build(target.GetType());
            }
            serializedObject.Update();

            DrawScriptField();
            DrawExpandCollapseButtons();

            HashSet<string> claimed = new HashSet<string>();
            for (int i = 0; i < groups.Count; i++) {
                DrawGroup(groups[i], claimed);
            }
            DrawUnclaimed(claimed);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField() {
            MonoBehaviour behaviour = target as MonoBehaviour;
            if (behaviour == null) {
                return;
            }
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(behaviour), typeof(MonoScript), false);
            }
        }

        private void DrawExpandCollapseButtons() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Expand all", EditorStyles.miniButtonLeft, GUILayout.Width(80f))) {
                SetAllExpanded(true);
            }
            if (GUILayout.Button("Collapse all", EditorStyles.miniButtonRight, GUILayout.Width(80f))) {
                SetAllExpanded(false);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllExpanded(bool expanded) {
            for (int i = 0; i < groups.Count; i++) {
                SessionState.SetBool(StateKey(groups[i].Title), expanded);
            }
            SessionState.SetBool(StateKey(OtherTitle), expanded);
        }

        private void DrawGroup(InspectorFieldGroup group, HashSet<string> claimed) {
            List<SerializedProperty> properties = new List<SerializedProperty>();
            for (int i = 0; i < group.FieldNames.Count; i++) {
                SerializedProperty property = serializedObject.FindProperty(group.FieldNames[i]);
                if (property != null) {
                    properties.Add(property);
                    claimed.Add(group.FieldNames[i]);
                }
            }
            if (properties.Count == 0) {
                return;
            }
            DrawSection(group.Title, group.ExpandedByDefault, properties);
        }

        private void DrawUnclaimed(HashSet<string> claimed) {
            List<SerializedProperty> leftovers = new List<SerializedProperty>();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren)) {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" || claimed.Contains(iterator.propertyPath)) {
                    continue;
                }
                leftovers.Add(serializedObject.FindProperty(iterator.propertyPath));
            }
            if (leftovers.Count > 0) {
                DrawSection(OtherTitle, true, leftovers);
            }
        }

        private void DrawSection(string title, bool expandedByDefault, List<SerializedProperty> properties) {
            string key = StateKey(title);
            bool expanded = SessionState.GetBool(key, expandedByDefault);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool now = EditorGUILayout.Foldout(expanded, title + "   (" + properties.Count + ")", true, EditorStyles.foldoutHeader);
            if (now != expanded) {
                SessionState.SetBool(key, now);
            }
            if (now) {
                EditorGUI.indentLevel++;
                for (int i = 0; i < properties.Count; i++) {
                    EditorGUILayout.PropertyField(properties[i], true);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private string StateKey(string title) {
            return "VirtualMirror.GroupedInspector." + target.GetType().FullName + "." + title;
        }
    }
}
