using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Bridge.Core;

/// <summary>A denomination, in bidding order: clubs lowest, no trumps highest.</summary>
public enum Strain { Clubs, Diamonds, Hearts, Spades, NoTrump }

public enum CallKind { Pass, Bid, Double, Redouble }

/// <summary>One call in the auction.</summary>
public readonly record struct Call(CallKind Kind, int Level = 0, Strain Strain = Strain.Clubs)
{
	public static readonly Call Pass = new(CallKind.Pass);
	public static readonly Call Double = new(CallKind.Double);
	public static readonly Call Redouble = new(CallKind.Redouble);
	public static Call Bid(int level, Strain strain) => new(CallKind.Bid, level, strain);

	/// <summary>Bids in order (1♣ = 0 … 7NT = 34), for comparing.</summary>
	public int Rank => (Level - 1) * 5 + (int)Strain;

	public override string ToString() => Kind switch
	{
		CallKind.Pass => "Pass",
		CallKind.Double => "Double",
		CallKind.Redouble => "Redouble",
		_ => $"{Level}{Symbol(Strain)}",
	};

	public static string Symbol(Strain strain) => strain switch
	{
		Strain.Clubs => "♣",
		Strain.Diamonds => "♦",
		Strain.Hearts => "♥",
		Strain.Spades => "♠",
		_ => "NT",
	};

	public static Suit? Trump(Strain strain) => strain == Strain.NoTrump ? null : (Suit)(strain switch
	{
		Strain.Clubs => Suit.Clubs,
		Strain.Diamonds => Suit.Diamonds,
		Strain.Hearts => Suit.Hearts,
		_ => Suit.Spades,
	});
}

/// <summary>The final contract: level, strain, doubled or not, and who plays it.</summary>
public sealed record Contract(int Level, Strain Strain, int Doubling, int Declarer)
{
	public int Dummy => (Declarer + 2) % 4;
	public Suit? Trump => Call.Trump(Strain);
	public int Needed => Level + 6;

	public override string ToString() => $"{Level}{Call.Symbol(Strain)}{(Doubling == 1 ? " doubled" : Doubling == 2 ? " redoubled" : "")}";
}

/// <summary>
/// The auction: starting with the dealer and going round to the left, each player passes, bids
/// higher than the last bid, doubles the opponents' last bid, or redoubles their double. Three
/// passes after a bid end it; four passes at the start mean the deal is passed out. The declarer
/// is whichever of the winning side first named the contract's strain.
/// </summary>
public sealed class Auction
{
	private readonly List<Call> _calls = new();

	public Auction(int dealer)
	{
		Dealer = dealer;
	}

	public int Dealer { get; }
	public IReadOnlyList<Call> Calls => _calls;
	public int Current => (Dealer + _calls.Count) % 4;
	public bool IsOver => PassedOut || (_calls.Count >= 4 && _calls.Skip(_calls.Count - 3).All(c => c.Kind == CallKind.Pass) && _calls.Any(c => c.Kind == CallKind.Bid));
	public bool PassedOut => _calls.Count == 4 && _calls.All(c => c.Kind == CallKind.Pass);

	public static int Side(int seat) => seat % 2;

	/// <summary>The seat that made a call (by its position in the auction).</summary>
	public int SeatOf(int index) => (Dealer + index) % 4;

	/// <summary>The last bid so far, and who made it.</summary>
	public (Call Bid, int Seat)? LastBid
	{
		get
		{
			for (int i = _calls.Count - 1; i >= 0; i--)
				if (_calls[i].Kind == CallKind.Bid)
					return (_calls[i], SeatOf(i));
			return null;
		}
	}

	/// <summary>0 undoubled, 1 doubled, 2 redoubled: the state of the last bid.</summary>
	public int Doubling
	{
		get
		{
			int d = 0;
			for (int i = _calls.Count - 1; i >= 0 && _calls[i].Kind != CallKind.Bid; i--)
				if (_calls[i].Kind == CallKind.Double)
					d = Math.Max(d, 1);
				else if (_calls[i].Kind == CallKind.Redouble)
					d = 2;
			return d;
		}
	}

	public bool IsLegal(Call call)
	{
		if (IsOver)
			return false;
		var last = LastBid;
		switch (call.Kind)
		{
			case CallKind.Pass:
				return true;
			case CallKind.Bid:
				return call.Level is >= 1 and <= 7 && (last == null || call.Rank > last.Value.Bid.Rank);
			case CallKind.Double:
				return last != null && Side(last.Value.Seat) != Side(Current) && Doubling == 0;
			default:
				return last != null && Side(last.Value.Seat) == Side(Current) && Doubling == 1;
		}
	}

	public void Make(Call call)
	{
		if (!IsLegal(call))
			throw new InvalidOperationException($"{call} isn't allowed now.");
		_calls.Add(call);
	}

	/// <summary>The contract, once the auction is over (null if it was passed out).</summary>
	public Contract? Contract
	{
		get
		{
			if (!IsOver || PassedOut || LastBid is not { } last)
				return null;
			int side = Side(last.Seat);
			for (int i = 0; i < _calls.Count; i++)
				if (_calls[i].Kind == CallKind.Bid && _calls[i].Strain == last.Bid.Strain && Side(SeatOf(i)) == side)
					return new Contract(last.Bid.Level, last.Bid.Strain, Doubling, SeatOf(i));
			throw new InvalidOperationException("No declarer found.");
		}
	}

	/// <summary>The calls a seat has made, in order.</summary>
	public IEnumerable<Call> CallsBy(int seat) => _calls.Where((_, i) => SeatOf(i) == seat);
}

/// <summary>Contract bridge scoring (as in duplicate and Chicago).</summary>
public static class Scoring
{
	/// <summary>The declaring side's score (negative if the contract went down).</summary>
	public static int Score(Contract contract, int tricks, bool vulnerable)
	{
		int needed = contract.Needed;
		int mult = contract.Doubling switch { 1 => 2, 2 => 4, _ => 1 };
		if (tricks < needed)
		{
			int down = needed - tricks;
			if (contract.Doubling == 0)
				return -down * (vulnerable ? 100 : 50);
			int penalty = 0;
			for (int k = 1; k <= down; k++)
				penalty += vulnerable ? (k == 1 ? 200 : 300) : (k == 1 ? 100 : k <= 3 ? 200 : 300);
			return -penalty * (contract.Doubling == 2 ? 2 : 1);
		}
		int TrickValue(int k) => contract.Strain switch
		{
			Strain.Clubs or Strain.Diamonds => 20,
			Strain.Hearts or Strain.Spades => 30,
			_ => k == 1 ? 40 : 30,
		};
		int contractPoints = Enumerable.Range(1, contract.Level).Sum(TrickValue) * mult;
		int score = contractPoints;
		score += contractPoints >= 100 ? (vulnerable ? 500 : 300) : 50;
		if (contract.Level == 6)
			score += vulnerable ? 750 : 500;
		if (contract.Level == 7)
			score += vulnerable ? 1500 : 1000;
		int over = tricks - needed;
		score += contract.Doubling switch
		{
			0 => over * TrickValue(2),
			1 => over * (vulnerable ? 200 : 100),
			_ => over * (vulnerable ? 400 : 200),
		};
		score += contract.Doubling == 1 ? 50 : contract.Doubling == 2 ? 100 : 0;
		return score;
	}

	/// <summary>
	/// Chicago: four deals make a set. Deal 1 nobody is vulnerable; deals 2 and 3 the dealer's
	/// side is; deal 4 both are.
	/// </summary>
	public static bool Vulnerable(int deal, int side)
	{
		int dealer = deal % 4;
		return (deal % 4) switch
		{
			0 => false,
			3 => true,
			_ => Auction.Side(dealer) == side,
		};
	}
}
