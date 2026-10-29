using Xunit;

namespace MessengerDesktop.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Default_path_is_the_local_app_data_settings_file()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MessengerDesktop",
            "settings.json");

        Assert.Equal(expected, SettingsStore.DefaultPath);
    }

    [Fact]
    public void Missing_file_loads_defaults_and_does_not_create_a_file()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        var store = new SettingsStore(path);

        SettingsLoadResult loaded = store.Load();

        Assert.Equal(SettingsLoadKind.Missing, loaded.Kind);
        Assert.True(loaded.IsFirstRun);
        Assert.Equal(SettingsStore.DefaultVersion, loaded.Settings.Version);
        Assert.Null(loaded.CorruptBackupPath);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_then_load_round_trips_and_leaves_no_temp_file()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        var store = new SettingsStore(path);
        var settings = new AppSettings { Version = 4 };

        store.Save(settings);

        Assert.False(File.Exists(path + SettingsStore.TempSuffix));
        string json = File.ReadAllText(path);
        Assert.Contains("\"version\": 4", json, StringComparison.Ordinal);
        SettingsLoadResult loaded = store.Load();
        Assert.Equal(SettingsLoadKind.Loaded, loaded.Kind);
        Assert.False(loaded.IsFirstRun);
        Assert.Equal(4, loaded.Settings.Version);
    }

    [Fact]
    public void Save_replaces_the_previous_document()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Version = 1 });

        store.Save(new AppSettings { Version = 7 });

        Assert.Equal(7, store.Load().Settings.Version);
        Assert.False(File.Exists(path + SettingsStore.TempSuffix));
    }

    [Fact]
    public void Empty_object_uses_the_default_version()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}\n");
        var store = new SettingsStore(path);

        SettingsLoadResult loaded = store.Load();

        Assert.Equal(SettingsLoadKind.Loaded, loaded.Kind);
        Assert.Equal(SettingsStore.DefaultVersion, loaded.Settings.Version);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_load()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var store = new SettingsStore(path);

        SettingsLoadResult loaded = store.Load();

        Assert.Equal(SettingsLoadKind.RecoveredFromCorrupt, loaded.Kind);
        Assert.False(loaded.IsFirstRun);
        Assert.Equal(SettingsStore.DefaultVersion, loaded.Settings.Version);
        Assert.NotNull(loaded.CorruptBackupPath);
        Assert.Equal("{ not json", File.ReadAllText(loaded.CorruptBackupPath));
        Assert.False(File.Exists(path));
        Assert.EndsWith(".bak", loaded.CorruptBackupPath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_corrupt_file_keeps_the_earlier_backup()
    {
        using var folder = new TempFolder();
        string path = SettingsPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new SettingsStore(path);
        File.WriteAllText(path, "first");
        string firstBackup = store.Load().CorruptBackupPath!;
        File.WriteAllText(path, "second");

        string secondBackup = store.Load().CorruptBackupPath!;

        Assert.Equal("first", File.ReadAllText(firstBackup));
        Assert.Equal("second", File.ReadAllText(secondBackup));
        Assert.NotEqual(firstBackup, secondBackup);
    }

    private static string SettingsPath(TempFolder folder) =>
        Path.Combine(folder.Path, "MessengerDesktop", "settings.json");
}
