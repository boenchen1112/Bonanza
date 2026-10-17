// Test-only fault injection (`?game=fault`, honoured only with `?autoplay` or `?debug`): a minigame whose
// Load throws, so the gate can prove the host catches it, logs one [ERROR] line and lands on the title.
// It is not in GameHost.Games, so it is never in the menu or a party.

using System;
using System.Collections.Generic;
using Bonanza.Core;

namespace Bonanza
{
    public sealed class FaultGame : Minigame
    {
        public override string Id => "fault";
        public override string Title => "FAULT";
        public override string Blurb => "Test only: Load throws.";
        public override string Controls => "";

        public override void Load() => throw new InvalidOperationException("injected fault in Load");
        public override void Begin(double now) { }
        public override void Tick(double now, float dt) { }
        public override void Press(string action, double time) { }
        public override Outcome Result() => null;
        public override IEnumerable<KeyValuePair<string, double>> TestChart() { yield break; }
        public override double MusicBeatAt(double time) => 0;
        public override double MusicTimeAt(double beat) => 0;
    }
}
