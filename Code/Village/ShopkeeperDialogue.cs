namespace VoidMage.Village;

/// <summary>One clip in a run of clips the shopkeeper plays: a one-shot, or a loop held for some seconds.</summary>
/// <param name="HoldMax">Above Hold: the loop is held a random time between the two, rolled each time it plays.</param>
/// <param name="Locked">A change of posture (on and off the boxes): never cut short, never dropped from the queue.</param>
public readonly record struct KeeperStep( string Clip, bool Loop = false, float Hold = 0f, float HoldMax = 0f, bool Locked = false );

/// <summary>Something the player can say to the shopkeeper, what he answers and what his body does meanwhile.</summary>
public sealed record KeeperTopic( string Ask, string Answer, KeeperStep[] Reaction );

/// <summary>
/// What John the shopkeeper has to say (user, 2026-09-18). He has no idea anything is wrong with him or the village.
/// Clip names are the elkdemon's sequences (Assets/Animations/PsxHorror/Elkdemon).
/// </summary>
public static class ShopkeeperDialogue
{
	public const string Name = "John";
	public const string Role = "Shopkeeper";
	public const string Greeting = "Hey, how you doin'?";
	public const string Leave = "Never mind.";

	public static readonly KeeperTopic[] Topics =
	{
		new( "John, what happened to you?!",
			"Gee, thanks. Do I look that bad today?",
			new KeeperStep[] { new( "inspect_feet" ) } ),

		new( "What is going on?!",
			"Uhhh... Nothing? What's wrong?",
			new KeeperStep[] { new( "headnod_greet" ) } ),

		new( "You look like... Am I tripping?",
			"I don't know, are you? Are you gonna buy something or just be rude?",
			new KeeperStep[] { new( "handsonhips_grumpy_enter" ), new( "handsonhips_grumpy_loop", Loop: true, Hold: 2f, HoldMax: 3f ), new( "handsonhips_grumpy_exit" ) } ),

		new( "Why are you selling at night?",
			"It's a sunny day as any other. Did you get enough sleep?...",
			new KeeperStep[] { new( "look_up" ) } ),

		new( "There is a mysterious portal at the campfire.",
			"Portal? You're definitely trippin', bro...",
			new KeeperStep[] { new( "look_l" ), new( "headshake_disappointed" ) } ),

		new( "Shards? Since when do you not take gold anymore?",
			"Gold? I have never taken that useless metal. Are you trying to scam me?",
			new KeeperStep[] { new( "thoughtful_l_enter" ), new( "thoughtful_l_chinscratch" ), new( "thoughtful_l_exit" ) } ),

		new( "What are these scrolls?",
			"Oh, these are newly imported enchantments. Why don't you try some on that staff of yours?",
			new KeeperStep[] { new( "pointhand_index_f_small" ) } ),
	};
}
