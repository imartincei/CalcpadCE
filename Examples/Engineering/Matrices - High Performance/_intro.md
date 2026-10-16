[CalcpadCE](https://calcpad-ce.org) offers a parallel **high-performance** matrix mode for the larger linear-algebra workloads.

Wrap any matrix with `hp([...])` (or use the `hp` creational helpers) and the engine switches to a packed numerical layout with [SIMD](https://en.wikipedia.org/wiki/Single_instruction,_multiple_data)-friendly element-wise operations, dropping per-element interpretation overhead — the gain scales with matrix size and is most visible on dense products, decompositions and reductions.

The six worksheets in this group are direct counterparts of the regular Matrices group, rewritten in HP form: [literal construction and indexing](#matrix-initialization-and-indexing), [creational helpers](#matrix-creational-functions), [row- and column-wise data toolkit](#matrix-data-functions), [linear-algebra primitives](#matrix-math-functions), [structural plumbing](#matrix-structural-functions), and the [matrix and matrix–vector operators](#matrix-operators).
The semantics match the plain mode one-to-one, so you can drop `hp(...)` around an existing pipeline and trade only the storage layout.
