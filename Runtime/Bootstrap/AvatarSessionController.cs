using System;
using System.IO;
using System.Threading;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Settings;

namespace VirtualMirror.App {
    /// <summary>
    /// Owns the currently displayed avatar under the avatar root. Hot-swaps avatars without a
    /// restart: the new avatar is loaded first, then the previous instance is disposed, so there
    /// is no empty frame. On a failed load the previous avatar is kept. Persists the last loaded
    /// path through <see cref="SettingsStore"/>.
    /// </summary>
    public sealed class AvatarSessionController : IDisposable, IAvatarSession {
        private readonly IAvatarLoader loader;
        private readonly ILogService logService;
        private readonly SettingsStore settingsStore;
        private readonly Transform avatarRoot;

        private IAvatarInstance current;
        private CancellationTokenSource loadCts;
        private bool disposed;

        public event Action AvatarChanged;

        public AvatarSessionController(IAvatarLoader loader, ILogService logService, SettingsStore settingsStore, Transform avatarRoot) {
            if (loader == null) {
                throw new ArgumentNullException(nameof(loader));
            }
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (settingsStore == null) {
                throw new ArgumentNullException(nameof(settingsStore));
            }
            if (avatarRoot == null) {
                throw new ArgumentNullException(nameof(avatarRoot));
            }
            this.loader = loader;
            this.logService = logService;
            this.settingsStore = settingsStore;
            this.avatarRoot = avatarRoot;
        }

        public IAvatarInstance Current {
            get {
                return current;
            }
        }

        public async Awaitable<AvatarLoadResult> LoadFromPathAsync(string path, bool showMeshes) {
            if (string.IsNullOrEmpty(path)) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.FileNotFound, "Empty avatar path.");
            }
            AvatarLoadRequest request = new AvatarLoadRequest(path, Path.GetFileName(path), showMeshes);
            AvatarLoadResult result = await LoadAsync(request);
            return result;
        }

        public async Awaitable<AvatarLoadResult> LoadAsync(AvatarLoadRequest request) {
            if (disposed) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, "Avatar session is disposed.");
            }
            if (request == null) {
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, "Load request was null.");
            }
            CancelInFlight();
            loadCts = new CancellationTokenSource();
            CancellationToken token = loadCts.Token;

            AvatarLoadResult result = await loader.LoadAsync(request, avatarRoot, token);
            // M8: a newer LoadAsync may have superseded this one while we awaited. The loader stops
            // observing the token after instantiation, so guard here: drop the stale instance instead of
            // disposing the newer `current` and installing the wrong avatar.
            if (token.IsCancellationRequested) {
                if (result.IsSuccess && result.Instance != null) {
                    result.Instance.Dispose();
                }
                return AvatarLoadResult.Failure(AvatarLoadStatus.Error, "Avatar load superseded by a newer request.");
            }
            if (!result.IsSuccess) {
                logService.Log(LogLevel.Warning, "Avatar load failed (" + result.Status + "): " + result.Message);
                return result;
            }

            if (current != null) {
                current.Dispose();
                current = null;
            }
            current = result.Instance;
            if (current != null && current.Root != null) {
                current.Root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            PersistLastPath(request);
            if (AvatarChanged != null) {
                AvatarChanged.Invoke();
            }
            return result;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            CancelInFlight();
            if (current != null) {
                current.Dispose();
                current = null;
            }
        }

        private void CancelInFlight() {
            if (loadCts == null) {
                return;
            }
            loadCts.Cancel();
            loadCts.Dispose();
            loadCts = null;
        }

        private void PersistLastPath(AvatarLoadRequest request) {
            if (string.IsNullOrEmpty(request.SourcePath)) {
                return;
            }
            AppSettings settings = settingsStore.Current;
            if (settings == null || settings.Avatar == null) {
                return;
            }
            settings.Avatar.LastPath = request.SourcePath;
            settingsStore.RequestSave();
        }
    }
}
