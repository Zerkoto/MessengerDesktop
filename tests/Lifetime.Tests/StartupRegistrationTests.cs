using Xunit;

namespace MessengerDesktop.Tests;

public class StartupRegistrationTests
{
    private const string Exe = @"C:\Program Files\Messenger\Messenger.exe";

    [Fact]
    public void Build_quotes_the_exe_and_appends_minimized()
    {
        Assert.Equal("MessengerDesktop", StartupCommand.ValueName);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartupCommand.RunKeyPath);

        string command = StartupCommand.Build(Exe);

        Assert.Equal("\"C:\\Program Files\\Messenger\\Messenger.exe\" --minimized", command);
        Assert.True(StartupCommand.Matches(command, Exe));
    }

    [Fact]
    public void Build_rejects_a_path_that_contains_a_quote()
    {
        Assert.Throws<ArgumentException>(() => StartupCommand.Build("C:\\bad\"path\\Messenger.exe"));
    }

    [Fact]
    public void Compare_ignores_case_and_rejects_a_different_path()
    {
        string command = StartupCommand.Build(@"c:\program files\messenger\messenger.exe");

        Assert.True(StartupCommand.Matches(command, Exe));
        Assert.False(StartupCommand.Matches(command, @"D:\Messenger.exe"));
        Assert.False(StartupCommand.Matches(null, Exe));
        Assert.False(StartupCommand.NeedsPathHeal(command, Exe));
    }

    [Fact]
    public void A_moved_exe_needs_a_path_heal_and_a_missing_value_does_not()
    {
        string oldCommand = StartupCommand.Build(@"D:\Old\Messenger.exe");

        Assert.True(StartupCommand.NeedsPathHeal(oldCommand, Exe));
        Assert.False(StartupCommand.NeedsPathHeal(null, Exe));
        Assert.False(StartupCommand.NeedsPathHeal("", Exe));
        Assert.False(StartupCommand.NeedsPathHeal("notepad.exe", Exe));
        Assert.False(StartupCommand.NeedsPathHeal(StartupCommand.Build(Exe), Exe));
    }

    [Fact]
    public void Enable_writes_the_built_value_and_disable_removes_it()
    {
        var key = new MemoryRunKey();
        var registration = new StartupRegistration(key);

        registration.Enable(Exe);

        Assert.Equal(StartupCommand.ValueName, key.Name);
        Assert.Equal(StartupCommand.Build(Exe), key.Value);
        Assert.True(registration.IsEnabled());

        registration.Disable();

        Assert.Null(key.Value);
        Assert.Equal(1, key.Deletes);
        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void IsEnabled_is_true_for_an_old_path_and_heal_rewrites_it()
    {
        var key = new MemoryRunKey { Value = StartupCommand.Build(@"D:\Old\Messenger.exe") };
        var registration = new StartupRegistration(key);

        Assert.True(registration.IsEnabled());
        Assert.True(registration.TryHeal(Exe));
        Assert.Equal(StartupCommand.Build(Exe), key.Value);
        Assert.False(registration.TryHeal(Exe));
    }

    [Fact]
    public void Heal_does_not_create_a_value_that_was_removed()
    {
        var key = new MemoryRunKey();
        var registration = new StartupRegistration(key);

        Assert.False(registration.TryHeal(Exe));
        Assert.Null(key.Value);
        Assert.Equal(0, key.Writes);
    }

    [Fact]
    public void First_run_enables_startup_when_the_value_is_absent()
    {
        var key = new MemoryRunKey();
        var registration = new StartupRegistration(key);

        registration.ApplyOnLaunch(Exe, firstRun: true);

        Assert.Equal(StartupCommand.Build(Exe), key.Value);
    }

    [Fact]
    public void First_run_does_not_replace_an_existing_value_except_to_heal_the_path()
    {
        var kept = new MemoryRunKey { Value = StartupCommand.Build(Exe) };
        new StartupRegistration(kept).ApplyOnLaunch(Exe, firstRun: true);
        Assert.Equal(StartupCommand.Build(Exe), kept.Value);
        Assert.Equal(0, kept.Writes);

        var moved = new MemoryRunKey { Value = StartupCommand.Build(@"D:\Old\Messenger.exe") };
        new StartupRegistration(moved).ApplyOnLaunch(Exe, firstRun: true);
        Assert.Equal(StartupCommand.Build(Exe), moved.Value);
    }

    [Fact]
    public void A_later_launch_heals_a_moved_path_and_does_not_turn_startup_back_on()
    {
        var moved = new MemoryRunKey { Value = StartupCommand.Build(@"D:\Old\Messenger.exe") };
        new StartupRegistration(moved).ApplyOnLaunch(Exe, firstRun: false);
        Assert.Equal(StartupCommand.Build(Exe), moved.Value);

        var removed = new MemoryRunKey();
        new StartupRegistration(removed).ApplyOnLaunch(Exe, firstRun: false);
        Assert.Null(removed.Value);
        Assert.Equal(0, removed.Writes);
    }

    [Fact]
    public void Missing_settings_turn_startup_on_and_save_defaults()
    {
        using var folder = new TempFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        var store = new SettingsStore(path);
        var key = new MemoryRunKey();

        AppStartup.Configure(store, new StartupRegistration(key), Exe);

        Assert.Equal(StartupCommand.Build(Exe), key.Value);
        SettingsLoadResult loaded = store.Load();
        Assert.Equal(SettingsLoadKind.Loaded, loaded.Kind);
        Assert.Equal(SettingsStore.DefaultVersion, loaded.Settings.Version);
    }

    [Fact]
    public void Corrupt_settings_do_not_reenable_startup()
    {
        using var folder = new TempFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, "{");
        var store = new SettingsStore(path);
        var key = new MemoryRunKey();

        AppStartup.Configure(store, new StartupRegistration(key), Exe);

        Assert.Null(key.Value);
        Assert.Equal(0, key.Writes);
        Assert.Equal(SettingsLoadKind.Loaded, store.Load().Kind);
        Assert.Equal("{", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void A_valid_settings_file_is_left_unchanged()
    {
        using var folder = new TempFolder();
        string path = Path.Combine(folder.Path, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Version = 3 });
        string before = File.ReadAllText(path);
        var key = new MemoryRunKey { Value = StartupCommand.Build(Exe) };

        AppStartup.Configure(store, new StartupRegistration(key), Exe);

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(0, key.Writes);
    }

    private sealed class MemoryRunKey : IRunKey
    {
        public string? Name { get; private set; }
        public string? Value { get; set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }

        public string? Read(string valueName)
        {
            Name = valueName;
            return Value;
        }

        public void Write(string valueName, string value)
        {
            Name = valueName;
            Value = value;
            Writes++;
        }

        public void Delete(string valueName)
        {
            Name = valueName;
            Value = null;
            Deletes++;
        }
    }
}
