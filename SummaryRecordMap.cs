/// <summary>
/// CSV mapping for SummaryRecord.
/// Controls column order for TravelSummaryComparison.csv.
/// </summary>
using CsvHelper.Configuration;
public sealed class SummaryRecordMap : ClassMap<SummaryRecord>
{
    public SummaryRecordMap()
    {
        /* --------------------------------------------------------------
           Final summary output format:
             travel_id, travel_dt, travel_distance,
             actual_amount, MilesByState, adjusted_amount
        ---------------------------------------------------------------*/
        Map(s => s.travel_id);
        Map(s => s.travel_dt);
        Map(s => s.travel_distance);
        Map(s => s.actual_amount);
        Map(s => s.MilesByState);
        Map(s => s.adjusted_amount);
    }
}