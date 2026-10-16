using System;
using System.Collections.Generic;
using System.Reflection;

namespace VirtualMirror.Core {
    /// <summary>One Inspector section: a title and the serialized fields that belong to it.</summary>
    public sealed class InspectorFieldGroup {
        public string Title { get; private set; }
        public int Order { get; private set; }
        public bool ExpandedByDefault { get; private set; }
        public List<string> FieldNames { get; private set; }

        public InspectorFieldGroup(string title, int order, bool expandedByDefault) {
            Title = title;
            Order = order;
            ExpandedByDefault = expandedByDefault;
            FieldNames = new List<string>();
        }
    }

    /// <summary>
    /// Works out which Inspector section each visible serialized field of a component belongs to, from
    /// <see cref="InspectorGroupAttribute"/>. Pure reflection with no UnityEditor dependency, so the
    /// grouping rules can be asserted in an EditMode test without opening an Inspector.
    ///
    /// A field belongs to the section of the nearest attributed field at or before it. Sections that
    /// share a title are merged, so a component split across partial files cannot end up with two
    /// sections of the same name, and the result is sorted (stably) by <see cref="InspectorGroupAttribute.Order"/>
    /// because the order partial files are compiled in is not something to depend on.
    /// </summary>
    public static class InspectorGrouping {
        /// <summary>Title of the section for visible fields that come before any attributed field.</summary>
        public const string FallbackTitle = "General";

        public static List<InspectorFieldGroup> Build(Type type) {
            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields);

            List<InspectorFieldGroup> groups = new List<InspectorFieldGroup>();
            InspectorFieldGroup current = null;
            for (int i = 0; i < fields.Count; i++) {
                FieldInfo field = fields[i];
                if (!IsInspectorVisible(field)) {
                    continue;
                }
                InspectorGroupAttribute attribute =
                    (InspectorGroupAttribute)Attribute.GetCustomAttribute(field, typeof(InspectorGroupAttribute));
                if (attribute != null) {
                    current = FindOrAdd(groups, attribute.Title, attribute.Order, attribute.ExpandedByDefault);
                } else if (current == null) {
                    current = FindOrAdd(groups, FallbackTitle, int.MinValue, true);
                }
                current.FieldNames.Add(field.Name);
            }
            return SortedByOrder(groups);
        }

        /// <summary>The fields Unity's Inspector would show: not static or constant, not hidden, not
        /// non-serialized, and either public or marked <c>[SerializeField]</c>.</summary>
        public static bool IsInspectorVisible(FieldInfo field) {
            if (field.IsStatic || field.IsLiteral || field.IsInitOnly) {
                return false;
            }
            if (field.IsDefined(typeof(UnityEngine.HideInInspector), false) || field.IsDefined(typeof(NonSerializedAttribute), false)) {
                return false;
            }
            // Unity never serializes a delegate, public or not (AppBootstrap.HumanizeTap is one).
            if (typeof(Delegate).IsAssignableFrom(field.FieldType)) {
                return false;
            }
            return field.IsPublic || field.IsDefined(typeof(UnityEngine.SerializeField), false);
        }

        private static void CollectFields(Type type, List<FieldInfo> into) {
            if (type == null || type == typeof(object) || type == typeof(UnityEngine.MonoBehaviour)) {
                return;
            }
            CollectFields(type.BaseType, into);
            FieldInfo[] declared = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.DeclaredOnly);
            for (int i = 0; i < declared.Length; i++) {
                into.Add(declared[i]);
            }
        }

        private static InspectorFieldGroup FindOrAdd(List<InspectorFieldGroup> groups, string title, int order, bool expanded) {
            for (int i = 0; i < groups.Count; i++) {
                if (string.Equals(groups[i].Title, title, StringComparison.Ordinal)) {
                    return groups[i];
                }
            }
            InspectorFieldGroup created = new InspectorFieldGroup(title, order, expanded);
            groups.Add(created);
            return created;
        }

        private static List<InspectorFieldGroup> SortedByOrder(List<InspectorFieldGroup> groups) {
            // Insertion sort: stable (equal Order keeps declaration order) and the list holds a dozen items.
            List<InspectorFieldGroup> sorted = new List<InspectorFieldGroup>();
            for (int i = 0; i < groups.Count; i++) {
                int at = sorted.Count;
                while (at > 0 && sorted[at - 1].Order > groups[i].Order) {
                    at--;
                }
                sorted.Insert(at, groups[i]);
            }
            return sorted;
        }
    }
}
