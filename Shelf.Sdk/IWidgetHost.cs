namespace Shelf.Sdk;

public interface IWidgetHost
{
    void RequestSaveStates();
}

public static class WidgetServices
{
    public static IWidgetHost? Host { get; set; }

    public static void RequestSaveStates() => Host?.RequestSaveStates();
}
