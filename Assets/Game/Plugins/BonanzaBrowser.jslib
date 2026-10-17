// Browser bridge: the WebAudio context's state and output latency, and the OS reduced-motion flag.
//
// Which AudioContext? Unity's WebGL audio owns one (WEBAudio.audioContext inside the framework), but
// that name is not part of any public contract and a release build may rename it or keep it out of the
// jslib scope. So it is looked up in this order and the source is reported to C# (AudioSource):
//   1 = WEBAudio.audioContext (read straight from Unity's audio internals, if reachable)
//   2 = a constructor hook the page template installs before the loader runs (window.__bonanzaAudioCtxs)
//   0 = not found (everything below then reports "unavailable" and the game carries on with no latency)
//
// Safari and some Firefox builds do not expose outputLatency; C# falls back to baseLatency, then 0.

mergeInto(LibraryManager.library, {
  Bonanza_BrowserInstall: function () {
    if (window.__bonanzaBrowser) return;
    var B = window.__bonanzaBrowser = { reduce: 0, reduceVer: 0, src: 0, resumes: 0 };

    B.ctx = function () {
      var c = null;
      B.src = 0;
      try {
        if (typeof WEBAudio !== 'undefined' && WEBAudio && WEBAudio.audioContext) { c = WEBAudio.audioContext; B.src = 1; }
      } catch (e) { c = null; }
      if (!c) {
        var l = window.__bonanzaAudioCtxs;
        if (l && l.length) {
          for (var i = l.length - 1; i >= 0; i--) { if (l[i].state !== 'closed') { c = l[i]; B.src = 2; break; } }
        }
      }
      return c;
    };

    // prefers-reduced-motion, with change events (the OS setting can flip while the page is open).
    try {
      var mq = window.matchMedia('(prefers-reduced-motion: reduce)');
      B.reduce = mq.matches ? 1 : 0;
      var onChange = function (e) { B.reduce = e.matches ? 1 : 0; B.reduceVer++; };
      if (mq.addEventListener) mq.addEventListener('change', onChange);
      else if (mq.addListener) mq.addListener(onChange);
    } catch (e) { /* no matchMedia */ }

    // Audio unlock re-arm. The first click unlocks the context (Unity does that itself). If the OS later
    // suspends it (iOS interruption, a tab put to sleep) it stays suspended until a user gesture calls
    // resume(), so every gesture tries it while the context is not running. resume() must be called inside
    // the gesture's own event, hence a window listener here and not a poll from C#.
    var tryResume = function () {
      var c = B.ctx();
      if (c && c.state !== 'running' && c.state !== 'closed') {
        B.resumes++;
        try { var p = c.resume(); if (p && p.catch) p.catch(function () {}); } catch (e) { /* ignore */ }
      }
    };
    var ev = ['pointerdown', 'keydown', 'touchstart', 'touchend', 'mousedown', 'click'];
    for (var k = 0; k < ev.length; k++) window.addEventListener(ev[k], tryResume, { capture: true, passive: true });
    B.tryResume = tryResume;
  },

  // 0 = no context found, 1 = running, 2 = suspended or interrupted, 3 = closed.
  Bonanza_AudioState: function () {
    var B = window.__bonanzaBrowser; if (!B) return 0;
    var c = B.ctx(); if (!c) return 0;
    if (c.state === 'running') return 1;
    if (c.state === 'closed') return 3;
    return 2;
  },

  // Which lookup found the context: 0 none, 1 WEBAudio, 2 constructor hook.
  Bonanza_AudioSource: function () {
    var B = window.__bonanzaBrowser; if (!B) return 0;
    B.ctx(); return B.src;
  },

  // Seconds, or -1 when the browser does not expose it.
  Bonanza_OutputLatency: function () {
    var B = window.__bonanzaBrowser; if (!B) return -1;
    var c = B.ctx(); if (!c) return -1;
    var l = c.outputLatency;
    return (typeof l === 'number' && isFinite(l)) ? l : -1;
  },

  Bonanza_BaseLatency: function () {
    var B = window.__bonanzaBrowser; if (!B) return -1;
    var c = B.ctx(); if (!c) return -1;
    var l = c.baseLatency;
    return (typeof l === 'number' && isFinite(l)) ? l : -1;
  },

  // Resume from C# (best effort: browsers only honour it inside a user gesture).
  Bonanza_AudioResume: function () {
    var B = window.__bonanzaBrowser; if (B) B.tryResume();
  },

  Bonanza_PrefersReducedMotion: function () {
    var B = window.__bonanzaBrowser; return B ? B.reduce : 0;
  },

  // Bumped on every change event of the media query.
  Bonanza_ReducedMotionVersion: function () {
    var B = window.__bonanzaBrowser; return B ? B.reduceVer : 0;
  }
});
