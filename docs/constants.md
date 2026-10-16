# Constants

## Real

Real constants can be positive and negative integer and decimal numbers.
They can include digits "**0**" - "**9**" and decimal point "**.**". You can also enter numbers as fractions like "**3/4**". However, the program will treat them as expressions (division of two numbers). You cannot define numbers in floating point format: "**3.4e+6**". You have to use an expression like "**3.4\*10^6**" instead.

All constants and variables are internally stored as "double-precision floating point" numbers.
Their values are ranged from **-1.7976931348623157E+308** to **1.7976931348623157E+308**. If a result is out of the above interval, the program returns "-∞" or "+∞, respectively". Division by zero gives the same result, but "**0/0**" = "Undefined". The smallest positive number is **4.94065645841247E-324**. Smaller values are rounded exactly to 0.

## Complex

If you select "**Complex**" mode, you can use complex numbers in calculations.
Otherwise, only real arithmetic is applied.
Each complex number is represented by the ordered couple (**a**; **b**), where "**a**" is real number, and "**b** = \|**b**\|·***i***" is called "imaginary". It can be written in so called algebraic form: ±**a** ± **b*i*** (e.g. "2 + 3*i*"). You can also use other forms, such as polar or exponential form, by entering the respective expressions.
In CalcpadCE, the imaginary unit can be entered either as "*i*" or as "1*i*" in case you have a variable named "*i*". It is a special number that satisfies the expression $i^2 = -1$.

## Custom

You can declare any variable or function as constant (readonly) by adding `#const` before its definition.
For example:

```calcpad
#const γ = 0.57721566490153286

#const f(x) = x^2
```

After that, any attempt to assign a new value to the variable or new expression to the function throws an error message.
