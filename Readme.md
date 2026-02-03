# Mileage by State Calculator

This tool calculates mileage inside each U.S. state for travel legs based on start and end GPS coordinates. It supports multi-leg travel IDs, accurate mileage computation, state-by-state reimbursement logic, and deduction rules.

## Summary
Our system calculates mileage inside each U.S. state using only the start and end GPS coordinates of each travel leg. It determines which states are crossed, how many miles were traveled in each state, and applies the appropriate reimbursement rate. State miles from all legs under the same travel ID are combined, and mileage deductions are applied once per travel ID (prioritizing high‑pay states). Finally, the system produces both detailed and summary reports for auditing and comparison against actual paid amounts.
This approach ensures accurate, consistent, and auditable mileage reimbursement without requiring turn‑by‑turn routing or external API dependencies.

***

# Mileage‑by‑State Calculation — Technical Documentation

## 1. Input Data

Two input CSV files are used:

### **TravelItems.csv**

Contains metadata per `travel_id`:

*   `travel_id`
*   `travel_dt`
*   `travel_distance`
*   `deduct_miles` (applied once per travel\_id)
*   `actual_amount` (actual paid amount)

### **TravelItemDetails.csv**

Contains one or more travel legs for each `travel_id`:

*   `travel_id`
*   `Start_latitude`, `Start_longitude`
*   `End_latitude`, `End_longitude`

***

## 2. Construct Travel Geometry

For each travel leg, a `LineString` is created using NetTopologySuite:

    (Start_latitude, Start_longitude) → (End_latitude, End_longitude)

This creates a geometric line that represents the straight‑line travel path.

***

## 3. Load U.S. State Boundary Polygons

A GeoJSON file containing polygon boundaries for all U.S. states is loaded.

Key steps:

*   Parse GeoJSON using `GeoJsonReader`
*   Extract FIPS state codes (e.g., `"13"`)
*   Convert FIPS → postal code (e.g., `"13" → "GA"`)
*   Store polygons in a dictionary:  
    `Dictionary<string, Geometry> states`

***

## 4. Intersect Travel Line With State Polygons

For each state polygon:

    if (travelLine intersects statePolygon)
        intersectedSegment = travelLine ∩ statePolygon

Each intersected segment represents the portion of the trip inside that state.

This produces **N segments** for each travel leg:

Example:

    GA: LineString segment
    AL: LineString segment
    MS: LineString segment

If the travel stays within one state, only one intersection is produced.

***

## 5. Calculate Mileage Using Haversine Formula

For each state segment:

1.  Extract the first and last coordinate of the segment
2.  Apply the **Haversine formula** to compute distance in miles

Haversine ensures accuracy by accounting for Earth’s curvature.

    Miles = Haversine(startCoord, endCoord)

This avoids the inaccuracies of degree-based approximations and matches GPS-based distance.

***

## 6. Determine Travel Order Through States

For multi‑state legs, states must be reported in the sequence they are entered.

Process:

1.  Compute the **centroid** of each intersection segment
2.  Measure its distance from the start of the travel leg using a linear scan of the line
3.  Sort states by this “distance-from-start”

Result:

    GA → AL → MS

This ensures correct ordering for deduction and reporting.

***

## 7. Multi‑Leg Aggregation (Per travel\_id)

A single travel\_id may contain multiple legs.

For each travel\_id:

1.  Collect all legs
2.  Compute state mileage per leg
3.  Merge all state mileage entries into a single list
4.  Sum miles per state across all legs

Example:

    Leg1: SC 18.51 miles
    Leg2: SC 18.45 miles

    Total SC = 36.96 miles

***

## 8. Deduction Logic (Applied Once Per travel\_id)

`deduct_miles` is a total reduction applied across **all legs**.

Deduction rules:

1.  Apply deduction **once per travel\_id**
2.  Deduct from **high‑pay states first** (CA, IL, MA)
3.  Then deduct from low‑pay states
4.  Do not exceed total available miles
5.  Deduction affects only the miles used for reimbursement, not recorded mileage

Example:

    Total state miles:
      AL: 300
      GA: 100
    Deduction: 20 miles

    Apply:
      AL (low rate) gets full 20 deducted → AL = 280

If AL were high‑rate, deduction would apply there first.

***

## 9. Reimbursement Calculation

Each state mileage is multiplied by its per‑state rate:

    Reimbursement = Final_Mile × Rate

Where:

*   High‑pay states: 0.70
*   All others: 0.30

This is computed **after** deduction has been applied.

***

## 10. Output Files

### **1. AllStateMileageOutput.csv**

*   Full per‑state mileage for every leg of every travel\_id
*   Ordered in true travel sequence

### **2. HighPayTravelOnly.csv**

*   Only travel\_ids containing **any** high‑pay state miles

### **3. TravelSummaryComparison.csv**

One row per travel\_id containing:

*   travel\_id
*   travel\_dt
*   travel\_distance
*   actual\_amount
*   MilesByState (final miles after deduction)
*   adjusted\_amount (total reimbursement)

This file is used to compare system‑calculated reimbursement vs. actual paid amounts.

***

## 🚀 Features

- Calculates miles by state using geographic intersection
- Supports multiple travel legs per travel_id
- Determines exact state traversal order
- Uses Haversine formula for accurate mileage
- Applies deduction miles **once per travel_id**
- Prioritizes high-pay states (CA, IL, MA)
- Computes reimbursement per state
- Generates multiple audit-ready CSV reports

---

## 🧠 How Mileage Is Computed

### 1. Build Travel Line
Each travel leg provides:
- Start latitude/longitude  
- End latitude/longitude  

We create a `LineString` between these points.

### 2. Load State Boundaries
U.S. state boundary polygons are loaded from a GeoJSON file.  
FIPS state codes are converted to postal abbreviations.

### 3. Intersect Line With States
The line is intersected with each state polygon, producing one or more travel segments inside each state.

### 4. Preserve Travel Order
Centroids of state segments are projected onto the main line.  
States are sorted in traversal order (e.g., `GA → AL → MS`).

### 5. Mileage Calculation
We use Haversine distance between segment endpoints for accurate mileage.

### 6. Multi-Leg Aggregation
All legs with the same travel_id are merged.

### 7. Deduction Logic
Deduction miles are applied:
- Once per travel_id  
- High-pay states first  
- Low-pay next  

### 8. Reimbursement
Mileage × rate (0.70 or 0.30).

---

## 📂 Output Files

### **1. `AllStateMileageOutput.csv`**
All states for all travel legs.

### **2. `HighPayTravelOnly.csv`**
Only rows where the travel_id includes at least one high-pay state.

### **3. `TravelSummaryComparison.csv`**
One row per travel_id:
- travel_id  
- travel_dt  
- travel_distance  
- actual_amount  
- MilesByState  
- adjusted_amount  

Used to compare actual vs. calculated reimbursement.

---

## 🛠 Requirements

- .NET 10 or later  
- NetTopologySuite  
- CsvHelper  
- GeoJSON U.S. state boundaries  

---

## 📎 Notes

- No external APIs required  
- 100% deterministic  
- Accurate for all U.S. state boundary intersections  
- Safe for batch processing large datasets  

---

# calculatemileagebystate
# calculatemileagebystate
