// Camera conducting, browser side (Swing Kings). Plain script, loaded by index.html; the C# side talks to it
// through Assets/Game/Plugins/BonanzaCamera.jslib (polling, no SendMessage).
//
// It only produces positions: the palm (landmark 9) of one hand, as (perfMs, y, x) with y growing downward in
// frame heights, like the web's swingKings/camera.js, which this ports. The conducting stroke itself is found in
// C# (Core/IctusDetector), the same detector the mouse feeds.
//
// Everything heavy is lazy: nothing under mediapipe/ is requested until start() is called, and start() is only
// called when the player conducts with the camera. The model and the wasm are local files (no CDN).
//
// Status: 0 idle, 1 loading, 2 running, 3 failed (reason in B.reason), 4 suspended (page hidden; resumes by itself).
//
// Test hook: ?cameraSim replaces the camera and the model with scripted landmark traces that enter through the
// same push() the tracker uses (Tools/camera-check.mjs). ?cameraSim=auto conducts every pitch the game launches.
(function () {
  'use strict';
  var BASE = (function () {
    var s = document.currentScript;
    return new URL('mediapipe/', s && s.src ? s.src : document.baseURI).href;
  })();
  var TRACK_LANDMARK = 9;       // palm centre (middle-finger knuckle): steadier than a fingertip for a beat
  var QUEUE_MAX = 240;          // ~8 s of frames: C# drains every frame; a paused game just drops the oldest

  var q = new URLSearchParams(location.search);
  var SIM = q.has('cameraSim') ? (q.get('cameraSim') || 'manual') : '';

  var B = window.BonanzaCamera = {
    status: 0, reason: '', gen: 0, queue: [], frames: 0, samples: 0, losts: 0, starts: 0, stops: 0,
    previewOn: true, sim: null, simMode: SIM,
  };

  var stream = null, video = null, landmarker = null, el = null, dot = null;
  var wanted = false, looping = false, lastVideoTime = -1, seen = false;
  var flow = 0;   // bumped by every start of work and every teardown: an async step that finds it changed is stale

  B.supported = function () {
    if (SIM) return true;
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.WebAssembly);
  };

  // The one entry for a position, shared by the tracker loop and the test hook.
  B.push = function (perfMs, y, x) {
    B.samples++;
    B.queue.push([0, perfMs, y, x]);
    if (B.queue.length > QUEUE_MAX) B.queue.shift();
    if (dot) { dot.style.display = 'block'; dot.style.left = ((1 - x) * 100) + '%'; dot.style.top = (y * 100) + '%'; }
  };
  B.lost = function (perfMs) {
    B.losts++;
    B.queue.push([1, perfMs == null ? performance.now() : perfMs, 0, 0]);
    if (B.queue.length > QUEUE_MAX) B.queue.shift();
    if (dot) dot.style.display = 'none';
  };

  function fail(my, e) {
    if (my !== flow) return;
    var name = e && e.name ? e.name : 'Error';
    B.reason = name + (e && e.message ? ': ' + e.message : '');
    B.status = 3;
    console.info('camera conduct unavailable, using mouse:', B.reason);
    teardown(false);
  }

  function stopStream() {
    if (stream) { try { stream.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { /* gone */ } }
    stream = null;
    if (video) { try { video.srcObject = null; } catch (e) { /* ignore */ } }
  }

  function teardown(closeModel) {
    flow++;
    wanted = false; looping = false;
    stopStream();
    if (closeModel && landmarker) { try { landmarker.close(); } catch (e) { /* already gone */ } landmarker = null; }
    if (el) { el.remove(); el = null; video = null; dot = null; }
    seen = false;
  }

  function buildPreview() {
    if (el) return;
    // A small mirrored preview with the tracked point, so the player can see that the game sees their hand.
    el = document.createElement('div');
    el.id = 'bonanza-camera-preview';
    el.style.cssText = 'position:fixed;left:14px;bottom:14px;width:176px;height:132px;z-index:40;'
      + 'border-radius:12px;overflow:hidden;border:2px solid rgba(255,229,138,.8);'
      + 'box-shadow:0 6px 18px rgba(0,0,0,.45);background:#0a0820;pointer-events:none;'
      + 'display:' + (B.previewOn ? 'block' : 'none') + ';';
    video = document.createElement('video');
    video.muted = true; video.playsInline = true; video.autoplay = true;
    video.style.cssText = 'width:100%;height:100%;object-fit:cover;transform:scaleX(-1);opacity:.85;';
    dot = document.createElement('div');
    dot.style.cssText = 'position:absolute;width:14px;height:14px;margin:-7px 0 0 -7px;border-radius:50%;'
      + 'background:#ffe58a;box-shadow:0 0 10px #ffe58a;display:none;';
    el.appendChild(video); el.appendChild(dot);
    document.body.appendChild(el);
  }

  B.setPreview = function (on) {
    B.previewOn = !!on;
    if (el) el.style.display = on ? 'block' : 'none';
  };

  function openStream() {
    return navigator.mediaDevices.getUserMedia({
      video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' }, audio: false,
    });
  }

  function loop(my) {
    if (looping) return;
    looping = true;
    function tick() {
      if (!looping || my !== flow || !wanted) { looping = false; return; }
      if (video && video.readyState >= 2 && video.currentTime !== lastVideoTime) {
        lastVideoTime = video.currentTime;
        var perfMs = performance.now();
        var res = null;
        try { res = landmarker.detectForVideo(video, perfMs); } catch (e) { looping = false; fail(my, e); return; }
        B.frames++;
        var hand = res && res.landmarks && res.landmarks[0];
        if (hand) { var p = hand[TRACK_LANDMARK]; seen = true; B.push(perfMs, p.y, 1 - p.x); }
        else if (seen) { seen = false; B.lost(perfMs); }
      }
      requestAnimationFrame(tick);
    }
    requestAnimationFrame(tick);
  }

  // Start (or join a running session: a restarted game must not prompt again). Returns the token stop() needs.
  B.start = function () {
    var gen = ++B.gen;
    if (B.status === 1 || B.status === 2 || B.status === 4) return gen;     // already on the way: the new owner takes over
    B.starts++;
    B.queue.length = 0; B.reason = '';
    wanted = true; B.status = 1;
    var my = ++flow;
    if (SIM) { B.status = 2; B.sim = B.sim || makeSim(); B.sim.start(); return gen; }
    if (!B.supported()) { fail(my, new Error('no camera API')); return gen; }
    (async function () {
      var s;
      try { s = await openStream(); } catch (e) { fail(my, e); return; }
      if (my !== flow) { s.getTracks().forEach(function (t) { t.stop(); }); return; }   // left the scene while the prompt was open
      stream = s;
      try {
        if (!landmarker) {
          var mod = await import(BASE + 'vision_bundle.js');
          if (my !== flow) { stopStream(); return; }
          landmarker = await mod.HandLandmarker.createFromOptions(
            { wasmLoaderPath: BASE + 'vision_wasm_internal.js', wasmBinaryPath: BASE + 'vision_wasm_internal.wasm' },
            { baseOptions: { modelAssetPath: BASE + 'hand_landmarker.task', delegate: 'GPU' },
              runningMode: 'VIDEO', numHands: 1, minHandDetectionConfidence: 0.5, minTrackingConfidence: 0.5 });
        }
      } catch (e) { stopStream(); fail(my, e); return; }
      if (my !== flow) { stopStream(); return; }
      buildPreview();
      video.srcObject = stream;
      try { await video.play(); } catch (e) { /* the first frame event still arrives */ }
      if (my !== flow) return;
      B.status = 2; lastVideoTime = -1;
      loop(my);
    })();
    return gen;
  };

  // Stop for the owner `gen` (a stale owner, already replaced by a restarted game, is ignored).
  B.stop = function (gen) {
    if (gen !== undefined && gen !== B.gen) return;
    B.stops++;
    var was = B.status;
    teardown(true);
    B.queue.length = 0;
    if (was !== 3) B.status = 0;
    if (B.sim && B.sim.cancel) B.sim.cancel();
  };

  // The camera light must not stay on behind a hidden tab; it comes back by itself when the page does.
  document.addEventListener('visibilitychange', function () {
    if (SIM) return;
    if (document.hidden) {
      if (B.status === 2) { B.status = 4; looping = false; stopStream(); B.lost(); }
    } else if (B.status === 4 && wanted) {
      var my = ++flow;
      openStream().then(function (s) {
        if (my !== flow) { s.getTracks().forEach(function (t) { t.stop(); }); return; }
        stream = s; video.srcObject = s;
        return video.play().catch(function () {}).then(function () { if (my !== flow) return; B.status = 2; lastVideoTime = -1; loop(my); });
      }).catch(function (e) { fail(my, e); });
    }
  });
  window.addEventListener('pagehide', function () { stopStream(); });

  // ------------------------------------------------------------------ ?cameraSim
  // Scripted palm traces for the tests. The stroke is the one ConductScript/mouse-check use, relative to the
  // contact time, but sampled in-page on requestAnimationFrame so node round trips add no jitter. Each sample is
  // stamped when it is pushed, and shows where the hand was LATENCY_MS earlier, as a real camera frame does
  // (capture + inference delay): the C# side subtracts 0.07 s, so a correct port swings on the contact time.
  function makeSim() {
    var F = 1 / 60, LOW = 0.7, LIFT = 0.25, LATENCY_MS = 70;
    function yAt(u) {
      var stop = -2 * F, sa = stop - 0.12, le = sa - 0.18, ls = le - 0.4, high = LOW - LIFT;
      if (u <= ls) return LOW;
      if (u < le) return LOW - LIFT * ((u - ls) / 0.4);
      if (u < sa) return high;
      if (u < stop) { var w = (u - sa) / 0.12; return high + LIFT * w * w; }
      return LOW;
    }
    var s = { contacts: [], raf: 0, running: false, shown: 0, restHand: true, drift: 0, paused: false, latencyMs: LATENCY_MS, x: 0.5 };
    function frame() {
      s.raf = requestAnimationFrame(frame);
      if (s.paused) return;
      var now = performance.now();
      var y = LOW;
      if (s.drift > 0) y = LOW - 0.05 * Math.sin((now - s.drift) / 1000 * 3);
      else {
        for (var i = 0; i < s.contacts.length; i++) {
          var u = (now - s.latencyMs - s.contacts[i]) / 1000;
          if (u < 1.0 && u > -1.2) { y = yAt(u); break; }
        }
      }
      if (s.restHand) B.push(now, y, s.x);
    }
    s.start = function () { if (!s.running) { s.running = true; s.raf = requestAnimationFrame(frame); } };
    s.cancel = function () { s.running = false; cancelAnimationFrame(s.raf); s.contacts.length = 0; };
    // Conduct a pitch whose contact is at performance.now() = `contactMs`.
    s.play = function (contactMs) { s.start(); s.contacts.push(contactMs); };
    // A slow lazy drift (never a stroke) from now until `untilMs`.
    s.driftUntil = function (untilMs) { s.start(); s.drift = performance.now(); setTimeout(function () { s.drift = 0; }, Math.max(0, untilMs - performance.now())); };
    s.handLost = function () { s.restHand = false; B.lost(); };
    s.handBack = function () { s.restHand = true; };
    s.pause = function (on) { s.paused = !!on; };
    return s;
  }

  if (SIM === 'auto') {
    // Conduct every launched pitch: the game logs "[LAUNCH] id=n eta=s" (seconds to contact) the frame it launches.
    var wrap = function (name) {
      var orig = console[name];
      console[name] = function () {
        try {
          var m = typeof arguments[0] === 'string' && /^\[LAUNCH\] id=(\d+) eta=([\d.-]+)/.exec(arguments[0]);
          if (m && B.status === 2 && B.autoOn !== false) { B.sim = B.sim || makeSim(); B.sim.play(performance.now() + Number(m[2]) * 1000); }
        } catch (e) { /* never break logging */ }
        return orig.apply(this, arguments);
      };
    };
    wrap('log'); wrap('info'); wrap('warn');
  }
})();
