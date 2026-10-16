# High Performance Vectors and Matrices

High performance (hp) vectors and matrices were introduced in Calcpad version 7.3.0 with the purpose of solving larger engineering problems faster and with less memory consumption.
But this comes with a trade-off: all elements of an hp vector or matrix must have the same units.
This allows CalcpadCE to store and process the units only once for the whole vector/matrix and perform a lot of additional optimizations like SIMD vectorization of operations, application of more cache-friendly algorithms, etc.
All this results in dozens of times improvement in speed and reduces the required memory size more than twice, even if there are no units at all.

Hp vectors and matrices are initially created by special functions, similar to standard creational functions, but ending with “\_hp”, as follows:

Functions for creating hp vectors:

| Function | Description |
| -------- | ----------- |
| `vector_hp(n)` | Creates an empty hp vector with length *n* |
| `range_hp(x1; xn; s)` | Creates an hp vector from a range of values |

Functions for creating hp matrices:

| Function | Description |
| -------- | ----------- |
| `matrix_hp(m; n)` | Creates an hp empty matrix with dimensions *m*⨯*n* |
| `identity_hp(n)` | Creates an hp identity matrix with dimensions *n*⨯*n* |
| `diagonal_hp(n; d)` | Creates an *n*⨯*n* hp diagonal matrix filled with value *d* |
| `column_hp(m; c)` | Creates an *m*⨯1 hp column matrix filled with value *c* |
| `utriang_hp(n)` | Creates an *n*⨯*n* hp upper triangular matrix |
| `ltriang_hp(n)` | Creates an *n*⨯*n* hp lower triangular matrix |
| `symmetric_hp(n)` | Creates an *n*⨯*n* hp symmetric matrix |

The function `hp(x)` converts any argument *x* to its high-performance equivalent.
It can be used together with the square brackets operator `[]` for initialization of vectors and matrices from a list of values.
For example:

- `a = hp([1; 2; 4])` will create a high-performance vector and…
- `A = hp([1; 2; 3|4; 5; 6])` will create a high-performance matrix.

The conversion includes coping the values from the standard array to the hp one, so it must be used only for small arrays.
If the standard array contains different, but consistent units, they will be converted to the units of the first element.
If the units are not consistent, the conversion is not possible and error is returned instead.
For example:

`a = hp([1m; 20dm; 30cm])` $= [1\text{ m}\ 2\text{ m}\ 0.3\text{ m}]$

`a = hp([1m; 20s; 30kg])` → *Inconsistent units: "m, kg".*

Any expression that contains only hp vectors/arrays will return also an hp type.
If the expression contains only standard vectors/arrays or mixed standard and hp, it will return a standard type.
To check if the type of *x* is a high-performance (hp) vector or matrix you can use the function `ishp(x)`.

## High Performance Symmetric Solvers

CalcpadCE also includes advanced solvers for two of the most common matrix problems – solution of linear systems of equations and finding the eigenvalues and eigenvector of a matrix:

### PCG Symmetric Linear Solver

Direct methods using Cholesky and $LDL^T$ factorizations are suitable for small to medium sized matrices.
For larger matrices, the computational cost and solution time get too high for practical use because the asymptotic complexity of factorization is $O(n^3)$. In such cases, iterative solution methods are preferred.
They are much faster than direct ones, especially if the matrix is well conditioned.
One of the most popular of them is the preconditioned conjugate gradient (PCG) method.
Its complexity is $`O(m\sqrt{k})`$, where *m* is the number of nonzero elements and *k* is the condition number of the matrix.

In CalcpadCE, the PCG method is used in the following functions:

| Function | Description |
| -------- | ----------- |
| `slsolve(A; b)` | Solves the symmetric linear system of equations $A\vec{x} = \vec{b}$ |
| `smsolve(A; B)` | Solves the generalized symmetric matrix equation $AX = B$ |

Since most engineering methods like FEM and finite differences use symmetric matrices, we put a lot of effort into improving this particular kind of problem.
If the matrix has banded or skyline structure, the algorithm takes advantage of that by storing and processing only the elements within the bandwidth.
You can achieve such structure and minimize the bandwidth by providing appropriate numbering for the joints of the FE model.

Iterative methods like PCG are approximate and the solution continues until a certain precision is achieved.
In CalcpadCE, it is specified by setting the variable `Tol`. Its default value is `Tol` $= 10^{-6}$ just like in MATLAB.
If the required precision is not reached for under 1000 iterations, no convergence is assumed, so the solution is stopped with an error message.
Preconditioning can often improve convergence by reducing the condition number *k*. In CalcpadCE, a simple Jacoby preconditioner is used for that purpose.

### Symmetric Lanczos Eigensolver

Similarly to the system of equations, the direct QL algorithm with implicit shifts we use for finding the eigenvalues and eigenvectors of matrices has a complexity of $O(n^3)$ which makes it suitable for small to medium sized problems.
In addition, it always finds all eigenvalues and eigenvectors, which is not required in most cases.
For example, in structural dynamics we usually need only the first of the most significant vibration frequencies and modes.
The Lanczos method is much more appropriate for that purpose.
It can find several of the extreme (smallest or largest) eigenvalues of a large matrix but much faster than the Implicit QL algorithm.
It has a time complexity of $O(k^2 m)$, where *k* is the number of iterations and *m* is the number of nonzero elements of the matrix.
It is applied at the tridiagonalization step, replacing the Householder’s reflections method.

In CalcpadCE, it is used for the same functions as the QL method, when the size of the matrix is \> 200:

| Function | Description |
| -------- | ----------- |
| `eigenvals(M; n_e)` | The first $n_e$ eigenvalues of matrix *M* |
| `eigenvecs(M; n_e)` | The first $n_e$ eigenvectors of matrix *M* |
| `eigen(M; n_e)` | The first $n_e$ eigenvalues and eigenvectors of matrix *M* |

Argument $n_e$ is optional and can be omitted.
In this case or if $n_e = 0$, all eigenvalues/vectors are returned.
If $n_e < 0$ the lowest eigenvalues are returned and if $n_e > 0$ - the largest ones.  
The maximum number of iterations is set to be $4n_e + 100$. So, if the size of the matrix is $n < 1000$ and $n_e > n/5$ the QL method is used again.
This is because the Lanczos method is less accurate and when the number of iterations gets close to the matrix size, the performance is reduced to the one of the QL algorithm.
