using Microsoft.Win32;

namespace MessengerDesktop;

// HKCU Run entry. The tray check reads this key. Tests use IRunKey instead.
internal sealed class WindowsRunKey : IRunKey
{
    public string? Read(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupCommand.RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupCommand.RunKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupCommand.RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
