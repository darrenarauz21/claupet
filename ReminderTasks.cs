using System.Diagnostics;
using System.Security;
using System.Text;

namespace Claupet;

/// <summary>
/// Por si la app está cerrada cuando llega un recordatorio: una tarea del Programador de tareas de Windows
/// («\Claupet\Recordatorio &lt;id&gt;», del usuario y sin administrador) la abre a esa hora con
/// «--recordatorio &lt;id&gt;». Si la app ya está abierta, la que se abre se cierra enseguida (hay otra) y
/// avisa la que estaba. Con el equipo apagado a esa hora, corre al encenderlo («StartWhenAvailable»). La
/// tarea se borra al avisar o cancelar el recordatorio y, si no, sola un día después.
/// </summary>
static class ReminderTasks
{
    const string FolderName = "Claupet";

    static string Name(string id) => $@"{FolderName}\Recordatorio {id}";

    public static void Create(Reminder reminder)
    {
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var at = reminder.At.ToString("yyyy-MM-dd'T'HH:mm:ss");
        var end = reminder.At.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{SecurityElement.Escape(L.T("Claupet: abre la app para avisar un recordatorio."))}</Description></RegistrationInfo>
              <Triggers>
                <TimeTrigger><StartBoundary>{at}</StartBoundary><EndBoundary>{end}</EndBoundary><Enabled>true</Enabled></TimeTrigger>
              </Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <DeleteExpiredTaskAfter>PT0S</DeleteExpiredTaskAfter>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--recordatorio {reminder.Id}</Arguments></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"claupet-recordatorio-{reminder.Id}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try { Run("/Create", "/TN", Name(reminder.Id), "/XML", file, "/F"); }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    /// <summary>Borra la tarea de ese recordatorio, si todavía está.</summary>
    public static void Delete(string id)
    {
        try { Run("/Delete", "/TN", Name(id), "/F"); }
        catch (InvalidOperationException) { } // ya no estaba (se borró sola o nunca se pudo crear)
    }

    static void Run(params string[] args)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("schtasks");
        var error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(15000)) { process.Kill(); throw new InvalidOperationException("schtasks: sin respuesta"); }
        if (process.ExitCode != 0) throw new InvalidOperationException("schtasks: " + error.Result.Trim());
    }
}
