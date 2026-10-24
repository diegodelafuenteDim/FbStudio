using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace FbStudio.Workspace;

/// <summary>
/// Linux: el llavero del escritorio (Secret Service: GNOME Keyring, KWallet…) por medio de <c>secret-tool</c>, del
/// paquete libsecret-tools. El secreto viaja por la entrada estándar, nunca en la línea de comandos (se vería con ps).
/// Cada secreto lleva los atributos application=fbstudio y key=&lt;clave&gt;.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretStore(string? tool = null) : ISecretStore
{
    private readonly string tool = tool ?? FindTool("secret-tool");

    /// <summary>
    /// La ruta completa del programa, buscada solo en las carpetas absolutas del PATH: nunca uno que esté en la carpeta
    /// desde donde se abrió FbStudio (recibiría las contraseñas). Si no está, el nombre solo (y fallará como antes).
    /// </summary>
    internal static string FindTool(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathRooted(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var candidate in new[] { "/usr/bin/" + name, "/usr/local/bin/" + name })
            if (File.Exists(candidate)) return candidate;
        return name;
    }

    // Un llavero bloqueado puede pedir la clave del usuario en una ventana: hay que darle tiempo.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Si está secret-tool y el llavero guarda de verdad: se guarda y se borra un dato de prueba. Solo buscar no alcanza:
    /// sin escritorio el Secret Service responde las búsquedas pero no puede crear el llavero (pide una ventana).
    /// </summary>
    public static bool IsAvailable(string tool = "secret-tool")
    {
        try
        {
            var probe = new[] { "application", "fbstudio", "key", "fbstudio-probe" };
            var (stored, _, _) = Run(tool, ["store", "--label", "FbStudio (prueba)", .. probe], "ok", TimeSpan.FromSeconds(30));
            if (stored != 0) return false;
            var (found, value, _) = Run(tool, ["lookup", .. probe], null, TimeSpan.FromSeconds(10));
            Run(tool, ["clear", .. probe], null, TimeSpan.FromSeconds(10));
            return found == 0 && value.TrimEnd('\n') == "ok";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
        {
            return false; // no está instalado, o no hay sesión de D-Bus
        }
    }

    public void Set(string key, string secret)
    {
        var (exit, _, error) = Run(tool, ["store", "--label", "FbStudio: " + key, "application", "fbstudio", "key", key], secret, Timeout);
        if (exit != 0) throw new InvalidOperationException(string.Format(Core.Resources.CoreStrings.KeyringFailed, error.Trim()));
    }

    public string? Get(string key)
    {
        var (exit, output, error) = Run(tool, ["lookup", "application", "fbstudio", "key", key], null, Timeout);
        if (exit == 1 && error.Trim().Length == 0) return null; // no está
        if (exit != 0) throw new InvalidOperationException(string.Format(Core.Resources.CoreStrings.KeyringFailed, error.Trim()));
        return output.EndsWith('\n') ? output[..^1] : output;
    }

    public void Remove(string key) => Run(tool, ["clear", "application", "fbstudio", "key", key], null, Timeout);

    private static (int Exit, string Output, string Error) Run(string tool, IReadOnlyList<string> args, string? input, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(tool);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null) process.StandardInput.Write(input);
        process.StandardInput.Close();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new TimeoutException(tool);
        }
        return (process.ExitCode, output.Result, error.Result);
    }
}
