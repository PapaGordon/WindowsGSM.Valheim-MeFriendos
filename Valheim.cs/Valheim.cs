using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
            version = "0.1.2",
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
        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private static readonly object ConsoleAttachLock = new object();

        private delegate bool ConsoleCtrlDelegate(uint ctrlType);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            // Embedded Console remains output-only. stdin stays on the native console so
            // Valheim can still receive a clean CTRL+C shutdown request.
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

                    // Raziel v1.25.1.22 disables the Toggle Console button while this
                    // StartInfo flag remains true. The redirected stdout pipe was already
                    // created by Process.Start(), so changing StartInfo afterwards does not
                    // alter the running process or stop the asynchronous Embedded Console.
                    process.StartInfo.RedirectStandardOutput = false;
                }

#pragma warning disable 4014
                Task.Run(() => MonitorNativeConsoleHandle(process));
#pragma warning restore 4014

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

                // Fallback to the original console-keystroke approach. Prefer the HWND
                // already repaired by the Toggle Console monitor when it is available.
                try
                {
                    process.Refresh();
                    if (!process.HasExited)
                    {
                        IntPtr consoleWindow = GetRegisteredConsoleWindow(process);
                        if (consoleWindow == IntPtr.Zero)
                            consoleWindow = process.MainWindowHandle;

                        if (consoleWindow != IntPtr.Zero && IsWindow(consoleWindow))
                        {
                            ServerConsole.SetMainWindow(consoleWindow);
                            ServerConsole.SendWaitToMainWindow("^c");
                            if (process.WaitForExit(10000))
                                return;
                        }
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
            // -logFile is used. Suppress it only while WindowsGSM Embed Console is enabled.
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

        private static string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return string.Empty;

            try
            {
                var className = new StringBuilder(256);
                int length = GetClassName(hWnd, className, className.Capacity);
                return length > 0 ? className.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IntPtr FindTopLevelWindowForProcess(Process process)
        {
            if (process == null)
                return IntPtr.Zero;

            int processId;
            try
            {
                processId = process.Id;
            }
            catch
            {
                return IntPtr.Zero;
            }

            IntPtr visibleWindow = IntPtr.Zero;
            IntPtr preferredConsoleWindow = IntPtr.Zero;

            try
            {
                EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    uint windowProcessId;
                    GetWindowThreadProcessId(hWnd, out windowProcessId);

                    if (windowProcessId != (uint)processId || !IsWindow(hWnd))
                        return true;

                    string className = GetWindowClassName(hWnd);
                    if (string.Equals(className, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
                    {
                        preferredConsoleWindow = hWnd;
                        return false;
                    }

                    if (visibleWindow == IntPtr.Zero && IsWindowVisible(hWnd))
                        visibleWindow = hWnd;

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                return IntPtr.Zero;
            }

            return preferredConsoleWindow != IntPtr.Zero ? preferredConsoleWindow : visibleWindow;
        }

        private static IntPtr GetAttachedConsoleWindow(Process process, out string windowClass, out int attachError)
        {
            windowClass = string.Empty;
            attachError = 0;

            if (process == null)
                return IntPtr.Zero;

            lock (ConsoleAttachLock)
            {
                try
                {
                    if (process.HasExited)
                        return IntPtr.Zero;

                    // AttachConsole is process-wide. Do not disturb a console WindowsGSM
                    // was already attached to before this short discovery probe.
                    if (GetConsoleWindow() != IntPtr.Zero)
                        return IntPtr.Zero;

                    if (!AttachConsole((uint)process.Id))
                    {
                        attachError = Marshal.GetLastWin32Error();
                        return IntPtr.Zero;
                    }

                    try
                    {
                        IntPtr consoleWindow = GetConsoleWindow();
                        if (consoleWindow == IntPtr.Zero || !IsWindow(consoleWindow))
                            return IntPtr.Zero;

                        windowClass = GetWindowClassName(consoleWindow);
                        return consoleWindow;
                    }
                    finally
                    {
                        FreeConsole();
                    }
                }
                catch
                {
                    attachError = Marshal.GetLastWin32Error();
                    return IntPtr.Zero;
                }
            }
        }

        private static IntPtr ResolveToggleWindow(
            Process process,
            out string source,
            out string windowClass,
            out int attachError)
        {
            source = string.Empty;
            windowClass = string.Empty;
            attachError = 0;

            if (process == null)
                return IntPtr.Zero;

            try
            {
                process.Refresh();
                IntPtr mainWindow = process.MainWindowHandle;
                if (mainWindow != IntPtr.Zero && IsWindow(mainWindow))
                {
                    string mainWindowClass = GetWindowClassName(mainWindow);
                    if (IsWindowVisible(mainWindow) ||
                        string.Equals(mainWindowClass, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "Process.MainWindowHandle after Refresh";
                        windowClass = mainWindowClass;
                        return mainWindow;
                    }
                }
            }
            catch
            {
                // Continue with explicit window discovery.
            }

            IntPtr processWindow = FindTopLevelWindowForProcess(process);
            if (processWindow != IntPtr.Zero && IsWindow(processWindow))
            {
                source = "EnumWindows by Valheim PID";
                windowClass = GetWindowClassName(processWindow);
                return processWindow;
            }

            string consoleClass;
            IntPtr consoleWindow = GetAttachedConsoleWindow(process, out consoleClass, out attachError);
            if (consoleWindow == IntPtr.Zero)
                return IntPtr.Zero;

            source = "AttachConsole/GetConsoleWindow";
            windowClass = consoleClass;
            return consoleWindow;
        }

        private static bool IsSafeToggleTarget(IntPtr hWnd, string windowClass)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            return !string.Equals(windowClass, "PseudoConsoleWindow", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetShowConsoleState(object metadata, out bool showConsole)
        {
            showConsole = false;
            if (metadata == null)
                return false;

            try
            {
                Type metadataType = metadata.GetType();
                FieldInfo field = metadataType.GetField(
                    "ShowConsole",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (field != null && field.FieldType == typeof(bool))
                {
                    showConsole = (bool)field.GetValue(metadata);
                    return true;
                }

                PropertyInfo property = metadataType.GetProperty(
                    "ShowConsole",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
                {
                    showConsole = (bool)property.GetValue(metadata, null);
                    return true;
                }
            }
            catch
            {
                // Other WindowsGSM builds can still use normal HWND synchronization.
            }

            return false;
        }

        private string GetToggleConsoleDiagnosticPath()
        {
            return Path.Combine(
                ServerPath.GetServersCache(_serverData.ServerID),
                "valheim-toggle-console.log");
        }

        private void ResetToggleConsoleDiagnostic(Process process)
        {
            try
            {
                string path = GetToggleConsoleDiagnosticPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " [0.1.2] Toggle Console monitor started for PID " + process.Id +
                    "; WindowsGSM " + WindowsGSM.MainWindow.WGSM_VERSION + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never interfere with server startup.
            }
        }

        private void WriteToggleConsoleDiagnostic(string message)
        {
            try
            {
                string path = GetToggleConsoleDiagnosticPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never interfere with server operation.
            }
        }

        private void SaveWindowsGsmConsoleHandle(IntPtr consoleWindow)
        {
            try
            {
                string cachePath = ServerPath.GetServersCache(_serverData.ServerID);
                Directory.CreateDirectory(cachePath);
                File.WriteAllText(Path.Combine(cachePath, "windowsIntPtr"), consoleWindow.ToString());
            }
            catch
            {
                // The in-memory handle is sufficient for the current WindowsGSM session.
            }
        }

        private IntPtr GetRegisteredConsoleWindow(Process process)
        {
            int serverId;
            if (process == null || !int.TryParse(Convert.ToString(_serverData.ServerID), out serverId))
                return IntPtr.Zero;

            try
            {
                if (!WindowsGSM.MainWindow._serverMetadata.ContainsKey(serverId))
                    return IntPtr.Zero;

                var metadata = WindowsGSM.MainWindow._serverMetadata[serverId];
                Process trackedProcess = metadata.Process;
                if (trackedProcess == null || trackedProcess.Id != process.Id)
                    return IntPtr.Zero;

                return metadata.MainWindow != IntPtr.Zero && IsWindow(metadata.MainWindow)
                    ? metadata.MainWindow
                    : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private async Task MonitorNativeConsoleHandle(Process process)
        {
            int serverId;
            if (!int.TryParse(Convert.ToString(_serverData.ServerID), out serverId))
                return;

            ResetToggleConsoleDiagnostic(process);

            IntPtr resolvedWindow = IntPtr.Zero;
            string resolvedSource = string.Empty;
            string resolvedClass = string.Empty;
            int lastAttachError = 0;
            DateTime nextResolveAt = DateTime.MinValue;
            DateTime unresolvedNoticeAt = DateTime.UtcNow.AddSeconds(15);
            bool unresolvedNoticeShown = false;
            bool showConsoleCapabilityLogged = false;
            bool showConsoleUnavailableLogged = false;
            bool? lastAppliedShowConsole = null;

            while (true)
            {
                try
                {
                    if (process.HasExited)
                    {
                        WriteToggleConsoleDiagnostic("Valheim process exited; Toggle Console monitor stopped.");
                        return;
                    }
                }
                catch
                {
                    WriteToggleConsoleDiagnostic("Valheim process state became unavailable; Toggle Console monitor stopped.");
                    return;
                }

                if (resolvedWindow == IntPtr.Zero ||
                    !IsWindow(resolvedWindow) ||
                    DateTime.UtcNow >= nextResolveAt)
                {
                    string source;
                    string windowClass;
                    int attachError;
                    IntPtr candidate = ResolveToggleWindow(process, out source, out windowClass, out attachError);
                    lastAttachError = attachError;
                    nextResolveAt = DateTime.UtcNow.AddSeconds(5);

                    if (candidate != IntPtr.Zero && !IsSafeToggleTarget(candidate, windowClass))
                    {
                        WriteToggleConsoleDiagnostic(
                            "Rejected HWND 0x" + candidate.ToInt64().ToString("X") +
                            " via " + source + " [" + windowClass + "] because it is not a safe native toggle target.");
                        candidate = IntPtr.Zero;
                    }

                    if (candidate != IntPtr.Zero)
                    {
                        if (resolvedWindow != candidate)
                        {
                            WriteToggleConsoleDiagnostic(
                                "Resolved HWND 0x" + candidate.ToInt64().ToString("X") +
                                " via " + source +
                                (string.IsNullOrWhiteSpace(windowClass) ? string.Empty : " [" + windowClass + "]") + ".");
                            lastAppliedShowConsole = null;
                        }

                        resolvedWindow = candidate;
                        resolvedSource = source;
                        resolvedClass = windowClass;
                        unresolvedNoticeShown = false;
                        unresolvedNoticeAt = DateTime.UtcNow.AddSeconds(15);
                    }
                    else if (resolvedWindow != IntPtr.Zero && !IsWindow(resolvedWindow))
                    {
                        WriteToggleConsoleDiagnostic("Previously resolved Toggle Console HWND became invalid.");
                        resolvedWindow = IntPtr.Zero;
                        resolvedSource = string.Empty;
                        resolvedClass = string.Empty;
                        lastAppliedShowConsole = null;
                    }
                }

                bool matchingProcessRegistered = false;
                if (WindowsGSM.MainWindow._serverMetadata.ContainsKey(serverId))
                {
                    try
                    {
                        var metadata = WindowsGSM.MainWindow._serverMetadata[serverId];
                        Process trackedProcess = metadata.Process;
                        matchingProcessRegistered = trackedProcess != null && trackedProcess.Id == process.Id;

                        if (matchingProcessRegistered && resolvedWindow != IntPtr.Zero && IsWindow(resolvedWindow))
                        {
                            if (metadata.MainWindow != resolvedWindow)
                            {
                                IntPtr previousWindow = metadata.MainWindow;
                                metadata.MainWindow = resolvedWindow;
                                SaveWindowsGsmConsoleHandle(resolvedWindow);

                                WriteToggleConsoleDiagnostic(
                                    "WindowsGSM MainWindow updated from 0x" +
                                    previousWindow.ToInt64().ToString("X") + " to 0x" +
                                    resolvedWindow.ToInt64().ToString("X") +
                                    " via " + resolvedSource +
                                    (string.IsNullOrWhiteSpace(resolvedClass) ? string.Empty : " [" + resolvedClass + "]") + ".");
                            }

                            bool desiredShowConsole;
                            if (TryGetShowConsoleState(metadata, out desiredShowConsole))
                            {
                                if (!showConsoleCapabilityLogged)
                                {
                                    WriteToggleConsoleDiagnostic(
                                        "Detected WindowsGSM ShowConsole state support; direct visibility synchronization enabled.");
                                    showConsoleCapabilityLogged = true;
                                }

                                bool currentlyVisible = IsWindowVisible(resolvedWindow);
                                if (currentlyVisible != desiredShowConsole ||
                                    !lastAppliedShowConsole.HasValue ||
                                    lastAppliedShowConsole.Value != desiredShowConsole)
                                {
                                    ShowWindow(resolvedWindow, desiredShowConsole ? SW_SHOWNORMAL : SW_HIDE);
                                    lastAppliedShowConsole = desiredShowConsole;

                                    WriteToggleConsoleDiagnostic(
                                        "Applied ShowConsole=" + desiredShowConsole +
                                        " directly to HWND 0x" + resolvedWindow.ToInt64().ToString("X") + ".");
                                }
                            }
                            else if (!showConsoleUnavailableLogged)
                            {
                                WriteToggleConsoleDiagnostic(
                                    "WindowsGSM does not expose ShowConsole; using handle synchronization only.");
                                showConsoleUnavailableLogged = true;
                            }
                        }
                    }
                    catch
                    {
                        // WindowsGSM may update metadata concurrently. Retry on the next pass.
                    }
                }

                if (resolvedWindow == IntPtr.Zero &&
                    DateTime.UtcNow >= unresolvedNoticeAt &&
                    !unresolvedNoticeShown)
                {
                    string detail = lastAttachError == 0
                        ? "No usable native window was found."
                        : "AttachConsole failed with Win32 error " + lastAttachError + ".";

                    WriteToggleConsoleDiagnostic(
                        detail + " WindowsGSM process registered=" + matchingProcessRegistered + ".");
                    unresolvedNoticeShown = true;
                }

                await Task.Delay(250);
            }
        }

        private static bool TrySendCtrlC(Process process, int timeoutMilliseconds)
        {
            lock (ConsoleAttachLock)
            {
                bool attached = false;

                try
                {
                    if (process == null || process.HasExited)
                        return true;

                    // The monitor also uses AttachConsole for HWND discovery. The shared lock
                    // prevents both operations from changing WindowsGSM's console attachment at once.
                    if (GetConsoleWindow() != IntPtr.Zero)
                        return false;

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
