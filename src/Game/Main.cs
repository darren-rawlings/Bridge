using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bridge.Core;
using Godot;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;

namespace Bridge.Game;

/// <summary>
/// Contract bridge, Chicago style: four deals, you (South) and Max (North) against Rosie (West)
/// and Lena (East). Bid with the bidding box (your suggested call is shown). After the opening
/// lead the dummy's hand turns face up; when your side declares you play both your hands. Tap a
/// card to play it; cards you can't play are greyed.
/// </summary>
public partial class Main : CardGame
{
	private static readonly string[] SeatNames = { "South", "West", "North", "East" };

	private readonly Dictionary<Card, Card3D> _nodes = new();
	private Chicago _set = new();
	private BridgeDeal? _deal;
	private readonly BridgeAi[] _ais = new BridgeAi[4];
	private readonly List<Card3D> _trick = new();
	private readonly int[] _wonCount = new int[4];
	private BiddingBox _box = null!;

	protected override string Title => "Bridge";
	protected override string Hint => "Bid, then tap a card to play it";
	protected override int MinPlayers => 4;
	protected override int MaxPlayers => 4;
	protected override bool SortYourHand => false;
	protected override string NewGameText => _set.IsOver ? "New set" : "Next deal";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_set.IsOver && (_set.Played > 0 || _deal is { Phase: not DealPhase.Over });

	protected override void LevelChanged(Difficulty difficulty) => MakeAis(difficulty);

	private void MakeAis(Difficulty difficulty)
	{
		for (int s = 0; s < 4; s++)
			_ais[s] = new BridgeAi(s, s % 2 == 0 ? Difficulty.Hard : difficulty, new Random(Random.Next()));
	}

	protected override void AddSettings()
	{
		_box = new BiddingBox(Hud.Theme) { Visible = false };
		Widgets.Anchor(_box, Control.LayoutPreset.Center, left: -330, right: 330, top: -260, bottom: 40);
		_box.Called += call =>
		{
			if (!Busy && _deal is { Phase: DealPhase.Auction } deal && deal.Auction.Current == 0 && deal.Auction.IsLegal(call))
				Run(MakeCall(0, call));
		};
		Hud.AddChild(_box);
	}

	protected override async Task NewRound()
	{
		ClipMoment("deal", 10);
		int round = Round;
		_box.Visible = false;
		if (_set.IsOver)
			_set = new Chicago();
		MakeAis(Settings.Difficulty);
		_nodes.Clear();
		_trick.Clear();
		Array.Clear(_wonCount);
		_deal = null;
		for (int s = 0; s < 4; s++)
		{
			Fans[s].Pivot = Table.FanPivot[s];
			Hands[s].Holder!.Visible = true;
			if (s > 0)
			{
				// Back to the size every other player's fan is (the kit's), after a partner's hand was spread for you.
				Fans[s].Radius = 0.5f;
				Fans[s].Spread = 9f;
				Fans[s].MaxArc = 80f;
			}
		}
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var deal = new BridgeDeal(order, _set.NextDealer);
		_deal = deal;
		await DealRound(nodes, Enumerable.Range(0, 52).Select(i => (deal.Dealer + 1 + i) % 4).ToList(), spacing: 0.045f);
		if (round != Round)
			return;
		await PickUpAll(s => deal.Hand(s).Select(c => _nodes[c]));
		ArrangeBySuit();
		Hud.SetRestartVisible(true);
		await NextCall();
	}

	private string History()
	{
		var auction = _deal!.Auction;
		var recent = auction.Calls.Select((c, i) => $"{SeatNames[auction.SeatOf(i)][0]}: {c}").TakeLast(6);
		return auction.Calls.Count == 0 ? $"{SeatNames[auction.Dealer]} deals." : string.Join("  ", recent);
	}

	private async Task NextCall()
	{
		ClipMoment("auction", 8, log: true);
		var deal = _deal!;
		int round = Round;
		SetScore();
		if (deal.Phase == DealPhase.Over)
		{
			await EndDeal();
			return;
		}
		if (deal.Phase == DealPhase.Play)
		{
			SayNone();
			var c = deal.Contract!;
			Hud.SetStatus($"The contract: {c} by {Who(c.Declarer)}. {Who((c.Declarer + 1) % 4)} leads.", Bright);
			await Pause(1.2f);
			await NextTurn();
			return;
		}
		int seat = deal.Auction.Current;
		ShowTurn(seat);
		if (seat == 0 && !Demo)
		{
			_box.Open(deal.Auction, BridgeAi.Suggest(deal, 0), History());
			Busy = false;
			Hud.SetStatus($"Your call. You have {BridgeAi.Hcp(deal.Hand(0))} points.", Bright);
			return;
		}
		Busy = true;
		await Pause(0.75f);
		if (round != Round)
			return;
		await MakeCall(seat, _ais[seat].ChooseCall(deal));
	}

	private async Task MakeCall(int seat, Bridge.Core.Call call)
	{
		var deal = _deal!;
		Busy = true;
		_box.Visible = false;
		deal.MakeCall(call);
		Say(seat, call.ToString());
		Hud.SetStatus(History(), Muted);
		await Pause(0.5f);
		await NextCall();
	}

	/// <summary>Whether you choose the card for a seat: your own (unless you're dummy), and your partner's when your side declares.</summary>
	private bool YouPlay(int seat)
	{
		if (Demo || _deal?.Contract is not { } c)
			return false;
		bool ourSide = Auction.Side(c.Declarer) == 0;
		return ourSide ? seat is 0 or 2 : seat == 0;
	}

	/// <summary>Turns a hand to face you, laid out by suit (the dummy, or your partner's hand when you declare).</summary>
	private void FaceUp(int seat)
	{
		ClipMoment("dummy", 6);
		var pivot = Table.FanPivot[seat];
		var eye = Table.Camera.GlobalPosition;
		// Your partner's hand, when you're to play it, comes well over towards you and spreads wide, so
		// every card is easy to pick; the other side's dummy is just turned to face you.
		var at = seat == 2 ? pivot.Origin.Lerp(eye, 0.32f) + Vector3.Down * 0.35f : pivot.Origin + new Vector3(seat == 1 ? 0.35f : -0.35f, 0.1f, 0.35f);
		Fans[seat].Pivot = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), at);
		if (seat == 2)
		{
			Fans[seat].Radius = 0.62f;
			Fans[seat].Spread = 12f;
			Fans[seat].MaxArc = 110f;
		}
		var sorted = Tricks.Sorted(Fans[seat].Cards.Select(n => n.Card));
		var lookup = Fans[seat].Cards.ToDictionary(n => n.Card);
		Fans[seat].Reorder(sorted.Select(c => lookup[c]));
		Fans[seat].Settle(0.4f);
		Hands[seat].Holder!.Visible = false;
	}

	private void Dim(int seat, IEnumerable<Card>? playable)
	{
		var allowed = playable?.ToHashSet();
		foreach (int s in new[] { 0, 2 })
			foreach (var node in Fans[s].Cards)
				node.Dimmed = allowed != null && s == seat && !allowed.Contains(node.Card);
	}

	private async Task NextTurn()
	{
		var deal = _deal!;
		int round = Round;
		SetScore();
		if (deal.Phase == DealPhase.Over)
		{
			await EndDeal();
			return;
		}
		int seat = deal.Current;
		// The dummy's cards are played by the declarer: their hands show whose turn it is.
		ShowTurn(deal.Contract is { } contract && seat == contract.Dummy ? contract.Declarer : seat);
		if (YouPlay(seat))
		{
			Dim(seat, deal.Legal());
			Busy = false;
			string whose = seat == 0 ? "your hand" : "Max's hand (the dummy)";
			if (deal.Contract!.Declarer == 2 && seat == 2)
				whose = "Max's hand (you're playing it)";
			Hud.SetStatus(deal.Trick.Count == 0 ? $"Lead from {whose}." : $"Play from {whose}.", Bright);
			return;
		}
		Busy = true;
		var copy = deal.Clone();
		var ai = _ais[seat == deal.Contract!.Dummy ? deal.Contract.Declarer : seat];
		var think = Task.Run(() => ai.ChoosePlay(copy));
		await Pause(0.45f);
		var card = await think;
		if (round != Round)
			return;
		await Play(seat, card);
	}

	private async Task Play(int seat, Card card)
	{
		ClipMoment("trick", 8);
		var deal = _deal!;
		Busy = true;
		Dim(seat, null);
		bool firstLead = deal.TricksPlayed == 0 && deal.Trick.Count == 0;
		var won = deal.PlayCard(card);
		var node = _nodes[card];
		_trick.Add(node);
		await PlayToTrick(seat, node);
		if (seat == 0)
			ArrangeBySuit();
		if (firstLead)
		{
			// The dummy goes down; when our side declares, we see both our hands.
			var c = deal.Contract!;
			FaceUp(c.Dummy);
			if (Auction.Side(c.Declarer) == 0 && c.Declarer == 2)
				FaceUp(2);
			Hud.SetStatus($"{Who(c.Dummy)} {(c.Dummy == 0 ? "are" : "is")} the dummy.", Muted);
			await Pause(0.6f);
		}
		if (won != null)
		{
			await Pause(0.6f);
			int side = Auction.Side(won.Winner);
			TrickNews(won.Cards, won.Winner);
			await SweepTrick(won.Winner, _trick.ToList(), _wonCount[won.Winner]++);
			_trick.Clear();
		}
		await NextTurn();
	}

	private async Task EndDeal()
	{
		ClipMoment("result", 6);
		var deal = _deal!;
		Busy = true;
		_box.Visible = false;
		SayNone();
		int score = _set.Record(deal);
		SetScore();
		string message;
		if (deal.Contract is not { } c)
		{
			message = "Everyone passed: the deal is passed out.";
		}
		else
		{
			int made = deal.DeclarerTricks - c.Needed;
			string by = c.Declarer == 0 ? "You" : Names[c.Declarer];
			message = made >= 0
				? $"{by} made {c}{(made > 0 ? $" with {made} over" : "")}: {score} to {(Auction.Side(c.Declarer) == 0 ? "us" : "them")}."
				: $"{by} went {-made} down in {c}: {-score} to {(Auction.Side(c.Declarer) == 0 ? "them" : "us")}.";
			if (Auction.Side(c.Declarer) == 0 && made >= 0)
				Sparkle.Burst(this, Table.Winnings[c.Declarer] + Vector3.Up * 0.3f);
		}
		await Pause(0.3f);
		if (_set.IsOver)
		{
			int w = _set.Winner;
			string result = w == 0 ? $"You and Max win the set, {_set.Scores[0]} to {_set.Scores[1]}!" : w == 1 ? $"Rosie and Lena win the set, {_set.Scores[1]} to {_set.Scores[0]}." : "The set is tied!";
			Finish($"{message} {result}", w == 0 ? 0 : w == 1 ? 1 : -1, winningSeats: w == 0 ? new[] { 0, 2 } : new[] { 1, 3 });
		}
		else
		{
			Finish(message, -1);
		}
	}

	private void SetScore()
	{
		string contract = _deal?.Contract is { } c ? $" · {c.Level}{Bridge.Core.Call.Symbol(c.Strain)}{(c.Doubling > 0 ? "x" : "")} by {SeatNames[c.Declarer]}: {_deal.DeclarerTricks}/{c.Needed}" : "";
		bool usVul = Scoring.Vulnerable(_set.Played, 0), themVul = Scoring.Vulnerable(_set.Played, 1);
		string vul = usVul && themVul ? "both vul" : usVul ? "us vul" : themVul ? "them vul" : "none vul";
		Hud.SetScore($"Deal {Math.Min(_set.Played + 1, 4)}/4 ({vul}) · Us {_set.Scores[0]} · Them {_set.Scores[1]}{contract}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_deal is not { Phase: DealPhase.Play } deal || !YouPlay(deal.Current))
			return;
		int seat = deal.Current;
		if (Fans[seat].At(origin, direction) is not { } card)
			return;
		if (!deal.Legal().Contains(card.Card))
		{
			Hud.SetStatus("You must follow suit.", Bright);
			return;
		}
		Hud.HideHint();
		Run(Play(seat, card.Card));
	}
}
