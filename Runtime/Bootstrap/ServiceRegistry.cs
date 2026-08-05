using System;

using VirtualMirror.Core;
using VirtualMirror.Settings;

namespace VirtualMirror.App {
    /// <summary>
    /// Immutable holder for the core services constructed by <see cref="AppBootstrap"/>.
    /// Feature systems resolve their dependencies from here rather than constructing them.
    /// </summary>
    public sealed class ServiceRegistry {
        private readonly IPathProvider pathProvider;
        private readonly ILogService logService;
        private readonly SettingsStore settingsStore;
        private readonly IAvatarLoader avatarLoader;

        public ServiceRegistry(IPathProvider pathProvider, ILogService logService, SettingsStore settingsStore, IAvatarLoader avatarLoader) {
            if (pathProvider == null) {
                throw new ArgumentNullException(nameof(pathProvider));
            }
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (settingsStore == null) {
                throw new ArgumentNullException(nameof(settingsStore));
            }
            if (avatarLoader == null) {
                throw new ArgumentNullException(nameof(avatarLoader));
            }
            this.pathProvider = pathProvider;
            this.logService = logService;
            this.settingsStore = settingsStore;
            this.avatarLoader = avatarLoader;
        }

        public IPathProvider PathProvider {
            get {
                return pathProvider;
            }
        }

        public ILogService LogService {
            get {
                return logService;
            }
        }

        public SettingsStore SettingsStore {
            get {
                return settingsStore;
            }
        }

        public IAvatarLoader AvatarLoader {
            get {
                return avatarLoader;
            }
        }
    }
}
