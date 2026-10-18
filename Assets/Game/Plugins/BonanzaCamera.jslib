// Camera conducting bridge: C# polls the queue the template's bonanza-camera.js fills (positions of the tracked
// palm with performance.now() stamps). All the work (getUserMedia, MediaPipe, the preview) lives in that script;
// without it, or without a camera API, every call answers "not available".
mergeInto(LibraryManager.library, {
  Bonanza_CameraSupported: function () {
    var C = window.BonanzaCamera;
    return C && C.supported() ? 1 : 0;
  },

  // Returns the owner token Bonanza_CameraStop needs (0 = no script).
  Bonanza_CameraStart: function () {
    var C = window.BonanzaCamera;
    return C ? C.start() : 0;
  },

  Bonanza_CameraStop: function (token) {
    var C = window.BonanzaCamera;
    if (C) C.stop(token);
  },

  // 0 idle, 1 loading, 2 running, 3 failed, 4 suspended (page hidden).
  Bonanza_CameraStatus: function () {
    var C = window.BonanzaCamera;
    return C ? C.status : 3;
  },

  Bonanza_CameraReason: function () {
    var C = window.BonanzaCamera;
    var s = C ? (C.reason || '') : 'no camera script';
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },

  Bonanza_CameraSetPreview: function (on) {
    var C = window.BonanzaCamera;
    if (C) C.setPreview(on !== 0);
  },

  // performance.now() in ms: the clock the samples are stamped in.
  Bonanza_CameraNow: function () {
    return performance.now();
  },

  // Moves up to `max` queued entries into buf as (kind, perfMs, y, x) doubles (kind 0 = a position, 1 = hand
  // lost) and returns how many.
  Bonanza_CameraPoll: function (buf, max) {
    var C = window.BonanzaCamera;
    if (!C) return 0;
    var q = C.queue, n = Math.min(q.length, max), o = buf >> 3;
    for (var i = 0; i < n; i++) {
      var e = q[i];
      HEAPF64[o + i * 4] = e[0]; HEAPF64[o + i * 4 + 1] = e[1]; HEAPF64[o + i * 4 + 2] = e[2]; HEAPF64[o + i * 4 + 3] = e[3];
    }
    q.splice(0, n);
    return n;
  }
});
