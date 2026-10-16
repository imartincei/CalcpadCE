[CalcpadCE](https://calcpad-ce.org) worksheets in this section deal with [beam deflection](https://en.wikipedia.org/wiki/Deflection) under classical Euler-Bernoulli theory, where the curvature $\kappa$ is proportional to the bending moment through the bending stiffness $EI$.
Each sheet integrates the curvature twice along the span to give the rotation $\varphi(x)$ and deflection $d(x)$ closed forms for a single member and load case.

A [cantilever with a tip force](#cantilever-with-concentrated-load) and a [cantilever under uniform load](#cantilever-with-unifrom-load) cover the two basic clamped-free responses, recovering $d_{max} = F l^3 / (3 E I)$ and $d_{max} = q l^4 / (8 E I)$ at the free end respectively.
A [simply supported beam with a mid-span point load](#simply-supported-beam-with-concentrated-load) and a [simply supported beam under uniform load](#simply-supported-beam-with-unifrom-load) provide the pinned-pinned counterparts, with the central $5 q l^4 / (384 E I)$ deflection used in serviceability checks.

Span, load magnitude, second moment of area $I$, modulus $E$ and a probe coordinate $x$ are exposed as inputs, so $d(x)$ and the maximum rotation $\varphi_{max}$ can be read for any geometry without editing the formulas.
