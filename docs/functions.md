# Functions

CalcpadCE includes a library with common math functions, ready to use.

## Trigonometric

| Name | Description |
| -------- | - |
| `sin(x)` | sine |
| `cos(x)` | cosine |
| `tan(x)` | tangent = **sin**(*x*)/**cos**(*x*), for each *x* ≠ kπ, k=1, 2, 3… |
| `csc(x)` | cosecant = 1/**sin**(*x*), for each *x* ≠ kπ, k=1, 2, 3… |
| `sec(x)` | secant = 1/**cos**(*x*), for each *x* ≠ π/2 + kπ, k=1, 2, 3… |
| `cot(x)` | cotangent = **cos**(*x*)/**sin**(*x*), for each *x* ≠ π/2 + kπ, k=1, 2, 3… |

## Hyperbolic

| Name | Description |
| -------- | - |
| `sinh(x)` | hyperbolic sine = (e*x* - e-*x*)/2 |
| `cosh(x)` | hyperbolic cosine = (e*x* + e-*x*)/2 |
| `tanh(x)` | hyperbolic tangent = (e*x* - e-*x*)/(e*x* + e-*x*) |
| `csch(x)` | hyperbolic cosecant = 1/**sinh**(*x*) |
| `sech(x)` | hyperbolic secant = 1/**cosh**(*x*) |
| `coth(x)` | hyperbolic cotangent = (e*x* + e-*x*)/(e*x* - e-*x*), for *x* ≠ 0 |

## Inverse Trigonometric

| Name | Description |
| -------- | - |
| `asin(x)` | inverse sine, defined for -1 ≤ *x* ≤ 1 |
| `acos(x)` | inverse cosine, defined for -1 ≤ *x* ≤ 1 |
| `atan(x)` | inverse tangent |
| `atan2(x; y)` | the angle whose tangent is the quotient of *y* and *x* |
| `acsc(x)` | inverse cosecant = **asin**(1/*x*) |
| `asec(x)` | inverse secant = **acos**(1/*x*) |
| `acot(x)` | inverse cotangent |

## Inverse Hyperbolic

| Name | Description |
| -------- | - |
| `asinh(x)` | inverse hyperbolic sine = **ln**(*x* + √(*x*2 + 1)), defined for -∞ ≤ *x* ≤ +∞ |
| `acosh(x)` | inverse hyperbolic cosine = **ln**(*x* + √(*x* + 1)·√(*x* – 1)), defined for *x* ≥ 1 |
| `atanh(x)` | inverse hyperbolic tangent = 1/2·**ln**\[(1 + *x*)/(1 - *x*)\], for -1 < *x* < 1 |
| `acsch(x)` | inverse hyperbolic cosecant = **atanh**(1/*x*) |
| `asech(x)` | inverse hyperbolic secant = **acosh**(1/*x*) |
| `acoth(x)` | inverse hyperbolic cotangent = 1/2·**ln**\[(*x* + 1)/(*x* - 1)\], for \|*x*\| \> 1 |

## Log/Exponential and Roots

| Name | Description |
| -------- | - |
| `log(x)` | decimal logarithm (with base 10), for each *x* \> 0 |
| `ln(x)` | natural logarithm (with base *e* ≈ 2.7183), for each *x* \> 0 |
| `log_2(x)` | binary logarithm (with base 2), for each *x* \> 0 |
| `exp(x)` | exponential function = e*x* |
| `sqr(x)` or `sqrt(x)` | square root (√‾*x*), defined for each *x* ≥ 0 |
| `cbrt(x)` | cubic root (3√‾*x*) |
| `root(x; n)` | n-th root (n√‾*x*) |

## Rounding

| Name | Description |
| -------- | - |
| `round(x)` | rounds to the nearest integer |
| `floor(x)` | rounds to the smaller integer (towards -∞) |
| `ceiling(x)` | rounds to the greater integer (towards +∞) |
| `trunc(x)` | rounds to the smaller integer (towards zero) |

## Integer

| Name | Description |
| -------- | - |
| `mod(x; y)` | the remainder of an integer division |
| `gcd(x; y; z…)` | the greatest common divisor of several integers |
| `lcm(x; y; z…)` | the least common multiple of several integers |
| `fact(n)` | factorial: multiplies all whole numbers from 1 up to *n*, e.g. **fact**(5) = 120. Works for whole numbers 0 to 170. Same as writing *n*! |

## Complex

| Name | Description |
| -------- | - |
| `re(a + bi)` | returns the real part only, **re**(a + b*i*) = a |
| `im(a + bi)` | returns the imaginary part as a real number, **im**(a + b*i*) = b |
| `abs(a + bi)` | complex modulus = **sqrt**(a2 + b2) |
| `phase(a + bi)` | complex number phase (argument) = **atan2**(a; b) |
| `conj(a + bi)` | complex number conjugate = a - bi. |

## Aggregate and Interpolation

| Name | Description |
| -------- | - |
| $min(A; \vec{b}; c…)$ | the smallest of multiple values |
| $max(A; \vec{b}; c…)$ | the greatest of multiple values |
| $sum(A; \vec{b}; c…)$ | sum of multiple values |
| $sumsq(A; \vec{b}; c…)$ | sum of squares |
| $srss(A; \vec{b}; c…)$ | square root of sum of squares |
| $average(A; \vec{b}; c…)$ | average of multiple values |
| $product(A; \vec{b}; c…)$ | product of multiple values |
| $mean(A; \vec{b}; c…)$ | geometric mean |
| $take(n; A; \vec{b}; c…)$ | returns the n-th element from the list |
| $line(x; A; \vec{b}; c…)$ | performs linear interpolation among the specified values for *x* |
| $spline(x; A; \vec{b}; c…)$ | performs Hermite spline interpolation |

## Conditional and Logical

| Name | Description |
| -------- | - |
| **if**(<*cond*>; <*value-if-true*>; <*value-if-false*>) | if the condition *cond* is satisfied, the function returns the first value, otherwise it returns the second value. The condition is satisfied when it evaluates to any non-zero number |
| **switch**(<*cond1*>; <*value1*>; <*cond2*>; <*value2*>;…; <*default-value*>) | returns the value for which the respective condition is satisfied. Conditions are checked from left to right. If none is satisfied, it returns the default value in the end. |
| `not(x)` | logical "not" |
| `and(x; y; z…)` | logical "and" |
| `or(x; y; z…)` | logical "or" |
| `xor(x; y; z…)` | logical "xor" |

## Other

| Name | Description |
| -------- | - |
| `abs(x)` | absolute value (modulus) of a real number \| *x* \| |
| `sign(x)` | sign of a number = -1 if *x* \< 0; 1 if *x* \> 0; 0 if *x* = 0 |
| `random(x)` | a random number between 0 and *x* |
| `getunits(x)` | gets the units of x without the value. Returns 1 if *x* is unitless |
| `setunits(x; u)` | sets the units *u* to *x*, where *x* can be scalar, vector or matrix |
| `clrunits(x)` | clears the units from a scalar, vector or matrix *x* |
| `hp(x)` | converts x to its high-performance (hp) equivalent type |
| `ishp(x)` | checks if the type of x is a high-performance (hp) vector or matrix |
| `timer(x)` | how many seconds the calculation has taken so far, measured up to the point where *x* is worked out. Useful for timing long calculations |
| `mandelbrot(x; y)` | tells you whether the point (*x*; *y*) belongs to the Mandelbrot set, mainly for plotting it. Returns "Undefined" for points inside the set, and a larger value the further outside a point lies |

Vector and Matrix functions are described in their sections.

Arguments must be enclosed by round brackets.
They can be constants, variables or any valid expression.
Multiple arguments must be separated by semicolons ";". When arguments are out of range, the function returns "Undefined". Exceptions from this rule are "**cot**(0)" and "**coth**(0)", which return "+∞".

Arguments of trigonometric functions can be in **degrees**, **radians** or **grades**. The units for angles can be specified in three different ways:

1. By the radio buttons above the output window (🔘**D**, 🔘**R**, 🔘**G**).
2. By compiler switches inside the code.
  You have to insert a separate line containing: `#deg` for degrees, `#rad` for radians or `#gra` for grades.
  This will affect all expressions after the current line to the end or until an alternative directive is found.
3. By attaching native units to the value itself: *deg*, *°*, ′, ″, *rad*, *grad*, *rev* (see the “Units” section, further in this manual).

Native units are of highest priority, followed by compiler switches in source code.
Both override radio buttons settings, which are of lowest priority.

All functions are also defined in the complex domain, except for **mod**(*x*; *y*), **gcd**(*x*; *y*), **lcm**(*x*; *y*), **min**(*x*; *y*) and **max**(*x*; *y*).

Logical functions accept numerical values and return “**0**” for “**false**” and “**1**” for “**true**”.

Any numerical value, different from 0, is treated as 1 (true). Multiple arguments are evaluated sequentially from left to right, according to the above tables.
We start with the first and the second.
Then, the obtained result and the next value are evaluated in turn, and so on.

Rounding of midpoint values with **round**() evaluates to the nearest integer away from zero.
The **floor**() function rounds to the smaller value (towards -∞). The **ceiling**() function rounds in the opposite direction to the larger value (towards +∞). Unlike **floor**(), **trunc**() rounds towards zero, which is equivalent to simply truncating the fractional part.
Some examples for rounding of negative and positive numbers are provided in the tables below:

**Positive**

| Function | x | Result |
| --- | --- | --- |
| round(x) | 4.5 | 5 |
| floor(x) | 4.8 | 4 |
| ceiling(x) | 4.2 | 5 |
| trunc(x) | 4.8 | 4 |

**Negative**

| Function | x | Result |
| --- | --- | --- |
| round(x) | -4.5 | -5 |
| floor(x) | -4.8 | -5 |
| ceiling(x) | -4.2 | -4 |
| trunc(x) | -4.8 | -4 |

Rounding of complex numbers affects both real and imaginary parts.

## Custom (User Defined) Functions

You can define your own functions and use them further in the calculations.
Custom functions can have unlimited number of parameters.
They are specified after the function name, enclosed in brackets "(" … ")" and separated by semicolons ";". Each function is defined, using the following format: "**f** ( *x*; *y*; *z*; … ) = **expression**", where "**f**" is the function name and "**x**", "**y**" and "**z**" are function parameters.
On the right side you can have any valid expression including constants, operators, variables and even other functions, e.g.:

```calcpad
f(x) = x^2 + 2*x*sin(x)  
g(x; y) = f(x)/(y - 4)
```

Once defined, you can use a function in any expression by inserting a function call.
Just write the function name and then specify the arguments in brackets, e. g. *b* = **g**(*a* + 2; 3) + 3. Function names must conform to the same rules as variable names.
Arguments can be any valid expressions.
You have to provide as many arguments as the number of function parameters.
The life cycle of a function is from the place of definition to the end of the code.
If you define a new function with the same name, the old one will be replaced.
You cannot redefine a library function.
For example, **sin**(*x*) = *x*^2 will return an error.

It is not necessary to pre-define the variables that are used for parameters.
However, if other variables are used inside the function body, they must be defined before the first call to the function.
Parameters work as local level variables inside the function body.
If a variable with the same name exists outside the function, a call to that function will not rewrite the value of the global variable.
For example:

> If you have a variable `x = 4` and a function `f(x) = x^2`.
>
> When you call `f(2)`, it will evaluate to $x^2 = 2^2 = 4$, because local *x* = 2
>
> If you call $x^2$ after that, it will return $x^2 = 4^2 = 16$, because global *x* remains 4.

User defined functions support both real and complex numbers.
