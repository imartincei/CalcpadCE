[CalcpadCE](https://calcpad-ce.org) lets a function definition span multiple lines using `_` continuations and the `if` / `$while` constructs, so recursive, conditional and iterative routines fit into the same `f(x; y; …) = …` syntax as plain one-liners.

The four worksheets in this group show that pattern at work.
[Cubic Roots Function](#cubic-roots-function) packages the [closed-form solution](https://en.wikipedia.org/wiki/Cubic_equation) of $x^3 + a x^2 + b x + c = 0$ as a single multiline call returning all three roots; [Quadratic Roots Function](#quadratic-roots-function) does the same for $a x^2 + b x + c = 0$ with discriminant-based branching.
[Fibonacci Numbers](#fibonacci-numbers) defines $fib(n)$ recursively to illustrate self-referential calls, and [Greatest Common Divisor - Euclid Algorithm](#greatest-common-divisor-euclid-algorithm) builds an iterative $gcd(a, b)$ around a `$while` block.

Together the four pages serve as templates for any user-defined function that needs more than a single expression.
