namespace ClaudeTracker.Utilities;

/// <summary>Generates whimsical profile names like "Quantum Llama" or "Ninja Narwhal".</summary>
public static class FunnyNameGenerator
{
    private static readonly string[] Names =
    [
        "Quantum Llama", "Sneaky Penguin", "Turbo Sloth", "Cosmic Cat",
        "Digital Dragon", "Ninja Narwhal", "Pixel Panda", "Rocket Raccoon",
        "Thunder Turtle", "Wizard Wombat", "Electric Eel", "Funky Falcon",
        "Galaxy Gopher", "Happy Hippo", "Jazzy Jaguar", "Laser Lemur",
        "Mystic Moose", "Neon Newt", "Psychic Puffin", "Quirky Quokka",
        "Rainbow Rhino", "Stellar Seahorse", "Techno Tiger", "Ultra Unicorn",
        "Vibrant Viper", "Wild Walrus", "Xenon Xerus", "Yolo Yak",
        "Zippy Zebra", "Awesome Axolotl"
    ];

    private static readonly Random _random = new();

    /// <summary>Returns a random unused name, or a numeric fallback if all names are taken.</summary>
    public static string GetRandomName(IEnumerable<string>? usedNames = null)
    {
        var excluded = usedNames?.ToHashSet() ?? [];
        var available = Names.Where(n => !excluded.Contains(n)).ToArray();
        return available.Length > 0
            ? available[_random.Next(available.Length)]
            : $"Profile {_random.Next(1000, 9999)}";
    }
}
