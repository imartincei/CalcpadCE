[CalcpadCE](https://calcpad-ce.org) worksheets in this section treat the [fully restrained beam](https://en.wikipedia.org/wiki/Fixed-end_moment) — a single span clamped against rotation and translation at both ends.
The two end fixities make the system twice statically indeterminate, so each load case produces a pair of fixed-end moments at the supports along with the shears and the in-span bending peak.

The [point force](#concentrated-force) and [point moment](#concentrated-moment) cases give the textbook fixed-end formulas $M_A = F a b^2 / l^2$ and $M_B = F a^2 b / l^2$ (and their moment-load counterparts), useful as inputs to slope-deflection and moment-distribution analyses.
The distributed cases cover the [uniform load](#uniformly-distributed-load) with $M_A = M_B = q l^2 / 12$, the [linearly varying load](#linearly-distributed-load) split between the two supports as $(3 q_1 + 2 q_2) l^2 / 60$ and $(2 q_1 + 3 q_2) l^2 / 60$, and a [partial uniform load](#partially-distributed-load) of length $b$ between offsets $a$ and $c$.

Span, load magnitudes, distances and a probe coordinate $x_1$ are exposed as inputs, so $M(x_1)$ and $V(x_1)$ can be read off the bending and shear diagrams at any cross-section.
