using System.Security.Principal;

namespace NetMaster.Core;

public static class StartupRegistration
{
    public static string Name => "NetMaster_WinUI_" + Protocol.UserKey;
    private static dynamic Service()
    {
        var service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        ((dynamic)service).Connect(); return service;
    }
    public static bool LegacyTaskRunning()
    {
        dynamic? task = FindTask("NetMaster_SHU_Auto_Login");
        return task is not null && (int)task.State == 4;
    }
    public static bool Enabled()
    {
        dynamic? task = FindTask(Name);
        return task is not null && (bool)task.Enabled;
    }
    internal static object? FindTask(string name)
    {
        dynamic folder = Service().GetFolder("\\");
        // COM's ERROR_FILE_NOT_FOUND is mapped to FileNotFoundException by .NET.
        // Limit this handling to looking up a task, not loading the COM service.
        try { return folder.GetTask(name); }
        catch (Exception ex) when (MissingTask(ex)) { return null; }
    }
    private static bool MissingTask(Exception ex) =>
        (ex is FileNotFoundException or System.Runtime.InteropServices.COMException) && (uint)ex.HResult == 0x80070002;
    public static void SetEnabled(bool enabled, string executable, string home)
    {
        dynamic service = Service(); dynamic folder = service.GetFolder("\\");
        if (!enabled) { try { folder.DeleteTask(Name, 0); } catch (Exception ex) when (MissingTask(ex)) { } return; }
        if (!File.Exists(executable)) throw new FileNotFoundException();
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "NetMaster 校园网后台守护（当前用户登录后启动）";
        definition.Principal.UserId = WindowsIdentity.GetCurrent().User!.Value;
        definition.Principal.LogonType = 3; definition.Principal.RunLevel = 0;
        definition.Settings.MultipleInstances = 2; definition.Settings.DisallowStartIfOnBatteries = false; definition.Settings.StopIfGoingOnBatteries = false; definition.Settings.ExecutionTimeLimit = "PT0S";
        dynamic trigger = definition.Triggers.Create(9); trigger.UserId = WindowsIdentity.GetCurrent().User!.Value;
        dynamic action = definition.Actions.Create(0); action.Path = executable; action.Arguments = "--home \"" + home + "\""; action.WorkingDirectory = Path.GetDirectoryName(executable);
        folder.RegisterTaskDefinition(Name, definition, 6, null, null, 3, null);
        if (!Enabled()) throw new IOException("登录启动注册未生效。");
    }
}
