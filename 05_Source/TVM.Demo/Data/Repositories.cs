using System.Data;
using Microsoft.Data.SqlClient;
using TVM.Demo.Models;

namespace TVM.Demo.Data;

public class Db
{
    private readonly string _cs;
    public Db(string connectionString) => _cs = connectionString;

    public SqlConnection Open()
    {
        var c = new SqlConnection(_cs);
        c.Open();
        return c;
    }
}

/// <summary>Reference data: stations, ticket types and fares.</summary>
public class FareService
{
    private readonly Db _db;
    public FareService(Db db) => _db = db;

    public Station GetStation(int id) => QueryStations("WHERE StationId = @p", id).Single();

    public List<Station> GetDestinations(int originId) =>
        QueryStations("WHERE IsActive = 1 AND StationId <> @p ORDER BY Mode DESC, Line, Seq", originId);

    public List<TicketType> GetTicketTypes()
    {
        using var c = _db.Open();
        using var cmd = new SqlCommand("SELECT TicketTypeId, Name, ValidityHours, MaxRides FROM TicketType ORDER BY TicketTypeId", c);
        using var r = cmd.ExecuteReader();
        var list = new List<TicketType>();
        while (r.Read())
            list.Add(new TicketType { TicketTypeId = r.GetInt32(0), Name = r.GetString(1), ValidityHours = r.GetInt32(2), MaxRides = r.GetInt32(3) });
        return list;
    }

    public decimal GetFare(int originId, int destinationId, int ticketTypeId)
    {
        using var c = _db.Open();
        using var cmd = new SqlCommand(
            "SELECT Price FROM Fare WHERE FromStationId = @o AND ToStationId = @d AND TicketTypeId = @t", c);
        cmd.Parameters.AddWithValue("@o", originId);
        cmd.Parameters.AddWithValue("@d", destinationId);
        cmd.Parameters.AddWithValue("@t", ticketTypeId);
        return cmd.ExecuteScalar() is decimal price ? price : throw new InvalidOperationException("No fare defined for this trip.");
    }

    private List<Station> QueryStations(string where, int p)
    {
        using var c = _db.Open();
        using var cmd = new SqlCommand($"SELECT StationId, Code, Name, Line, Mode, IsActive FROM Station {where}", c);
        cmd.Parameters.AddWithValue("@p", p);
        using var r = cmd.ExecuteReader();
        var list = new List<Station>();
        while (r.Read())
            list.Add(new Station
            {
                StationId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), Line = r.GetString(3),
                Mode = Enum.Parse<TransportMode>(r.GetString(4)), IsActive = r.GetBoolean(5),
            });
        return list;
    }
}

/// <summary>Persists a completed (or refunded) transaction atomically.</summary>
public class TransactionRepository
{
    private readonly Db _db;
    public TransactionRepository(Db db) => _db = db;

    public string? GetMachineStation(string machineId, out int stationId)
    {
        using var c = _db.Open();
        using var cmd = new SqlCommand("SELECT StationId, Status FROM Machine WHERE MachineId = @m", c);
        cmd.Parameters.AddWithValue("@m", machineId);
        using var r = cmd.ExecuteReader();
        stationId = 0;
        if (!r.Read()) return null;
        stationId = r.GetInt32(0);
        return r.GetString(1);
    }

    public void Save(string machineId, Order order)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, @"INSERT INTO SalesOrder (OrderId, MachineId, OriginId, DestinationId, TicketTypeId, Quantity, UnitPrice, TotalAmount, Status, CreatedAt)
                      VALUES (@id, @m, @o, @d, @t, @q, @u, @tot, @s, @at)",
            ("@id", order.OrderId), ("@m", machineId), ("@o", order.Origin.StationId), ("@d", order.Destination.StationId),
            ("@t", order.TicketType.TicketTypeId), ("@q", order.Quantity), ("@u", order.UnitPrice), ("@tot", order.TotalAmount),
            ("@s", order.Status.ToString()), ("@at", order.CreatedAt));
        foreach (var p in order.Payments)
        {
            var card = p as CardPayment;
            Exec(c, tx, @"INSERT INTO Payment (PaymentId, OrderId, Method, Amount, Status, GatewayRef, MaskedPan, AuthCode, PaidAt)
                          VALUES (@id, @o, @me, @a, @s, @g, @pan, @auth, @paid)",
                ("@id", p.PaymentId), ("@o", order.OrderId), ("@me", p.Method.ToString()), ("@a", p.Amount),
                ("@s", p.Status.ToString()), ("@g", p.GatewayRef), ("@pan", card?.MaskedPan), ("@auth", card?.AuthCode), ("@paid", p.PaidAt));
        }
        foreach (var t in order.Tickets)
            Exec(c, tx, "INSERT INTO Ticket (TicketId, OrderId, IssuedAt, ValidUntil, Signature) VALUES (@id, @o, @i, @v, @sig)",
                ("@id", t.TicketId), ("@o", order.OrderId), ("@i", t.IssuedAt), ("@v", t.ValidUntil), ("@sig", t.Signature));
        tx.Commit();
    }

    /// <summary>
    /// Saves the sale; if the database is unreachable the sale is appended to a local queue file so that a
    /// paid transaction is never lost (NFR-08 / NFR-10). The sync agent uploads the queue later.
    /// </summary>
    public void SaveOrQueue(string machineId, Order order)
    {
        try
        {
            Save(machineId, order);
        }
        catch (SqlException ex)
        {
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                order.OrderId, machineId, Origin = order.Origin.StationId, Destination = order.Destination.StationId,
                Type = order.TicketType.TicketTypeId, order.Quantity, order.UnitPrice, order.TotalAmount, Status = order.Status.ToString(),
                order.CreatedAt, Payments = order.Payments.Select(p => new { p.PaymentId, Method = p.Method.ToString(), p.Amount, Status = p.Status.ToString(), p.GatewayRef }),
                Tickets = order.Tickets.Select(t => new { t.TicketId, t.IssuedAt, t.ValidUntil, t.Signature }), Error = ex.Message,
            });
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "pending_sync.jsonl"), line + Environment.NewLine);
        }
    }

    public void Log(Guid sessionId, string machineId, string step, string? detail = null)
    {
        try
        {
            using var c = _db.Open();
            Exec(c, null, "INSERT INTO TransactionLog (SessionId, MachineId, Step, Detail) VALUES (@s, @m, @st, @d)",
                ("@s", sessionId), ("@m", machineId), ("@st", step), ("@d", detail));
        }
        catch (SqlException)
        {
            // Logging must never break a sale (NFR-10: buffer locally in production).
        }
    }

    private static void Exec(SqlConnection c, SqlTransaction? tx, string sql, params (string, object?)[] ps)
    {
        using var cmd = new SqlCommand(sql, c, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Output report: sales by day / payment method (FR-20).</summary>
public class ReportRepository
{
    private readonly Db _db;
    public ReportRepository(Db db) => _db = db;

    public DataTable GetSales(DateTime from, DateTime to, string? method)
    {
        const string sql = @"SELECT CreatedAt AS [Time], MachineId AS [Machine], Origin, Destination, TicketType AS [Ticket],
                                    Quantity AS [Qty], UnitPrice AS [Unit price], TotalAmount AS [Total], PaymentMethod AS [Method],
                                    PaymentStatus AS [Payment], OrderStatus AS [Order], GatewayRef AS [Gateway ref]
                             FROM vw_SalesReport
                             WHERE SaleDate BETWEEN @f AND @t AND (@m IS NULL OR PaymentMethod = @m)
                             ORDER BY CreatedAt DESC";
        return Fill(sql, from, to, method);
    }

    public DataTable GetSummary(DateTime from, DateTime to, string? method)
    {
        const string sql = @"SELECT PaymentMethod AS [Method], COUNT(*) AS [Orders], SUM(Quantity) AS [Tickets],
                                    SUM(CASE WHEN OrderStatus = 'Completed' THEN TotalAmount ELSE 0 END) AS [Revenue (VND)]
                             FROM vw_SalesReport
                             WHERE SaleDate BETWEEN @f AND @t AND (@m IS NULL OR PaymentMethod = @m)
                             GROUP BY PaymentMethod ORDER BY [Revenue (VND)] DESC";
        return Fill(sql, from, to, method);
    }

    private DataTable Fill(string sql, DateTime from, DateTime to, string? method)
    {
        using var c = _db.Open();
        using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@f", SqlDbType.Date).Value = from.Date;
        cmd.Parameters.Add("@t", SqlDbType.Date).Value = to.Date;
        cmd.Parameters.Add("@m", SqlDbType.NVarChar, 20).Value = (object?)method ?? DBNull.Value;
        var dt = new DataTable();
        using var r = cmd.ExecuteReader();
        dt.Load(r);
        return dt;
    }
}
