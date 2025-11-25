using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Rulesets.SteamPresence.Notifications.Windows;

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
    }

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