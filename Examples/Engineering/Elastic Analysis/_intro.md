[CalcpadCE](https://calcpad-ce.org) provides closed-form analytical solutions for classical elastic problems — useful as benchmarks for validating finite-element models, for fast parametric studies before committing to a mesh, and as hand-check baselines on engineering reports.

The three worksheets below all hinge on [Fourier series](https://en.wikipedia.org/wiki/Fourier_series) expansions of the loading or compatibility conditions, with the number of retained terms exposed as an input.
The [deep-beam stress field](#deep-beam) follows Filonenko-Borodich's plane-stress series solution; the [rectangular slab](#rectangular-slab) uses the Navier double-trigonometric series from Kirchhoff–Love plate theory; and the rectangular-section [Saint-Venant torsion](#saint-venant-torsion) recovers the warping shear-stress field from a single sine series.

Every geometric and material input is parametric, so each worksheet doubles as a sensitivity tool: change the slab aspect ratio, the load span on the deep beam, or the cross-section proportions for torsion and the report — equations, numbers and plots — updates in one pass.
