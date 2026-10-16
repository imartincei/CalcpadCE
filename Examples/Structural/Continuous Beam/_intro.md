[CalcpadCE](https://calcpad-ce.org) worksheets in this section deal with the continuous beam — a single member spanning over three or more supports, where each interior support adds one degree of static indeterminacy.

The [continuous-beam worksheet](#continuous-beam_1) takes a vector of span lengths $l$, a uniform load $q$, and rectangular-section material and geometry properties.
It builds the primary system by removing the interior supports, treats the redundant reactions as unknowns $X_i$, and assembles the flexibility matrix $\delta$ and load vector $\Delta_F$ from bending and shear contributions through `Area` integrals along the full beam.
Solving $\delta\,X = -\Delta_F$ with `clsolve` gives the support reactions, and the bending moment, shear and deflection diagrams along the entire span follow by superposition of the primary-system response and the unit-load fields.

Span lengths, section dimensions and material constants are exposed at the top of the sheet, so different geometries and load levels can be checked without rewriting any of the analysis code.
