namespace MessengerDesktop;

internal interface IRunKey
{
    string? Read(string valueName);

    void Write(string valueName, string value);

    void Delete(string valueName);
}

internal static class StartupCommand
{
    public const string ValueName = "MessengerDesktop";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Argument = "--minimized";

    public static string Build(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        if (exePath.Contains('"'))
            throw new ArgumentException("The executable path cannot contain a quote.", nameof(exePath));
        return $"\"{exePath}\" {Argument}";
    }

    public static bool Matches(string? actual, string exePath) =>
        string.Equals(actual, Build(exePath), StringComparison.OrdinalIgnoreCase);

    public static bool TryReadExecutable(string? actual, out string exePath)
    {
        exePath = "";
        if (string.IsNullOrWhiteSpace(actual)) return false;
        string suffix = " " + Argument;
        if (!actual.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        string quoted = actual[..^suffix.Length];
        if (quoted.Length < 3 || quoted[0] != '"' || quoted[^1] != '"') return false;
        exePath = quoted[1..^1];
        return exePath.Length > 0 && !exePath.Contains('"');
    }

    public static bool NeedsPathHeal(string? actual, string exePath)
    {
        if (!TryReadExecutable(actual, out string stored)) return false;
        return !string.Equals(stored, exePath, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class StartupRegistration
{
    private readonly IRunKey runKey;

    public StartupRegistration(IRunKey runKey)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        this.runKey = runKey;
    }

    public bool IsEnabled() =>
        StartupCommand.TryReadExecutable(runKey.Read(StartupCommand.ValueName), out _);

    public void Enable(string exePath) =>
        runKey.Write(StartupCommand.ValueName, StartupCommand.Build(exePath));

    public void Disable() => runKey.Delete(StartupCommand.ValueName);

    public bool TryHeal(string exePath)
    {
        string? actual = runKey.Read(StartupCommand.ValueName);
        if (!StartupCommand.NeedsPathHeal(actual, exePath)) return false;
        Enable(exePath);
        return true;
    }

    public void ApplyOnLaunch(string exePath, bool firstRun)
    {
        string? actual = runKey.Read(StartupCommand.ValueName);
        if (firstRun && string.IsNullOrEmpty(actual))
        {
            Enable(exePath);
            return;
        }

        TryHeal(exePath);
    }
}
