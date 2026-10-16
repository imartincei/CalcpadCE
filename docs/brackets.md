# Brackets

Brackets are used in two cases: to change the order of calculations and to enclose arguments of functions.
Only round brackets are allowed: "**(**" and "**)**". The software checks if the following rules are satisfied for each expression:

- The first bracket in an expression must be a left one;
- The count of left and right brackets must be equal;
- Only operator or function identifier are allowed before a left bracket;
- Right bracket is not allowed after operator or function identifier;
- A function identifier always must be followed by a left bracket.

CalcpadCE uses "smart" bracket insertion while rendering the output.
It means that brackets, which are duplicate or do not affect the order of calculations, are omitted from the output.
On the other hand, there are places where brackets are added for clarity, although not required in the input.
It happens mostly when negative or complex variables are substituted.
For example:

- If $a = -2$, then $a^2 = (-2)^2 = 4$, and not $a^2 = -2^2$. The second case is ambiguous, and the sign can be applied after the exponentiation which will evaluate to -4. Also, brackets are added to exponentiation of a complex variable;
- If $a = -2$, then $b = -a = -(-2) = 2$, and not $b = -a = --2 = 2$;
- Brackets are also added in the case of multiplication and division to a negative variable: $a*b = -2*(-3) = 6$;
- Brackets are required almost every time we have to substitute complex variables: $a*b = (2 + 3\mathrm{i})*(3 - 2\mathrm{i}) = 12 + 5\mathrm{i}$.
