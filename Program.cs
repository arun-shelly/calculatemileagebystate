using CsvHelper;
using CsvHelper.Configuration;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Globalization;

internal class Program
{

    /* --------------------------------------------------------------
     STATE REIMBURSEMENT RATES
     --------------------------------------------------------------
     This dictionary defines mileage reimbursement rates by state.

     • Key   = Postal state abbreviation (e.g., "CA", "IL")
     • Value = Rate per mile (USD)

     High‑pay states (at $0.70/mile) are defined here.
     All other states default to $0.30/mile in the logic.

     WHY THIS IS SEPARATE:
     ---------------------
     Deduction and reimbursement calculations depend on knowing
     which states qualify for the higher reimbursement rate.  
     The deduction engine ALWAYS deducts from high‑rate states first.

     To add additional high‑rate states in the future, simply add:
         { "XX", 0.70 }
     --------------------------------------------------------------*/
    static readonly Dictionary<string, double> StateRates = new()
{
    { "CA", 0.70 },
    { "IL", 0.70 },
    { "MA", 0.70 }
};



    /* --------------------------------------------------------------
       FIPS → POSTAL STATE CODE LOOKUP
       --------------------------------------------------------------
       This dictionary converts U.S. Census FIPS state codes
       (string values like "13", "45", "28") into two‑letter postal
       state abbreviations (e.g., GA, SC, MS).

       The GeoJSON file us_states.geojson stores state identifiers
       using the "STATE" FIPS field.  
       The mileage engine ALWAYS uses postal abbreviations internally,
       because:

          • Postal codes are human‑readable.
          • Reimbursement rules reference postal codes.
          • CSV outputs should use postal codes.
          • High‑pay state logic is keyed by postal code.

       WHY WE NEED THIS:
       -----------------
       Without converting FIPS → postal, the system would:
         • Fail to match high‑pay states correctly
         • Output numeric codes (01, 13, 28) instead of GA, AL, MS
         • Misalign with business rules and summary reports

       If the GeoJSON dataset is ever updated to use different fields,
       this mapping ensures the mileage engine remains stable.

       SOURCE OF FIPS CODES:
       ---------------------
       U.S. Census Bureau — official federal standard.
       https://www.census.gov

       To add new mappings (e.g., territories), extend this list.
       --------------------------------------------------------------*/
    static readonly Dictionary<string, string> FipsToPostal = new()
{
    { "01", "AL" },
    { "02", "AK" },
    { "04", "AZ" },
    { "05", "AR" },
    { "06", "CA" },
    { "08", "CO" },
    { "09", "CT" },
    { "10", "DE" },
    { "11", "DC" },
    { "12", "FL" },
    { "13", "GA" },
    { "15", "HI" },
    { "16", "ID" },
    { "17", "IL" },
    { "18", "IN" },
    { "19", "IA" },
    { "20", "KS" },
    { "21", "KY" },
    { "22", "LA" },
    { "23", "ME" },
    { "24", "MD" },
    { "25", "MA" },
    { "26", "MI" },
    { "27", "MN" },
    { "28", "MS" },
    { "29", "MO" },
    { "30", "MT" },
    { "31", "NE" },
    { "32", "NV" },
    { "33", "NH" },
    { "34", "NJ" },
    { "35", "NM" },
    { "36", "NY" },
    { "37", "NC" },
    { "38", "ND" },
    { "39", "OH" },
    { "40", "OK" },
    { "41", "OR" },
    { "42", "PA" },
    { "44", "RI" },
    { "45", "SC" },
    { "46", "SD" },
    { "47", "TN" },
    { "48", "TX" },
    { "49", "UT" },
    { "50", "VT" },
    { "51", "VA" },
    { "53", "WA" },
    { "54", "WV" },
    { "55", "WI" },
    { "56", "WY" }
};

    static async Task Main()
    {
        /* -----------------------------------------------------------
           Load input CSVs containing travel-level and leg-level data.
           TravelItems  = high-level trip metadata (deductions, amounts)
           TravelDetail = individual GPS-based legs making up a trip
        ------------------------------------------------------------*/
        var travelItems = LoadCsv<TravelItem>("Data/Input/TravelItems.csv");
        var travelDetails = LoadCsv<TravelDetail>("Data/Input/TravelItemDetails.csv");

        Console.WriteLine("Loading state boundaries...");

        /* -----------------------------------------------------------
           Load U.S. state polygons from GeoJSON.
           These geometric shapes represent official state borders and
           will be used to intersect with travel LineStrings.
        ------------------------------------------------------------*/
        var states = LoadStatesFromGeoJson("Data/us_states.geojson");
        Console.WriteLine("Loaded states: " + states.Count);

        // Final output list of state-by-state mileage records
        List<OutputRecord> results = new();

        /* -----------------------------------------------------------
           Group all travel legs (TravelItemDetails) by travel_id.
           A travel_id may contain multiple legs; deductions must be
           applied ONCE per travel_id, not per leg.
        ------------------------------------------------------------*/
        var groupedTravel = travelDetails
            .GroupBy(d => d.travel_id)
            .ToList();

        /* -----------------------------------------------------------
           Iterate through each group of legs belonging to the same trip.
           For each travel_id:
           1. Build geometry per leg
           2. Compute mileage per state
           3. Apply deduction ONCE
           4. Produce final reimbursement rows
        ------------------------------------------------------------*/
        foreach (var group in groupedTravel)
        {
            string travelId = group.Key;

            // Retrieve the TravelItem record (deduction, actual amount, etc.)
            var item = travelItems.First(i => i.travel_id == travelId);

            // Holds all state mileage entries for ALL legs under this travel_id
            var allLegStateMiles = new List<StateMileage>();

            /* -----------------------------------------------------------
               STEP A — Process each leg and compute per-state mileage.
               This includes:
                 - Constructing the LineString from GPS points
                 - Intersecting that line with state polygons
                 - Computing haversine mileage per segment
                 - Sorting states by travel order (GA → AL → MS, etc.)
            ------------------------------------------------------------*/
            foreach (var detail in group)
            {
                // Construct geometric line for the leg
                Geometry line = BuildLine(detail);

                // Raw list of which states this leg intersects + mileage
                var raw = IntersectLineWithStates(line, states);

                // Order these state segments in actual travel order
                var orderedStates = BuildStateSequence(line, raw, states);

                // Append to full trip list (multiple legs)
                allLegStateMiles.AddRange(orderedStates);
            }

            /* -----------------------------------------------------------
               STEP B — Deduct Miles ONCE per travel_id.
               Deduct miles starting from high-pay states → low-pay states.
               Modify StateMileage.Deducted and Final_Mile values.
            ------------------------------------------------------------*/
            ApplyDeductMiles(item.deduct_miles, allLegStateMiles);

            /* -----------------------------------------------------------
               STEP C — Convert state mileage into final reimbursement records.
               For each state:
                 - Miles before deduction
                 - Miles after deduction
                 - Rate (0.70 or 0.30)
                 - Reimbursement (Final_Mile × Rate)
            ------------------------------------------------------------*/
            foreach (var s in allLegStateMiles)
            {
                double rate = StateRates.ContainsKey(s.State) ? StateRates[s.State] : 0.30;
                double finalMiles = s.Miles - s.Deducted;
                double reimbursement = finalMiles * rate;

                results.Add(new OutputRecord
                {
                    travel_id = travelId,
                    travel_dt = item.travel_dt,
                    State = s.State,
                    Rate = rate,
                    Miles = s.Miles,
                    Deducted = s.Deducted,
                    Final_Mile = finalMiles,
                    Reimbursement = reimbursement
                });
            }
        } // END foreach travel_id

        /* -----------------------------------------------------------
           STEP 6 — Sort output for consistent CSV formatting
           Sorted by travel_id then alphabetical state for readability.
        ------------------------------------------------------------*/
        var sorted = results
            .OrderBy(r => r.travel_id)
            .ThenBy(r => r.State)
            .ToList();

        ExportCsv("Data/Output/AllStateMileageOutput.csv", sorted);

        /* -----------------------------------------------------------
           STEP 7 — Filter to only travel_ids that include high-pay states.
           High-pay states: CA, IL, MA
           Used to generate separate reporting.
        ------------------------------------------------------------*/
        var travelIdsWithHighPay = sorted
            .Where(r => StateRates.ContainsKey(r.State))
            .Select(r => r.travel_id)
            .Distinct()
            .ToHashSet();

        var filtered = sorted
            .Where(r => travelIdsWithHighPay.Contains(r.travel_id))
            .ToList();

        ExportCsv("Data/Output/HighPayTravelOnly.csv", filtered);

        /* -----------------------------------------------------------
           STEP 8 — Generate summary file.
           Summary includes:
             - travel_id
             - travel_date
             - actual_distance (sum from TravelItems)
             - actual_amount (paid)
             - MilesByState (calculated final miles)
             - adjusted_amount (calculated reimbursement)

           Only includes travel IDs that contained high-pay states.
        ------------------------------------------------------------*/
        var highPayIds = travelIdsWithHighPay;

        var summary = sorted
            .Where(r => highPayIds.Contains(r.travel_id))
            .GroupBy(r => r.travel_id)
            .Select(g =>
            {
                string id = g.Key;
                var items = travelItems.Where(t => t.travel_id == id);

                return new SummaryRecord
                {
                    travel_id = id,
                    travel_dt = items.First().travel_dt,
                    travel_distance = items.Sum(t => t.travel_distance),
                    actual_amount = items.Sum(t => t.actual_amount),
                    MilesByState = g.Sum(x => x.Final_Mile),
                    adjusted_amount = g.Sum(x => x.Reimbursement)
                };
            })
            .OrderBy(x => x.travel_id)
            .ToList();

        ExportCsv("Data/Output/TravelSummaryComparison.csv", summary);

        Console.WriteLine("Done.");
        Console.WriteLine("Output 1: AllStateMileageOutput.csv");
        Console.WriteLine("Output 2: HighPayTravelOnly.csv");
        Console.WriteLine("Output 3: TravelSummaryComparison.csv");
    }

    /* =============================================================
       CSV LOADING AND EXPORT INFRASTRUCTURE
       -------------------------------------------------------------
       This section defines:
         - Generic CSV loader
         - Generic CSV exporter
         - CSV class maps ensuring columns appear in correct order
           (CsvHelper sorts alphabetically by default)
       =============================================================*/

    /// <summary>
    /// Loads a CSV file using CsvHelper and maps each row to a class T.
    /// This method:
    ///   - Opens the file with StreamReader
    ///   - Uses CsvHelper to read rows
    ///   - Maps columns by property name
    /// </summary>
    static List<T> LoadCsv<T>(string file)
    {
        using var reader = new StreamReader(file);   // Open CSV file for reading
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        // Reads the CSV into a list of T (TravelItem, TravelDetail, etc.)
        return csv.GetRecords<T>().ToList();
    }


    /// <summary>
    /// Exports a list of rows to a CSV file.
    /// Detects if the row type requires a custom column order.
    /// </summary>
    static void ExportCsv<T>(string file, List<T> rows)
    {
        using var writer = new StreamWriter(file);  // Create / overwrite output file
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        /* --------------------------------------------------------------
           Register the correct ClassMap based on the type being written.
           This ensures:
             - OutputRecord columns appear in correct business order.
             - SummaryRecord columns appear in correct summary order.
           Without this, CsvHelper outputs alphabetically.
        ---------------------------------------------------------------*/
        if (typeof(T) == typeof(OutputRecord))
            csv.Context.RegisterClassMap<OutputRecordMap>();

        if (typeof(T) == typeof(SummaryRecord))
            csv.Context.RegisterClassMap<SummaryRecordMap>();

        // Write all rows to the CSV file in the mapped order
        csv.WriteRecords(rows);
    }

    /* =============================================================
       GEOJSON LOADING & GEOMETRY CONSTRUCTION
       -------------------------------------------------------------
       This section is responsible for:
         - Loading U.S. state polygons from us_states.geojson
         - Converting FIPS state codes to postal abbreviations
         - Providing introspection utilities
         - Building LineString geometry from GPS coordinates

       These methods together form the geographic backbone of the
       mileage-by-state calculation engine.
       =============================================================*/
    /// <summary>
    /// Loads the U.S. state polygons from a GeoJSON file.
    /// Each state is stored as:
    ///   - A Feature with polygon/multipolygon geometry
    ///   - FIPS code in the 'STATE' property
    ///
    /// The method:
    ///   1. Parses the GeoJSON into FeatureCollection
    ///   2. Extracts FIPS code and converts it to postal abbreviation
    ///   3. Stores (postal → polygon) in a dictionary
    ///
    /// This dictionary is later used to intersect travel LineStrings
    /// with official state boundaries (NetTopologySuite geometry).
    /// </summary>
    static Dictionary<string, Geometry> LoadStatesFromGeoJson(string file)
    {
        // Load file text into memory
        var json = File.ReadAllText(file);

        // Parse JSON into NTS FeatureCollection using GeoJsonReader
        var reader = new GeoJsonReader();
        var fc = reader.Read<FeatureCollection>(json);

        var dict = new Dictionary<string, Geometry>();

        /* --------------------------------------------------------------
           Iterate through each state feature in the GeoJSON.
           Each feature contains metadata (attributes) + polygon geometry.
        ---------------------------------------------------------------*/
        foreach (var f in fc)
        {
            var atts = f.Attributes;

            // Ensure the dataset includes the expected STATE (FIPS) field
            string raw = atts.Exists("STATE") ? atts["STATE"].ToString() : null;
            if (raw == null)
                throw new Exception("STATE field missing in GeoJSON.");

            // Convert FIPS → Postal (e.g., "13" → "GA")
            string st = FipsToPostal.ContainsKey(raw) ? FipsToPostal[raw] : raw;

            // Store the geometry in dictionary indexed by postal code
            dict[st] = f.Geometry;   // Geometry can be Polygon or MultiPolygon
        }

        return dict;
    }
    /// <summary>
    /// Utility method used for debugging a new GeoJSON file.
    /// Prints the attribute names found in the first feature so
    /// developers can identify the correct property keys.
    ///
    /// Helpful when switching datasets or validating schema.
    /// </summary>
    static void ListGeoJsonAttributes(string file)
    {
        var json = File.ReadAllText(file);
        var reader = new GeoJsonReader();
        var fc = reader.Read<FeatureCollection>(json);

        foreach (var f in fc)
        {
            Console.WriteLine("Feature attributes:");
            foreach (var name in f.Attributes.GetNames())
            {
                Console.WriteLine(" - " + name);
            }

            break; // Only show first feature's attributes
        }
    }
    /// <summary>
    /// Constructs a NetTopologySuite LineString from start and end GPS coordinates.
    /// This geometric line represents the actual travel leg.
    /// 
    /// The mileage engine:
    ///   - Intersects this line with state polygons
    ///   - Splits it into per-state segments
    ///   - Calculates Haversine distance for each segment
    ///
    /// The coordinate order uses (longitude, latitude) because NTS expects:
    ///     Coordinate(X = lon, Y = lat)
    /// </summary>
    static Geometry BuildLine(TravelDetail d)
    {
        var f = new GeometryFactory();

        /* --------------------------------------------------------------
           Create LineString from two points:
             - Start: (lon, lat)
             - End:   (lon, lat)

           This represents the straight-line path of the travel segment.
        ---------------------------------------------------------------*/
        return f.CreateLineString([
            new Coordinate(d.Start_longitude, d.Start_latitude),
        new Coordinate(d.End_longitude, d.End_latitude)
        ]);
    }

    /* =============================================================
       CORE GIS ENGINE — STATE INTERSECTION & MILEAGE SPLITTING
       -------------------------------------------------------------
       These functions perform the heavy lifting of the mileage engine:
         • Intersecting straight-line GPS paths with state polygons
         • Computing mileage inside each state using Haversine
         • Sorting states in true travel order
         • Measuring centroid distance from start (for ordering)
       =============================================================*/

    /// <summary>
    /// Intersects a travel LineString with every US state polygon to
    /// determine which states the trip crosses and how far inside each.
    ///
    /// PROCESS:
    ///   1. Check if the line intersects the state's polygon.
    ///   2. Extract intersection geometry (part = line ∩ state)
    ///   3. Identify its first and last coordinate.
    ///   4. Compute distance using Haversine (accurate on Earth’s sphere).
    ///
    /// NOTE:
    ///   Each returned StateMileage object includes ONLY raw mileage.
    ///   Deduction and final mile adjustments happen later.
    /// </summary>
    static List<StateMileage> IntersectLineWithStates(Geometry line, Dictionary<string, Geometry> states)
    {
        var list = new List<StateMileage>();

        /* --------------------------------------------------------------
           Iterate every state polygon, checking intersection.
           NetTopologySuite returns:
             • Empty geometry → no intersection
             • LineString/GeometryCollection → intersection segment
        ---------------------------------------------------------------*/
        foreach (var kvp in states)
        {
            if (!line.Intersects(kvp.Value))
                continue;   // No overlap → skip state entirely

            var part = line.Intersection(kvp.Value);
            if (part.IsEmpty)
                continue;   // No geometric segment inside state

            // Extract coordinates for mileage calculation
            var coords = part.Coordinates;

            if (coords.Length >= 2)
            {
                /* ------------------------------------------------------
                   Haversine gives accurate great‑circle distance between
                   the endpoints of the state's intersection segment.

                   We use:
                     coords[0]   = entry point into state
                     coords[^1]  = exit point from state
                -------------------------------------------------------*/
                double miles = HaversineMiles(
                    coords[0].Y, coords[0].X,
                    coords[^1].Y, coords[^1].X
                );

                list.Add(new StateMileage
                {
                    State = kvp.Key,
                    Miles = miles
                });
            }
        }

        return list;
    }

    /// <summary>
    /// Orders the raw list of StateMileage entries in the ACTUAL
    /// sequence the user traveled.
    ///
    /// WHY THIS MATTERS:
    ///   Alphabetical ordering (SC, GA, AL) is WRONG.
    ///   We must determine:
    ///      GA → AL → MS
    ///   based on the path of the travel line.
    ///
    /// IMPLEMENTATION:
    ///   For each state segment:
    ///     • Compute state intersection "part"
    ///     • Compute centroid of part
    ///     • Project centroid onto LineString
    ///     • Measure distance from starting point
    ///   Then sort states by that distance.
    /// </summary>
    static List<StateMileage> BuildStateSequence(
        Geometry line,
        List<StateMileage> stateMilesRaw,
        Dictionary<string, Geometry> states)
    {
        var orderedStates = new List<StateIntersection>();

        /* --------------------------------------------------------------
           Loop through each raw mileage entry and compute its
           "distance from start" using centroid projection.
        ---------------------------------------------------------------*/
        foreach (var sm in stateMilesRaw)
        {
            // Build the intersection geometry again to capture full polygon shape
            var part = line.Intersection(states[sm.State]);

            // Centroid projection distance from start
            double dist = GetDistanceFromStart((LineString)line, part);

            orderedStates.Add(new StateIntersection
            {
                State = sm.State,
                DistanceFromStart = dist,
                Mileage = sm
            });
        }

        /* --------------------------------------------------------------
           Now sort by the computed distance:
             • First state entered = smallest distance
             • Next state = larger distance
             • and so on
        ---------------------------------------------------------------*/
        return orderedStates
            .OrderBy(s => s.DistanceFromStart)
            .Select(s => s.Mileage)
            .ToList();
    }
    /// <summary>
    /// Computes how far along the travel LineString a state's centroid lies.
    ///
    /// PURPOSE:
    ///   Used to determine entry order of states.
    ///
    /// PROCESS:
    ///   1. Compute centroid of state-intersection geometry.
    ///   2. Walk line segment-by-segment from the start.
    ///   3. When centroid falls inside segment bounding box,
    ///      measure partial distance to centroid.
    ///   4. Return cumulative distance.
    ///
    /// This provides a monotonic "travel progression" metric.
    /// </summary>
    static double GetDistanceFromStart(LineString line, Geometry statePart)
    {
        // Compute centroid of intersection geometry
        var centroid = statePart.Centroid;

        // Get starting coordinate of the LineString
        var start = line.GetCoordinateN(0);

        double dist = 0;

        /* --------------------------------------------------------------
           Iterate through the line's coordinate sequence.
           Each pair (a → b) represents a tiny straight segment.
           We accumulate distance until we reach the segment
           containing the centroid.
        ---------------------------------------------------------------*/
        for (int i = 0; i < line.NumPoints - 1; i++)
        {
            var a = line.GetCoordinateN(i);
            var b = line.GetCoordinateN(i + 1);

            // Euclidean length (fine because coordinates are small deltas)
            double segLength = new Coordinate(a).Distance(new Coordinate(b));

            /* ----------------------------------------------------------
               Check whether the centroid lies within this segment using
               a bounding box overlap test — fast and adequate for GIS.
            -----------------------------------------------------------*/
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);

            bool centroidInSegmentBounds =
                centroid.X >= minX && centroid.X <= maxX &&
                centroid.Y >= minY && centroid.Y <= maxY;

            if (centroidInSegmentBounds)
            {
                // Add partial distance from point a → centroid
                dist += new Coordinate(a).Distance(
                    new Coordinate(centroid.Coordinate));

                return dist;   // Found our ordering distance
            }

            // Add full segment length and continue scanning
            dist += segLength;
        }

        // Fallback if centroid fails bounding box test
        return dist;
    }

    /// <summary>
    /// Computes great‑circle distance between two GPS points (lat/lon)
    /// using the Haversine formula.
    ///
    /// WHY HAVERSINE:
    ///   - Correct for Earth curvature
    ///   - GIS standard for geodesic distance
    ///   - Much more accurate than degree-based approximations
    ///
    /// Used for:
    ///   • Mileage inside each state
    ///   • Mileage across travel legs
    /// </summary>
    static double HaversineMiles(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 3958.8; // Radius of Earth in miles

        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;

        lat1 *= Math.PI / 180;
        lat2 *= Math.PI / 180;

        double a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1) * Math.Cos(lat2) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;   // Return distance in miles
    }

    /* =============================================================
       DEDUCTION ENGINE — APPLY DEDUCTION ONCE PER TRAVEL_ID
       -------------------------------------------------------------
       The deduction logic must:
         • Apply deduct_miles only ONE TIME per travel_id
         • Prioritize high‑pay states first (CA, IL, MA)
         • Deduct from low‑pay states after high‑pay states
         • Never deduct more than the total available miles
         • Modify the StateMileage objects in-place:
              - Miles   (original miles)
              - Deducted (amount deducted)
              - Final_Mile = Miles - Deducted (computed later)
       This method does NOT compute final reimbursement — only deduction.
       =============================================================*/
    /// <summary>
    /// Applies the deduction miles for a given travel_id across all
    /// state mileage records (from all legs of the trip).
    ///
    /// RULES:
    ///   • Deduct miles ONCE (across all legs)
    ///   • High‑rate states are deducted FIRST
    ///   • Low‑rate states are deducted AFTER high‑rate states
    ///   • Deduction never exceeds available miles
    ///   • Operates directly on the provided stateMiles list
    ///
    /// INPUT:
    ///   deduct  = total deduction (e.g., 20.82)
    ///   stateMiles = combined list of all StateMileage entries for trip
    /// </summary>
    static void ApplyDeductMiles(double deduct, List<StateMileage> stateMiles)
    {
        /* --------------------------------------------------------------
           STEP 1: Order states for deduction priority.
           High‑pay states MUST be deducted first because:
             • Their reimbursement rate is higher
             • Company policy prefers whole deduction from highest-cost states

           Sorting:
             TRUE  → high‑rate states first
             FALSE → low‑rate states afterwards
        ---------------------------------------------------------------*/
        var ordered = stateMiles
            .OrderByDescending(x => StateRates.ContainsKey(x.State))
            .ToList();

        /* --------------------------------------------------------------
           STEP 2: Walk through ordered states and subtract miles until:
             • deduction is fully used up (deduct <= 0)
             • OR no more miles remain in states

           NOTE:
             Deduction is applied to *raw Miles*, not Final_Mile.
             Deducted amount is tracked in each StateMileage object.
        ---------------------------------------------------------------*/
        foreach (var sm in ordered)
        {
            if (deduct <= 0)
                break; // Deduction fully applied

            // Deduct up to available miles for this state
            double take = Math.Min(sm.Miles, deduct);

            sm.Deducted = take;   // Store deducted amount
            deduct -= take;       // Reduce remaining deduction
        }

        /* --------------------------------------------------------------
           STEP 3: Any remaining states past this loop receive
                   NO deduction (sm.Deducted remains 0).

           The deduction process is complete when:
               • deduct reaches zero, OR
               • all states have been visited.
        ---------------------------------------------------------------*/
    }
}

/* =============================================================
   MODEL DEFINITIONS
   -------------------------------------------------------------
   These classes represent all major data structures used by the
   mileage-by-state engine.

   Models include:
     • TravelItem       : High-level trip metadata
     • TravelDetail     : Individual GPS-based legs
     • StateMileage     : Mileage data per state segment
     • StateIntersection: Intermediate GIS ordering structure
     • OutputRecord     : Per-state mileage output row
     • SummaryRecord    : Per-travel_id aggregated summary

   Each class is thoroughly documented below.
   =============================================================*/
/// <summary>
/// Represents one travel record from TravelItems.csv.
/// 
/// A TravelItem is the parent container for one or more TravelDetail legs.
/// It includes:
///   • travel_date
///   • total traveled distance (reported, not computed)
///   • deduct_miles (applied ONCE per travel_id)
///   • actual_amount (actual reimbursement paid)
///
/// NOTE:
///   Deduction and summary calculations rely heavily on this model.
/// </summary>
public class TravelItem
{
    public string travel_id { get; set; }            // Unique trip ID (primary key)
    public string travel_dt { get; set; }            // Trip date
    public string job_no { get; set; }
    public string wave_no { get; set; }
    public string task_no { get; set; }
    public string store_id { get; set; }
    public string merch_no { get; set; }
    public string rep_homestate { get; set; }
    public double travel_distance { get; set; }      // Total distance reported by system
    public double deduct_miles { get; set; }         // Deduction applied ONCE per travel_id
    public double actual_amount { get; set; }        // Amount actually paid
}

/// <summary>
/// Represents one travel segment (leg) from TravelItemDetails.csv.
/// 
/// A travel_id may contain MANY TravelDetail rows.
/// Each row contains:
///   • Start GPS coordinate
///   • End GPS coordinate
///   • Raw travel distance (reported)
///
/// GIS engine converts these coordinates into LineString geometry
/// for state intersection + Haversine mileage.
/// </summary>
public class TravelDetail
{
    public string travel_id { get; set; }            // Foreign key linking to TravelItem
    public string travel_dt { get; set; }            // Date for this leg
    public double Start_latitude { get; set; }       // GPS start (lat)
    public double Start_longitude { get; set; }      // GPS start (lon)
    public double End_latitude { get; set; }         // GPS end (lat)
    public double End_longitude { get; set; }        // GPS end (lon)
    public double travel_distance { get; set; }      // System-reported miles for this leg
}

/// <summary>
/// Represents mileage inside ONE state for ONE segment of a travel leg.
/// 
/// Contains:
///   • Miles: original computed mileage (via Haversine)
///   • Deducted: deduction amount applied to this state
///
/// Final mileage:
///      Final_Mile = Miles - Deducted
///
/// StateMileage objects are combined across ALL legs of a travel_id.
/// </summary>
public class StateMileage
{
    public string State { get; set; }                // Postal abbreviation (GA, SC, MS, etc.)
    public double Miles { get; set; }                // Raw mileage inside state (Haversine)
    public double Deducted { get; set; }             // Mileage deducted during ApplyDeductMiles()
}



/// <summary>
/// Intermediate helper class used to determine the order in which
/// states appear along the travel path.
///
/// Contains:
///   • DistanceFromStart: distance along LineString where centroid
///                         of state-intersection lies (GIS ordering)
///   • Mileage: reference back to the StateMileage object
///
/// This class is NOT included in final output — only used for sorting.
/// </summary>
public class StateIntersection
{
    public string State { get; set; }                // Postal code
    public double DistanceFromStart { get; set; }    // GIS distance metric for sorting
    public StateMileage Mileage { get; set; }        // Associated mileage record
}

/// <summary>
/// Represents one row in AllStateMileageOutput.csv or HighPayTravelOnly.csv.
/// 
/// This is the main per-state mileage breakdown containing:
///   • Raw miles
///   • Deducted miles
///   • Final miles
///   • Reimbursement dollars
///
/// Sorted by:
///   travel_id, State
/// </summary>
public class OutputRecord
{
    public string travel_id { get; set; }            // Trip key
    public string travel_dt { get; set; }            // Trip date
    public string State { get; set; }                // State (postal)
    public double Rate { get; set; }                 // Reimbursement rate (0.70 or 0.30)
    public double Miles { get; set; }                // Raw miles inside this state
    public double Deducted { get; set; }             // Deducted miles applied
    public double Final_Mile { get; set; }           // Miles after deduction
    public double Reimbursement { get; set; }        // Final_Mile × Rate
}

/// <summary>
/// Represents the summary-level output for each travel_id.
///
/// Contains:
///   • travel_distance: system-reported total miles
///   • actual_amount: amount paid (from TravelItem)
///   • MilesByState: sum of all Final_Mile values
///   • adjusted_amount: sum of all reimbursement values
///
/// Used to compare calculated reimbursement vs. actual reimbursement.
/// Only includes travel_ids that contain high‑pay states.
/// </summary>
public class SummaryRecord
{
    public string travel_id { get; set; }            // Unique travel ID
    public string travel_dt { get; set; }            // Trip date from TravelItem
    public double travel_distance { get; set; }      // Reported distance for entire trip
    public double actual_amount { get; set; }        // Actual amount paid
    public double MilesByState { get; set; }         // Sum of Final_Mile per travel_id
    public double adjusted_amount { get; set; }      // Sum of reimbursement per travel_id
}