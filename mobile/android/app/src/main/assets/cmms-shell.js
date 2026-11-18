// Injected by the 4C CMMS Android app into every page it shows (see MainActivity). It routes what a web view cannot
// do by itself to the app (window.CmmsAndroid, see AppBridge.java): printing, and saving files the web app creates in
// the browser (e.g. the stock-take CSV, a "blob:" link). Tested by mobile/test-shell.mjs.
(function () {
  if (window.__cmmsShell) return;
  window.__cmmsShell = true;
  var app = window.CmmsAndroid;
  if (!app) return;

  // Purchase orders, reports and stock sheets call window.print(): open Android's print dialog (printer or PDF).
  window.print = function () {
    app.print(document.title || '4C CMMS');
  };

  // A link with a download attribute (even an empty one: the app then picks a name) to a file built in the page.
  function isBlobDownload(a) {
    return !!a && !!a.hasAttribute && a.hasAttribute('download') && /^blob:/.test(a.href || '');
  }

  function saveBlobLink(a) {
    var name = a.download || '';
    fetch(a.href)
      .then(function (r) { return r.blob(); })
      .then(function (blob) {
        return blob.arrayBuffer().then(function (buffer) {
          var bytes = new Uint8Array(buffer), binary = '', chunk = 0x8000;
          for (var i = 0; i < bytes.length; i += chunk) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
          app.saveFile(name, blob.type || 'application/octet-stream', btoa(binary));
        });
      })
      .catch(function () { app.couldNotSave(name); }); // never silently nothing
  }

  // Files built in the browser are downloaded with a programmatic click on a link the page never shows...
  var nativeClick = HTMLAnchorElement.prototype.click;
  HTMLAnchorElement.prototype.click = function () {
    if (isBlobDownload(this)) { saveBlobLink(this); return; }
    return nativeClick.call(this);
  };

  // ...or with an ordinary tap on such a link.
  if (document.addEventListener) {
    document.addEventListener('click', function (e) {
      var a = e.target && e.target.closest ? e.target.closest('a[download]') : null;
      if (isBlobDownload(a)) { e.preventDefault(); saveBlobLink(a); }
    }, true);
  }
})();
