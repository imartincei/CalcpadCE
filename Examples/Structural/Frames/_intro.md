[CalcpadCE](https://calcpad-ce.org) worksheets in this section model plane frames by the [direct stiffness method](https://en.wikipedia.org/wiki/Direct_stiffness_method), assembling each frame from joint coordinates, an element table and a list of nodal supports.
The local 6×6 element stiffness matrices include axial, bending and shear flexibility, are rotated into the global axes through the transformation $T$, and are added into the global matrix $K$ that is solved by Cholesky decomposition for the joint displacements.

A [single-storey frame with prismatic members](#frame) treats two arbitrary cross-sections (a circular and a rectangular one) and shows how the section properties — area, centroid, second moment of area and shear area — are computed by integrating $b(z)$ over the section depth.
A [tapered-section variant](#frame-var) lets the width and height vary linearly along each element, so the stiffness coefficients themselves become integrals along $x$, evaluated numerically with `Area`.
A [five-storey three-bay reinforced concrete frame](#multi-story-frame) builds the geometry programmatically, derives the dead, live and self-weight loads from material unit weights, and uses T-shaped beam sections with an effective flange width.

A small SVG drawing library, [svg_drawing.cpd](https://github.com/imartincei/CalcpadCE/blob/main/Examples/Structural/Frames/svg_drawing.cpd), is included by each worksheet to render the structural scheme — joints, members, supports, loads and dimensions — directly into the report.
