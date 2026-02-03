# **us\_states.geojson — Technical Documentation**

The file `us_states.geojson` is a GeoJSON dataset containing the official geographic boundaries of all U.S. states. It is a core component of the mileage‑calculation engine, enabling accurate state‑level mileage segmentation for travel legs based on GPS coordinates.

***

# 📘 Purpose of `us_states.geojson`

The mileage system must determine:

*   Which states a travel path enters
*   How many miles are traveled inside each state
*   The order in which states are crossed
*   State‑specific reimbursement rates
*   Accurate audit‑ready mileage summaries

To accomplish this, the system intersects travel paths with state polygons defined in `us_states.geojson`.

This file provides:

*   High‑precision state boundary polygons
*   FIPS identifiers (“01”, “13”, etc.)
*   Metadata such as state names, census info

***

# 🗺 What is GeoJSON?

GeoJSON is a standard JSON format for geographic data.

Supported geometry types include:

*   `Point`
*   `LineString`
*   `Polygon`
*   `MultiPolygon` (used for states like Hawaii and Michigan)
*   `FeatureCollection` (used in this file)

`us_states.geojson` stores each U.S. state as a **Feature** with:

*   `properties` (state metadata)
*   `geometry` (Polygon or MultiPolygon)

***

# 📄 Structure of the File

A typical state entry looks like:

```json
{
  "type": "Feature",
  "properties": {
    "GEO_ID": "0400000US13",
    "STATE": "13",
    "NAME": "Georgia",
    "LSAD": "State",
    "CENSUSAREA": 57906.14
  },
  "geometry": {
    "type": "Polygon",
    "coordinates": [
      [
        [-85.605165, 34.98407],
        [-85.30436, 34.9901],
        ...
      ]
    ]
  }
}
```

### Important fields

| Field      | Meaning                 | Usage                                   |
| ---------- | ----------------------- | --------------------------------------- |
| `STATE`    | FIPS state code         | Used to map to postal codes (`13 → GA`) |
| `NAME`     | State name              | Optional for debugging                  |
| `geometry` | Polygon or MultiPolygon | Used for spatial intersection           |

***

# 🔧 How the System Uses `us_states.geojson`

## 1. Load the GeoJSON into NetTopologySuite

The file is parsed with `GeoJsonReader`, producing a `FeatureCollection`.

Each feature’s geometry becomes a state polygon that can be intersected with travel lines.

***

## 2. Convert FIPS Codes → Postal Codes

The file uses FIPS codes (e.g., `"13"`, `"28"`).  
We convert these to postal codes (e.g., `"GA"`, `"MS"`) using a lookup table.

Postal codes are used for:

*   State‑rate lookup (high‑pay states)
*   CSV output clarity
*   Reimbursement logic

***

## 3. Intersect Travel Path with State Polygons

For a given travel leg:

    startCoord -------- endCoord

we build a `LineString` and compute:

    intersectionSegment = travelLine ∩ statePolygon

If not empty, this segment represents the portion of the trip traveled inside that state.

Example results:

| State | Miles |
| ----- | ----- |
| GA    | 56.3  |
| AL    | 258.8 |
| MS    | 38.3  |

***

## 4. Determine Travel Order

When multiple states are traversed, we:

1.  Take the **centroid** of each intersection segment
2.  Project it along the travel line
3.  Sort by distance‑from‑start

This ensures the correct travel sequence:

    GA → AL → MS

***

## 5. Compute Mileage Using Haversine

The system computes exact mileage inside each state using the **Haversine formula**, which accounts for Earth’s curvature.

This ensures accurate results for:

*   Short legs
*   Curved borders
*   Multi‑state trips

***

## 6. Multi‑Leg Travel Support

A single `travel_id` may contain several legs.  
We:

*   Combine miles from all legs
*   Apply deductions once
*   Produce complete per‑travel\_id state mileage

***

# 📦 Why Use GeoJSON Instead of Shapefiles?

*   Human‑readable
*   Version‑control friendly
*   Lightweight
*   Does not require `.dbf`, `.prj`, `.shx` sidecar files
*   Simpler deployment
*   Directly supported by NetTopologySuite

***

# 🔍 Keeping `us_states.geojson` Up to Date

State boundaries rarely change, but updated datasets avoid:

*   Outdated coastlines
*   Removed or corrected geometry
*   Census cartographic adjustments
*   Minor border refinements

### 📅 Recommended Update Schedule

*   Update **annually** (Jan–Feb)
*   Or whenever U.S. Census publishes new TIGER/Line data

### 📥 Official Source

U.S. Census Bureau (TIGER/Line Shapefiles):  
<https://www.census.gov/geographies/mapping-files/time-series/geo/tiger-line-file.html>

File to download:

    tl_<YEAR>_us_state.zip

### 🔄 Convert Shapefile → GeoJSON

Using MapShaper: <https://mapshaper.org>

    import tl_2024_us_state.shp
    export format=geojson us_states.geojson

Or GDAL:

    ogr2ogr -f GeoJSON us_states.geojson tl_2024_us_state.shp

### ✔ Validation Checklist

*   Confirm `"STATE"` field exists
*   Confirm polygon shapes load correctly
*   Verify FIPS codes map to postal codes
*   Run a test intersection with a known GA→AL→MS leg

***

# 🧪 Why This File Matters

`us_states.geojson` is essential for:

*   Accurate state‑split mileage
*   Correct deduction logic
*   Valid reimbursement per state
*   Auditability and consistency
*   Avoiding reliance on external APIs

If this file is incorrect or outdated, calculations may be wrong.

***

# 📄 Summary

`us_states.geojson` provides:

*   The complete geographic shapes of U.S. states
*   A reliable foundation for state‑level travel breakdown
*   Accurate computations using geometric intersection
*   A deterministic, offline solution for mileage reimbursement

Keeping this file current ensures the mileage engine remains precise and compliant.

