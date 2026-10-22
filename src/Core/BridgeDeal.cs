using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Bridge.Core;

public enum DealPhase { Auction, Play, Over }

/// <summary>A trick that's been won: who took it and the cards.</summary>
public sealed record TrickWon(int Winner, IReadOnlyList<Play> Cards);

/// <summary>
/// One deal of contract bridge. Seats run clockwise: 0 South (you), 1 West, 2 North (your
/// partner), 3 East. Thirteen cards each. After the auction, the player left of the declarer
/// leads; then the declarer's partner (the dummy) lays their hand face up and the declarer plays
/// both hands. Follow suit if you can; the contract's trump suit (if any) beats the others.
/// </summary>
public sealed class BridgeDeal
{
	public const int Seats = 4;
	private readonly List<Card>[] _hands;
	private readonly List<Play> _trick = new();
	private readonly List<Card> _played = new();
	private readonly int[] _tricks = new int[2];
	private readonly HashSet<Suit>[] _void = Enumerable.Range(0, Seats).Select(_ => new HashSet<Suit>()).ToArray();

	public BridgeDeal(IReadOnlyList<Card> deck, int dealer)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Bridge needs a full deck.", nameof(deck));
		Dealer = dealer;
		_hands = Tricks.Deal(deck, Seats, first: (dealer + 1) % Seats);
		Auction = new Auction(dealer);
	}

	private BridgeDeal(BridgeDeal other, List<Card>[] hands)
	{
		Dealer = other.Dealer;
		Auction = other.Auction;
		_hands = hands;
		Phase = other.Phase;
		Contract = other.Contract;
		Current = other.Current;
		TricksPlayed = other.TricksPlayed;
		_trick.AddRange(other._trick);
		_played.AddRange(other._played);
		other._tricks.CopyTo(_tricks, 0);
		for (int s = 0; s < Seats; s++)
			_void[s].UnionWith(other._void[s]);
	}

	public int Dealer { get; }
	public Auction Auction { get; }
	public DealPhase Phase { get; private set; } = DealPhase.Auction;
	public Contract? Contract { get; private set; }
	public int Current { get; private set; }
	public int TricksPlayed { get; private set; }
	public IReadOnlyList<Card> Hand(int seat) => _hands[seat];
	public IReadOnlyList<Play> Trick => _trick;
	public IReadOnlyList<Card> Played => _played;
	public IReadOnlySet<Suit> VoidIn(int seat) => _void[seat];

	/// <summary>Tricks taken by a side (0 North–South, 1 East–West).</summary>
	public int TricksWon(int side) => _tricks[side];

	public int DeclarerTricks => Contract == null ? 0 : _tricks[Auction.Side(Contract.Declarer)];

	/// <summary>Whether the dummy's hand is face up (after the opening lead).</summary>
	public bool DummyShown => Phase == DealPhase.Play && (TricksPlayed > 0 || _trick.Count > 0);

	public BridgeDeal Clone() => new(this, _hands.Select(h => h.ToList()).ToArray());

	/// <summary>Makes the next call in the auction; when it ends, play begins (or the deal is passed out).</summary>
	public void MakeCall(Call call)
	{
		if (Phase != DealPhase.Auction)
			throw new InvalidOperationException("The auction is over.");
		Auction.Make(call);
		if (!Auction.IsOver)
			return;
		Contract = Auction.Contract;
		if (Contract == null)
		{
			Phase = DealPhase.Over;
			return;
		}
		Phase = DealPhase.Play;
		Current = (Contract.Declarer + 1) % Seats;
	}

	public List<Card> Legal() => Legal(Current);

	public List<Card> Legal(int seat) =>
		Tricks.Following(_hands[seat], _trick.Count == 0 ? null : _trick[0].Card.Suit);

	public TrickWon? PlayCard(Card card)
	{
		if (Phase != DealPhase.Play)
			throw new InvalidOperationException("Not playing now.");
		if (!Legal().Contains(card))
			throw new InvalidOperationException($"You can't play {card} now.");
		if (_trick.Count > 0 && card.Suit != _trick[0].Card.Suit)
			_void[Current].Add(_trick[0].Card.Suit);
		_hands[Current].Remove(card);
		_trick.Add(new Play(Current, card));
		_played.Add(card);
		if (_trick.Count < Seats)
		{
			Current = (Current + 1) % Seats;
			return null;
		}
		int winner = Tricks.Winner(_trick, Contract!.Trump).Seat;
		var won = new TrickWon(winner, _trick.ToList());
		_tricks[Auction.Side(winner)]++;
		_trick.Clear();
		TricksPlayed++;
		Current = winner;
		if (TricksPlayed == 13)
			Phase = DealPhase.Over;
		return won;
	}

	/// <summary>
	/// A copy with the cards this seat can't see dealt out again among the hidden hands (the
	/// dummy, once shown, stays as it is), keeping their known voids.
	/// </summary>
	public BridgeDeal Guess(int seat, Random random)
	{
		var known = new HashSet<int> { seat };
		if (DummyShown && Contract != null)
			known.Add(Contract.Dummy);
		var hiddenSeats = Enumerable.Range(0, Seats).Where(s => !known.Contains(s)).ToList();
		var hidden = hiddenSeats.SelectMany(s => _hands[s]).ToList();
		for (int attempt = 0; attempt < 40; attempt++)
		{
			Deck.Shuffle(hidden, random);
			var hands = new List<Card>[Seats];
			foreach (int s in known)
				hands[s] = _hands[s].ToList();
			var pool = hidden.ToList();
			bool ok = true;
			foreach (int s in hiddenSeats.OrderByDescending(s => _void[s].Count))
			{
				var fits = pool.Where(c => !_void[s].Contains(c.Suit)).Take(_hands[s].Count).ToList();
				if (fits.Count < _hands[s].Count)
				{
					ok = false;
					break;
				}
				hands[s] = fits;
				foreach (var c in fits)
					pool.Remove(c);
			}
			if (ok)
				return new BridgeDeal(this, hands);
		}
		return Clone();
	}
}

/// <summary>A set of four deals (Chicago): the scores for each side, deal by deal.</summary>
public sealed class Chicago
{
	public const int Deals = 4;
	public int[] Scores { get; } = new int[2];
	public int Played { get; private set; }
	public bool IsOver => Played >= Deals;
	public int Winner => !IsOver || Scores[0] == Scores[1] ? -1 : Scores[0] > Scores[1] ? 0 : 1;
	public int NextDealer => Played % 4;

	/// <summary>Books a finished deal; returns the declaring side's score (0 if passed out).</summary>
	public int Record(BridgeDeal deal)
	{
		int score = 0;
		if (deal.Contract is { } contract)
		{
			int side = Auction.Side(contract.Declarer);
			score = Scoring.Score(contract, deal.DeclarerTricks, Scoring.Vulnerable(Played, side));
			if (score > 0)
				Scores[side] += score;
			else
				Scores[1 - side] -= score;
		}
		Played++;
		return score;
	}
}
