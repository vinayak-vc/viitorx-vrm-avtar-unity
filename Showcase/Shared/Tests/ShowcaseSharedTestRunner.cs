using System;
using System.IO;
using System.Reflection;
using System.Text;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;

namespace ShowcaseShared.Tests {
    /// <summary>
    /// Runs this assembly's [Test] methods in-process and writes a plain-text report, like the runners of the showcases that
    /// use the shared code (the project's Editor is usually open, so batch mode cannot take the project lock). The Unity Test
    /// Runner (Window > General > Test Runner > EditMode) also runs these.
    /// </summary>
    public static class ShowcaseSharedTestRunner {
        public const string ReportPath = "Temp/ShowcaseSharedTestResults.txt";

        [MenuItem("Showcase/Shared/Run Self Tests")]
        public static void RunFromMenu() {
            string summary = Run();
            Debug.Log("[ShowcaseShared] " + summary);
        }

        public static string Run() {
            StringBuilder report = new StringBuilder();
            int passed = 0;
            int failed = 0;
            Type[] types = typeof(ShowcaseSharedTestRunner).Assembly.GetTypes();
            for (int t = 0; t < types.Length; t++) {
                MethodInfo[] methods = types[t].GetMethods(BindingFlags.Public | BindingFlags.Instance);
                object instance = null;
                for (int m = 0; m < methods.Length; m++) {
                    if (methods[m].GetCustomAttribute<TestAttribute>() == null) {
                        continue;
                    }
                    if (instance == null) {
                        instance = Activator.CreateInstance(types[t]);
                    }
                    try {
                        methods[m].Invoke(instance, null);
                        passed++;
                        report.AppendLine("PASS  " + types[t].Name + "." + methods[m].Name);
                    } catch (TargetInvocationException ex) {
                        failed++;
                        report.AppendLine("FAIL  " + types[t].Name + "." + methods[m].Name + "  ::  "
                                          + (ex.InnerException != null ? ex.InnerException.Message.Replace("\n", " ") : ex.Message));
                    }
                }
            }
            string summary = "self tests: " + passed + " passed, " + failed + " failed";
            report.AppendLine(summary);
            File.WriteAllText(ReportPath, report.ToString());
            return summary;
        }
    }
}
