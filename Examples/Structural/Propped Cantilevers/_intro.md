[CalcpadCE](https://calcpad-ce.org) worksheets in this section cover the [propped cantilever](https://en.wikipedia.org/wiki/Statically_indeterminate) — a beam fixed at one end and simply supported at the other.
The extra support makes the system one degree statically indeterminate, so the fixed-end moment and the two reactions depend on the geometry and load alone (the bending stiffness cancels for a prismatic beam), and equilibrium by itself is not enough to find them.

Each sheet works through the force-method result for one load case and reports the fixed-end moment $M_A$, the support reactions $V_A$ and $V_B$, and the location and magnitude of the in-span peak.
Concentrated actions are treated as a [point force](#concentrated-force) at distance $a$ from the fixed end and a [point moment](#concentrated-moment) at the same position, with both formulas written in terms of the dimensionless ratio $k = b / l$.
The distributed cases include a [uniform load](#uniformly-distributed-load) over the full span, a [linearly varying load](#linearly-distributed-load) between intensities $q_1$ and $q_2$, and a [partial uniform load](#partially-distributed-load) of length $b$ offset from the supports.

Span, load magnitudes, distances and a probe coordinate $x_1$ are exposed as inputs, so $M(x_1)$ and $V(x_1)$ can be read off the bending and shear diagrams at any cross-section.
