using ClaudeTracker.Utilities;
using Xunit;

namespace ClaudeTracker.Tests;

public class FunnyNameGeneratorTests
{
    [Fact]
    public void GetRandomName_NoExclusions_ReturnsName()
    {
        var name = FunnyNameGenerator.GetRandomName();
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void GetRandomName_WithExclusions_ExcludesNames()
    {
        var excluded = new[] { "Quantum Llama", "Sneaky Penguin" };
        for (int i = 0; i < 100; i++)
        {
            var name = FunnyNameGenerator.GetRandomName(excluded);
            Assert.DoesNotContain(name, excluded);
        }
    }

    [Fact]
    public void GetRandomName_AllExcluded_ReturnsFallback()
    {
        // Exclude all 30 names - should get fallback format
        var allNames = new[]
        {
            "Quantum Llama", "Sneaky Penguin", "Turbo Sloth", "Cosmic Cat",
            "Digital Dragon", "Ninja Narwhal", "Pixel Panda", "Rocket Raccoon",
            "Thunder Turtle", "Wizard Wombat", "Electric Eel", "Funky Falcon",
            "Galaxy Gopher", "Happy Hippo", "Jazzy Jaguar", "Laser Lemur",
            "Mystic Moose", "Neon Newt", "Psychic Puffin", "Quirky Quokka",
            "Rainbow Rhino", "Stellar Seahorse", "Techno Tiger", "Ultra Unicorn",
            "Vibrant Viper", "Wild Walrus", "Xenon Xerus", "Yolo Yak",
            "Zippy Zebra", "Awesome Axolotl"
        };

        var name = FunnyNameGenerator.GetRandomName(allNames);
        Assert.StartsWith("Profile ", name);
    }
}
