using System.Diagnostics;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.SteamPresence;

public partial class PresenceSettings : RulesetSettingsSubsection
{
    public PresenceSettings(Ruleset ruleset) : base(ruleset)
    {
    }

    protected override LocalisableString Header => "Steam Presence";

    [Resolved]
    private OsuGame game { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    private Bindable<bool> autoStart = null!;

    [BackgroundDependencyLoader]
    private void load(PresenceProvider? presenceProvider)
    {
        var config = (PresenceConfigManager)Config;

        autoStart = config.GetBindable<bool>(PresenceRulesetSettings.AutoStart);
        var attemptConnect = config.GetBindable<bool>(PresenceRulesetSettings.RetrySteamConnection);

        Children = new Drawable[]
        {
            new RestartRequiredSetting<bool>(autoStart)
            {
                Content = new SettingsCheckbox()
                {
                    LabelText = "Start Steam Presence on game launch",
                    Current = autoStart,
                    Keywords = new[] { "steam", "presence", "status", "enable" },
                }
            },
            new SettingsCheckbox()
            {
                LabelText = "Try to connect to Steam occasionally if connection fails",
                Current = attemptConnect,
                Keywords = new[] { "steam", "presence", "status", "connect" },
            },
            tryLaunchButton = new TryLaunchButton()
            {
                Text = "Launch Steam Presence",
                Action = tryLaunchSteamPresence,
                Keywords = new[] { "steam", "presence", "status", "launch" },
            },
            new SettingsButton()
            {
                Text = "Unlock frame rate limit",
                Action = () =>
                {
                    try
                    {
                        host.AllowBenchmarkUnlimitedFrames = true;
                        typeof(GameHost).GetMethod("updateFrameSyncMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                            .Invoke(host, null);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Failed to unlock frame rate limit: {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
                    }
                }
            }
        };

        this.presenceProvider = presenceProvider;
        var steamConnector = presenceProvider?.SteamConnector;

        tryLaunchButton.LaunchState.Value = steamConnector switch
        {
            null => LaunchStates.Off,
            _ when steamConnector.IsInitialized => LaunchStates.Connected,
            _ => LaunchStates.Connecting,
        };
    }

    private TryLaunchButton tryLaunchButton = null!;

    private PresenceProvider? presenceProvider;

    private void tryLaunchSteamPresence()
    {
        Debug.Assert(presenceProvider is null);

        game.InjectDependency(out presenceProvider, () => new PresenceProvider());

        Debug.Assert(presenceProvider is not null);

        presenceProvider?.Scheduler.Add(() =>
        {
            if (presenceProvider.SteamConnector.IsInitialized)
            {
                tryLaunchButton.LaunchState.Value = LaunchStates.Connected;
            }
            else
            {
                tryLaunchButton.LaunchState.Value = LaunchStates.Connecting;
                Logger.Log("Could not connect to Steam, is Steam running?", LoggingTarget.Information, LogLevel.Important);
            }
        });
    }

    private partial class RestartRequiredSetting<T> : CompositeDrawable
    {
        private T initialValue = default!;
        private IBindable<T> bindable = null!;

        public SettingsItem<T> Content { get; set; } = null!;

        public RestartRequiredSetting(IBindable<T> bindable)
        {
            this.bindable = bindable;
            this.initialValue = bindable.Value;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Debug.Assert(Content is not null);

            InternalChild = Content;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            bindable.BindValueChanged(_ =>
            {
                var isInitial = EqualityComparer<T>.Default.Equals(bindable.Value, initialValue);

                if (isInitial)
                {
                    Content.ClearNoticeText();
                    return;
                }

                dialogOverlay?.Push(new ConfirmDialog("Change this setting requires a restart. \nWould you like to restart now?", attemptRestart, () =>
                {
                    Content.SetNoticeText("Restart required to apply changes.", true);
                }));
            });
        }

        private void attemptRestart()
        {
            game?.RestartAppWhenExited();
            game?.AttemptExit();
        }
    }

    private partial class TryLaunchButton : SettingsButton
    {
        public Bindable<LaunchStates> LaunchState { get; set; } = new Bindable<LaunchStates>(LaunchStates.Off);

        public TryLaunchButton()
        {
            LaunchState.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            switch (LaunchState.Value)
            {
                case LaunchStates.Off:
                    Text = "Launch Steam Presence";
                    Enabled.Value = true;
                    TooltipText = "Steam Presence is not running.";
                    break;
                case LaunchStates.Connecting:
                    Text = "Try to connect to Steam";
                    Enabled.Value = true;
                    TooltipText = "Steam Presence is launched but not connected to Steam.";
                    break;
                case LaunchStates.Connected:
                    Text = "Steam Presence Launched";
                    Enabled.Value = false;
                    TooltipText = "Steam Presence is currently running.";
                    break;
            }

            SpriteText.FlashColour(LaunchState.Value switch
            {
                LaunchStates.Off => Colour4.Red,
                LaunchStates.Connecting => Colour4.Orange,
                LaunchStates.Connected => Colour4.Green,
                _ => Colour4.White,
            }, 500);
        }
    }
}
