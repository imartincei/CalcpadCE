[CalcpadCE](https://calcpad-ce.org) ships three built-in interpolators — `take()` for nearest-value lookup, `line()` for piecewise [linear interpolation](https://en.wikipedia.org/wiki/Linear_interpolation), and `spline()` for smooth Hermite [cubic-spline](https://en.wikipedia.org/wiki/Cubic_Hermite_spline) curves — exposed as plain functions usable anywhere in a worksheet.

The [interpolation primer](#interpolation_1) lays the three methods side-by-side on a single dataset, then re-uses them in a practical engineering case: deriving wind-load height factors $k_z$ for two terrain categories from tabulated code values.
The [function-approximation study](#interpolation-of-functions) measures how well each scheme reproduces analytical exponential and trigonometric functions sampled on a coarse grid, giving a quick visual feel for accuracy and overshoot.
The [bilinear-style table lookup](#double-interpolation) chains 1D splines along both axes to interpolate a tabulated $T(i,\, j)$ surface — the canonical pattern for working with two-input design tables.

Sample positions, values and interpolation method are parametric, so each worksheet doubles as a sandbox for choosing the right method on real engineering data.
