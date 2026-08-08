# The five minigames — design direction

These are the designs. A builder may improve a mechanic, but may not swap a
game for a different idea: the set is balanced as a set, and each game exists
to cover a verb the others do not.

## What the set has to cover

| Game | Verb | Input shape | Why it's in the set |
|---|---|---|---|
| Swing Kings | commit on the beat | one button, timed release | the pure timing test — the skill floor and the tutorial |
| Drumline Dash | listen, then repeat | one button, memory | the only game where you must *hear* rather than *see* |
| Bounce Brigade | sustain a groove | one button, taps + charges | continuous flow, no discrete "notes" to fixate on |
| Chomp Chorus | split attention | four lanes, chords | the dexterity ceiling |
| Finale Fever | all of it, faster | mixed | the payoff |

A player who has played all five has been tested on timing, memory, flow,
dexterity, and recall. That is a *series*, not five variations.

## The universal rules

- **Two bars of lead-in, always.** The player hears the groove and sees one
  full demonstration of the mechanic before anything is scored.
- **Every scored moment is telegraphed at least one beat early**, visually,
  by an object *moving* — not by a symbol appearing. Motion tells you *when*;
  a symbol only tells you *that*.
- **Roughly 75 seconds**, three sections: teach (8 bars), play (16 bars),
  escalate (8 bars). The last section must be visibly harder than the first.
- **No fail-out.** Party games do not eliminate you 20 seconds in. Mistakes
  cost score and dignity, never participation. (Swing Kings' "outs" are a
  scoring tier, not an ejection.)
- **Score normalises to 0–1000** so party standings compare across games.
- **The last hit of the round is the biggest** — a scripted finale beat with
  its own animation, worth double.

---

## G1 · Swing Kings — *"One swing. One beat. Send it."*
**124 bpm · 4/4 · one button (hold + release)**

The repo's own DNA: this is a conducting gesture wearing a baseball uniform.

A pitching machine lobs a ball on a parabola timed so it arrives at the plate
**exactly on the downbeat**. The ball's arc IS the metronome — it launches two
beats early and its apex is beat 3. A player can hit this without hearing
anything, which is what makes it the tutorial game.

**The twist that makes it more than a timing test:** you don't tap, you
*conduct*. Hold to wind up, release on the ictus. Two independent axes:

- **Release timing** → accuracy (perfect/great/good/whiff)
- **Windup length**, i.e. how long you held cleanly before release → power

Combined into a hit tier: **Bunt → Line Drive → Home Run**. A perfectly-timed
release with no windup is a bunt: correct, unrewarding, and it teaches you the
real mechanic without a tutorial screen. The windup arc draws on screen as a
conducting trace, so the *shape* of your gesture is visible and gets nicer as
you get better.

**Escalation:** faster pitches → two pitches per bar on the offbeats → a final
"grand slam" pitch with a two-bar windup and the whole stadium holding its
breath. Miss it and it's funny; hit it and the ball leaves the level.

---

## G2 · Drumline Dash — *"Hear it. Hit it back."*
**132 bpm · 4/4 · one button**

Call and response. The drum major plays a one-bar rhythm; you play it back in
the next bar. The only game in the set where the *ear* leads — the pattern is
reinforced visually (drum heads light in sequence) but the visual is
deliberately less precise than the audio, so a player who is only watching
will drift and a player who is listening will lock in.

**The twist:** it is staged as a *race*. Each correct bar advances your
marcher down the field against three CPU marchers; each fumbled bar is a
stumble. A memory test is a quiz; a memory test you can *see yourself losing*
is a party game. Standings are visible at all times.

**Escalation:** patterns grow from 3 notes to 7 → syncopation and rests
(rests are harder than notes and land later in the game for that reason) →
the finale is a two-bar call you must return whole.

---

## G3 · Bounce Brigade — *"Land every landing."*
**118 bpm · 4/4 · one button (taps + charges)**

Continuous forward motion across a chain of springboards. Press on the beat to
bounce. Wide gaps need a **charge**: hold across two beats, release on the
downbeat, launch far. Because motion never stops, a mistake is instantly and
comically visible — you faceplant, you scramble, you keep going.

**The twist, and the best idea in the set:** the platforms are instruments.
Landing on one plays its note, and the platform layout *is* the melody. A
clean run performs the song; a sloppy run plays it wrong, audibly. The player
is not scoring points against music, the player is **making** the music.
Nothing else in the set gives that feeling and it should be protected in
review.

**Escalation:** even spacing → syncopated gaps → a closing chain of eighths
taken at a sprint, ending on a single enormous charged jump into the finish.

---

## G4 · Chomp Chorus — *"Four lanes. One groove."*
**140 bpm · 4/4 · four keys**

Four singing creatures in a row. Each is a voice in a choir. They gulp air on
the offbeat (the telegraph) and sing on the beat; you press their lane to let
them. Two lanes at once = a chord, and chords are how difficulty scales
without speeding anything up.

**The twist:** the consequence is *audible and specific*. Miss a lane and that
voice drops out of the harmony until you catch it again — a four-part chord
becomes a thin, sour three-part one. Players learn which lane they are weak on
by ear, not from a stat screen.

**Escalation:** single notes → two-note chords → sixteenth runs across lanes →
a final held four-part chord, all four keys down together, sustained through
the last bar.

---

## G5 · Finale Fever — *"It only gets faster."*
**150 bpm rising to ~1.35× · mixed input**

The payoff. A conductor-boss duels you: it plays a phrase built from the verbs
of the previous four games, you answer it. Tempo ramps continuously — the
transport supports this via `clock.setBpm()` without breaking beat phase, so
the ramp must be *smooth*, never a jump.

Three hearts, and losing them all does not end the round — it drops you to a
"survive to the end" tier, because being knocked out of the finale is the
worst possible ending to a party.

**Escalation is the entire design.** The last four bars are a solo with no
visual telegraph at all: only the music tells you when. A player who has
played the other four games can do it. That is the point, and it is the note
the series should end on.

---

## Anti-goals

- No scrolling note highway. Every game here reads as a *place with things
  happening in it*, not a chart.
- No reading required during play. If a rule needs a sentence on screen while
  the round is live, the rule is wrong.
- No punishment spiral. Difficulty comes from what is asked, never from
  removing the player's ability to recover.
- No borrowed content. Study the craft, copy nothing: not a character, not a
  jingle, not a minigame beat-for-beat.
