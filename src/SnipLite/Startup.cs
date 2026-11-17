using Microsoft.Win32;

namespace SnipLite;

/// <summary>HKCU Run entry for "开机启动". No task scheduler, no elevation, no service.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SnipLite";
    private const string TaskName = "SnipLite.Startup";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Name) is string value && value.Length > 0 || HasAdminTask();
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Apply(bool enabled)
    {
        TryApply(enabled, false);
    }

    public static bool TryApply(bool enabled, bool administrator)
    {
        try
        {
            if (enabled && administrator || HasAdminTask())
            {
                using var helper = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath)
                {
                    Arguments = enabled && administrator ? "--startup-admin:on" : "--startup-admin:off",
                    UseShellExecute = true, Verb = "runas"
                });
                helper?.WaitForExit();
                if (helper is null || helper.ExitCode != 0) return false;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            if (enabled && !administrator)
                key.SetValue(Name, $"\"{Application.ExecutablePath}\"", RegistryValueKind.String);
            else
                key.DeleteValue(Name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("无法修改开机启动项", ex);
            return false;
        }
    }

    private static bool HasAdminTask()
    {
        object? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic scheduler = service!; scheduler.Connect();
            dynamic task = scheduler.GetFolder("\\").GetTask(TaskName);
            return task is not null;
        }
        catch { return false; }
        finally { if (service is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service); }
    }

    public static int ConfigureAdminTask(bool enabled)
    {
        object? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic scheduler = service!; scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            if (!enabled) { folder.DeleteTask(TaskName, 0); return 0; }
            dynamic definition = scheduler.NewTask(0);
            definition.RegistrationInfo.Description = UiText.T("在当前用户登录时启动 SnipLite");
            definition.Principal.UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            definition.Principal.LogonType = 3; // InteractiveToken, no password stored.
            definition.Principal.RunLevel = 1; // HighestAvailable; registration is explicitly elevated.
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            dynamic trigger = definition.Triggers.Create(9); // Logon.
            trigger.UserId = definition.Principal.UserId;
            dynamic action = definition.Actions.Create(0); action.Path = Application.ExecutablePath;
            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
            return 0;
        }
        catch { return 1; }
        finally { if (service is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service); }
    }
}
