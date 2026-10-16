using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Core;

namespace VirtualMirror.Tests {
    /// <summary>What the Play-mode body tests share: the small VRM they drive, a recording log, a free UDP port and a coroutine that loads the avatar.</summary>
    internal static class BodyTestSupport {
        internal sealed class RecordingLog : ILogService {
            public readonly List<string> Lines = new List<string>();

            public void Log(LogLevel level, string message) {
                lock (Lines) {
                    Lines.Add(level + ": " + message);
                }
            }

            public void LogException(Exception exception, string context) {
                lock (Lines) {
                    Lines.Add("EXCEPTION: " + context + ": " + exception.Message);
                }
            }

            public void Flush() {
            }

            public string All() {
                lock (Lines) {
                    return string.Join("\n", Lines.ToArray());
                }
            }
        }

        internal sealed class LoadedAvatar {
            public IAvatarInstance Instance;
        }

        /// <summary>The rig template VRM (a humanoid with no meshes to speak of, 180 KB), a .vrm renamed .bytes, next to this file.</summary>
        internal static string AvatarPath([CallerFilePath] string ownPath = "") {
            // the compiler may hand over a path relative to the project folder (a file: package), which is not what a bootstrap treats as a file path
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ownPath), "Data", "VirtualMirrorRigTemplate.vrm.bytes"));
        }

        internal static int FreePort() {
            UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            probe.Close();
            return port;
        }

        /// <summary>Loads the test avatar under <paramref name="parent"/>, with or without the VRM control rig, and hands it back through <paramref name="loaded"/>.</summary>
        internal static IEnumerator LoadAvatar(ILogService log, bool controlRig, Transform parent, LoadedAvatar loaded) {
            UniVrmAvatarLoader loader = new UniVrmAvatarLoader(log, 0L);
            loader.GenerateControlRig = controlRig;
            AvatarLoadRequest request = new AvatarLoadRequest(File.ReadAllBytes(AvatarPath()), "rig-template", true);
            Awaitable<AvatarLoadResult> pending = loader.LoadAsync(request, parent, CancellationToken.None);
            while (!pending.GetAwaiter().IsCompleted) {
                yield return null;
            }
            AvatarLoadResult result = pending.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, "the test avatar loads: " + result.Message);
            loaded.Instance = result.Instance;
        }

        /// <summary>The angle in degrees between two rotations.</summary>
        internal static float Degrees(Quaternion a, Quaternion b) {
            return Quaternion.Angle(a, b);
        }
    }
}
