using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MessengerDesktop;

internal sealed class AppSettings
{
    public int Version { get; set; } = SettingsStore.DefaultVersion;

    // Off until the tray menu turns it on. The chat-list script is not injected while this is false.
    public bool WatchChatListForReactions { get; set; }
}

internal enum SettingsLoadKind
{
    Loaded,
    Missing,
    RecoveredFromCorrupt
}

internal sealed class SettingsLoadResult
{
    public SettingsLoadResult(AppSettings settings, SettingsLoadKind kind, string? corruptBackupPath)
    {
        Settings = settings;
        Kind = kind;
        CorruptBackupPath = corruptBackupPath;
    }

    public AppSettings Settings { get; }

    public SettingsLoadKind Kind { get; }

    public string? CorruptBackupPath { get; }

    public bool IsFirstRun => Kind == SettingsLoadKind.Missing;
}

// One typed JSON document at %LOCALAPPDATA%\MessengerDesktop\settings.json.
// Writes go to a temp file in the same directory and then replace the real file.
// A file that is not JSON is moved aside and the caller gets defaults.
internal sealed class SettingsStore
{
    internal const int DefaultVersion = 1;
    internal const string TempSuffix = ".tmp";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string path;
    private readonly string directory;

    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("The settings path needs a directory.", nameof(path));
        this.path = path;
        this.directory = directory;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MessengerDesktop",
        "settings.json");

    public SettingsLoadResult Load()
    {
        if (!File.Exists(path))
            return new SettingsLoadResult(CreateDefault(), SettingsLoadKind.Missing, null);

        string text = File.ReadAllText(path, Utf8);
        if (string.IsNullOrWhiteSpace(text))
            return Recover();

        try
        {
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(text, JsonOptions);
            if (settings is null)
                return Recover();
            return new SettingsLoadResult(settings, SettingsLoadKind.Loaded, null);
        }
        catch (JsonException)
        {
            return Recover();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(settings, JsonOptions) + "\n";
        string temp = path + TempSuffix;
        File.WriteAllText(temp, json, Utf8);
        try
        {
            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null);
            else
                File.Move(temp, path);
        }
        catch
        {
            if (File.Exists(temp))
                File.Delete(temp);
            throw;
        }

        if (File.Exists(temp))
            File.Delete(temp);
    }

    private SettingsLoadResult Recover()
    {
        string backup = path + ".bak";
        int n = 0;
        while (File.Exists(backup))
        {
            n++;
            backup = string.Format(CultureInfo.InvariantCulture, "{0}.bak.{1}", path, n);
        }

        File.Move(path, backup);
        return new SettingsLoadResult(CreateDefault(), SettingsLoadKind.RecoveredFromCorrupt, backup);
    }

    private static AppSettings CreateDefault() => new() { Version = DefaultVersion };
}
