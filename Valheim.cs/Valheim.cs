using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    // MeFriendos build based on WindowsGSM.Valheim by Sarpendon.
    public class Valheim : SteamCMDAgent
    {
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Valheim",
            author = "MeFriendos",
            description = "WindowsGSM plugin for Valheim Dedicated Server (MeFriendos build)",
            version = "0.1.1",
            url = "https://github.com/PapaGordon/WindowsGSM.Valheim-MeFriendos",
            color = "#8802db"
        };

        public override bool loginAnonymous => true;
        public override string AppId => "896660";
        public override string StartPath => @"valheim_server.exe";

        public Valheim(ServerConfig serverData) : base(serverData)
        {
            base.serverData = _serverData = serverData;
        }

        private readonly ServerConfig _serverData;

        public string FullName = "Valheim Dedicated Server";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 2;
        public object QueryMethod = new A2S();

        public string Port = "2456";
        public string QueryPort = "2457";
        public string Defaultmap = "Dedicated";
        public string Maxplayers = "10";

        // Steam backend only. Crossplay is intentionally not enabled in this MeFriendos build.
        // Change the placeholder password before starting the server.
        public string Additional = "-password \"CHANGE_ME\" -savedir \".\\save-data\" -public 1 -saveinterval 1800 -backups 4 -backupshort 7200 -backuplong 43200";

        private const uint CTRL_C_EVENT = 0;
        private delegate bool ConsoleCtrlDelegate(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        public Task<Process> Start()
        {
            string serverFiles = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string executable = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            bool embedConsole = AllowsEmbedConsole;

            if (!File.Exists(executable))
            {
                Error = $"File not found: {executable}";
                return Task.FromResult<Process>(null);
            }

            if (!ValidateConfiguration(out string validationError))
            {
                Error = validationError;
                return Task.FromResult<Process>(null);
            }

            if (!RemoveAutomaticBroadFirewallRule())
            {
                Error = "Automatic firewall access could not be disabled. Start WindowsGSM as administrator or remove the broad valheim_server.exe rule manually.";
                return Task.FromResult<Process>(null);
            }

            Directory.CreateDirectory(Path.Combine(serverFiles, "save-data"));
            Directory.CreateDirectory(Path.Combine(serverFiles, "logs"));

            string parameters = BuildParameters(embedConsole);

            var process = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFiles,
                    FileName = executable,
                    Arguments = parameters,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            // Valheim is administered in-game. The embedded console is output-only so
            // stdin remains attached to the native console for a clean CTRL+C shutdown.
            if (embedConsole)
            {
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                var serverConsole = new ServerConsole(_serverData.ServerID);
                process.OutputDataReceived += serverConsole.AddOutput;
                process.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                process.Start();

                if (embedConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                return Task.FromResult(process);
            }
            catch (FileNotFoundException e)
            {
                Error = $"File not found: {e.Message}";
            }
            catch (UnauthorizedAccessException e)
            {
                Error = $"Access denied: {e.Message}";
            }
            catch (Exception e)
            {
                Error = e.Message;
            }

            process.Dispose();
            return Task.FromResult<Process>(null);
        }

        public async Task Stop(Process process)
        {
            if (process == null)
                return;

            try
            {
                process.Refresh();
                if (process.HasExited)
                    return;
            }
            catch
            {
                return;
            }

            await Task.Run(() =>
            {
                if (TrySendCtrlC(process, 20000))
                    return;

                // Fallback to the same console keystroke approach used by the original
                // WindowsGSM.Valheim plugin when a normal console window is available.
                try
                {
                    process.Refresh();
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ServerConsole.SetMainWindow(process.MainWindowHandle);
                        ServerConsole.SendWaitToMainWindow("^c");
                        if (process.WaitForExit(10000))
                            return;
                    }
                }
                catch
                {
                }

                // Last resort only. Valheim's official guidance prefers CTRL+C because
                // it gives the server a chance to save and shut down cleanly.
                try
                {
                    process.Refresh();
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            });
        }

        private bool ValidateConfiguration(out string error)
        {
            error = null;

            if (HasArgument(_serverData.ServerParam, "-crossplay"))
            {
                error = "This MeFriendos build is configured for the Steam backend. Remove -crossplay from Server Start Param.";
                return false;
            }

            string password = GetArgumentValue(_serverData.ServerParam, "-password");
            if (string.IsNullOrWhiteSpace(password))
            {
                error = "Valheim requires -password in Server Start Param.";
                return false;
            }

            if (password.Length < 5)
            {
                error = "Valheim requires a server password of at least 5 characters.";
                return false;
            }

            if (IsPlaceholderPassword(password))
            {
                error = "Change the default Valheim password placeholder before starting the server.";
                return false;
            }

            if (!int.TryParse(_serverData.ServerPort, out int gamePort) || gamePort < 1 || gamePort > 65534)
            {
                error = "Valheim Server Port must be a number between 1 and 65534 because Valheim also uses port +1.";
                return false;
            }

            return true;
        }

        private string BuildParameters(bool embedConsole)
        {
            string parameters = "-nographics -batchmode";
            string serverParameters = _serverData.ServerParam ?? string.Empty;

            // Valheim redirects its live server output away from the process streams when
            // -logFile is used. WindowsGSM's embedded console reads those process streams,
            // so suppress -logFile only while Embed Console is enabled.
            if (embedConsole)
                serverParameters = RemoveArgumentWithValue(serverParameters, "-logFile");

            if (!HasArgument(serverParameters, "-public"))
                parameters += " -public 1";

            if (!string.IsNullOrWhiteSpace(_serverData.ServerName))
                parameters += $" -name {Quote(_serverData.ServerName)}";

            parameters += $" -port {_serverData.ServerPort}";

            if (!string.IsNullOrWhiteSpace(_serverData.ServerMap))
                parameters += $" -world {Quote(_serverData.ServerMap)}";

            if (!string.IsNullOrWhiteSpace(serverParameters))
                parameters += $" {serverParameters}";

            return parameters;
        }

        private static string RemoveArgumentWithValue(string arguments, string name)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return arguments;

            string cleaned = Regex.Replace(
                arguments,
                $@"(?<!\S){Regex.Escape(name)}(?:\s+(?:""[^""]*""|\S+))?",
                string.Empty,
                RegexOptions.IgnoreCase
            );

            return Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        }

        private bool RemoveAutomaticBroadFirewallRule()
        {
            string programPath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);

            try
            {
                Type managerType = Type.GetTypeFromProgID("HNetCfg.FwMgr");
                if (managerType == null)
                    return false;

                object manager = Activator.CreateInstance(managerType);
                object localPolicy = manager.GetType().InvokeMember(
                    "LocalPolicy", BindingFlags.GetProperty, null, manager, null);
                object currentProfile = localPolicy.GetType().InvokeMember(
                    "CurrentProfile", BindingFlags.GetProperty, null, localPolicy, null);
                object applications = currentProfile.GetType().InvokeMember(
                    "AuthorizedApplications", BindingFlags.GetProperty, null, currentProfile, null);

                IEnumerable entries = applications as IEnumerable;
                if (entries == null)
                    return false;

                bool found = false;
                foreach (object application in entries)
                {
                    string applicationPath = Convert.ToString(application.GetType().InvokeMember(
                        "ProcessImageFileName", BindingFlags.GetProperty, null, application, null));

                    if (string.Equals(applicationPath, programPath, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return true;

                applications.GetType().InvokeMember(
                    "Remove", BindingFlags.InvokeMethod, null, applications, new object[] { programPath });

                // Reacquire and verify after removal instead of assuming success.
                applications = currentProfile.GetType().InvokeMember(
                    "AuthorizedApplications", BindingFlags.GetProperty, null, currentProfile, null);
                entries = applications as IEnumerable;
                if (entries == null)
                    return false;

                foreach (object application in entries)
                {
                    string applicationPath = Convert.ToString(application.GetType().InvokeMember(
                        "ProcessImageFileName", BindingFlags.GetProperty, null, application, null));

                    if (string.Equals(applicationPath, programPath, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySendCtrlC(Process process, int timeoutMilliseconds)
        {
            bool attached = false;

            try
            {
                if (process == null || process.HasExited)
                    return true;

                attached = AttachConsole((uint)process.Id);
                if (!attached)
                    return false;

                // Ignore CTRL+C in WindowsGSM itself while forwarding it to the server console.
                SetConsoleCtrlHandler(null, true);

                if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
                    return false;

                return process.WaitForExit(timeoutMilliseconds);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (attached)
                {
                    try
                    {
                        SetConsoleCtrlHandler(null, false);
                        FreeConsole();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool IsPlaceholderPassword(string password)
        {
            string normalized = (password ?? string.Empty).Trim();
            return normalized.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("CHANGEME", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ChangeMe123", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("123456", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasArgument(string arguments, string name)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return false;

            return Regex.IsMatch(
                arguments,
                $@"(?:^|\s){Regex.Escape(name)}(?:\s|$)",
                RegexOptions.IgnoreCase
            );
        }

        private static string GetArgumentValue(string arguments, string name)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return null;

            Match match = Regex.Match(
                arguments,
                $@"(?:^|\s){Regex.Escape(name)}\s+(?:""([^""]*)""|(\S+))",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
                return null;

            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        }

        private static string Quote(string value)
        {
            string safe = (value ?? string.Empty).Replace("\"", "'");
            return $"\"{safe}\"";
        }
    }
}
