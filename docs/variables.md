# Variables

A variable is defined by its name and value using expressions like "*a* = 4".
The "**=**" symbol is the assignment operator.
On the left side, only a single variable name is allowed.
On the right side, you can have any valid mathematical expression, e.g. "*b* = *a* + 4".
A variable "lives" from the place of its definition to the end of the program.
That is why you cannot use a variable before it is defined.
This will raise an error, which the [linter](new-linter.md) marks as you type.
If you redefine an existing variable with a new value, it will replace the old one and the new value will be used further on.

Variable names can include small or capital Unicode letters, digits and commas.
Names are case sensitive.
For example, "*a*" and "*A*" are different variables.
A name must start with a letter or ∡.
It can also contain superscripts: " **⁰** " - " **⁹** ", " **ⁿ** ", " **⁺** ", " **⁻** ", subscripts: " **₀**" - " **₉**", " **₊** ", " **₋** ", " **₌** ", " **₍** ", " **₎** " and other symbols: " **′** ", " **″** ", " **‴** ", " **⁗** ", " **‾** ", " **ø** ", "**Ø**", " **°** ", "**∡**".
The first occurrence of an underscore in a name starts a subscript.
For example, "a_1_1" will be formatted as $a_{1\_1}$.
Variables can store either real or complex numbers (in "Complex" mode), vectors or matrices.

A name cannot contain a dot.
A dot after a name is always element access, so `v.2` is the second element of the vector `v`.
Use an underscore or a comma instead, e.g. `F_max` or `a,1`.
See the [Migration Guide](migration-guide.md#dots-are-no-longer-allowed-as-part-of-a-variable-name).

You can add a description to a variable with a [metadata comment](new-metadata-comments.md) on the line above it.
It is shown when you hover over the variable's name in the editor.
