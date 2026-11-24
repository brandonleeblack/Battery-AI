namespace Battery.Tests;

static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    public static string Text(string name) => File.ReadAllText(PathOf(name));
    public static string[] Lines(string name) => File.ReadAllLines(PathOf(name));
}
