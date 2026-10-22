using System;
using Bridge.Core;
using Godot;
using Renegade.Ui;

namespace Bridge.Game;

/// <summary>
/// The bidding box: choose a level (1–7) and then a denomination to bid, or Pass, Double or
/// Redouble. Calls that aren't allowed are greyed out. The suggested call is shown on top.
/// </summary>
public partial class BiddingBox : PanelContainer
{
	public event Action<Bridge.Core.Call>? Called;

	private readonly HudTheme _theme;
	private readonly Button[] _levels = new Button[7];
	private readonly Button[] _strains = new Button[5];
	private readonly Button _pass, _double, _redouble;
	private readonly Label _note;
	private int _level = 1;
	private Auction? _auction;

	public BiddingBox(HudTheme theme)
	{
		_theme = theme;
		AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = theme.Panel, CornerRadiusBottomLeft = 22, CornerRadiusBottomRight = 22, CornerRadiusTopLeft = 22, CornerRadiusTopRight = 22, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14 });
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		AddChild(column);
		_note = Widgets.Label("", 24, theme.TextMuted);
		column.AddChild(_note);

		var levels = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		levels.AddThemeConstantOverride("separation", 8);
		for (int i = 0; i < 7; i++)
		{
			int level = i + 1;
			var b = Widgets.Button(level.ToString(), 28, theme.ButtonDark, theme);
			b.CustomMinimumSize = new Vector2(78, 64);
			b.Pressed += () => { _level = level; Refresh(); };
			_levels[i] = b;
			levels.AddChild(b);
		}
		column.AddChild(levels);

		var strains = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		strains.AddThemeConstantOverride("separation", 8);
		for (int i = 0; i < 5; i++)
		{
			var strain = (Strain)i;
			var b = Widgets.Button(Bridge.Core.Call.Symbol(strain), 30, theme.Accent, theme);
			b.CustomMinimumSize = new Vector2(110, 64);
			if (strain is Strain.Hearts or Strain.Diamonds)
				b.AddThemeColorOverride("font_color", new Color("ffd2cc"));
			b.Pressed += () => Called?.Invoke(Bridge.Core.Call.Bid(_level, strain));
			_strains[i] = b;
			strains.AddChild(b);
		}
		column.AddChild(strains);

		var others = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		others.AddThemeConstantOverride("separation", 10);
		_pass = Widgets.Button("Pass", 26, theme.ButtonDark, theme);
		_double = Widgets.Button("Double", 26, theme.ButtonDark, theme);
		_redouble = Widgets.Button("Redouble", 26, theme.ButtonDark, theme);
		foreach (var (button, call) in new[] { (_pass, Bridge.Core.Call.Pass), (_double, Bridge.Core.Call.Double), (_redouble, Bridge.Core.Call.Redouble) })
		{
			button.CustomMinimumSize = new Vector2(180, 60);
			button.Pressed += () => Called?.Invoke(call);
			others.AddChild(button);
		}
		column.AddChild(others);
	}

	/// <summary>Shows the box for an auction, with the suggested call noted and the cheapest sensible level picked.</summary>
	public void Open(Auction auction, Bridge.Core.Call suggestion, string history)
	{
		_auction = auction;
		_level = suggestion.Kind == CallKind.Bid ? suggestion.Level : Math.Max(1, auction.LastBid?.Bid.Level ?? 1);
		_note.Text = $"{history}\nSuggested: {suggestion}";
		Refresh();
		Visible = true;
	}

	private void Refresh()
	{
		if (_auction == null)
			return;
		for (int i = 0; i < 7; i++)
		{
			bool any = false;
			for (int s = 0; s < 5; s++)
				any |= _auction.IsLegal(Bridge.Core.Call.Bid(i + 1, (Strain)s));
			_levels[i].Disabled = !any;
			Widgets.Restyle(_levels[i], i + 1 == _level ? _theme.Accent : _theme.ButtonDark, _theme);
		}
		for (int s = 0; s < 5; s++)
			_strains[s].Disabled = !_auction.IsLegal(Bridge.Core.Call.Bid(_level, (Strain)s));
		_double.Disabled = !_auction.IsLegal(Bridge.Core.Call.Double);
		_redouble.Disabled = !_auction.IsLegal(Bridge.Core.Call.Redouble);
	}
}
