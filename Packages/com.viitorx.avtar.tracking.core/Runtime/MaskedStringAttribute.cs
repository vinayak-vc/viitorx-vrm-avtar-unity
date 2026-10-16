using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// F-50 — marks a string field as a SECRET, so the Inspector shows it as dots instead of text.
    /// Used for the LAN camera's access PIN. It changes only how the field is DRAWN (see
    /// <c>MaskedStringDrawer</c> in the Editor assembly): the value is still serialised into the scene or
    /// prefab as plain text, so a PIN worth protecting belongs in the CAMERA_PIN environment variable
    /// instead, with this field left empty.
    /// </summary>
    public sealed class MaskedStringAttribute : PropertyAttribute {
    }
}
