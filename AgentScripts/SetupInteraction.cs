using SnakeCatcher.Editor;

public static class SetupInteraction
{
    public static string Run()
    {
        PlayerInteractionSetup.Configure();
        return "Interaction detector wired to player prefab and playground; practice marker added.";
    }
}
