using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI.Notifications;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Notifications;
using osu.Game.Users.Drawables;
using System.Globalization;
using osu.Game.Overlays;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Game.Online.Multiplayer;
using osu.Framework.Platform;
using osu.Game.Online.Chat;

using OsuNotification = osu.Game.Overlays.Notifications.Notification;
using NotificationData = Windows.UI.Notifications.NotificationData;
using Windows.UI.Notifications;
using osu.Framework.Configuration;
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.SteamPresence.Notifications.Windows;

public partial class WindowsNotifications : Drawable
{
    private const string toast_group = "osu-lazer-presence";

    private const double defaultToastLifetime = 600;

    private readonly Dictionary<Guid, WindowsToast> notifications = new();
    private readonly Dictionary<OsuNotification, Guid> notificationLookup = new();
    private readonly Dictionary<Guid, Action> activationLookup = new();
    
    private DefaultToastLifecycleManager defaultToastManager = null!;

    private class ToastProperty(ToastContentBuilder builder)
    {
        public ToastContentBuilder Builder { get; init; } = builder;
        public Guid BodyId { get; set; } = Guid.NewGuid();
        public bool KeepOnScreen { get; set; }
        public List<Guid> ActionIds { get; } = new();
    }

    private void RegisterAction(ToastProperty prop, Guid actionId, Action action)
    {
        prop.ActionIds.Add(actionId);
        activationLookup[actionId] = action;
    }

    public async Task OnNotification(OsuNotification notification)
    {
        ToastProperty prop = new ToastProperty(new ToastContentBuilder());

        var toastInfo = new WindowsToast(prop, notification);

        notifications[prop.BodyId] = toastInfo;
        notificationLookup[notification] = prop.BodyId;

        RegisterAction(prop, prop.BodyId, () =>
        {
            notification.Activated?.Invoke();
        });

        prop.Builder.AddText(localisation.GetLocalisedString(notification.Text) ?? notification.Text.ToString());

        var notificationType = notification.GetType();
        var notificationTypeName = notificationType.FullName;

        if (notification.IsImportant
            && !notification.Transient
            // We assume pure simplenotifications are all not so important
            && notificationType != typeof(SimpleNotification))
            prop.Builder.SetToastScenario(ToastScenario.Reminder);

        if (notification is UserAvatarNotification userAvatarNotification)
            await OnUserAvatarNotification(prop, userAvatarNotification);

        if (notification is ProgressNotification progressNotification)
            OnProgressNotification(prop, progressNotification);

        if (notificationTypeName == $"{typeof(MultiplayerClient).FullName}+MultiplayerInvitationNotification")
            OnMultiplayerInvitationNotification(prop, notification);

        if (notificationTypeName == $"{typeof(MessageNotifier).FullName}+PrivateMessageNotification")
            OnPrivateMessageNotification(prop, notification);

        if (notificationTypeName == $"{typeof(ChannelManager).FullName}+MentionNotification")
            OnMentionNotification(prop, notification);

        prop.Builder
            .SetToastDuration(ToastDuration.Short)
            .AddArgument("id", prop.BodyId.ToString());

        NotificationData? initialData = null;

        if (notification is ProgressNotification progress)
        {
            toastInfo.Content = prop.Builder.Content;
            toastInfo.LastProgress = double.NaN;
            toastInfo.LastState = progress.State;
            toastInfo.SequenceNumber = 0;

            initialData = createProgressData(toastInfo, progress, toastInfo.SequenceNumber);
        }

        var xml = prop.Builder.GetXml();
        bool isDefaultScenario = prop.Builder.Content.Scenario == ToastScenario.Default;

        toastInfo.IsDefaultScenario = isDefaultScenario;

        void show()
        {
            var toastNotification = new ToastNotification(xml);
            toastNotification.Tag = toastInfo.Tag;
            toastNotification.Group = toast_group;

            if (initialData is not null)
                toastNotification.Data = initialData;

            void expire(ToastNotification t) => Scheduler.Add(() =>
            {
                removeToast(notification);
                removeToast(prop.BodyId); // in case notification reference was lost
            });

            toastNotification.Activated += (t, _) =>
            {
                if (prop.KeepOnScreen)
                    show(); // re-show the toast if it was meant to be kept on screen
                else
                    expire(t);
            };

            toastNotification.Dismissed += (t, e) =>
            {
                switch (e.Reason)
                {
                    case ToastDismissalReason.UserCanceled:
                        prop.KeepOnScreen = false;
                        expire(t);
                        break;

                    default:
                        break;
                }
            };

            toastNotification.ExpiresOnReboot = false;

            toastNotification.Failed += (t, _) => expire(t);

            void display()
            {
                toastNotification.SuppressPopup = windowMode.Value switch
                {
                    WindowMode.Fullscreen => true,
                    WindowMode.Borderless when host.IsActive.Value => true,
                    _ => false,
                };

                notifier?.Show(toastNotification);
            }

            scheduleToastDisplay(toastInfo, toastNotification, display);
        }

        show();
    }

    private record struct ReflectionKey(Type type, string fieldName);

    private readonly Dictionary<ReflectionKey, FieldInfo?> fieldInfoCache = new();

    private (Message?, Channel?) GetMessageAndChannel(OsuNotification notification)
    {
        Message? message;
        Channel? channel;

        var type = notification.GetType();

        FieldInfo? getFieldInfo(ReflectionKey key)
        {
            if (!fieldInfoCache.TryGetValue(key, out var fieldInfo))
            {
                fieldInfo = type.GetField(key.fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                fieldInfoCache[key] = fieldInfo;
            }

            return fieldInfo;
        }

        var messageFieldKey = new ReflectionKey(type, "message");
        var channelFieldKey = new ReflectionKey(type, "channel");

        message = getFieldInfo(messageFieldKey)?.GetValue(notification) as Message;
        channel = getFieldInfo(channelFieldKey)?.GetValue(notification) as Channel;

        return (message, channel);
    }

    private void OnMentionNotification(ToastProperty prop, OsuNotification notification)
    {
        var (message, channel) = GetMessageAndChannel(notification);

        if (message is null || channel is null)
            return;

        prop.Builder
            .AddText(message.Content)
            .AddAttributionText($"{message.Sender.Username} mentioned you in {channel.Name}")
            .SetToastScenario(ToastScenario.Default);
    }

    private void OnPrivateMessageNotification(ToastProperty prop, OsuNotification notification)
    {
        var (message, _) = GetMessageAndChannel(notification);

        if (message is null)
            return;

        prop.Builder
            .AddText(message.Content)
            .AddAttributionText($"Message from {message.Sender.Username}")
            .SetToastScenario(ToastScenario.Default);
    }

    private void OnMultiplayerInvitationNotification(ToastProperty prop, OsuNotification notification)
    {
        var password = TryFindPasswordFromDelegate(notification.Activated);

        if (!string.IsNullOrEmpty(password))
        {
            var copyActionId = Guid.NewGuid();

            prop.Builder.AddAttributionText($"Password: {password}")
                .AddButton(new ToastButton()
                    .SetContent("Copy Password")
                    .AddArgument("id", copyActionId.ToString())
                    .SetBackgroundActivation())
                .AddButton(new ToastButton()
                    .SetContent("Join")
                    // use the body's activation
                    .AddArgument("id", prop.BodyId.ToString())
                    .SetBackgroundActivation())
                .AddButton(new ToastButtonDismiss());

            RegisterAction(prop, copyActionId, () => clipboard.SetText(password));
            prop.KeepOnScreen = true;
        }

        prop.Builder.SetToastScenario(ToastScenario.Reminder);
    }

    private string? TryFindPasswordFromDelegate(Delegate? del)
    {
        if (del is null)
            return null;

        var invokations = del.GetInvocationList();

        foreach (var invocation in invokations)
        {
            var target = invocation.Target;

            string? password = target?.GetType().GetField("password",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?
                .GetValue(target) as string;

            if (password is not null)
                return password;
        }

        var firstInvokation = invokations.FirstOrDefault()?.Target;

        if (firstInvokation is null)
            return null;

        return firstInvokation.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => f.GetValue(firstInvokation) as string)
            .FirstOrDefault();
    }

    private void OnProgressNotification(ToastProperty toast, ProgressNotification _progressNotification)
    {
        toast.Builder.AddProgressBar()
            .AddButton(new ToastButtonDismiss("Hide"));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Avatar")]
    private static extern DrawableAvatar GetDrawableAvatar(UserAvatarNotification notification);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "user")]
    private static extern ref osu.Game.Users.IUser GetIUser(DrawableAvatar notification);

    private HttpClient httpClient = new();

    private HashSet<int> DownloadedAvatars = new();

    private async Task OnUserAvatarNotification(ToastProperty prop, UserAvatarNotification notification)
    {
        var avatar = GetDrawableAvatar(notification);
        var user = GetIUser(avatar);

        string avatarUrl = user is null ?
            @"https://a.ppy.sh/" :
            (user as APIUser)?.AvatarUrl ?? $@"https://a.ppy.sh/{user.OnlineID}";

        // Download the avatar in temp folder to work around Windows toast image requirements.
        var tempFolder = Path.Combine(Path.GetTempPath(), "osu", "avatars");
        Directory.CreateDirectory(tempFolder);

        var id = user?.OnlineID.ToString() ?? "default";

        var avatarFileName = Path.Combine(tempFolder, $"{id}.png");

        if (id != "default"
            || !File.Exists(avatarFileName)
            || !DownloadedAvatars.Contains(user?.OnlineID ?? -1))
        {
            using var response = await httpClient.GetAsync(avatarUrl);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var fs = new FileStream(avatarFileName, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    await contentStream.CopyToAsync(fs);

                    DownloadedAvatars.Add(user?.OnlineID ?? -1);
                }
                catch (Exception)
                {
                    avatarFileName = Path.Combine(tempFolder, "default.png");
                }
            }
            else
            {
                avatarFileName = Path.Combine(tempFolder, "default.png");
            }
        }

        if (!File.Exists(avatarFileName))
            return;

        prop.Builder.AddAppLogoOverride(new Uri(avatarFileName), ToastGenericAppLogoCrop.Circle);
    }

    [Resolved]
    private Clipboard clipboard { get; set; } = null!;

    [Resolved]
    private INotificationOverlay notificationOverlay { get; set; } = null!;

    private IBindable<int> unreadCount = null!;

    private ToastNotifierCompat? notifier = null;

    private Bindable<WindowMode> windowMode = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private LocalisationManager localisation { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager config)
    {
        windowMode = config.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ToastNotificationManagerCompat.OnActivated += OnActivated;
        notifier = ToastNotificationManagerCompat.CreateToastNotifier();

        defaultToastManager = new DefaultToastLifecycleManager(this, notifier, defaultToastLifetime);

        unreadCount = notificationOverlay.UnreadCount.GetBoundCopy();

        unreadCount.BindValueChanged(v => MaybeNewNotificationsPosted(), true);
    }

    private HashSet<OsuNotification> frontNotifications = new();
    private HashSet<OsuNotification> backNotifications = new();

    private void MaybeNewNotificationsPosted()
    {
        backNotifications.Clear();
        backNotifications.UnionWith(frontNotifications);

        frontNotifications.Clear();
        frontNotifications.UnionWith(notificationOverlay.AllNotifications);

        // be aware of order!
        foreach (var notification in notificationOverlay.AllNotifications)
        {
            if (backNotifications.Contains(notification))
                continue;

            if (notificationLookup.ContainsKey(notification))
                continue;

            OnNotification(notification).FireAndForget();
        }

        foreach (var notification in backNotifications)
        {
            if (frontNotifications.Contains(notification))
                continue;

            removeToast(notification);
        }
    }

    protected override void Update()
    {
        base.Update();

        defaultToastManager.Process();

        foreach (var toast in notifications.Values)
        {
            if (toast.Notification is not ProgressNotification progressNotification)
                continue;

            if (toast.Content is null)
                continue;

            var progressValue = clampProgress(progressNotification.Progress);
            var state = progressNotification.State;

            var requiresUpdate = toast.LastProgress is null
                                  || double.IsNaN(toast.LastProgress.Value)
                                  || !areClose(toast.LastProgress.Value, progressValue)
                                  || toast.LastState != state;

            if (!requiresUpdate)
            {
                if (!progressNotification.Ongoing)
                    finalizeProgressToast(toast);

                continue;
            }

            toast.SequenceNumber++;
            toast.LastProgress = progressValue;
            toast.LastState = state;

            var data = createProgressData(toast, progressNotification, toast.SequenceNumber);

            notifier?.Update(data, toast.Tag, toast_group);

            if (!progressNotification.Ongoing)
                finalizeProgressToast(toast);
        }
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat arg)
    {
        ToastArguments args = ToastArguments.Parse(arg.Argument);

        if (!args.TryGetValue("id", out var idValue))
            return;

        if (!Guid.TryParse(idValue, out var id))
            return;

        if (!activationLookup.TryGetValue(id, out var action))
            return;

        // Let's just assume all actions must be run on the update thread.
        Scheduler.Add(action);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (isDisposing)
            ToastNotificationManagerCompat.OnActivated -= OnActivated;
    }

    private void finalizeProgressToast(WindowsToast toast)
    {
        removeToast(toast.Id);
    }

    private void removeToast(Guid id)
    {
        if (!notifications.Remove(id, out var toast))
            return;

        notificationLookup.Remove(toast.Notification);
        ToastNotificationManagerCompat.History.Remove(toast.Tag, toast_group);

        foreach (var actionId in toast.Property.ActionIds)
            activationLookup.Remove(actionId);

        if (toast.IsDefaultScenario)
            defaultToastManager.OnToastRemoved(id);
    }

    private void removeToast(OsuNotification notification)
    {
        if (!notificationLookup.TryGetValue(notification, out var id))
            return;

        removeToast(id);
    }

    private NotificationData createProgressData(WindowsToast toast, ProgressNotification progressNotification, uint sequence)
    {
        var progressValue = clampProgress(progressNotification.Progress);
        var status = getProgressStatus(progressNotification.State);
        var valueString = string.Format(CultureInfo.CurrentCulture, "{0:P0}", progressValue);

        return ToastContentBuilder.CreateProgressBarData(
            toast.Content!,
            title: string.Empty, // already contained in the toast
            value: progressValue,
            valueStringOverride: valueString,
            status: status,
            sequence: sequence);
    }

    private static double clampProgress(float value) => Math.Clamp(value, 0f, 1f);

    private static bool areClose(double left, double right) => Math.Abs(left - right) <= 0.001;

    private static string getProgressStatus(ProgressNotificationState state) => state switch
    {
        ProgressNotificationState.Queued => "Queued",
        ProgressNotificationState.Active => "In progress",
        ProgressNotificationState.Completed => "Completed",
        ProgressNotificationState.Cancelled => "Cancelled",
        _ => string.Empty
    };

    private void scheduleToastDisplay(WindowsToast toast, ToastNotification toastNotification, Action display)
    {
        if (!toast.IsDefaultScenario)
        {
            Scheduler.Add(display);
            return;
        }

        var context = new DefaultToastContext(toast.Id, toastNotification, display);
        defaultToastManager.Enqueue(context);
    }

    private sealed class WindowsToast
    {
        public WindowsToast(ToastProperty prop, OsuNotification notification)
        {
            Property = prop;
            Notification = notification;
            Tag = prop.BodyId.ToString();
        }

        public Guid Id => Property.BodyId;

        public string Tag { get; }

        public OsuNotification Notification { get; }

        public ToastContent? Content { get; set; }

        public uint SequenceNumber { get; set; }

        public double? LastProgress { get; set; }

        public ProgressNotificationState? LastState { get; set; }

        public ToastProperty Property { get; set; }

        public bool IsDefaultScenario { get; set; }
    }

    private sealed class DefaultToastContext
    {
        private readonly Action display;

        public DefaultToastContext(Guid id, ToastNotification toastNotification, Action display)
        {
            Id = id;
            ToastNotification = toastNotification;
            this.display = display;
        }

        public Guid Id { get; }

        public ToastNotification ToastNotification { get; }

        public void Display()
        {
            display();
        }
    }

    private sealed class DefaultToastLifecycleManager
    {
        private readonly WindowsNotifications owner;
        private readonly ToastNotifierCompat? notifier;
        private readonly double defaultLifetime;
        private readonly Queue<DefaultToastContext> queue = new();

        private DefaultToastContext? active;
        private double activeShownAt;

        public DefaultToastLifecycleManager(WindowsNotifications owner, ToastNotifierCompat? notifier, double lifetime)
        {
            this.owner = owner;
            this.notifier = notifier;
            this.defaultLifetime = lifetime;
        }

        public void Enqueue(DefaultToastContext context)
        {
            owner.Scheduler.Add(() => enqueueInternal(context));
        }

        public void OnToastRemoved(Guid toastId)
        {
            handleRemoval(toastId);
        }

        public void Process()
        {
            var elapsed = owner.Clock.CurrentTime - activeShownAt;

            if (active is null || (queue.Count > 0 && elapsed >= defaultLifetime))
            {
                if (!hideActive())
                    // wait a frame to ensure previous expiration is processed
                    activateNext();
            }
        }

        private void enqueueInternal(DefaultToastContext context)
        {
            if (active is null)
            {
                activate(context);
                return;
            }

            queue.Enqueue(context);
        }

        private void handleRemoval(Guid toastId)
        {
            if (active?.Id == toastId)
            {
                active = null;
                activateNext();
                return;
            }

            removeFromQueue(toastId);
        }

        private void activateNext()
        {
            if (queue.Count == 0)
                return;

            var next = queue.Dequeue();
            activate(next);
        }

        private void activate(DefaultToastContext context)
        {
            active = context;
            var now = owner.Clock.CurrentTime;
            activeShownAt = now;

            context.Display();
        }

        private bool hideActive()
        {
            if (active is null)
                return false;

            var current = active;

            try
            {
                // make a copy and send it to notification center without popup
                var shadow = new ToastNotification(current.ToastNotification.Content)
                {
                    Tag = Guid.NewGuid().ToString(), // prevent re-hiding
                    Group = current.ToastNotification.Group,
                    SuppressPopup = true,
                    Priority = ToastNotificationPriority.Default,
                    ExpiresOnReboot = false,
                    Data = current.ToastNotification.Data,
                };

                notifier?.Hide(current.ToastNotification);

                // Although the document says Hide method sends the toast to notification center,
                // in practice it just removes the toast.
                // So we send a shadow toast to notification center to simulate the expected behavior.
                notifier?.Show(shadow);
            }
            catch (Exception) { }

            active = null;

            return true;
        }

        private void removeFromQueue(Guid toastId)
        {
            if (queue.Count == 0)
                return;

            int count = queue.Count;
            var retained = new Queue<DefaultToastContext>(count);

            while (queue.Count > 0)
            {
                var context = queue.Dequeue();

                if (context.Id == toastId)
                    continue;

                retained.Enqueue(context);
            }

            while (retained.Count > 0)
                queue.Enqueue(retained.Dequeue());
        }
    }
}
