window.MathJax = {
  loader: {
    paths: {
      "mathjax-newcm": "[mathjax]/output/fonts/mathjax-newcm",
    },
    load: ["[mathjax-newcm]/chtml", "[tex]/boldsymbol"],
  },
  output: {
    font: "[mathjax-newcm]",
  },
  tex: {
    packages: { "[+]": ["boldsymbol"] },
    inlineMath: [["\\(", "\\)"]],
    displayMath: [["\\[", "\\]"]],
    processEscapes: true,
    processEnvironments: true,
  },
  options: {
    ignoreHtmlClass: ".*",
    processHtmlClass: "arithmatex|md-ellipsis",
  },
  startup: {
    typeset: false,
  },
};

// Material for MkDocs uses instant loading, which swaps page content via XHR
// without a full browser reload. MathJax must re-typeset after each navigation.
document$.subscribe(function () {
  if (!window.MathJax || !MathJax.startup || !MathJax.startup.promise) {
    return;
  }
  MathJax.startup.promise = MathJax.startup.promise.then(function () {
    if (MathJax.startup.output && MathJax.startup.output.clearCache) {
      MathJax.startup.output.clearCache();
    }
    MathJax.typesetClear();
    MathJax.texReset();
    return MathJax.typesetPromise();
  });
});
