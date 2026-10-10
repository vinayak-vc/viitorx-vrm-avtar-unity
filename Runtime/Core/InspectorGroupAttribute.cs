using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Puts a serialized field - and every serialized field after it, up to the next one carrying this
    /// attribute - into a named, collapsible section of the Inspector. It replaces <c>[Header]</c> on
    /// components with enough settings that one flat list is unusable (AppBootstrap has over a hundred).
    ///
    /// It changes only how the Inspector is DRAWN (see <c>GroupedInspectorEditor</c> in the Editor
    /// assembly). Nothing about serialization changes: field names, types and order are untouched, so
    /// scenes and prefabs keep every saved value. It is deliberately not a <c>PropertyAttribute</c>, so it
    /// cannot interfere with <c>[Range]</c>, <c>[Tooltip]</c> or <see cref="MaskedStringAttribute"/> on the
    /// same field (Unity applies only one property drawer per field).
    ///
    /// <see cref="Order"/> exists because a component split across partial class files has no reliable
    /// declaration order: the sections are sorted by it, not by which file the compiler read first.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class InspectorGroupAttribute : Attribute {
        /// <summary>The section's title.</summary>
        public string Title { get; private set; }

        /// <summary>Sections are shown in ascending order of this value.</summary>
        public int Order { get; set; }

        /// <summary>Whether the section starts open. The state a user chooses is remembered for the
        /// session, so this only decides what a fresh Inspector looks like.</summary>
        public bool ExpandedByDefault { get; set; }

        public InspectorGroupAttribute(string title) {
            Title = title;
        }
    }
}
