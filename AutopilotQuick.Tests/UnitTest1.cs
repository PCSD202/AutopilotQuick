namespace AutopilotQuick.Tests;

public class Tests
{
    public static App _App = new App();
    public static MainWindow window;
    public static UserDataContext Context;
    [SetUp]
    public void Setup()
    {
        _App.Run();
        window = _App.MainWindow as MainWindow ?? throw new InvalidOperationException();
        Context = window.DataContext as UserDataContext ?? throw new InvalidOperationException();
    }

    [Test, RequiresSTA]
    public void Test1()
    {
        var testCacher = new Cacher("https://www.google.com", "google.html", Context);
        Assert.That(testCacher.IsUpToDate, Is.EqualTo(false));
    }
}