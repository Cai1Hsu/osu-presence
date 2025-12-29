using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Overlays;
using osu.Game.Rulesets.SteamPresence.Notifications.Windows;
using osu.Game.Screens;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Menu;

namespace osu.Game.Rulesets.SteamPresence;

public partial class OsuHookDrawable : CompositeDrawable
{
    private Ruleset ruleset;

    public OsuHookDrawable(Ruleset ruleset)
    {
        this.ruleset = ruleset;
    }

    [BackgroundDependencyLoader]
    private void load(OsuGame game, IRulesetConfigCache rulesetConfigCache, IBindable<RulesetInfo>? currentRuleset)
    {
        var config = (PresenceConfigManager?)rulesetConfigCache.GetConfigFor(ruleset);

        if (config is null)
        {
            Logger.Log($"config is null", LoggingTarget.Runtime, LogLevel.Important);
            return;
        }

        if (game.Dependencies.TryGet<PresenceProvider>(out _))
            return;

        var autoStart = config.Get<bool>(PresenceRulesetSettings.AutoStart);

        // use the game's scheduler to ensure code executed on the update thread
        var scheduler = GetScheduler(game);

        scheduler.Add(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                Version requiredVersion = new Version(10, 0, 18362, 0);
                Version runningVersion = Environment.OSVersion.Version;

                if (runningVersion >= requiredVersion)
                {
                    game.InjectDependency(out _, () => new WindowsNotifications());
                }
                else
                {
                    Logger.Log($"Windows notifications require at least Windows 10 version 1903 (build 18362). Current version: {runningVersion}", LoggingTarget.Runtime, LogLevel.Important);
                }
            }
            else
            {
                Logger.Log("Notifications are only supported on Windows.", LoggingTarget.Runtime, LogLevel.Important);
            }
        });

        if (autoStart)
        {
            scheduler.Add(() => game.InjectDependency(out _, () => new PresenceProvider()));
        }

        if (currentRuleset is Bindable<RulesetInfo> rulesetBindable)
        {
            // Prevent selecting this ruleset
            rulesetBindable.BindValueChanged(v =>
            {
                if (v.NewValue.Equals(ruleset.RulesetInfo))
                {
                    var disabled = rulesetBindable.Disabled;
                    rulesetBindable.Disabled = false;

                    rulesetBindable.Value = v.OldValue;

                    rulesetBindable.Disabled = disabled;
                }
            });
        }

        scheduler.Add(() =>
        {
            if (game is null)
                return;

            var screenStack = getOsuScreenStack(game);

            void switchAction(IScreen oldScreen, IScreen newScreen)
            {
                if (newScreen is null)
                    return;

                if (newScreen is not Screens.SelectV2.SongSelect songSelect)
                    return;

                var footerContent = getFooterContent(footer);

                if (songSelect.IsLoaded)
                    addButton(songSelect);
                else
                    songSelect.OnLoadComplete += d => addButton((Screens.SelectV2.SongSelect)d);

                void addButton(IScreen screen)
                {
                    if (!screen.IsCurrentScreen())
                        return;

                    if (footerContent.Children.OfType<OpenInStableFooterButton>().Any())
                        return;

                    var button = new OpenInStableFooterButton();
                    button.AppearFromBottom(0);

                    footerContent.Add(button);
                }
            }

            screenStack.ScreenPushed += switchAction;
            screenStack.ScreenExited += switchAction;
        });

        scheduler.Add(() =>
        {
            game.InjectDependency(out openInStableAction, () => new OpenInStableAction());
            game.InjectDependency(out var trackObserver, () => new TrackStateObserver());

            bool requestedMuteStable = false;

            // host.Activated += () => scheduler.Add(() => openInStableAction?.TryMuteStable());
            host.Activated += () => requestedMuteStable = true;
            trackObserver!.OnPlayingStateChanged += playing =>
            {
                if (requestedMuteStable && playing && host.IsActive.Value)
                {
                    openInStableAction?.TryMuteStable();
                    requestedMuteStable = false;
                }
            };
        });


        scheduler.Add(() => performer?.PerformFromScreen(screen =>
        {
            if (screen is MainMenu menu)
            {
                scheduler.Add(() =>
                {
                    try
                    {
                        AddInternal(menu, new MainMenuLogoController());
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Failed to add MainMenuLogoController: {e}", LoggingTarget.Runtime, LogLevel.Error);
                    }
                });
            }
        }, new[] { typeof(MainMenu) }));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddInternal")]
    private extern static void AddInternal(CompositeDrawable composite, Drawable drawable);

    private partial class TrackStateObserver : Drawable
    {
        [Resolved]
        private MusicController musicController { get; set; } = null!;

        private bool lastPlayingState = false;

        public event Action<bool>? OnPlayingStateChanged = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            lastPlayingState = musicController.CurrentTrack.IsRunning;
        }

        protected override void Update()
        {
            base.Update();

            bool playingState = musicController.CurrentTrack.IsRunning;

            if (playingState != lastPlayingState)
            {
                OnPlayingStateChanged?.Invoke(playingState);
            }

            lastPlayingState = playingState;
        }
    }

    [Resolved]
    private IPerformFromScreenRunner? performer { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    private OpenInStableAction? openInStableAction;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ScreenStack")]
    private static extern ref OsuScreenStack getOsuScreenStack(OsuGame game);

    [Resolved]
    private ScreenFooter footer { get; set; } = null!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "buttonsFlow")]
    private extern static ref FillFlowContainer<ScreenFooterButton> getFooterContent(ScreenFooter footer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Scheduler")]
    private extern static Scheduler GetScheduler(Drawable drawable);

    private Drawable? content;

    public Drawable? Content
    {
        get => content;
        set
        {
            if (content == value)
                return;

            content = value;

            if (content is null)
                ClearInternal(false);
            else
                InternalChild = content;
        }
    }
}