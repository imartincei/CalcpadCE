[CalcpadCE](https://calcpad-ce.org) worksheets in this section deal with [cantilevers](https://en.wikipedia.org/wiki/Cantilever) — beams fixed at one end and free at the other, where the support has to carry the full bending moment and shear from every load applied along the span.
They evaluate the bending moment and shear directly from the load parameters, covering the loadings encountered most often in practice.

The simplest sheets isolate a single action at the tip — a [point force](#concentrated-force) or a [point moment](#concentrated-moment) — and recover the elementary $M = F \, l$ and $M = M_0$ results.
The distributed-load variants cover a [uniform load](#uniformly-distributed-load) over the full length, a [linearly varying load](#linearly-distributed-load) defined by intensities $q_1$ and $q_2$ at the ends, and a [partial uniform load](#partially-distributed-load) acting only on a stretch near the tip.

Beam length, load intensities and a probe coordinate $x_1$ are exposed as inputs, so you can read $M(x_1)$ and $V(x_1)$ at any cross-section without touching the formulas.
