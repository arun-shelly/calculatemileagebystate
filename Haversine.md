# 🌍 Haversine Formula — Technical Reference

The **Haversine formula** is a widely used equation for calculating the great‑circle distance between two latitude/longitude points on the surface of the Earth. It is critical for accurate travel‑distance computations when using raw GPS coordinates.

This file explains what the formula does, why we use it, how it is applied in our mileage engine, and provides resources for further reading.

***

# 📘 What Is the Haversine Formula?

The Haversine formula computes the **shortest distance over the Earth’s surface** (a sphere) between two coordinate points:

*   Point A: latitude φ₁, longitude λ₁
*   Point B: latitude φ₂, longitude λ₂

The result is the great‑circle distance in **miles** (or kilometers).

***

# 📐 Why We Use Haversine for Mileage Calculations

Our system uses start and end GPS coordinates to determine mileage inside each state. To achieve accuracy:

*   We must account for the Earth’s curvature
*   Simple degree‑based approximations (e.g., × 69 miles) are inaccurate
*   Straight‑line “Euclidean” distance is wrong for coordinates on a sphere
*   Haversine provides sub‑meter accuracy with minimal computation

Using the Haversine formula ensures mileage calculations closely match real‑world GPS distances, making them suitable for reimbursement, auditing, and compliance.

***

# 🧮 The Haversine Formula

    a = sin²((φ₂ − φ₁) / 2) 
        + cos(φ₁) * cos(φ₂) * sin²((λ₂ − λ₁) / 2)

    c = 2 * atan2( √a, √(1−a) )

    d = R * c

Where:

*   φ₁, φ₂ = latitudes in radians
*   λ₁, λ₂ = longitudes in radians
*   R = Earth’s radius (3958.8 miles)
*   d = great‑circle distance in miles

***

# 🛰 Application in Our Mileage Engine

We use Haversine to compute:

### ✔ Mileage inside each state

Each state segment is formed by intersecting the travel line with the state polygon.  
We compute:

    MilesInState = Haversine(firstPointInState, lastPointInState)

### ✔ Mileage used for reimbursement

Final miles after deductions use the Haversine-calculated values.

### ✔ Accurate multi-leg trips

Haversine ensures consistent distances when aggregating multiple legs.

***

# 🗺 Visual Diagram of the Geometry

### Great‑circle distance between two GPS points:

                 (North Pole)
                      |
                      |
          ● P1--------+--------● P2
            \        |        /
             \       |       /
              \      |      /
               \     |     /
                \    |    /
                 \   |   /
                    (Earth)

### Great‑circle arc

         P1 ●----------------------------------● P2
               \                              /
                \                            /
                 \                          /
                  \        Earth           /
                   \                      /
                    ----------------------
                       Great-circle arc

The arc length is what the Haversine formula returns.

***

# 🔍 GIF‑Style Step‑by‑Step Visualization

    Frame 1 — Two points
       ● P1                              ● P2
         \                                /
          \                              /
            \         Earth Sphere       /

    Frame 2 — Radius lines
       ● P1 -----[ radius lines ]------ ● P2

    Frame 3 — Central angle
       ● P1 -- c -- center -- c -- ● P2

    Frame 4 — Great-circle arc
       ● P1 ======================== ● P2

    Frame 5 — Formula applied
       d = R × c

***

# 📎 Official References

### Wikipedia — Haversine Formula

<https://en.wikipedia.org/wiki/Haversine_formula>

### Movable Type Scripts (GIS Reference)

<https://www.movable-type.co.uk/scripts/latlong.html>

### Aviation Formulary (Great‑circle distance)

<http://edwilliams.org/avform.htm#Dist>

### NOAA (Geodesic Information)

<https://www.nws.noaa.gov/>

***

# 💡 Summary

The Haversine formula provides:

*   Accurate great‑circle distance
*   Reliable results for travel mileage
*   No dependence on external APIs
*   Excellent performance and precision

This makes it ideal for computing **state‑split mileage** in our reimbursement engine.

***


