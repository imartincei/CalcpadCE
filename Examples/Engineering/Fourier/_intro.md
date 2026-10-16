[CalcpadCE](https://calcpad-ce.org) treats both classical analytical [Fourier series](https://en.wikipedia.org/wiki/Fourier_series) and the discrete [fast Fourier transform](https://en.wikipedia.org/wiki/Fast_Fourier_transform) as first-class operations, exposing every coefficient and spectrum on the report instead of inside opaque library calls.

The [analytical expansion](#fourier-series) computes Fourier coefficients of a user-defined function via direct integration and reconstructs it for any chosen number of terms.
The [classic-vs-FFT comparison](#fourier-series-fft) puts the same function side-by-side under both methods, making the equivalence and aliasing limits of FFT-based reconstruction visible.
The [synthetic-signal FFT](#fft) sweeps a two-tone signal through `fft()` and recovers power and amplitude spectral densities to illustrate the basic vibration-analysis workflow.
The [strong-motion record](#strong-motion-fft) applies the same pipeline to a real earthquake acceleration history (Anamizu, 2024) to extract its Fourier amplitude spectrum.

Sampling rate, signal length and retained-term count are parametric, so each worksheet doubles as a sandbox for convergence, leakage and resolution studies.
