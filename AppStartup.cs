namespace MessengerDesktop;

internal static class AppStartup
{
    // A missing settings file is the first run: register the startup command.
    // A corrupt file is replaced with defaults and does not turn startup back on.
    public static void Configure(SettingsStore store, StartupRegistration registration, string exePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        SettingsLoadResult loaded = store.Load();
        registration.ApplyOnLaunch(exePath, loaded.IsFirstRun);
        if (loaded.Kind != SettingsLoadKind.Loaded)
            store.Save(loaded.Settings);
    }
}
