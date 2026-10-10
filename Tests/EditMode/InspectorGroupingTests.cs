using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VirtualMirror.App;
using VirtualMirror.Core;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The Inspector sections are only drawing, but the rule that assigns a field to a section is what keeps
    /// a setting from vanishing from the Inspector or landing under the wrong title - so it is pinned here,
    /// on small stand-in types for the rules and on the real AppBootstrap for the result. Nothing here opens
    /// an Inspector: <see cref="InspectorGrouping"/> is plain reflection.
    /// </summary>
    public sealed class InspectorGroupingTests {
        // ---------------------------------------------------------------- stand-ins for the rules

        private class Plain {
            [SerializeField] private int first;
            public int second;
            [InspectorGroup("Alpha", Order = 10, ExpandedByDefault = true)]
            [SerializeField] private int third;
            [SerializeField] private int fourth;
            [InspectorGroup("Beta", Order = 5)]
            [SerializeField] private int fifth;
        }

        private class Excluded {
            [SerializeField] private int visible;
            [HideInInspector] [SerializeField] private int hidden;
            [NonSerialized] public int notSerialized;
            private int privateWithoutAttribute;
            public static int StaticField;
            public const int ConstField = 3;
            [SerializeField] private readonly int readOnlyField = 1;
            public Action callback;
            [SerializeField] private Func<int> privateCallback;
        }

        private class Interleaved {
            [InspectorGroup("One", Order = 1)]
            [SerializeField] private int a;
            [InspectorGroup("Two", Order = 2)]
            [SerializeField] private int b;
            [InspectorGroup("One", Order = 1)]
            [SerializeField] private int c;
        }

        private class SameOrder {
            [InspectorGroup("Zulu", Order = 7)]
            [SerializeField] private int a;
            [InspectorGroup("Alpha", Order = 7)]
            [SerializeField] private int b;
        }

        private class Parent {
            [InspectorGroup("Parent", Order = 1)]
            [SerializeField] private int fromParent;
        }

        private class Child : Parent {
            [InspectorGroup("Child", Order = 2)]
            [SerializeField] private int fromChild;
        }

        private static List<string> Titles(List<InspectorFieldGroup> groups) {
            List<string> titles = new List<string>();
            for (int i = 0; i < groups.Count; i++) {
                titles.Add(groups[i].Title);
            }
            return titles;
        }

        private static InspectorFieldGroup Find(List<InspectorFieldGroup> groups, string title) {
            for (int i = 0; i < groups.Count; i++) {
                if (groups[i].Title == title) {
                    return groups[i];
                }
            }
            return null;
        }

        [Test]
        public void FieldsBeforeTheFirstAttributeFallIntoGeneralAndTheRestFollowTheirAttribute() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(Plain));
            Assert.AreEqual(new List<string> { "General", "Beta", "Alpha" }, Titles(groups),
                            "sorted by Order, with the unattributed lead-in first");
            CollectionAssert.AreEqual(new List<string> { "first", "second" }, Find(groups, "General").FieldNames);
            CollectionAssert.AreEqual(new List<string> { "third", "fourth" }, Find(groups, "Alpha").FieldNames,
                                      "a field belongs to the nearest attributed field at or before it");
            CollectionAssert.AreEqual(new List<string> { "fifth" }, Find(groups, "Beta").FieldNames);
        }

        [Test]
        public void ExpandedByDefaultIsCarriedFromTheAttribute() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(Plain));
            Assert.IsTrue(Find(groups, "Alpha").ExpandedByDefault);
            Assert.IsFalse(Find(groups, "Beta").ExpandedByDefault);
            Assert.IsTrue(Find(groups, "General").ExpandedByDefault, "the lead-in section starts open");
        }

        [Test]
        public void OnlyWhatTheInspectorWouldShowIsGrouped() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(Excluded));
            Assert.AreEqual(1, groups.Count);
            CollectionAssert.AreEqual(new List<string> { "visible" }, groups[0].FieldNames,
                                      "hidden, non-serialized, private-without-attribute, static, const, readonly and delegate fields are not shown");
        }

        [Test]
        public void SectionsWithTheSameTitleAreMergedNotDuplicated() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(Interleaved));
            Assert.AreEqual(new List<string> { "One", "Two" }, Titles(groups));
            CollectionAssert.AreEqual(new List<string> { "a", "c" }, Find(groups, "One").FieldNames);
        }

        [Test]
        public void EqualOrderKeepsDeclarationOrder() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(SameOrder));
            Assert.AreEqual(new List<string> { "Zulu", "Alpha" }, Titles(groups), "stable, not alphabetical");
        }

        [Test]
        public void InheritedFieldsComeBeforeTheDerivedOnes() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(Child));
            Assert.AreEqual(new List<string> { "Parent", "Child" }, Titles(groups));
        }

        // ---------------------------------------------------------------- the real thing

        [Test]
        public void AppBootstrapShowsItsNineSectionsInTheIntendedOrder() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(AppBootstrap));
            Assert.AreEqual(new List<string> {
                "General",
                "Tracking Source",
                "Video Test Source (F-49)",
                "Kalidokit Body — active whole-body retarget (ADR-022/023)",
                "Humanized Skeleton (F-27)",
                "Operating Volume (F-48)",
                "Pose Mapping",
                "Features",
                "Debug"
            }, Titles(groups), "the order must not depend on which partial file the compiler read first");
        }

        [Test]
        public void EveryVisibleAppBootstrapFieldIsInExactlyOneSection() {
            List<string> visible = new List<string>();
            FieldInfo[] all = typeof(AppBootstrap).GetFields(BindingFlags.Instance | BindingFlags.Public
                                                             | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < all.Length; i++) {
                if (InspectorGrouping.IsInspectorVisible(all[i])) {
                    visible.Add(all[i].Name);
                }
            }
            List<string> grouped = new List<string>();
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(AppBootstrap));
            for (int i = 0; i < groups.Count; i++) {
                Assert.IsNotEmpty(groups[i].FieldNames, groups[i].Title + " must not be an empty section");
                grouped.AddRange(groups[i].FieldNames);
            }
            CollectionAssert.AllItemsAreUnique(grouped, "a field in two sections would be drawn twice");
            CollectionAssert.AreEquivalent(visible, grouped, "no visible setting may drop out of the Inspector");
            Assert.AreEqual(visible.Count, grouped.Count);
        }

        [Test]
        public void KeySettingsLandInTheSectionsAUserWouldLookIn() {
            List<InspectorFieldGroup> groups = InspectorGrouping.Build(typeof(AppBootstrap));
            string video = "Video Test Source (F-49)";
            CollectionAssert.Contains(Find(groups, video).FieldNames, "trackFromVideo");
            CollectionAssert.Contains(Find(groups, video).FieldNames, "videoSource");
            CollectionAssert.Contains(Find(groups, video).FieldNames, "lanCameraPin");
            CollectionAssert.Contains(Find(groups, video).FieldNames, "videoMaxPeople");
            CollectionAssert.Contains(Find(groups, "Tracking Source").FieldNames, "useOakUdpTracking");
            CollectionAssert.Contains(Find(groups, "Tracking Source").FieldNames, "oakUdpPort");
            CollectionAssert.Contains(Find(groups, "General").FieldNames, "mirrorSceneName");
            CollectionAssert.Contains(Find(groups, "Operating Volume (F-48)").FieldNames, "volumeNearMetres");
            CollectionAssert.Contains(Find(groups, "Humanized Skeleton (F-27)").FieldNames, "useHumanizedSkeleton");
            CollectionAssert.Contains(Find(groups, "Debug").FieldNames, "showDebugSkeleton");
            CollectionAssert.DoesNotContain(Find(groups, "General").FieldNames, "videoSource");
        }
    }
}
