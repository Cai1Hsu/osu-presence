using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Online;
using osu.Game.Online.Metadata;
using osu.Game.Online.Multiplayer;
using osu.Game.Users;
using static osu.Game.Users.UserActivity;

namespace osu.Game.Rulesets.SteamPresence;

public partial class PresenceProvider : CompositeDrawable
{
    private SteamConnector steamConnector;

    public SteamConnector SteamConnector => steamConnector;

    public new Scheduler Scheduler => base.Scheduler;

    public PresenceProvider()
    {
        InternalChild = steamConnector = new SteamConnector();
    }

    [BackgroundDependencyLoader]
    private void load(OsuConfigManager config, SessionStatics session)
    {
        userStatus = config.GetBindable<UserStatus>(OsuSetting.UserOnlineStatus);
        userActivity = session.GetBindable<UserActivity?>(Static.UserOnlineActivity);
    }

    [Resolved]
    private MetadataClient? metadata { get; set; } = null!;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private MultiplayerClient multiplayerClient { get; set; } = null!;

    [Resolved]
    private LocalUserStatisticsProvider statisticsProvider { get; set; } = null!;

    private IBindable<UserStatus> userStatus = null!;
    private IBindable<UserActivity?> userActivity = null!;

    protected override bool ShouldBeAlive => true;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => schedulePresenceUpdate());
        userStatus.BindValueChanged(_ => schedulePresenceUpdate());
        userActivity.BindValueChanged(_ => schedulePresenceUpdate());
        userActivity.BindValueChanged(v => lastActivity = v.NewValue);

        multiplayerClient.RoomUpdated += () => schedulePresenceUpdate();
        statisticsProvider.StatisticsUpdated += (_) => schedulePresenceUpdate();

        schedulePresenceUpdate();
    }

    private UserStatus? lastStatus;
    private UserActivity? lastActivity;

    private double lastPresenceUpdateTime;
    private const double presenceUpdateThrottleTime = 500;

    protected override void Update()
    {
        base.Update();

        if (Clock.CurrentTime - lastPresenceUpdateTime < presenceUpdateThrottleTime)
            return;

        lastPresenceUpdateTime = Clock.CurrentTime;

        UserPresence? presence = metadata?.LocalUserPresence;
        UserStatus status = presence?.Status ?? UserStatus.Offline;
        UserActivity? activity = presence?.Activity;

        if (lastStatus != status || lastActivity != activity)
        {
            lastStatus = status;
            lastActivity = activity;

            schedulePresenceUpdate();
        }
    }

    private ScheduledDelegate? presenceUpdateDelegate;

    private void schedulePresenceUpdate()
    {
        presenceUpdateDelegate?.Cancel();
        presenceUpdateDelegate = Scheduler.AddDelayed(() =>
        {
            string presenceString = generatePresenceString();

            try
            {
                steamConnector.SetPresence(presenceString);
            }
            catch(Exception e)
            {
                Logger.Log($"Failed to update Steam presence: {e.Message}", level: LogLevel.Error);
            }
        }, 200);
    }

    private string generatePresenceString()
    {
        if (lastActivity is not null)
        {
            switch (lastActivity)
            {
                case ChoosingBeatmap:
                    return $"Choosing a beatmap";
                case InGame inGame:
                    var rulesetPlayingVerb = inGame.RulesetPlayingVerb;

                    var verbPlaying = inGame switch
                    {
                        InSoloGame => $"Soloing {rulesetPlayingVerb.ToLowerInvariant()}",
                        InMultiplayerGame => $"Multiplaying {rulesetPlayingVerb.ToLowerInvariant()}",
                        InPlaylistGame => $"Playing from playlist {rulesetPlayingVerb.ToLowerInvariant()}",
                        SpectatingMultiplayerGame => $"Spectating multiplayer {rulesetPlayingVerb.ToLowerInvariant()}",
                        PlayingDailyChallenge => $"{rulesetPlayingVerb.ToLowerInvariant()} in daily challenge",
                        _ => $"Playing {rulesetPlayingVerb.ToLowerInvariant()}"
                    };

                    return $"{verbPlaying}: {inGame.BeatmapDisplayTitle}";
                case EditingBeatmap editing:
                    var verbEditing = editing switch
                    {
                        TestingBeatmap => "Testing",
                        ModdingBeatmap => "Modding",
                        _ => "Editing"
                    };
                    return $"{verbEditing} {editing.BeatmapDisplayTitle}";
                case WatchingReplay watching:
                    var verbWatching = watching switch
                    {
                        SpectatingUser su => $"Spectating {su.PlayerName} playing",
                        _ => $"Watching {watching.PlayerName} playing"
                    };
                    return $"{verbWatching} {watching.BeatmapDisplayTitle}";
                case SearchingForLobby:
                    return "Searching for multiplayer lobby";
                case InLobby inLobby:
                    return $"In multiplayer lobby: {inLobby.RoomName}";
                default:
                    return "Unknown";
            }
        }
        else
        {
            return $"Idle";
        }
    }
}