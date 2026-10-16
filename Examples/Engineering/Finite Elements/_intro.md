[CalcpadCE](https://calcpad-ce.org) implements transparent [finite-element method](https://en.wikipedia.org/wiki/Finite_element_method) solvers in worksheet form — every stiffness matrix, load vector and post-processing step is plain code rather than a black-box routine.

The [deep-beam plane-stress model](#deep-beam-fea) discretises a 2D elastic continuum with rectangular 8-DOF elements and handles elastic supports through a distributed-stiffness function.
The [rectangular slab](#rectangular-slab-fea) uses a 16-DOF Bogner–Fox–Schmit (BFS) plate element on a simply-supported plan, while the multi-span [flat slab](#flat-slab-fea) extends the same BFS element to irregular column grids with point supports.
The [analytically integrated variant](#flat-slab-fea-optimized) swaps numerical Gauss integration for closed-form element matrices, cutting solve time on dense meshes by an order of magnitude.

Mesh density, geometry and material constants are parametric, so each worksheet doubles as a benchmark for convergence studies and for cross-checking commercial FEA results.
