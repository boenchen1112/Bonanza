mergeInto(LibraryManager.library, {
  Bonanza_BlurInstall: function () {
    if (window.__bonanzaBlurInstalled) return;
    window.__bonanzaBlurInstalled = true;
    window.__bonanzaBlur = 0;
    window.addEventListener('blur', function () { window.__bonanzaBlur++; });
    document.addEventListener('visibilitychange', function () { if (document.hidden) window.__bonanzaBlur++; });
  },
  Bonanza_BlurCount: function () {
    return window.__bonanzaBlur | 0;
  }
});
