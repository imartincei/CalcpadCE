# Units

CalcpadCE provides comprehensive support for physical units of measurement.
The current version supports metric (SI and compatible), US and Imperial units.
There are seven basic units that correspond to the seven physical dimensions:

- mass - kilogram (kg)
- length - meter (m)
- time - second (s)
- electric current - ampere (A)
- temperature - degree Celsius (°C)
- amount of substance - mole (mol)
- luminous intensity - candela (cd)

All other units are derivative and they are obtained by the respective laws of physics.
For example, force = mass·acceleration, so Newton is obtained by N = kg·m/s2. Multiples of units are also supported by including the respective prefixes before their names.
For example, kN = 103 N, MN = 106 N and so on.
Additionally, there are some "dimensionless" units like percents, permilles and angles (degrees, radians, etc.) that do not include physical dimensions.
However, angles exist in a special 8-th non-physical dimension, in order not to cancel with or convert to percents when mixed (which would be weird).

You can attach units to numbers by typing the unit’s name after the value, e.g. 15 kg.
Then, you can use them in expressions, just like any other values.
Unit cancellation and conversion is performed automatically during calculations.
For example, the following expression will be evaluated as:

`1.23 m + 35 cm + 12 mm` $= 1.59m$ (and not: 1.23 + 35 + 12 = 48.23)

The result is usually obtained into the first unit in the expression.
If you want to use particular units, write a vertical bar "\|" followed by the target units at the end:

```calcpad
1.23 m + 35 cm + 12 mm | cm
```

The above expression will be evaluated to 159.2 *cm*. If you simply want to convert units, just write the source and the target units, separated by a vertical bar, like: mm \| cm or 10 m/s \| km/h.

Unit consistency is also verified automatically.
For example, you cannot add *m* and *s* (e.g. `6 m + 2 s`), but you can multiply and divide them: `6 m / 2 s = 3 m/s`.

Arguments for trigonometric, hyperbolic, logarithmic and exponential functions are unitless by definition.
However, you can use units in any custom defined functions, if this makes sense.
You can also attach units to variables.
If you specify target units in a variable definition, they will be stored permanently inside the variable.
They will be used further in the calculations together with the respective value.
In the next example, speed is calculated in m/s, but is converted and stored as km/h:

| Code | Output
| ---- | -
| `'Distance -'s_1 = 50m` | $Distance - s_1 = 50m$  
| `'Time -'t_1 = 2s` | $Time - t_1 = 2s$  
| `'Speed -'V = s_1/t_1\|km/h` | $\$Speed - V = s_1/t_1 = 50m/2s = 90 km/h$  
| `'What distance you will travel for't_2 = 5s'?` | What distance you will travel for $t_2 = 5s$?  
| `s_2 = V*t_2\|m` | $s_2 = V*t_2 = 90 km/h*5s = 125m$

## Predefined Units

CalcpadCE includes a large collection of predefined units as follows:

### Dimensionless

- Parts: %, ‰, ‱, pcm, ppm, ppb, ppt, ppq;
- Angles: °, ′, ″, deg, rad, grad, rev;

### Metric Units (SI and Compatible)

- Mass: g, hg, kg, t, kt, Mt, Gt, dg, cg, mg, μg, ng, pg, Da (or u);
- Length: m, km, dm, cm, mm, μm, nm, pm, AU, ly;
- Time: s, ms, μs, ns, ps, min, h, d, w, y;
- Frequency: Hz, kHz, MHz, GHz, THz, mHz, μHz, nHz, pHz, rpm;
- Speed: kmh;
- Electric current: A, kA, MA, GA, TA, mA, μA, nA, pA;
- Temperature: °C, Δ°C, K;
- Amount of substance: mol;
- Luminous intensity: cd;
- Area: a, daa, ha;
- Volume: L, daL, hL, dL, cL, mL, μL, nL, pL;
- Force: N, daN, hN, kN, MN, GN, TN, gf, kgf, tf, dyn;
- Moment: Nm, kNm;
- Pressure: Pa, daPa, hPa, kPa, MPa, GPa, TPa, dPa, cPa, mPa, μPa, nPa, pPa,  
      bar, mbar, μbar, atm, at, Torr, mmHg;
- Viscosity: P, cP, St, cSt;
- Energy work: J, kJ, MJ, GJ, TJ, mJ, μJ, nJ, pJ,  
         Wh, kWh, MWh, GWh, TWh, mWh, μWh, nWh, pWh,  
         eV, keV, MeV, GeV, TeV, PeV, EeV, cal, kcal, erg;
- Power: W, kW, MW, GW, TW, mW, μW, nW, pW, hpM, ks,  
     VA, kVA, MVA, GVA, TVA, mVA, μVA, nVA, pVA,  
     VAR, kVAR, MVAR, GVAR, TVAR, mVAR, μVAR, nVAR, pVAR;
- Electric charge: C, kC, MC, GC, TC, mC, μC, nC, pC, Ah, mAh;
- Potential: V, kV, MV, GV, TV, mV, μV, nV, pV;
- Capacitance: F, kF, MF, GF, TF, mF, μF, nF, pF;
- Resistance: Ω, kΩ, MΩ, GΩ, TΩ, mΩ, μΩ, nΩ, pΩ;
- Conductance: S, kS, MS, GS, TS, mS, μS, nS, pS, ℧, k℧, M℧, G℧, T℧, m℧, μ℧, n℧, p℧;
- Magnetic flux: Wb , kWb, MWb, GWb, TWb, mWb, μWb, nWb, pWb;
- Magnetic flux density: T, kT, MT, GT, TT, mT, μT, nT, pT;
- Inductance: H, kH, MH, GH, TH, mH, μH, nH, pH;
- Luminous flux: lm;
- Illuminance: lx;
- Radioactivity: Bq, kBq, MBq, GBq, TBq, mBq, μBq, nBq, pBq, Ci, Rd;
- Absorbed dose: Gy, kGy, MGy, GGy, TGy, mGy, μGy, nGy, pGy;
- Equivalent dose: Sv, kSv, MSv, GSv, TSv, mSv, μSv, nSv, pSv;
- Catalytic activity: kat;

### Non-Metric Units (Imperial/US)

- Mass: gr, dr, oz, lb (or lbm, lb_m), kipm (or kip_m), st, qr,  
     cwt (or cwt_UK, cwt_US), ton (or ton_UK, ton_US), slug;
- Length: th, in, ft, yd, ch, fur, mi, ftm (or ftm_UK, ftm_US),  
       cable (or cable_UK, cable_US), nmi, li, rod, pole, perch, lea;
- Speed: mph, knot;
- Temperature: °F, Δ°F, °R;
- Area: rood, ac;
- Volume, fluid: fl_oz, gi, pt, qt, gal, bbl, or:  
        fl_oz_UK, gi_UK, pt_UK, qt_UK, gal_UK, bbl_UK,  
        fl_oz_US, gi_US, pt_US, qt_US, gal_US, bbl_US;
- Volume, dry: (US) pt_dry, (US) qt_dry, (US) gal_dry, (US) bbl_dry,  
          pk (or pk_UK, pk_US), bu (or bu_UK, bu_US);
- Force: ozf (or oz_f), lbf (or lb_f), kip (or kipf, kip_f), tonf (or ton_f), pdl;
- Pressure: osi, osf, psi, psf, ksi, ksf, tsi, tsf, inHg;
- Energy/work: BTU, therm (or therm_UK, therm_US), quad;
- Power: hp, hpE, hpS.

Angle units are accepted by trigonometric functions, and they override all other settings.
Inverse trigonometric functions return unitless values by default.
If you want them to return the result in the current units, you have to define a variable: *ReturnAngleUnits* = 1.

Literals that follow numbers immediately are parsed as units, e.g. "2 m". Standalone literals can be either units or variables, e.g. "N\*m". The rules for parsing are as follows: If a literal has not been defined as a variable, it is parsed as a unit.
Otherwise, it is parsed as a variable, even if a unit with the same name exists.
If you put a dot before the literal, you will force it to be parsed as a unit, even if a variable with the same name exists, e.g. ".N\*.m".

## Custom Units

You can define your own "custom" units and use them like any others in your code.
Defining a unit is similar to defining a variable, but the name must be prefixed with a dot ".":

```calcpad
.*Name* = expression
```

Names can include some currency symbols like: €, £, ₤, ¥, ¢, ₽, ₹, ₩, ₪. If you need to create a unit, that derive from others, you can write an expression with numbers and units on the right side.
You can also define dimensionless units, like currency (USD, EUR, €, ₤) or information (bit, byte, KiB, etc), by specifying "= 1" for the first unit and setting the others as multiples.
For example:

```calcpad
.bit = 1
.byte = 8*bit
.KiB = 1024*byte
...
```

Custom dimensionless units exist in a special (ninth) non-physical dimension.
That is how they do not cancel or convert to other dimensionless units, like percents or angles, when mixed.
However, if you have two types of dimensionless units in a single file, they will exist in the same dimension, so you should avoid mixing them.
