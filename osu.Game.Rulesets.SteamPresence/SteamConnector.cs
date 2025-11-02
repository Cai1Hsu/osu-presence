using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using osu.Framework;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Online.Multiplayer;
using Steamworks;

namespace osu.Game.Rulesets.SteamPresence;

public partial class SteamConnector : Drawable
{
    public bool IsInitialized => initialized;
    private volatile bool initialized;
    private bool retryInitialize = true;

    private const string appid = "607260"; // mcosu's Steam AppID

    protected override void LoadComplete()
    {
        base.LoadComplete();

        try
        {
            loadDlls();

            Environment.SetEnvironmentVariable("SteamAppId", appid.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", appid.ToString());

            tryInitializeSteamAPI(isStartup: true);
        }
        catch (Exception e)
        {
            retryInitialize = false;
            Logger.Log($"Failed to load Steamworks.NET DLLs: {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
        }
    }

    private void loadDlls()
    {
        var os = RuntimeInfo.OS switch
        {
            RuntimeInfo.Platform.Windows => @"Windows",
            RuntimeInfo.Platform.macOS or
            RuntimeInfo.Platform.Linux => @"OSX-Linux",
            _ => null,
        };

        if (os is null)
            throw new PlatformNotSupportedException("Steamworks.NET is only supported on Windows, macOS, and Linux.");

        var archSuffix = Environment.Is64BitProcess ? "x64" : "x86";

        var pluginDirectory = getPluginDirectory();

        if (string.IsNullOrEmpty(pluginDirectory))
            throw new InvalidOperationException("Could not determine the current assembly path.");

        var dllFolder = Path.Combine(pluginDirectory, $"{os}-{archSuffix}");

        foreach (var dll in collectNativeDlls(dllFolder))
        {
            Logger.Log($"Found native DLL: {dll}", LoggingTarget.Runtime, LogLevel.Debug);

            var fullPath = Path.Combine(pluginDirectory, dll);
            NativeLibrary.Load(fullPath);

            Logger.Log($"Loaded native DLL from {fullPath}", LoggingTarget.Runtime, LogLevel.Debug);
        }

        var steamworksDotNetDll = Path.Combine(dllFolder, @"Steamworks.NET.dll");

        var fullDllPath = Path.Combine(pluginDirectory, steamworksDotNetDll);
        Assembly.LoadFrom(fullDllPath);

        Logger.Log($"Loaded Steamworks.NET from {fullDllPath}", LoggingTarget.Runtime, LogLevel.Debug);
    }

    private string getPluginDirectory()
    {
        var asm = Assembly.GetExecutingAssembly();
        var location = Path.GetDirectoryName(asm.Location);

        if (!string.IsNullOrEmpty(location))
            return location;

        return Directory.GetCurrentDirectory();
    }

    private string[] collectNativeDlls(string folder)
    {
        var extension = RuntimeInfo.OS switch
        {
            RuntimeInfo.Platform.Windows => ".dll",
            RuntimeInfo.Platform.macOS => ".dylib",
            RuntimeInfo.Platform.Linux => ".so",
            _ => throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInfo.OS}"),
        };

        return Directory.GetFiles(folder, $"*{extension}");
    }

    private bool tryInitializeSteamAPI(bool isStartup = false)
    {
        if (initialized)
            return true;

        try
        {
            initialized = SteamAPI.Init();

            Debug.Assert(initialized, "Steam API failed to initialize.");

            SteamClient.SetWarningMessageHook((severity, message) => Scheduler.Add(() =>
            {
                Logger.Log($"[Steamworks.NET] Severity: {severity} - {message}", LoggingTarget.Runtime, LogLevel.Important);
            }));

            // I forgot where I found these keys, but they seem to be standard.
            const string STEAM_DISPLAY_KEY = @"steam_display";
            const string STATUS_VALUE = @"#Status";

            Scheduler.Add(() =>
            {
                SteamFriends.SetRichPresence(STEAM_DISPLAY_KEY, STATUS_VALUE);
                SetPresence("Playing osu!Lazer");

                Logger.Log($"Steam client successfully initialized.", LoggingTarget.Runtime, LogLevel.Important);
            });
        }
        catch (Exception e) when (isStartup)
        {
            Debug.Assert(!initialized);

            var isSteamRunning = SteamAPI.IsSteamRunning();

            Scheduler.Add(() =>
            {
                if (!isSteamRunning)
                    Logger.Log("Steam client is not running, steam presence may not work.", LoggingTarget.Runtime, LogLevel.Important);
                else
                    Logger.Log($"Failed to initialize steamworks, {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
            });
        }
        catch { }

        return initialized;
    }

    public void SetPresence(string value, string? key = null)
    {
        const string STEAM_STATUS_KEY = @"status";

        if (!initialized)
            return;

        key ??= STEAM_STATUS_KEY;

        value = truncatePresence(value);

        SteamFriends.SetRichPresence(key, value);
    }

    private string truncatePresence(string value)
    {
        // Steam's maximum length.
        const int k_cchMaxRichPresenceValueLength = 256;

        if (value.Length > k_cchMaxRichPresenceValueLength)
        {
            value = value.Substring(0, k_cchMaxRichPresenceValueLength - 3);
            value += "...";
        }

        return value;
    }

    private double lastTryInitializeTime;
    private const double tryInitializeThrottleTime = 5 * 1000;

    protected override void Update()
    {
        base.Update();

        if (initialized)
        {
            SteamAPI.RunCallbacks();
        }
        else if (retryInitialize)
        {
            if (Clock.CurrentTime - lastTryInitializeTime < tryInitializeThrottleTime)
                return;

            lastTryInitializeTime = Clock.CurrentTime;

            if (initializeTask is null)
            {
                initializeTask ??= Task.Run(() =>
                {
                    if (SteamAPI.IsSteamRunning())
                        tryInitializeSteamAPI();

                    initializeTask = null;
                });
            }
        }
    }

    private volatile Task? initializeTask;

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (!isDisposing)
            return;

        if (initialized)
        {
            SteamAPI.Shutdown();
            initialized = false;
        }
    }
}
