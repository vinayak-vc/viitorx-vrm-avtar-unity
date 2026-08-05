namespace VirtualMirror.Settings {
    /// <summary>
    /// Schema version constants and support checks for <see cref="AppSettings"/> migrations.
    /// </summary>
    public static class SettingsSchema {
        public const int CurrentVersion = 1;

        public static bool IsSupported(int schemaVersion) {
            return schemaVersion >= 1 && schemaVersion <= CurrentVersion;
        }
    }
}
