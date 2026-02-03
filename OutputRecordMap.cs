/// <summary>
/// CSV mapping for OutputRecord.
/// Ensures the mileage-by-state CSV columns appear in business order,
/// rather than alphabetical order (CsvHelper default).
/// </summary>
using CsvHelper.Configuration;
public sealed class OutputRecordMap : ClassMap<OutputRecord>
{
    public OutputRecordMap()
    {
        /* --------------------------------------------------------------
           Order matters for customer-facing CSV files!
           This map ensures CSV columns appear exactly like:
              travel_id, travel_dt, State, Rate, Miles,
              Deducted, Final_Mile, Reimbursement
        ---------------------------------------------------------------*/
        Map(m => m.travel_id);
        Map(m => m.travel_dt);
        Map(m => m.State);
        Map(m => m.Rate);
        Map(m => m.Miles);
        Map(m => m.Deducted);
        Map(m => m.Final_Mile);
        Map(m => m.Reimbursement);
    }
}