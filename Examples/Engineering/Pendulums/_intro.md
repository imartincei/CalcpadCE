[CalcpadCE](https://calcpad-ce.org) handles [pendulum dynamics](https://en.wikipedia.org/wiki/Pendulum) end-to-end on a single worksheet: nonlinear ODEs are integrated numerically, while inline SVG turns each time step into a frame of a physics animation.

All five sheets share the same recipe — derive the equations of motion, integrate with [Runge-Kutta 4](https://en.wikipedia.org/wiki/Runge%E2%80%93Kutta_methods), then draw the bob, rod and trajectory inside the worksheet.
The [simple undamped](#simple-undamped-pendulum) and [simple damped](#simple-damped-pendulum) cases stay in one degree of freedom, so the integration result can be compared against analytical and small-angle approximations on the same plot — see the [side-by-side comparison sheet](#comparison-of-different-methods-for-analysis-of-simple-undamped-pendulum) for that.
The [elastic-damped](#elastic-damped-pendulum) variant adds a radial spring degree of freedom, and the [double pendulum](#double-damped-pendulum) couples two angles into the chaotic regime.

Lengths, masses, damping factors and initial conditions are exposed at the top of each sheet, so every animation doubles as a parametric playground for stability and energy-loss studies.
