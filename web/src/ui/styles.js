/**
 * All UI styling, injected once.   [ui agent owns this dir]
 *
 * Two rules govern everything here:
 *
 * 1. **Nothing is sized in pixels.** Every dimension derives from `--u`, a
 *    single clamped vmin unit. One number re-proportions the whole interface,
 *    and 390x844 portrait, 1280x720 and 1920x1080 are the same layout at
 *    three sizes rather than three layouts.
 * 2. **The safe area is real estate, not decoration.** Notches and home
 *    indicators eat the corners the HUD wants most, so every edge inset is
 *    `max(env(safe-area-inset-*), var(--u))`.
 *
 * Motion lives in JS (see update()), not in CSS transitions, because it has to
 * be able to sync to the beat. CSS animations are used only for one-shot
 * flourishes that no one will ever try to lock to a downbeat.
 */

export function injectStyles() {
  if (document.getElementById('bbb-ui-style')) return;
  const s = document.createElement('style');
  s.id = 'bbb-ui-style';
  s.textContent = CSS;
  document.head.appendChild(s);
}

const CSS = `
.bbb-layer,.bbb-overlay{position:absolute;inset:0;pointer-events:none;
  font-family:system-ui,-apple-system,"Segoe UI",sans-serif;
  --u:clamp(8px,1.72vmin,19px);
  --pad-t:max(env(safe-area-inset-top,0px),calc(var(--u)*1.05));
  --pad-r:max(env(safe-area-inset-right,0px),calc(var(--u)*1.2));
  --pad-b:max(env(safe-area-inset-bottom,0px),calc(var(--u)*1.05));
  --pad-l:max(env(safe-area-inset-left,0px),calc(var(--u)*1.2));
  --ink:#f2eeff; --dim:rgba(226,222,255,.58);
  --panel:rgba(15,12,34,.56); --panel-line:rgba(255,255,255,.13);
  -webkit-font-smoothing:antialiased;}
.bbb-overlay{z-index:5;}

/* A vector-typeface string: a cached raster scaled by --cap. */
.bbb-t{display:block;background-repeat:no-repeat;background-size:100% 100%;
  height:calc(var(--cap) * var(--ratio,1.5));width:calc(var(--cap) * var(--ratio,1.5) * var(--ar,4));
  flex:0 0 auto;}

/* Small system-ui labels. Body text is allowed to be a system face. */
.bbb-lab{font-size:calc(var(--u)*0.86);font-weight:800;letter-spacing:.14em;
  text-transform:uppercase;color:var(--dim);line-height:1.1;
  text-shadow:0 calc(var(--u)*.09) 0 rgba(0,0,0,.55);white-space:nowrap;}

/* ------------------------------------------------------------------ HUD -- */
.bbb-hud{position:absolute;inset:0;
  padding:var(--pad-t) var(--pad-r) var(--pad-b) var(--pad-l);
  display:grid;grid-template-columns:auto minmax(0,1fr) auto;
  grid-template-rows:auto minmax(0,1fr) auto;
  grid-template-areas:"score meter acc" ". . ." "timing timing timing";
  gap:calc(var(--u)*0.7) calc(var(--u)*1.1);align-items:start;}
.bbb-hud__score{grid-area:score;display:flex;flex-direction:column;gap:calc(var(--u)*.25);}
.bbb-hud__meter{grid-area:meter;display:flex;flex-direction:column;align-items:center;
  gap:calc(var(--u)*.34);min-width:0;justify-self:center;width:min(46vw,calc(var(--u)*26));}
.bbb-hud__acc{grid-area:acc;display:flex;flex-direction:column;align-items:flex-end;gap:calc(var(--u)*.3);}
.bbb-hud__timing{grid-area:timing;justify-self:center;align-self:end;}

.bbb-panel{background:var(--panel);border:1px solid var(--panel-line);
  border-radius:calc(var(--u)*.9);backdrop-filter:blur(6px);
  box-shadow:0 calc(var(--u)*.3) 0 rgba(0,0,0,.34), inset 0 1px 0 rgba(255,255,255,.10);}

/* score odometer */
.bbb-odo{display:flex;align-items:flex-start;--cap:calc(var(--u)*2.35);}
.bbb-odo__col{overflow:hidden;position:relative;
  height:calc(var(--cap) * var(--ratio,1.5));
  width:calc(var(--cap) * var(--ratio,1.5) * var(--ar,.6));}
.bbb-odo__col>i{display:block;position:absolute;inset:0;height:1100%;
  background-repeat:no-repeat;background-size:100% 100%;will-change:transform;}
.bbb-score-row{display:flex;align-items:baseline;gap:calc(var(--u)*.4);}

/* combo */
.bbb-combo{display:flex;align-items:center;gap:calc(var(--u)*.42);
  transform-origin:0% 50%;opacity:0;will-change:transform,opacity;}
.bbb-combo__n{--cap:calc(var(--u)*2.0);}
.bbb-combo__lab{font-size:calc(var(--u)*.82);font-weight:900;letter-spacing:.16em;
  color:#fff;opacity:.72;text-shadow:0 calc(var(--u)*.1) 0 rgba(0,0,0,.5);}
.bbb-combo__rays{position:absolute;left:0;top:50%;width:calc(var(--u)*9);height:calc(var(--u)*9);
  margin:calc(var(--u)*-4.5) 0 0 calc(var(--u)*-2.5);opacity:0;pointer-events:none;
  background:conic-gradient(from 0deg,rgba(255,217,61,.85) 0 6deg,transparent 6deg 30deg,
    rgba(255,217,61,.85) 30deg 36deg,transparent 36deg 60deg,rgba(255,217,61,.85) 60deg 66deg,
    transparent 66deg 90deg,rgba(255,217,61,.85) 90deg 96deg,transparent 96deg 120deg,
    rgba(255,217,61,.85) 120deg 126deg,transparent 126deg 150deg,rgba(255,217,61,.85) 150deg 156deg,
    transparent 156deg 180deg,rgba(255,217,61,.85) 180deg 186deg,transparent 186deg 210deg,
    rgba(255,217,61,.85) 210deg 216deg,transparent 216deg 240deg,rgba(255,217,61,.85) 240deg 246deg,
    transparent 246deg 270deg,rgba(255,217,61,.85) 270deg 276deg,transparent 276deg 300deg,
    rgba(255,217,61,.85) 300deg 306deg,transparent 306deg 330deg,rgba(255,217,61,.85) 330deg 336deg,
    transparent 336deg 360deg);
  -webkit-mask-image:radial-gradient(closest-side,transparent 24%,#000 42%,#000 74%,transparent 96%);
  mask-image:radial-gradient(closest-side,transparent 24%,#000 42%,#000 74%,transparent 96%);}
.bbb-combo-wrap{position:relative;display:flex;align-items:center;min-height:calc(var(--u)*2.4);}

/* progress + beat dots */
.bbb-prog{position:relative;width:100%;height:calc(var(--u)*.86);
  background:rgba(8,6,22,.55);border:1px solid var(--panel-line);
  border-radius:99px;overflow:hidden;box-shadow:inset 0 2px 5px rgba(0,0,0,.45);}
.bbb-prog__fill{position:absolute;left:0;top:0;bottom:0;width:0%;
  background:linear-gradient(90deg,#4dd6ff,#9d7bff 52%,#ff8ad2);
  box-shadow:0 0 calc(var(--u)*.9) rgba(157,123,255,.75);border-radius:99px;}
.bbb-prog__ticks{position:absolute;inset:0;display:flex;}
.bbb-prog__ticks>i{flex:1 1 0;border-right:1px solid rgba(255,255,255,.14);}
.bbb-prog__ticks>i:last-child{border-right:0;}
.bbb-prog__head{position:absolute;top:50%;width:calc(var(--u)*1.15);height:calc(var(--u)*1.15);
  margin:calc(var(--u)*-.575) 0 0 calc(var(--u)*-.575);border-radius:50%;
  background:#fff;box-shadow:0 0 calc(var(--u)*.9) rgba(255,255,255,.9);will-change:transform;}
.bbb-beats{display:flex;gap:calc(var(--u)*.5);align-items:center;}
.bbb-beats>i{width:calc(var(--u)*.62);height:calc(var(--u)*.62);border-radius:50%;
  background:rgba(255,255,255,.22);will-change:transform,background-color;}
.bbb-prog__row{display:flex;align-items:center;gap:calc(var(--u)*.7);width:100%;}
.bbb-prog__row .bbb-lab{flex:0 0 auto;}

/* accuracy */
.bbb-acc__row{display:flex;align-items:center;gap:calc(var(--u)*.5);}
.bbb-acc__n{--cap:calc(var(--u)*1.75);}
.bbb-acc__bar{width:calc(var(--u)*7.4);height:calc(var(--u)*.62);border-radius:99px;
  background:rgba(8,6,22,.55);border:1px solid var(--panel-line);overflow:hidden;
  box-shadow:inset 0 2px 4px rgba(0,0,0,.4);}
.bbb-acc__fill{height:100%;width:0%;border-radius:99px;
  background:linear-gradient(90deg,#ff5d73,#ffd93d 46%,#9ee87a 78%,#4dd6ff);
  background-size:calc(var(--u)*7.4) 100%;}
.bbb-rank{--cap:calc(var(--u)*1.5);}

/* outs / lives */
.bbb-outs{display:flex;gap:calc(var(--u)*.42);align-items:center;}
.bbb-outs>i{width:calc(var(--u)*1.05);height:calc(var(--u)*1.05);border-radius:50%;
  background:radial-gradient(circle at 34% 30%,#fff,#ffd93d 46%,#e0952a);
  box-shadow:0 calc(var(--u)*.14) 0 rgba(0,0,0,.45);display:block;
  transition:none;will-change:transform,opacity;}
.bbb-outs>i.spent{background:radial-gradient(circle at 34% 30%,#4b4468,#2a2542);
  box-shadow:inset 0 0 0 2px rgba(255,93,115,.5);animation:bbbOutPop .42s cubic-bezier(.2,1.5,.4,1);}
@keyframes bbbOutPop{0%{transform:scale(1.7) rotate(-12deg)}60%{transform:scale(.86)}100%{transform:scale(1)}}

/* timing bar */
.bbb-timing{display:flex;flex-direction:column;align-items:center;gap:calc(var(--u)*.22);
  width:min(64vw,calc(var(--u)*30));opacity:0;will-change:transform,opacity;}
.bbb-timing__canvas{width:100%;height:calc(var(--u)*3.1);display:block;}
.bbb-timing__foot{display:flex;justify-content:space-between;width:100%;}

/* ------------------------------------------------------------- popups --- */
.bbb-pop{position:absolute;left:50%;top:40%;transform:translate(-50%,-50%);
  display:flex;flex-direction:column;align-items:center;gap:calc(var(--u)*.1);
  --cap:calc(var(--u)*3.1);will-change:transform,opacity;}
.bbb-pop__word{display:flex;align-items:flex-start;}
.bbb-pop__ch{will-change:transform;transform-origin:50% 78%;}
.bbb-pop__sub{font-size:calc(var(--u)*1.05);font-weight:900;letter-spacing:.06em;
  color:#fff;opacity:.85;text-shadow:0 calc(var(--u)*.12) 0 rgba(0,0,0,.55);}
.bbb-pop__glow{position:absolute;left:50%;top:46%;width:calc(var(--u)*13);height:calc(var(--u)*13);
  margin:calc(var(--u)*-6.5) 0 0 calc(var(--u)*-6.5);border-radius:50%;opacity:0;
  background:radial-gradient(closest-side,currentColor,transparent 72%);}
.bbb-pop__ring{position:absolute;left:50%;top:46%;width:calc(var(--u)*8);height:calc(var(--u)*8);
  margin:calc(var(--u)*-4) 0 0 calc(var(--u)*-4);border-radius:50%;opacity:0;
  border:calc(var(--u)*.28) solid currentColor;}

/* banners (also used by shell code that builds the classes by hand) */
.bbb-banner{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);
  text-align:center;display:flex;flex-direction:column;align-items:center;
  gap:calc(var(--u)*.5);will-change:transform,opacity;--cap:calc(var(--u)*5.2);}
.bbb-banner__main{font-weight:900;font-size:calc(var(--u)*5.2);color:#fff;
  letter-spacing:-.02em;line-height:1;text-transform:uppercase;
  text-shadow:0 calc(var(--u)*.3) 0 rgba(0,0,0,.55),0 0 calc(var(--u)*2) rgba(120,140,255,.4);}
.bbb-banner__main.bbb-t{text-shadow:none;}
.bbb-banner__sub{font-weight:800;font-size:calc(var(--u)*1.15);color:#cfd3ff;letter-spacing:.1em;
  text-transform:uppercase;text-shadow:0 calc(var(--u)*.12) 0 rgba(0,0,0,.5);}

/* --------------------------------------------------------- countdown ---- */
.bbb-count{position:absolute;left:50%;top:44%;transform:translate(-50%,-50%);
  display:flex;flex-direction:column;align-items:center;--cap:calc(var(--u)*9);
  will-change:transform,opacity;}
.bbb-count__ring{position:absolute;left:50%;top:50%;width:calc(var(--u)*15);height:calc(var(--u)*15);
  margin:calc(var(--u)*-7.5) 0 0 calc(var(--u)*-7.5);border-radius:50%;
  border:calc(var(--u)*.36) solid rgba(255,255,255,.85);opacity:0;}

/* -------------------------------------------------------- rules card ---- */
.bbb-rules{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);
  width:min(84vw,calc(var(--u)*40));padding:calc(var(--u)*1.6) calc(var(--u)*1.8) calc(var(--u)*1.5);
  display:flex;flex-direction:column;align-items:center;gap:calc(var(--u)*.85);
  background:linear-gradient(180deg,rgba(30,24,66,.94),rgba(14,11,34,.96));
  border:calc(var(--u)*.16) solid rgba(255,255,255,.16);border-radius:calc(var(--u)*1.5);
  box-shadow:0 calc(var(--u)*1.2) 0 rgba(0,0,0,.45),0 0 calc(var(--u)*4) rgba(90,70,200,.4);
  will-change:transform,opacity;--cap:calc(var(--u)*2.7);}
.bbb-rules__tag{font-size:calc(var(--u)*.8);font-weight:900;letter-spacing:.28em;
  color:#9d7bff;text-transform:uppercase;}
.bbb-rules__blurb{font-size:calc(var(--u)*1.05);font-weight:700;color:var(--ink);opacity:.85;
  text-align:center;line-height:1.35;}
.bbb-rules__demo{width:100%;height:calc(var(--u)*8);display:block;border-radius:calc(var(--u)*.8);
  background:rgba(6,4,18,.5);border:1px solid var(--panel-line);}
.bbb-rules__how{font-size:calc(var(--u)*.85);font-weight:800;letter-spacing:.16em;
  color:var(--dim);text-transform:uppercase;}

/* ------------------------------------------------------- transitions ---- */
.bbb-trans{position:absolute;inset:0;overflow:hidden;}
.bbb-trans__wipe{position:absolute;inset:-2% -12%;transform:translateX(-120%);
  background:linear-gradient(100deg,transparent 0 6%,#ffd93d 6% 9%,#0b0a1a 9% 100%);}
.bbb-trans__iris{position:absolute;inset:0;}
.bbb-trans__bar{position:absolute;left:-2%;width:104%;background:#0b0a1a;
  box-shadow:0 0 calc(var(--u)*1.4) rgba(0,0,0,.6);}
.bbb-trans__shape{position:absolute;left:50%;top:50%;width:160vmax;height:160vmax;
  margin:-80vmax 0 0 -80vmax;background:#0b0a1a;
  clip-path:polygon(50% 0%,61% 39%,100% 50%,61% 61%,50% 100%,39% 61%,0% 50%,39% 39%);}
.bbb-trans__fade{position:absolute;inset:0;background:#0b0a1a;}

/* --------------------------------------------------------- narrow --- */
@media (max-aspect-ratio: 5/6){
  .bbb-layer,.bbb-overlay{--u:clamp(9px,2.6vmin,22px);}
  .bbb-hud{grid-template-columns:auto auto;grid-template-rows:auto auto minmax(0,1fr) auto;
    grid-template-areas:"score acc" "meter meter" ". ." "timing timing";
    align-items:start;}
  .bbb-hud__meter{width:100%;justify-self:stretch;}
  .bbb-hud__acc{align-items:flex-end;}
  .bbb-timing{width:min(92vw,calc(var(--u)*30));}
  .bbb-pop{--cap:calc(var(--u)*2.5);}
  .bbb-rules{width:min(92vw,calc(var(--u)*40));}
}

@media (prefers-reduced-motion: reduce){
  .bbb-outs>i.spent{animation:none;}
}
`;
