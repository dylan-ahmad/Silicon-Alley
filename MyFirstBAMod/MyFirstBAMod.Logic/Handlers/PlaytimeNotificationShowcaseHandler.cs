using UI.Notification;

namespace MyFirstBAMod.Logic;

public class PlaytimeNotificationShowcaseHandler
{
    private int _inGameHoursSinceSessionStart;

    public void Start()
    {
        _inGameHoursSinceSessionStart = 0;
        GlobalEvents.onNewHour += OnNewHour;
    }

    public void Stop()
    {
        GlobalEvents.onNewHour -= OnNewHour;
    }

    private void OnNewHour()
    {
        _inGameHoursSinceSessionStart++;
        var notificationData = new Dictionary<string, string>
            { { "toTime", _inGameHoursSinceSessionStart.ToString() } };
        Notifications.Show(
            NotificationType.Info,
            "myfirstmod_notification_played_for_hours", notificationData
        );
    }
}