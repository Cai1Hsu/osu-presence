using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Replays;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Game.Rulesets.SteamPresence;

public partial class OpenInStableAction : Drawable
{
    [Resolved]
    public IBindable<WorkingBeatmap> Beatmap { get; private set; } = null!;

    [Resolved]
    private LegacyImportManager? legacyImportManager { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private MusicController musicController { get; set; } = null!;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    public string? StablePath => legacyImportManager?.GetCurrentStableStorage()?.GetFullPath(string.Empty);

    [BackgroundDependencyLoader]
    private void load()
    {
        audio.VolumeTrack.BindValueChanged(trackVolumeChanged);
        host.Activated += () => Schedule(() => windowActivated());
    }

    private double? previousVolume;
    private bool internalTriggering = false;

    private void postLaunchStable()
    {
        if (musicController.AllowTrackControl.Value)
        {
            musicController.Stop(true);
        }
        else
        {
            internalTriggering = true;

            audio.VolumeTrack.Value = 0; // expected `trackVolumeChanged` invoked here

            internalTriggering = false;
        }

        // activate newly launched stable window
        focusStableWindow();
    }

    private void trackVolumeChanged(ValueChangedEvent<double> args)
    {
        if (internalTriggering)
            return;

        previousVolume = args.OldValue;
    }

    private void windowActivated()
    {
        if (previousVolume.HasValue)
        {
            audio.VolumeTrack.Value = previousVolume.Value;
            previousVolume = null;
        }
    }

    private Process? getStableProcess()
    {
        if (StablePath is null)
            return null;

        string stableExeName = Path.Combine(StablePath, "osu!.exe");
        var processes = Process.GetProcessesByName("osu!")
            .FirstOrDefault(p => string.Equals(p.MainModule?.FileName, stableExeName, StringComparison.OrdinalIgnoreCase));

        return processes;
    }

    private void focusStableWindow()
    {
        if (RuntimeInfo.OS is not RuntimeInfo.Platform.Windows)
            return;

        Debug.Assert(StablePath != null); // button should be disabled if stablePath is null

        var processes = getStableProcess();

        if (processes == null)
            return;

        IntPtr hWnd = processes.MainWindowHandle;

        // let's hide minimize foreground first, most of the time, ourself
        IntPtr foregroundWindow = Win32.GetForegroundWindow();

        // if hWnd equals and we are not active
        // probably the process were resolved to lazer
        Debug.Assert(foregroundWindow != hWnd && hWnd != IntPtr.Zero && !host.IsActive.Value);

        try
        {
            Win32.ShowWindow(foregroundWindow, Win32.SW_FORCEMINIMIZE);

            if (hWnd == IntPtr.Zero)
                return;

            Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
            Win32.SetForegroundWindow(hWnd);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to focus stable osu! window.");
        }
    }

    static class Win32
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;
        public const int SW_FORCEMINIMIZE = 11;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }

    private volatile bool muteInProgress = false;

    private ScheduledDelegate? scheduledMute;

    public void TryMuteStable()
    {
        // Debounce to at most once per second
        if (muteInProgress)
        {
            scheduledMute?.Cancel();
            scheduledMute = Scheduler.AddDelayed(TryMuteStable, 200);

            return;
        }

        muteInProgress = true;

        Task.Run(() =>
        {
            try
            {
                if (StablePath is null)
                    return;

                Process? stableProcess = getStableProcess();

                if (stableProcess is null)
                    return;

                // We play a silent replay so that stable osu! mutes itself.
                var emptyReplay = Path.Combine(StablePath, "empty.osr");

                if (!File.Exists(emptyReplay))
                {
                    var storage = legacyImportManager?.GetCurrentStableStorage();

                    if (storage is null)
                        return;

                    using (var stream = storage.CreateFileSafely("empty.osr"))
                    {
                        var encoder = createEmptyReplay();
                        encoder.Encode(stream);
                    }

                    Debug.Assert(File.Exists(emptyReplay));
                }

                try
                {
                    var stablehWnd = stableProcess.MainWindowHandle;
                    var foregroundWindow = Win32.GetForegroundWindow();

                    try
                    {
                        if (stablehWnd != IntPtr.Zero)
                        {
                            // Hide stable window so that it mutes itself without stealing focus.
                            Win32.ShowWindow(stablehWnd, Win32.SW_HIDE);
                        }

                        var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.Combine(StablePath, "osu!.exe"),
                            Arguments = $"\"{emptyReplay}\"",
                            UseShellExecute = false,
                        });

                        p?.WaitForExit();

                        Task.Delay(500).Wait(); // wait a bit to ensure stable has loaded the replay
                    }
                    finally
                    {
                        if (stablehWnd != IntPtr.Zero)
                        {
                            Win32.ShowWindow(stablehWnd, Win32.SW_SHOWNA);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to mute stable osu!.");
                }
            }
            finally
            {
                muteInProgress = false;
            }
        });
    }

    private LegacyScoreEncoder createEmptyReplay()
    {
        var scoreInfo = new ScoreInfo
        {
            BeatmapHash = "00000000000000000000000000000000",
            BeatmapInfo = new BeatmapInfo
            {
                // TODO: we may find a better way, like select an existing beatmap from stable
                // Hash for the built-in "circle"
                MD5Hash = "54309531bb7402174969a8d837aead31",
            },
            Date = DateTime.Now,
            OnlineID = -1,
            Rank = ScoreRank.S,
            User = new APIUser
            {
                Id = 0,
                Username = "player",
            },
            TotalScore = 1,
            TotalScoreWithoutMods = 1,
            LegacyOnlineID = 0,
            Ruleset = new RulesetInfo()
            {
                Available = true,
                OnlineID = 0,
                InstantiationInfo = getOsuRulesetInstantiationInfo(),
            }
        };

        var replay = new Replay()
        {
            Frames = new List<ReplayFrame>
            {
                new LegacyReplayFrame(0, null, null, ReplayButtonState.None),
            }
        };

        return new LegacyScoreEncoder(new Score
        {
            ScoreInfo = scoreInfo,
            Replay = replay
        }, null);
    }

    private static string getOsuRulesetInstantiationInfo()
    {
        const string osuRulesetAssemblyQualifiedName = "osu.Game.Rulesets.Osu.OsuRuleset, osu.Game.Rulesets.Osu";

        var type = Type.GetType(osuRulesetAssemblyQualifiedName, throwOnError: false);

        if (type is not null)
            return osuRulesetAssemblyQualifiedName;

        return string.Join(',', (typeof(LegacyOsuRuleset).AssemblyQualifiedName ?? string.Empty).Split(',').Take(2));
    }

    private class LegacyOsuRuleset : Ruleset
    {
        // not used, only for getting the assembly
        public override string Description => string.Empty;
        public override string ShortName => string.Empty;

        public override LegacyMods ConvertToLegacyMods(Mod[] mods)
        {
            return LegacyMods.None;
        }

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        {
            throw new NotImplementedException();
        }

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        {
            throw new NotImplementedException();
        }

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Mod> GetModsFor(ModType type)
        {
            throw new NotImplementedException();
        }
    }

    public void Action()
    {
        if (StablePath is null)
            return;

        var onlineId = Beatmap.Value.BeatmapInfo.OnlineID;

        string protocolLink = $"osu://b/{onlineId}";

        string stableExecutable = Path.Combine(StablePath, "osu!.exe");

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = stableExecutable,
                Arguments = protocolLink,
                UseShellExecute = false,
            });

            postLaunchStable();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open beatmap in stable osu!.");
        }
    }
}
