# Bridge: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Contract bridge, Chicago scoring: a set of four deals. You (South) and Max (North) against
Rosie (West) and Lena (East). The auction: bid a level and a suit or no trumps, higher each
time, or pass, double, redouble; three passes after a bid end it, four at the start pass the
deal out. The declarer's left leads; the dummy goes face up and the declarer plays both hands.
Standard duplicate scoring (part scores, game and slam bonuses, doubled overtricks and
undertricks, the insult), with Chicago vulnerability (none, dealer's side, dealer's side, both).
Rules and scoring in `src/Core` (tested against the standard score table).

## Feel
- The bidding box shows the suggested call (a simple standard system) and greys out calls that
  aren't allowed; each call shows over its player.
- After the opening lead the dummy's fan turns to face you, sorted by suit. When your side
  declares you play both your hand and Max's.

## Computer
Bidding: 1NT 15–17 balanced, 2NT 20–21, 2♣ 22+, one of a suit with 12+ (five-card majors);
responses describe the hand; then both sides estimate the partnership's points and best fit and
bid to the right level (26 game, 33 small slam, 37 grand). Overcalls with a five-card suit;
penalty doubles when the opponents clearly overreach. Play: Easy is careless, Medium plays sound
technique, Hard looks ahead over guessed deals (on a background thread). Max always plays at Hard.

## Scope for version 1
Everything above. Later: rubber scoring, conventions (Stayman, transfers, Blackwood), claiming
the rest of the tricks, a hand record to review deals.
