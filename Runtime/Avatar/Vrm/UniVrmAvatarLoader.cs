using System;
using System.IO;
using System.Threading;

using UnityEngine;

using UniGLTF;
using UniVRM10;

using VirtualMirror.Core;

namespace VirtualMirror.Avatar.Vrm {
    /// <summary>
    /// <see cref="IAvatarLoader"/> using UniVRM (VRM 1.0). Reads bytes off the main thread,
    /// instantiates on the main thread, parents under the supplied root, and validates that the
    /// model is humanoid. Failures return a status instead of throwing.
    /// </summary>
    public sealed class UniVrmAvatarLoader : IAvatarLoader {
        private const long DefaultMaxFileBytes = 256L * 1024L * 1024L;

        private readonly ILogService logService;
        private readonly long maxFileBytes;

        public UniVrmAvatarLoader(ILogService logService, long maxFileBytes) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            this.logService = logService;
            if (maxFileBytes > 0L) {
                this.maxFileBytes = maxFileBytes;
            } else {
                this.maxFileBytes = DefaultMaxFileBytes;
            }
        }

        public async Awaitable<AvatarLoadResult> LoadAsync(AvatarLoadRequest request, Transform parent, CancellationToken cancellationToken) {
            if (request == null) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, "Load request was null.");
            }
            try {
                byte[] bytes = await ResolveBytesAsync(request, cancellationToken);
                if (bytes == null) {
                    return AvatarLoadResult.Failure(AvatarLoadStatus.FileNotFound, "No avatar bytes or readable path supplied.");
                }
                if (bytes.LongLength > maxFileBytes) {
                    return AvatarLoadResult.Failure(AvatarLoadStatus.FileTooLarge, "Avatar exceeds max size of " + maxFileBytes + " bytes.");
                }
                cancellationToken.ThrowIfCancellationRequested();

                Vrm10Instance vrmInstance = await Vrm10.LoadBytesAsync(
                    bytes,
                    canLoadVrm0X: true,
                    controlRigGenerationOption: ControlRigGenerationOption.None,
                    showMeshes: request.ShowMeshesOnLoad,
                    awaitCaller: new RuntimeOnlyAwaitCaller(),
                    ct: cancellationToken);

                if (vrmInstance == null) {
                    return AvatarLoadResult.Failure(AvatarLoadStatus.InvalidVrm, "UniVRM returned no instance.");
                }

                if (parent != null) {
                    vrmInstance.transform.SetParent(parent, false);
                }

                Animator animator = vrmInstance.GetComponent<Animator>();
                if (animator == null || !animator.isHuman) {
                    logService.Log(LogLevel.Warning, "Loaded VRM is not humanoid: " + DescribeRequest(request));
                    VrmAvatarInstance invalidInstance = new VrmAvatarInstance(vrmInstance, animator);
                    invalidInstance.Dispose();
                    return AvatarLoadResult.Failure(AvatarLoadStatus.NotHumanoid, "Loaded VRM has no humanoid avatar.");
                }

                logService.Log(LogLevel.Info, "Avatar loaded: " + DescribeRequest(request));
                VrmAvatarInstance instance = new VrmAvatarInstance(vrmInstance, animator);
                return AvatarLoadResult.Success(instance);
            } catch (OperationCanceledException) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Cancelled, "Avatar load cancelled.");
            } catch (Exception exception) {
                logService.LogException(exception, "Avatar load failed");
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, exception.Message);
            }
        }

        private async Awaitable<byte[]> ResolveBytesAsync(AvatarLoadRequest request, CancellationToken cancellationToken) {
            if (request.SourceBytes != null) {
                return request.SourceBytes;
            }
            string path = request.SourcePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                return null;
            }
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return bytes;
        }

        private static string DescribeRequest(AvatarLoadRequest request) {
            if (!string.IsNullOrEmpty(request.DisplayName)) {
                return request.DisplayName;
            }
            if (!string.IsNullOrEmpty(request.SourcePath)) {
                return Path.GetFileName(request.SourcePath);
            }
            return "(in-memory avatar)";
        }
    }
}
