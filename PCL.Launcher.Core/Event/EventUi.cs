namespace PCL;

/// <summary>自定义事件涉及页面/工具的操作。</summary>
public interface IEventUi
{
    bool RefreshPage();
    void SwitchPage(string pageType, string? subType);
    void OpenHelp(string location);
}

public static class EventUi
{
    public static IEventUi? Current { get; set; }

    public static bool RefreshPage()
    {
        return Current?.RefreshPage() ?? false;
    }

    public static void SwitchPage(string pageType, string? subType)
    {
        Current?.SwitchPage(pageType, subType);
    }

    public static void OpenHelp(string location)
    {
        Current?.OpenHelp(location);
    }
}