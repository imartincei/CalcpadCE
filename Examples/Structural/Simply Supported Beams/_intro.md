[CalcpadCE](https://calcpad-ce.org) worksheets in this section collect the standard load cases for the simply supported beam — a span resting on a pin at one end and a roller at the other.
The system is [statically determinate](https://en.wikipedia.org/wiki/Statically_determinate), so the reactions and internal forces follow from equilibrium alone and depend only on the geometry and the applied load.

A [single point force](#concentrated-force) and a [point moment](#concentrated-moment) at distance $a$ from the left support cover the basic concentrated cases, while a [series of equal concentrated forces](#concentrated-forces) at constant spacing $a = l / (n + 1)$ models a row of identical loads such as wheel groups or hangers.
For distributed actions, the [uniform load](#uniformly-distributed-load) reproduces the classroom $M_{max} = q l^2 / 8$, $V_{max} = q l / 2$ result, the [linearly varying load](#linearly-distributed-load) covers the trapezoidal and triangular shapes via end intensities $q_1$ and $q_2$, and the [partial uniform load](#partially-distributed-load) acts on a stretch of length $b$ between offsets $a$ and $c = l - a - b$.

Span, load magnitudes, distances and a probe coordinate $x_1$ are exposed as inputs, so $M(x_1)$ and $V(x_1)$ can be picked off the bending and shear diagrams at any cross-section.
