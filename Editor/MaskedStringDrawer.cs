using UnityEditor;
using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Editor {
    /// <summary>
    /// Draws a <see cref="MaskedStringAttribute"/> field as a password box (dots, no text), so a PIN is not
    /// readable over a shoulder or in a screen recording of the Inspector. Drawing only: see the attribute
    /// for what this does NOT protect.
    /// </summary>
    [CustomPropertyDrawer(typeof(MaskedStringAttribute))]
    public sealed class MaskedStringDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            if (property.propertyType != SerializedPropertyType.String) {
                EditorGUI.LabelField(position, label.text, "[MaskedString] needs a string field.");
                return;
            }
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            string value = EditorGUI.PasswordField(position, label, property.stringValue);
            if (EditorGUI.EndChangeCheck()) {
                property.stringValue = value;
            }
            EditorGUI.EndProperty();
        }
    }
}
