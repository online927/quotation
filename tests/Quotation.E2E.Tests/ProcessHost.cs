using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Quotation.E2E.Tests;

/// <summary>Runs a built .NET application as a real child process.</summary>
public sealed class ProcessHost : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    public ProcessHost(string dll, string arguments, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;
        _process = Process.Start(psi)!;
        _process.OutputDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    public bool HasExited => _process.HasExited;

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static async Task WaitForHttpAsync(string url, TimeSpan timeout, Func<string>? diagnostics = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var r = await http.GetAsync(url);
                if ((int)r.StatusCode < 500) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(300);
        }
        throw new TimeoutException($"{url} did not start.\n{diagnostics?.Invoke()}");
    }

    /// <summary>Hard kill — simulates a crash / power loss.</summary>
    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10000);
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    public static string BuiltDll(string projectDir, string dllName)
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory); // …/tests/X/bin/{Config}/net10.0/
        var configuration = testBin.Parent!.Name;
        var root = testBin;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Quotation.sln"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, projectDir, "bin", configuration, "net10.0", dllName);
        return File.Exists(path) ? path : throw new FileNotFoundException("Build the solution first.", path);
    }
}
