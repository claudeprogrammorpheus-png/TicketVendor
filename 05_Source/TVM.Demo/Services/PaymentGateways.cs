using TVM.Demo.Models;

namespace TVM.Demo.Services;

/// <summary>Strategy interface implemented by every payment gateway adapter.</summary>
public interface IPaymentGateway
{
    bool Supports(PaymentMethod method);
    Payment Authorize(Order order, PaymentMethod method, string? cardNumber = null);
    PaymentStatus CheckStatus(Payment p);
    bool Capture(Payment p);
    bool Refund(Payment p);
}

/// <summary>
/// Simulated card acquirer. In production the PCI PTS reader sends encrypted card data to the acquirer;
/// here the card number is only used to build the masked PAN and decide approve/decline.
/// Test cards: any Luhn-valid number approves; numbers ending in 0002 are declined.
/// </summary>
public class CardGateway : IPaymentGateway
{
    public bool Supports(PaymentMethod method) => method == PaymentMethod.Card;

    public Payment Authorize(Order order, PaymentMethod method, string? cardNumber = null)
    {
        var digits = new string((cardNumber ?? "").Where(char.IsDigit).ToArray());
        var p = new CardPayment(order.TotalAmount)
        {
            MaskedPan = digits.Length >= 4 ? $"**** **** **** {digits[^4..]}" : "****",
            CardScheme = digits.StartsWith('4') ? "VISA" : digits.StartsWith('5') ? "Mastercard" : digits.StartsWith("35") ? "JCB" : "NAPAS",
        };
        if (digits.Length < 12 || !Luhn(digits) || digits.EndsWith("0002"))
        {
            p.Status = PaymentStatus.Failed;
            return p;
        }
        p.Status = PaymentStatus.Authorized;
        p.AuthCode = Random.Shared.Next(100000, 999999).ToString();
        p.GatewayRef = $"ACQ-{Random.Shared.Next(1_000_000, 9_999_999)}";
        return p;
    }

    public PaymentStatus CheckStatus(Payment p) => p.Status;

    public bool Capture(Payment p)
    {
        if (p.Status != PaymentStatus.Authorized) return false;
        p.Status = PaymentStatus.Captured;
        p.PaidAt = DateTime.Now;
        return true;
    }

    public bool Refund(Payment p)
    {
        p.Status = p.Status == PaymentStatus.Authorized ? PaymentStatus.Voided : PaymentStatus.Refunded;
        return true;
    }

    public static bool Luhn(string digits)
    {
        int sum = 0;
        bool dbl = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl && (d *= 2) > 9) d -= 9;
            sum += d;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }
}

/// <summary>
/// Simulated QR / e-wallet gateway (VietQR, MoMo, VNPay, ZaloPay). The real adapters would call the
/// provider's create-payment API with an HMAC signature and receive an IPN webhook; the demo marks the
/// payment as paid when <see cref="SimulateCustomerScan"/> is called.
/// </summary>
public class QrWalletGateway : IPaymentGateway
{
    public static readonly TimeSpan QrLifetime = TimeSpan.FromSeconds(120);
    private readonly HashSet<Guid> _paid = new();

    public bool Supports(PaymentMethod method) => method != PaymentMethod.Card;

    public Payment Authorize(Order order, PaymentMethod method, string? cardNumber = null)
    {
        var p = new QrPayment(order.TotalAmount, method)
        {
            GatewayRef = $"{method.ToString().ToUpperInvariant()}-{Random.Shared.Next(1_000_000, 9_999_999)}",
            ExpiresAt = DateTime.Now.Add(QrLifetime),
        };
        p.QrContent = $"https://pay.example.vn/{method}/{p.GatewayRef}?amount={order.TotalAmount:0}&order={order.OrderId:N}";
        return p;
    }

    public void SimulateCustomerScan(Payment p) => _paid.Add(p.PaymentId);

    public PaymentStatus CheckStatus(Payment p)
    {
        if (p.Status != PaymentStatus.Pending) return p.Status;
        if (_paid.Contains(p.PaymentId))
        {
            p.Status = PaymentStatus.Captured; // wallet payments are captured by the provider
            p.PaidAt = DateTime.Now;
        }
        else if (p is QrPayment q && q.IsExpired())
        {
            p.Status = PaymentStatus.Failed;
        }
        return p.Status;
    }

    public bool Capture(Payment p) => p.Status == PaymentStatus.Captured;

    public bool Refund(Payment p)
    {
        p.Status = PaymentStatus.Refunded;
        return true;
    }
}

public class PaymentService
{
    private readonly IReadOnlyList<IPaymentGateway> _gateways;

    public PaymentService(params IPaymentGateway[] gateways) => _gateways = gateways;

    public IPaymentGateway GatewayFor(PaymentMethod m) =>
        _gateways.FirstOrDefault(g => g.Supports(m)) ?? throw new NotSupportedException($"No gateway for {m}");

    public Payment CreatePayment(Order o, PaymentMethod m, string? cardNumber = null)
    {
        var p = GatewayFor(m).Authorize(o, m, cardNumber);
        o.Payments.Add(p);
        return p;
    }

    public PaymentStatus CheckStatus(Payment p) => GatewayFor(p.Method).CheckStatus(p);
    public bool Capture(Payment p) => GatewayFor(p.Method).Capture(p);
    public bool Refund(Payment p) => GatewayFor(p.Method).Refund(p);
}
