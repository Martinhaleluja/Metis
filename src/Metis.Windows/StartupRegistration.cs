using Metis.Core.Contracts;
using Microsoft.Win32;

namespace Metis.Windows;

public sealed class StartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Metis";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windows did not allow Metis to update startup settings.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("Metis could not resolve its executable path for Windows startup.");
        }

        key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
    }
}
