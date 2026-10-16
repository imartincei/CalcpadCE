[CalcpadCE](https://calcpad-ce.org) offers a parallel **high-performance** vector mode for workloads that outgrow the regular interpreter.

Wrap any vector with `hp([...])` (or use the `hp` creational helpers) and the engine switches to a packed numerical layout with [SIMD](https://en.wikipedia.org/wiki/Single_instruction,_multiple_data)-friendly element-wise operations, dropping per-element interpretation overhead — the speed-up grows with vector length and is most pronounced on dense reductions and broadcasts.

The four worksheets in this group are direct counterparts of the regular Vectors group, rewritten in HP form: [literal and creational construction, indexing and broadcast operators](#vector-initialization-indexing-and-operators), [sorting, ordering and the standard reductions](#vector-data-functions), [norms, products and statistical primitives](#vector-math-functions), and [resizing, slicing and joining](#vector-structural-functions).
The semantics match the plain mode one-to-one, so you can drop `hp(...)` around an existing pipeline and trade only the storage layout.
