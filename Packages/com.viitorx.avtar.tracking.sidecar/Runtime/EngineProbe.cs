using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>What <c>viitorx-tracking.exe version --json</c> answered, or why it did not.</summary>
    public sealed class EngineProbeResult {
        /// <summary>True when the engine started, exited 0 and answered with JSON.</summary>
        public bool Ran;

        /// <summary>When <see cref="Ran"/> is false, why (it could not be started, did not answer in time, exited with an error, answered with something else).</summary>
        public string Problem = string.Empty;

        public string EngineVersion = string.Empty;
        public string WireSpec = string.Empty;
    }

    /// <summary>Starts an engine once, with <c>version --json</c>, and reads what it says about itself. It runs nothing else.</summary>
    public static class EngineProbe {
        public const int DefaultTimeoutSeconds = 60;

        public static EngineProbeResult Run(string exe, int timeoutSeconds) {
            EngineProbeResult result = new EngineProbeResult();
            ProcessStartInfo info = new ProcessStartInfo(exe, "version --json");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.WorkingDirectory = Path.GetDirectoryName(exe);
            Process started;
            try {
                started = Process.Start(info);
            } catch (System.ComponentModel.Win32Exception exception) {
                result.Problem = "The engine could not be started: " + exception.Message;
                return result;
            }
            using (Process process = started) {
                if (process == null) {
                    result.Problem = "The engine could not be started.";
                    return result;
                }
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutSeconds * 1000)) {
                    try {
                        process.Kill();
                    } catch (InvalidOperationException) {
                        // it ended between the timeout and the kill
                    }
                    result.Problem = "The engine did not answer `version --json` within " + timeoutSeconds + " s.";
                    return result;
                }
                if (process.ExitCode != 0) {
                    result.Problem = "The engine exited " + process.ExitCode + " on `version --json`: " + Clip(errors.Result);
                    return result;
                }
                try {
                    JObject answer = JObject.Parse(output.Result);
                    result.EngineVersion = answer["engine"] != null ? answer["engine"].Value<string>() : string.Empty;
                    result.WireSpec = answer["wire_spec"] != null ? answer["wire_spec"].Value<string>() : string.Empty;
                    result.Ran = true;
                } catch (Exception exception) {
                    result.Problem = "The engine's answer to `version --json` is not what an engine gives: " + exception.Message;
                }
            }
            return result;
        }

        private static string Clip(string text) {
            string trimmed = (text ?? string.Empty).Trim();
            return trimmed.Length <= 300 ? trimmed : trimmed.Substring(0, 300) + "...";
        }
    }

    public enum EngineCompatibilityStatus {
        Compatible = 0,
        Incompatible = 1,
        Unknown = 2
    }

    public sealed class EngineCompatibilityResult {
        public EngineCompatibilityStatus Status = EngineCompatibilityStatus.Unknown;
        public string EngineVersion = string.Empty;
        public string WireSpec = string.Empty;
        public string Message = string.Empty;

        /// <summary>True only for a definite mismatch. An engine that cannot answer is not blocked here: starting it will report what is wrong with it.</summary>
        public bool Blocks {
            get {
                return Status == EngineCompatibilityStatus.Incompatible;
            }
        }
    }

    /// <summary>
    /// Does the engine speak the wire these packages read? The engine and the packages are released separately; what ties them is the wire specification (docs/WIRE.md),
    /// whose MAJOR number changes when a message changes incompatibly. An engine that answers a major number this package family does not read is refused with a message,
    /// instead of starting and sending datagrams nothing can parse. The answer is remembered per engine file (path, size, last write), so the engine is asked once.
    /// </summary>
    public static class EngineCompatibility {
        private static readonly Dictionary<string, EngineCompatibilityResult> Cache = new Dictionary<string, EngineCompatibilityResult>();
        private static readonly object CacheGate = new object();

        /// <summary>The wire specification major number this package family reads. Settable only so a test can pretend to be a different generation.</summary>
        public static int SupportedWireSpecMajor = 1;

        /// <summary>True for "1.0", "1.7", "1": the major number is the one this family reads. "2.0", "", "x" are not.</summary>
        public static bool IsWireSpecSupported(string spec, out string reason) {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(spec)) {
                reason = "the engine does not say which wire specification it speaks";
                return false;
            }
            string major = spec.Trim().Split('.')[0];
            int value;
            if (!int.TryParse(major, out value)) {
                reason = "the engine's wire specification '" + spec + "' is not a version number";
                return false;
            }
            if (value != SupportedWireSpecMajor) {
                reason = "the engine speaks wire specification " + spec + "; these packages read " + SupportedWireSpecMajor + ".x";
                return false;
            }
            return true;
        }

        public static EngineCompatibilityResult Check(string exe) {
            string key;
            try {
                FileInfo file = new FileInfo(exe);
                if (!file.Exists) {
                    return new EngineCompatibilityResult { Message = "The engine file '" + exe + "' does not exist." };
                }
                key = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks + "|" + SupportedWireSpecMajor;
            } catch (Exception exception) {
                return new EngineCompatibilityResult { Message = "The engine file could not be read: " + exception.Message };
            }
            lock (CacheGate) {
                EngineCompatibilityResult cached;
                if (Cache.TryGetValue(key, out cached)) {
                    return cached;
                }
            }
            EngineProbeResult probe = EngineProbe.Run(exe, 20);
            EngineCompatibilityResult result = new EngineCompatibilityResult();
            if (!probe.Ran) {
                result.Status = EngineCompatibilityStatus.Unknown;
                result.Message = "Could not ask the engine which wire specification it speaks (" + probe.Problem + ")";
                return result;   // not remembered: a transient failure should be asked again
            }
            result.EngineVersion = probe.EngineVersion;
            result.WireSpec = probe.WireSpec;
            string reason;
            if (IsWireSpecSupported(probe.WireSpec, out reason)) {
                result.Status = EngineCompatibilityStatus.Compatible;
                result.Message = "Engine " + probe.EngineVersion + " speaks wire specification " + probe.WireSpec + ".";
            } else {
                result.Status = EngineCompatibilityStatus.Incompatible;
                result.Message = "Engine " + probe.EngineVersion + " is not compatible with these packages: " + reason + ". Install an engine that matches (see docs/INSTALL.md).";
            }
            lock (CacheGate) {
                Cache[key] = result;
            }
            return result;
        }
    }
}
