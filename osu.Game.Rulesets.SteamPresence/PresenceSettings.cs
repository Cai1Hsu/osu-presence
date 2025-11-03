using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Rulesets.SteamPresence;

public partial class PresenceSettings : RulesetSettingsSubsection
{
    public PresenceSettings(Ruleset ruleset) : base(ruleset)
    {
    }

    protected override LocalisableString Header => "Steam Presence";

    [Resolved]
    private OsuGame game { get; set; } = null!;

    private Bindable<LaunchMode> launchMode = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        var config = (PresenceConfigManager)Config;

        launchMode = config.GetBindable<LaunchMode>(PresenceRulesetSettings.LaunchMode);
        var attemptConnect = config.GetBindable<bool>(PresenceRulesetSettings.RetrySteamConnection);

        Children = new Drawable[]
        {
            new RestartRequiredSetting<LaunchMode>(launchMode)
            {
                Content = new SettingsEnumDropdown<LaunchMode>()
                {
                    LabelText = "Steam presence launch mode",
                    Current = launchMode,
                    Keywords = new[] { "steam", "presence", "status", "enable" },
                }
            },
            new SettingsCheckbox()
            {
                LabelText = "Attempt to connect to Steam if not running",
                Current = attemptConnect,
                Keywords = new[] { "steam", "presence", "status", "connect" },
            },
            tryLaunchButton = new SettingsButton()
            {
                Text = "Launch Steam Presence",
                Action = tryLaunchSteamPresence,
            }
        };

        var isLaunched = IsSteamPresenceLaunched();

        if (isLaunched)
            launched();
    }

    private SettingsButton? tryLaunchButton;

    private void launched()
    {
        if (tryLaunchButton is null)
            return;

        tryLaunchButton.Enabled.Value = false;
        tryLaunchButton.TooltipText = "Steam presence has already launched";
    }

    private void tryLaunchSteamPresence()
    {
        game.Add(new PresenceProvider());

        if (launchMode.Value == LaunchMode.Off)
        {
            launchMode.Value = LaunchMode.Manual;
        }

        launched();
    }

    private bool launchedCache = false;

    private bool IsSteamPresenceLaunched()
    {
        return !launchedCache ? launchedCache = game.ChildrenOfType<PresenceProvider>().Any() : launchedCache;
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
}
