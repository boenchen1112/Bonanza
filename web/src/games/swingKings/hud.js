/**
 * Swing Kings HUD extras (DOM, on the shared UI layer).  [swingKings]
 *
 *   outs   Three lamps with an OUTS label, top-right under the accuracy. They
 *          used to be 3D spheres floating over the crowd: unlit they vanished
 *          and a lit one read as a spectator's head.
 *   hint   The one instruction, as a bar at the bottom of the frame for the
 *          whole teach section — the title card's subtitle lasted 1.5s and was
 *          too small to read over the grass.
 *
 * Both live on `ctx.ui.layer`, which main.js clears on scene exit.
 */

const CSS = `
.sk-outs{position:absolute;right:3.2%;top:12.5%;display:flex;align-items:center;gap:.5em;
  font:800 clamp(10px,1.15vw,15px)/1 system-ui,-apple-system,"Segoe UI",sans-serif;letter-spacing:.14em;
  color:#e2e8ff;text-shadow:0 0 .4em rgba(4,4,24,.9),0 .1em 0 rgba(0,0,0,.7);
  background:rgba(12,8,32,.55);border:1px solid rgba(255,255,255,.14);border-radius:999px;padding:.45em .8em;}
.sk-outs__lamp{width:1.05em;height:1.05em;border-radius:50%;background:#2a3550;
  box-shadow:inset 0 0 0 2px rgba(255,255,255,.25);transition:transform .12s;}
.sk-outs__lamp--on{background:#ff5d73;box-shadow:0 0 .7em #ff5d73,inset 0 0 0 2px rgba(255,255,255,.55);}
.sk-outs__lamp--pop{transform:scale(1.45);}
.sk-outs--fresh{color:#ffe58a;border-color:rgba(255,229,138,.8);box-shadow:0 0 1em rgba(255,229,138,.45);}
.sk-hint{position:absolute;left:50%;bottom:6.5%;transform:translateX(-50%);white-space:nowrap;
  font:800 clamp(12px,1.55vw,20px)/1 system-ui,-apple-system,"Segoe UI",sans-serif;letter-spacing:.06em;
  color:#fff6d8;text-shadow:0 .1em 0 rgba(0,0,0,.7);background:rgba(12,8,32,.66);
  border:2px solid rgba(255,229,138,.7);border-radius:999px;padding:.55em 1.2em;transition:opacity .6s;}
.sk-hint b{color:#ffe58a;}
`;

export function createHud(ctx, hintHtml) {
  const layer = ctx.ui.layer;
  if (!document.getElementById('sk-hud-css')) {
    const s = document.createElement('style');
    s.id = 'sk-hud-css';
    s.textContent = CSS;
    document.head.appendChild(s);
  }

  const outs = document.createElement('div');
  outs.className = 'sk-outs';
  outs.innerHTML = '<span>OUTS</span>';
  const label = outs.firstChild;
  let labelTimer = null;
  const lamps = [];
  for (let i = 0; i < 3; i++) {
    const l = document.createElement('span');
    l.className = 'sk-outs__lamp';
    outs.appendChild(l);
    lamps.push(l);
  }
  layer.appendChild(outs);

  const hint = document.createElement('div');
  hint.className = 'sk-hint';
  hint.innerHTML = hintHtml;
  layer.appendChild(hint);

  let shown = 0;
  return {
    setOuts(n) {
      lamps.forEach((l, i) => {
        l.classList.toggle('sk-outs__lamp--on', i < n);
        l.classList.toggle('sk-outs__lamp--pop', i === n - 1 && n > shown);
      });
      if (n > shown) setTimeout(() => lamps[n - 1]?.classList.remove('sk-outs__lamp--pop'), 160);
      // Three down, count back to zero: say so, or the reset reads as a bug.
      const plain = () => { label.textContent = 'OUTS'; outs.classList.remove('sk-outs--fresh'); };
      if (n === 0 && shown >= 3) {
        label.textContent = 'NEW INNING';
        outs.classList.add('sk-outs--fresh');
        clearTimeout(labelTimer);
        labelTimer = setTimeout(plain, 1800);
      } else if (n > 0) {
        // The first out of the new inning ends the announcement — "NEW
        // INNING" beside a lit lamp read as a contradiction.
        clearTimeout(labelTimer);
        plain();
      }
      shown = n;
    },
    /** Fade the instruction once the player has had the teach section. */
    hideHint() { hint.style.opacity = '0'; },
    dispose() { clearTimeout(labelTimer); outs.remove(); hint.remove(); },
  };
}
