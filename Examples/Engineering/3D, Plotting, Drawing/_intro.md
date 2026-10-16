[CalcpadCE](https://calcpad-ce.org) pairs symbolic engineering calculations with browser-rendered figures, so the same worksheet computes and illustrates the result.
Drawings, plots and 3D models are generated from the same inputs as the calculations, so changing a dimension or load updates the figures and the report consistently — no separate CAD or charting tool is needed in the loop.

Four complementary techniques cover most engineering-reporting needs: [native 2D plots](#plot-with-title-legend-and-labels) via the `$Plot` and `$Map` directives for line graphs and contour maps, parametric SVG drawings assembled from reusable `#def` macros for [annotated reinforced-concrete cross-sections](#parametric-svg-generation) and structural details, embedded glTF models via Google's [`<model-viewer>`](https://modelviewer.dev) for rotating CAD previews, and fully interactive surfaces powered by [Plotly.js](https://plotly.com/javascript/) or [three.js](https://threejs.org) when pan, zoom or animation is needed.

Each example pairs the worksheet source with its rendered output — copy the snippet and swap in your own geometry, functions, or data.
