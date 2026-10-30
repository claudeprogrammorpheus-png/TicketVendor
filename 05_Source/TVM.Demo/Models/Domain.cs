using System.Security.Cryptography;
using System.Text;

namespace TVM.Demo.Models;

public enum MachineStatus { InService, OutOfService, Maintenance }
public enum TransportMode { Bus, MRT }
public enum OrderStatus { Created, AwaitingPayment, Paid, Completed, Cancelled, Refunded }
public enum PaymentStatus { Pending, Authorized, Captured, Failed, Voided, Refunded }
public enum PaymentMethod { Card, BankQr, Momo, VnPay, ZaloPay }

public class Station
{
    public int StationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Line { get; set; } = "";
    public TransportMode Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public override string ToString() => $"{Name}  ({Line})";
}

public class TicketType
{
    public int TicketTypeId { get; set; }
    public string Name { get; set; } = "";
    public int ValidityHours { get; set; }
    public int MaxRides { get; set; }
    public override string ToString() => Name;
}

public class Fare
{
    public int FareId { get; set; }
    public int FromStationId { get; set; }
    public int ToStationId { get; set; }
    public int TicketTypeId { get; set; }
    public decimal Price { get; set; }
}

public class TicketVendingMachine
{
    public string MachineId { get; }
    public Station Location { get; }
    public MachineStatus Status { get; private set; } = MachineStatus.InService;
    public TicketPrinter Printer { get; }

    public TicketVendingMachine(string machineId, Station location, TicketPrinter printer)
    {
        MachineId = machineId;
        Location = location;
        Printer = printer;
    }

    public void StartSession()
    {
        if (!CheckDevices()) throw new InvalidOperationException("Machine is out of service.");
    }

    public bool CheckDevices()
    {
        bool ok = Printer.IsReady();
        SetStatus(ok ? MachineStatus.InService : MachineStatus.OutOfService);
        return ok;
    }

    public void SetStatus(MachineStatus s) => Status = s;
}

public class Order
{
    public const int MaxQuantity = 10;

    public Guid OrderId { get; } = Guid.NewGuid();
    public Station Origin { get; }
    public Station Destination { get; }
    public TicketType TicketType { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Created;
    public DateTime CreatedAt { get; } = DateTime.Now;
    public List<Payment> Payments { get; } = new();
    public List<Ticket> Tickets { get; } = new();

    public Order(Station origin, Station destination, TicketType type, int quantity, decimal unitPrice)
    {
        if (origin.StationId == destination.StationId) throw new ArgumentException("Destination must differ from origin.");
        if (quantity < 1 || quantity > MaxQuantity) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be 1-10.");
        if (unitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        Origin = origin;
        Destination = destination;
        TicketType = type;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CalculateTotal();
    }

    public decimal CalculateTotal() => TotalAmount = UnitPrice * Quantity;

    public void MarkAwaitingPayment() => Status = OrderStatus.AwaitingPayment;

    public void MarkPaid(Payment p)
    {
        if (p.Status is not (PaymentStatus.Authorized or PaymentStatus.Captured))
            throw new InvalidOperationException("Payment is not approved.");
        Status = OrderStatus.Paid;
    }

    public Ticket IssueTicket(string secret)
    {
        if (Status != OrderStatus.Paid) throw new InvalidOperationException("Order is not paid.");
        if (Tickets.Count >= Quantity) throw new InvalidOperationException("All tickets already issued.");
        var now = DateTime.Now;
        // TK + yyMMddHHmm + 6 hex chars of the order id + sequence = 20 chars, unique across machines.
        var id = $"TK{now:yyMMddHHmm}{OrderId.ToString("N")[..6].ToUpperInvariant()}{Tickets.Count + 1:00}";
        var t = new Ticket(this, id, now, now.Date.AddDays(1).AddSeconds(-1));
        t.GenerateQrPayload(secret);
        Tickets.Add(t);
        return t;
    }

    public void Complete() => Status = OrderStatus.Completed;
    public void Cancel() => Status = OrderStatus.Cancelled;
    public void MarkRefunded() => Status = OrderStatus.Refunded;
}

public abstract class Payment
{
    public Guid PaymentId { get; } = Guid.NewGuid();
    public decimal Amount { get; }
    public PaymentMethod Method { get; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? GatewayRef { get; set; }
    public DateTime? PaidAt { get; set; }

    protected Payment(decimal amount, PaymentMethod method)
    {
        Amount = amount;
        Method = method;
    }

    public abstract string Describe();
}

public class CardPayment : Payment
{
    public string MaskedPan { get; set; } = "";
    public string CardScheme { get; set; } = "";
    public string? AuthCode { get; set; }

    public CardPayment(decimal amount) : base(amount, PaymentMethod.Card) { }
    public override string Describe() => $"{CardScheme} {MaskedPan} (auth {AuthCode})";
}

public class QrPayment : Payment
{
    public string Provider { get; }
    public string QrContent { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    public QrPayment(decimal amount, PaymentMethod method) : base(amount, method) => Provider = method.ToString();
    public bool IsExpired() => DateTime.Now >= ExpiresAt;
    public override string Describe() => $"{Provider} QR (ref {GatewayRef})";
}

public class Ticket
{
    public string TicketId { get; }
    public Order Order { get; }
    public DateTime IssuedAt { get; }
    public DateTime ValidUntil { get; }
    public string Signature { get; private set; } = "";
    public string QrPayload { get; private set; } = "";

    public Ticket(Order order, string ticketId, DateTime issuedAt, DateTime validUntil)
    {
        Order = order;
        TicketId = ticketId;
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
    }

    /// <summary>Payload signed with HMAC-SHA256 so gates can validate offline (NFR-17).</summary>
    public string GenerateQrPayload(string secret)
    {
        string data = $"{TicketId}|{Order.Origin.Code}|{Order.Destination.Code}|{Order.TicketType.TicketTypeId}|{ValidUntil:yyyyMMddHHmm}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        Signature = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(data)));
        return QrPayload = $"{data}|{Signature[..16]}";
    }

    public bool IsValid(DateTime now) => now <= ValidUntil;
}

/// <summary>Simulated thermal printer (hardware abstraction).</summary>
public class TicketPrinter
{
    public int PaperLevel { get; set; } = 500;
    /// <summary>Test switch: the next print jams (used to demonstrate UC-08 Refund Payment).</summary>
    public bool SimulateJam { get; set; }

    public bool IsReady() => PaperLevel > 0;

    public bool Print(Ticket t)
    {
        if (!IsReady() || SimulateJam) return false;
        PaperLevel--;
        return true;
    }
}
