// Language handling for the two sites (English at /SqlSense/, Vietnamese at /SqlSense/vi/).
// - First visit: if the browser prefers Vietnamese, go to the Vietnamese version of this page. GitHub Pages can't
//   negotiate languages on the server, so this runs in <head> before the page is drawn.
// - Picking a language in the header selector is remembered and stops any further automatic redirect.
// - The selector links to the other language's home page (Material stays on the page only when paths match);
//   pages whose path differs per language, like the user guide, are pointed at their counterpart here.
(function () {
  var KEY = "sqlsense-lang";
  var PAIRS = [ // [English, Vietnamese]
    ["/SqlSense/", "/SqlSense/vi/"],
    ["/SqlSense/user-guide/", "/SqlSense/vi/huong-dan/"],
  ];
  var path = location.pathname.replace(/index\.html$/, "");
  var pageLang = path.indexOf("/SqlSense/vi/") === 0 ? "vi" : "en";

  function counterpart(lang) {
    for (var i = 0; i < PAIRS.length; i++) {
      if (PAIRS[i][0] === path || PAIRS[i][1] === path) return lang === "vi" ? PAIRS[i][1] : PAIRS[i][0];
    }
    return null;
  }

  function stored() {
    try { return localStorage.getItem(KEY); } catch (e) { return null; }
  }

  function store(lang) {
    try { localStorage.setItem(KEY, lang); } catch (e) { /* private mode: no memory, still works */ }
  }

  function preferred() {
    var langs = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || ""];
    for (var i = 0; i < langs.length; i++) {
      var l = String(langs[i]).toLowerCase();
      if (l.indexOf("vi") === 0) return "vi";
      if (l.indexOf("en") === 0) return "en";
    }
    return "en";
  }

  var choice = stored();
  if (!choice) {
    choice = preferred();
    store(choice);
    var target = choice !== pageLang ? counterpart(choice) : null;
    if (target) {
      location.replace(target + location.search);
      return;
    }
  }

  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("a[hreflang]").forEach(function (a) {
      var lang = a.getAttribute("hreflang");
      var other = lang !== pageLang ? counterpart(lang) : null;
      if (other) a.setAttribute("href", other);
      a.addEventListener("click", function () { store(lang); }, true);
    });
  });
})();
