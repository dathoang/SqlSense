// The language selector links to the other language's home page; Material stays on the same page only when the
// path matches. The user guide has a different path per language, so point the selector at the other guide.
(function () {
  var pairs = [["/SqlSense/huong-dan/", "/SqlSense/en/user-guide/"]];
  var path = location.pathname;
  pairs.forEach(function (pair) {
    var other = path === pair[0] ? pair[1] : path === pair[1] ? pair[0] : null;
    if (!other) return;
    var lang = other.indexOf("/en/") >= 0 ? "en" : "vi";
    document.querySelectorAll('a[hreflang="' + lang + '"]').forEach(function (a) {
      a.setAttribute("href", other);
    });
  });
})();
