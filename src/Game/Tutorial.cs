using System.Collections.Generic;
using Renegade.Ui;

namespace Bridge.Game;

/// <summary>
/// The guided tour behind the Demo button. Bridge has more to learn than most card games, so it
/// goes a step at a time, from who's on your side to how the score works.
/// </summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Who's who",
			"Four players, in two partnerships. You sit at the bottom (South) and your partner Max sits across from you (North). Rosie (West, on your left) and Lena (East, on your right) are partners against you. Partners win and lose together."),
		new TutorialPage("Tricks",
			"All 52 cards are dealt, 13 each. One player leads a card, then each player in turn, clockwise, plays one card: those four cards are a trick. You must play the suit that was led if you have it. The highest card of that suit wins the trick, unless a trump was played."),
		new TutorialPage("Trumps",
			"The auction may choose a trump suit. If you have none of the suit led, you may play a trump, and any trump beats every card of the other suits; if two trumps are played, the higher wins. In a no-trumps contract there are no trumps: the highest card of the suit led always wins."),
		new TutorialPage("Counting your hand",
			"Before you bid, count your high-card points: ace 4, king 3, queen 2, jack 1. There are 40 in the pack, so 10 is an average hand. Long suits are worth something too. About 12 or 13 points is enough to open the bidding, and you and Max together need about 25 for a game."),
		new TutorialPage("The auction",
			"Starting with the dealer, each player in turn makes a call. A bid names a number of tricks over six (1 to 7) and a trump suit, or no trumps: 2♥ means eight tricks with hearts as trumps. Each bid must be higher than the last. Suits rank clubs, diamonds, hearts, spades, then no trumps."),
		new TutorialPage("Passing, doubling, and the end",
			"Instead of bidding you can pass. You can double the other side's bid if you think they'll fail, and they can redouble. Once three players pass in a row, the auction is over: the last bid is the contract, and the side that made it must win that many tricks."),
		new TutorialPage("Using the bidding box",
			"On your turn the bidding box opens. Tap a level (1 to 7), then a suit or NT, to bid; or tap Pass, Double or Redouble. Calls you can't make are greyed out. The box also shows a suggested call from a simple standard system: if you're not sure, take it. Everyone's calls appear in the log at the top."),
		new TutorialPage("Declarer and dummy",
			"On the side that won the auction, the player who first named the contract's suit is the declarer, and their partner is the dummy. The player on the declarer's left leads the first card. Then the dummy's whole hand is laid face up on the table, and the declarer plays it as well as their own."),
		new TutorialPage("When your side declares",
			"If you or Max wins the auction, you play both your hand and his, each in its turn. When it's his turn, his cards come up close to you: tap one to play it. The status line at the bottom always says whose turn it is."),
		new TutorialPage("Defending",
			"If Rosie or Lena wins the auction, you and Max defend: your job is to win enough tricks that they fall short. You can see the dummy's cards too, so use them to work out what to lead."),
		new TutorialPage("Making the contract",
			"Win at least as many tricks as the contract (six plus the level) and your side scores points for each trick bid, plus a bonus for extra tricks. Fall short and the other side scores for every trick you are down."),
		new TutorialPage("Games, slams and the set",
			"A contract worth 100 points or more is a game: 3NT, 4♥, 4♠, 5♣ or 5♦. Games earn a big bonus, and slams (12 or 13 tricks) more still. A set is four deals (Chicago scoring). On some deals a side is vulnerable, which raises both its bonuses and its penalties. The side with more points after four deals wins the set."),
	};
}
