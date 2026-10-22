using System.Collections.Generic;
using Renegade.Ui;

namespace Bridge.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Partners and tricks",
			"You (South) play with Max (North) against Rosie and Lena. Each deal, the side that wins the auction tries to take the number of tricks it bid: six, plus the level of the bid."),
		new TutorialPage("The auction",
			"Each player in turn bids a level (1 to 7) and a suit or no trumps, higher than the last bid, or passes. Use the bidding box; the suggested call follows a simple standard system. Count your points: ace 4, king 3, queen 2, jack 1."),
		new TutorialPage("The play",
			"The player left of the declarer leads, then the declarer's partner lays their hand face up: the dummy. The declarer plays both hands. Follow suit if you can; trumps beat everything else. When your side declares, you play both your hands."),
		new TutorialPage("Chicago scoring",
			"A set is four deals. Make your contract to score; game contracts (3NT, 4♥, 4♠, 5♣, 5♦) earn a big bonus, slams more. Go down and the other side scores. Being vulnerable raises the stakes."),
	};
}
