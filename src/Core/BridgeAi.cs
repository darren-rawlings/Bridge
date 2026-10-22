using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;

namespace Bridge.Core;

/// <summary>
/// A computer bridge player (and the bid suggestions shown to you).
/// Bidding is a simple standard system: count high-card points (A 4, K 3, Q 2, J 1); open 1NT on
/// 15–17 balanced, 2NT on 20–21, 2♣ on 22+, otherwise one of your longest suit with 12+ (five
/// for a major); respond and rebid by estimating partner's strength and suits from their bids,
/// choosing the best fit (an eight-card major, else no trumps, else a minor) and bidding to the
/// level the combined points support (26 for game, 33 for a small slam, 37 for a grand).
/// Overcalls need a five-card suit and 8–16 points. Play: Easy is often careless, Medium plays
/// sound card-by-card (second hand low, third hand high, win cheaply, ruff when it helps), and
/// Hard looks ahead over many guessed deals.
/// </summary>
public sealed class BridgeAi
{
	private readonly Random _random;

	public BridgeAi(int seat, Difficulty difficulty, Random random)
	{
		Seat = seat;
		Difficulty = difficulty;
		_random = random;
	}

	public int Seat { get; }
	public Difficulty Difficulty { get; }

	// ------------------------------------------------------------------ Hand evaluation

	public static int Hcp(IEnumerable<Card> hand) => hand.Sum(c => c.Rank switch { Card.Ace => 4, Card.King => 3, Card.Queen => 2, Card.Jack => 1, _ => 0 });

	public static int Length(IEnumerable<Card> hand, Suit suit) => hand.Count(c => c.Suit == suit);

	public static bool Balanced(IReadOnlyList<Card> hand)
	{
		var lengths = Enum.GetValues<Suit>().Select(s => Length(hand, s)).ToList();
		return lengths.Min() >= 2 && lengths.Count(l => l == 2) <= 1;
	}

	private static Suit SuitOf(Strain strain) => Call.Trump(strain)!.Value;
	private static Strain StrainOf(Suit suit) => suit switch { Suit.Clubs => Strain.Clubs, Suit.Diamonds => Strain.Diamonds, Suit.Hearts => Strain.Hearts, _ => Strain.Spades };
	private static bool Major(Strain s) => s is Strain.Hearts or Strain.Spades;

	/// <summary>What a partner's calls have shown: a points estimate and the least length in each suit.</summary>
	private sealed record Picture(double Points, Dictionary<Suit, int> Lengths, bool Balanced, bool Shown);

	private static Picture Read(Auction auction, int seat)
	{
		var lengths = Enum.GetValues<Suit>().ToDictionary(s => s, _ => 0);
		double points = 0;
		bool balanced = false, shown = false;
		int partnerOf = (seat + 2) % 4;
		int index = 0;
		bool anyBidBefore = false;
		bool ourSideBid = false;
		foreach (var call in auction.Calls)
		{
			int by = auction.SeatOf(index++);
			if (by != seat)
			{
				if (call.Kind == CallKind.Bid)
				{
					anyBidBefore = true;
					if (Auction.Side(by) == Auction.Side(seat))
						ourSideBid = true;
				}
				continue;
			}
			if (call.Kind == CallKind.Pass)
			{
				if (!shown && !anyBidBefore)
					points = Math.Max(points, 5); // passed as dealer or second: under an opening
				continue;
			}
			if (call.Kind != CallKind.Bid)
				continue;
			bool opening = !anyBidBefore;
			bool overcall = anyBidBefore && !ourSideBid;
			if (!shown)
			{
				shown = true;
				if (opening)
				{
					if (call.Level == 1 && call.Strain == Strain.NoTrump) { points = 16; balanced = true; }
					else if (call.Level == 2 && call.Strain == Strain.NoTrump) { points = 20.5; balanced = true; }
					else if (call.Level == 2 && call.Strain == Strain.Clubs) points = 23;
					else points = 14;
				}
				else if (overcall)
				{
					points = 11;
				}
				else
				{
					// A response to partner: new suit at the one level 6+, at the two level 10+; no trumps by level.
					points = call.Strain == Strain.NoTrump ? call.Level switch { 1 => 8, 2 => 14, _ => 17 } : call.Level == 1 ? 9 : 11;
					if (call.Strain == Strain.NoTrump)
						balanced = true;
				}
			}
			if (call.Strain != Strain.NoTrump)
			{
				var suit = SuitOf(call.Strain);
				// A raise of partner's suit shows support; a new suit shows length.
				bool raise = auction.CallsBy(partnerOf).Any(c => c.Kind == CallKind.Bid && c.Strain == call.Strain);
				lengths[suit] = Math.Max(lengths[suit], raise ? 3 : (opening || overcall) && Major(call.Strain) ? 5 : 4);
				if (raise && shown && points < 9)
					points = call.Level >= 3 ? 11 : 7.5;
			}
		}
		return new Picture(points, lengths, balanced, shown);
	}

	private static int LevelFor(Strain strain, double points)
	{
		if (points >= 37) return 7;
		if (points >= 33) return 6;
		if (points >= 26) return strain == Strain.NoTrump ? 3 : Major(strain) ? 4 : points >= 29 ? 5 : 4;
		if (points >= 23) return strain == Strain.NoTrump ? 2 : 3;
		if (points >= 20) return strain == Strain.NoTrump ? 1 : 2;
		return 0;
	}

	// ------------------------------------------------------------------ Bidding

	public Call ChooseCall(BridgeDeal deal)
	{
		var auction = deal.Auction;
		var hand = deal.Hand(Seat);
		var call = Decide(auction, hand);
		return auction.IsLegal(call) ? call : Call.Pass;
	}

	/// <summary>The bid this system would make for a seat (used for your suggestion too).</summary>
	public static Call Suggest(BridgeDeal deal, int seat) => new BridgeAi(seat, Difficulty.Medium, new Random(1)).ChooseCall(deal);

	private Call Decide(Auction auction, IReadOnlyList<Card> hand)
	{
		int partner = (Seat + 2) % 4;
		int hcp = Hcp(hand);
		bool balanced = Balanced(hand);
		var mine = Read(auction, Seat);
		var theirs = Read(auction, partner);
		var last = auction.LastBid;
		bool partnerBid = theirs.Shown;
		bool weBid = mine.Shown;
		bool opponentsBid = last != null && Auction.Side(last.Value.Seat) != Auction.Side(Seat);
		var longest = Enum.GetValues<Suit>().OrderByDescending(s => Length(hand, s)).ThenByDescending(s => s).First();

		// Opening.
		if (last == null)
		{
			if (hcp >= 22)
				return Call.Bid(2, Strain.Clubs);
			if (balanced && hcp is >= 20 and <= 21)
				return Call.Bid(2, Strain.NoTrump);
			if (balanced && hcp is >= 15 and <= 17)
				return Call.Bid(1, Strain.NoTrump);
			if (hcp >= 12 || (hcp >= 11 && Length(hand, longest) >= 6))
			{
				var opening = Length(hand, Suit.Spades) >= 5 && Length(hand, Suit.Spades) >= Length(hand, Suit.Hearts) ? Suit.Spades
					: Length(hand, Suit.Hearts) >= 5 ? Suit.Hearts
					: Length(hand, Suit.Diamonds) > Length(hand, Suit.Clubs) || (Length(hand, Suit.Diamonds) >= 4 && Length(hand, Suit.Diamonds) == Length(hand, Suit.Clubs)) ? Suit.Diamonds
					: Suit.Clubs;
				return Call.Bid(1, StrainOf(opening));
			}
			return Call.Pass;
		}

		// Only the opponents have bid: overcall, or stay out.
		if (!partnerBid && !weBid)
		{
			if (!opponentsBid)
				return Call.Pass;
			if (balanced && hcp is >= 15 and <= 18 && last!.Value.Bid.Level == 1)
				return Call.Bid(1, Strain.NoTrump);
			if (hcp is >= 8 and <= 16 && Length(hand, longest) >= 5)
			{
				var strain = StrainOf(longest);
				int level = Lowest(auction, strain);
				if (level <= (hcp >= 11 ? 2 : 1))
					return Call.Bid(level, strain);
			}
			return Call.Pass;
		}

		// Partner has bid: find our best denomination and how high the two hands should go.
		double myPoints = hcp;
		Strain best = Strain.NoTrump;
		int bestFit = 0;
		foreach (var suit in new[] { Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs })
		{
			int fit = Length(hand, suit) + theirs.Lengths[suit];
			bool major = suit is Suit.Spades or Suit.Hearts;
			if (fit >= 8 && (bestFit == 0 || (major && !Major(best)) || (major == Major(best) && fit > bestFit)))
			{
				best = StrainOf(suit);
				bestFit = fit;
			}
		}
		if (bestFit > 0 && !Major(best) && (balanced || theirs.Balanced))
			best = Strain.NoTrump; // prefer 3NT to a minor-suit game
		if (bestFit == 0 && !balanced && Length(hand, longest) >= 6)
			best = StrainOf(longest);
		if (best != Strain.NoTrump)
			myPoints += hand.GroupBy(c => c.Suit).Count() < 4 ? 3 : Enum.GetValues<Suit>().Sum(s => Length(hand, s) switch { 0 => 3, 1 => 2, 2 => 1, _ => 0 }) / 2.0;

		// Our first response to partner's opening: describe the hand before settling.
		if (!weBid && partnerBid && !opponentsBidAfter(auction))
		{
			var opened = auction.CallsBy(partner).FirstOrDefault(c => c.Kind == CallKind.Bid);
			if (hcp < 6 && !(opened.Level == 2 && opened.Strain == Strain.Clubs))
				return Call.Pass;
			if (opened.Kind == CallKind.Bid && opened.Strain != Strain.NoTrump && opened.Level == 1 && bestFit == 0)
			{
				// A new four-card suit at the one level, longest first (with 6+).
				var newSuit = Enum.GetValues<Suit>().Where(s => Length(hand, s) >= 4 && StrainOf(s) > opened.Strain)
					.OrderByDescending(s => Length(hand, s)).ThenByDescending(s => s).Cast<Suit?>().FirstOrDefault();
				if (newSuit is { } s1)
					return Call.Bid(1, StrainOf(s1));
				if (hcp <= 10)
					return Call.Bid(1, Strain.NoTrump);
			}
		}

		double combined = myPoints + theirs.Points;
		int target = LevelFor(best, combined);
		if (target == 0)
			target = last is { } l && Auction.Side(l.Seat) == Auction.Side(Seat) ? 0 : 1;

		// Already in our best spot, high enough: stop.
		if (last is { } current && Auction.Side(current.Seat) == Auction.Side(Seat) && current.Bid.Strain == best && current.Bid.Level >= target)
			return Call.Pass;

		int lowest = Lowest(auction, best);
		if (lowest <= target && target > 0)
		{
			// With the picture clear (partner has described their hand), go straight to the target.
			bool clear = theirs.Shown && mine.Shown || theirs.Balanced;
			return Call.Bid(clear || target >= 6 ? target : Math.Min(target, Math.Max(lowest, lowest)), best);
		}

		// The opponents have outbid us: double if they've clearly overreached.
		if (last is { } top && Auction.Side(top.Seat) != Auction.Side(Seat) && auction.Doubling == 0
			&& combined >= 24 && top.Bid.Level >= 3 && hcp >= 11)
			return Call.Double;
		return Call.Pass;

		static bool opponentsBidAfter(Auction a) => false;
	}

	private static int Lowest(Auction auction, Strain strain)
	{
		var last = auction.LastBid;
		if (last == null)
			return 1;
		var (bid, _) = last.Value;
		return strain > bid.Strain ? bid.Level : bid.Level + 1;
	}

	// ------------------------------------------------------------------ Card play

	/// <summary>The card to play for the seat whose turn it is (the declarer also chooses for the dummy).</summary>
	public Card ChoosePlay(BridgeDeal deal)
	{
		var legal = deal.Legal();
		if (legal.Count == 1)
			return legal[0];
		return Difficulty switch
		{
			Difficulty.Easy => _random.NextDouble() < 0.4 ? legal[_random.Next(legal.Count)] : Heuristic(deal, deal.Current, legal),
			Difficulty.Medium => Heuristic(deal, deal.Current, legal),
			_ => LookAhead(deal, legal),
		};
	}

	public static Card Heuristic(BridgeDeal deal, int seat, IReadOnlyList<Card> legal)
	{
		var trick = deal.Trick;
		var trump = deal.Contract!.Trump;
		int side = Auction.Side(seat);
		Card Low(IEnumerable<Card> cards) => cards.OrderBy(c => c.Suit == trump ? 1 : 0).ThenBy(Tricks.Value).First();
		if (trick.Count == 0)
		{
			var hand = deal.Hand(seat);
			bool declaring = Auction.Side(deal.Contract.Declarer) == side;
			// Draw trumps while we hold the top ones.
			if (declaring && trump is { } t && legal.Any(c => c.Suit == t))
			{
				var outstanding = Enumerable.Range(0, 4).Where(s => Auction.Side(s) != side).Sum(s => deal.Hand(s).Count(c => c.Suit == t));
				var top = legal.Where(c => c.Suit == t).OrderByDescending(Tricks.Value).First();
				if (outstanding > 0 && IsTop(deal, top))
					return top;
			}
			// Cash a winner, else lead top of a sequence, else low from the longest suit.
			var winner = legal.Where(c => c.Suit != trump && IsTop(deal, c)).OrderByDescending(Tricks.Value).FirstOrDefault();
			if (winner != default)
				return winner;
			var longest = legal.Where(c => c.Suit != trump).GroupBy(c => c.Suit).OrderByDescending(g => g.Count()).FirstOrDefault();
			if (longest == null)
				return Low(legal);
			var cards = longest.OrderByDescending(Tricks.Value).ToList();
			if (cards.Count >= 2 && Tricks.Value(cards[0]) - Tricks.Value(cards[1]) == 1 && Tricks.Value(cards[0]) >= 11)
				return cards[0];
			return cards[Math.Min(3, cards.Count - 1)];
		}
		var winning = Tricks.Winner(trick, trump);
		bool partnerWins = Auction.Side(winning.Seat) == side;
		var winners = legal.Where(c => Tricks.Winner(trick.Append(new Play(seat, c)).ToList(), trump).Seat == seat).ToList();
		bool last = trick.Count == 3;
		if (partnerWins && (last || trick.Count == 2 || IsTop(deal, winning.Card)))
			return Low(legal);
		if (trick.Count == 1 && !IsTop(deal, trick[0].Card) && winners.All(w => !IsTop(deal, w)))
			return Low(legal); // second hand low
		if (winners.Count > 0)
			return winners.OrderBy(c => c.Suit == trump ? 1 : 0).ThenBy(Tricks.Value).First();
		return Low(legal);
	}

	/// <summary>Whether a card is the highest left in its suit (no higher card of the suit is unplayed elsewhere).</summary>
	private static bool IsTop(BridgeDeal deal, Card card) =>
		!Enumerable.Range(0, 4).SelectMany(deal.Hand).Any(c => c.Suit == card.Suit && Tricks.Value(c) > Tricks.Value(card));

	private Card LookAhead(BridgeDeal deal, IReadOnlyList<Card> legal)
	{
		const int samples = 20;
		int side = Auction.Side(deal.Current);
		int viewer = deal.Contract!.Dummy == deal.Current ? deal.Contract.Declarer : deal.Current;
		var value = legal.ToDictionary(c => c, _ => 0.0);
		for (int n = 0; n < samples; n++)
		{
			var world = deal.Guess(viewer, _random);
			foreach (var card in legal)
			{
				var sim = world.Clone();
				sim.PlayCard(card);
				while (sim.Phase == DealPhase.Play)
					sim.PlayCard(Heuristic(sim, sim.Current, sim.Legal()));
				value[card] += sim.TricksWon(side);
			}
		}
		return value.OrderByDescending(kv => kv.Value).ThenBy(kv => Tricks.Value(kv.Key)).First().Key;
	}
}
