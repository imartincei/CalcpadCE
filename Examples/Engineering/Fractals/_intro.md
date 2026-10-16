[CalcpadCE](https://calcpad-ce.org) treats [fractals](https://en.wikipedia.org/wiki/Fractal) as a stress-test for its iterative-computation, complex-number and inline-SVG capabilities — the same primitives that drive engineering worksheets are repurposed here to render self-similar geometry.

Two flavours coexist: escape-time sets, where every pixel is the result of an iterated map evaluated to a divergence threshold (the [Mandelbrot set](#mandelbrot-set) is the canonical example), and L-system style replacements, where a seed polygon is recursively subdivided into ever-finer detail.
The animated variants ([Koch snowflake](#koch-snowflake_1), [Kochwave](#kochwave), [Kochwave snowflake](#kochwave-snowflake)) reveal each subdivision step as a separate SVG layer, turning convergence toward the limit shape into a watchable timeline.
A stochastic chaos-game ([Sierpinski tree](#sierpinski-christmas-tree)) closes the set, showing how randomness alone reproduces a fixed attractor.

Iteration depth, viewport and palette are exposed at the top of every sheet for easy zoom-and-render experiments.
