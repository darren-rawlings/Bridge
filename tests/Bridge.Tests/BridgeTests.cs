using System;
using System.Linq;
using Bridge.Core;
using Renegade.Cards;
using Renegade.Core;
using Xunit;

namespace Bridge.Tests;

public class AuctionTests
{
	[Fact]
	public void BidsMustGoUpAndThreePassesEndIt()
	{
		var a = new Auction(dealer: 0);
		a.Make(Call.Bid(1, Strain.Hearts));
		Assert.False(a.IsLegal(Call.Bid(1, Strain.Diamonds)));
		Assert.True(a.IsLegal(Call.Bid(1, Strain.Spades)));
		a.Make(Call.Pass);
		a.Make(Call.Bid(2, Strain.Hearts));
		a.Make(Call.Pass);
		a.Make(Call.Pass);
		Assert.False(a.IsOver);
		a.Make(Call.Pass);
		Assert.True(a.IsOver);
		var contract = a.Contract!;
		Assert.Equal(2, contract.Level);
		// South named hearts first, so South declares, though North bid the final 2♥.
		Assert.Equal(0, contract.Declarer);
		Assert.Equal(2, contract.Dummy);
	}

	[Fact]
	public void DoublesAreForTheOpponentsAndRedoublesForADouble()
	{
		var a = new Auction(dealer: 0);
		a.Make(Call.Bid(1, Strain.NoTrump));
		Assert.False(a.IsLegal(Call.Redouble));
		Assert.True(a.IsLegal(Call.Double));
		a.Make(Call.Double);
		Assert.True(a.IsLegal(Call.Redouble));
		a.Make(Call.Redouble);
		a.Make(Call.Pass);
		a.Make(Call.Pass);
		a.Make(Call.Pass);
		Assert.Equal(2, a.Contract!.Doubling);
	}

	[Fact]
	public void FourPassesPassTheDealOut()
	{
		var a = new Auction(dealer: 3);
		for (int i = 0; i < 4; i++)
			a.Make(Call.Pass);
		Assert.True(a.PassedOut);
		Assert.Null(a.Contract);
	}
}

public class ScoringTests
{
	[Theory]
	[InlineData(4, Strain.Spades, 0, 10, false, 420)]
	[InlineData(4, Strain.Spades, 0, 10, true, 620)]
	[InlineData(3, Strain.NoTrump, 0, 9, false, 400)]
	[InlineData(3, Strain.NoTrump, 0, 10, false, 430)]
	[InlineData(2, Strain.Hearts, 0, 8, false, 110)]
	[InlineData(1, Strain.NoTrump, 0, 7, false, 90)]
	[InlineData(6, Strain.Hearts, 0, 12, false, 980)]
	[InlineData(7, Strain.NoTrump, 0, 13, true, 2220)]
	[InlineData(2, Strain.Spades, 1, 8, false, 470)]
	[InlineData(4, Strain.Hearts, 0, 8, false, -100)]
	[InlineData(3, Strain.NoTrump, 1, 6, false, -500)]
	[InlineData(3, Strain.NoTrump, 1, 6, true, -800)]
	[InlineData(1, Strain.Clubs, 2, 5, false, -600)]
	public void ScoresMatchTheStandardTable(int level, Strain strain, int doubling, int tricks, bool vulnerable, int expected)
	{
		var contract = new Contract(level, strain, doubling, 0);
		Assert.Equal(expected, Scoring.Score(contract, tricks, vulnerable));
	}

	[Fact]
	public void ChicagoVulnerabilityRotates()
	{
		Assert.False(Scoring.Vulnerable(0, 0));
		Assert.False(Scoring.Vulnerable(0, 1));
		Assert.True(Scoring.Vulnerable(1, 1)); // dealer West: East–West vulnerable
		Assert.False(Scoring.Vulnerable(1, 0));
		Assert.True(Scoring.Vulnerable(2, 0));
		Assert.True(Scoring.Vulnerable(3, 0) && Scoring.Vulnerable(3, 1));
	}
}

public class DealTests
{
	private static void PlayDeal(BridgeDeal deal, BridgeAi[] ais)
	{
		int calls = 0;
		while (deal.Phase == DealPhase.Auction && calls++ < 80)
			deal.MakeCall(ais[deal.Auction.Current].ChooseCall(deal));
		Assert.NotEqual(DealPhase.Auction, deal.Phase);
		while (deal.Phase == DealPhase.Play)
		{
			// The declarer plays for the dummy.
			int mover = deal.Current == deal.Contract!.Dummy ? deal.Contract.Declarer : deal.Current;
			deal.PlayCard(ais[mover].ChoosePlay(deal));
		}
	}

	[Fact]
	public void StrongBalancedHandsOpenOneNoTrump()
	{
		// Find a deal where South has 15–17 balanced as dealer, and check the opening.
		for (int seed = 0; seed < 500; seed++)
		{
			var deal = new BridgeDeal(Deck.Shuffled(new Random(seed)), dealer: 0);
			var hand = deal.Hand(0);
			if (BridgeAi.Balanced(hand) && BridgeAi.Hcp(hand) is >= 15 and <= 17)
			{
				Assert.Equal(Call.Bid(1, Strain.NoTrump), BridgeAi.Suggest(deal, 0));
				return;
			}
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void ComputerDealsBidToAContractAndPlayAllThirteenTricks(int seed)
	{
		var random = new Random(seed);
		var chicago = new Chicago();
		var ais = Enumerable.Range(0, 4).Select(s => new BridgeAi(s, (Difficulty)(s % 3), random)).ToArray();
		while (!chicago.IsOver)
		{
			var deal = new BridgeDeal(Deck.Shuffled(random), chicago.NextDealer);
			PlayDeal(deal, ais);
			if (deal.Contract != null)
				Assert.Equal(13, deal.TricksWon(0) + deal.TricksWon(1));
			chicago.Record(deal);
		}
		Assert.Equal(4, chicago.Played);
	}

	[Fact]
	public void ContractsAreUsuallySensible()
	{
		// Over many deals, most contracts should make or go down by little: the bidding is reasonable.
		var random = new Random(7);
		var ais = Enumerable.Range(0, 4).Select(s => new BridgeAi(s, Difficulty.Medium, random)).ToArray();
		int played = 0, close = 0;
		for (int n = 0; n < 60; n++)
		{
			var deal = new BridgeDeal(Deck.Shuffled(random), n % 4);
			PlayDeal(deal, ais);
			if (deal.Contract is not { } c)
				continue;
			played++;
			if (deal.DeclarerTricks >= c.Needed - 2)
				close++;
		}
		Assert.True(close >= played * 0.7, $"{close} of {played} contracts within two tricks");
	}
}
