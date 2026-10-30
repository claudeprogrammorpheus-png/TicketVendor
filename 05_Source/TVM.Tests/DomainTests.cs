using TVM.Demo.Models;
using TVM.Demo.Services;

namespace TVM.Tests;

public class DomainTests
{
    private static readonly Station BenThanh = new() { StationId = 1, Code = "L1-01", Name = "Ben Thanh", Line = "MRT Line 1", Mode = TransportMode.MRT };
    private static readonly Station ThuDuc = new() { StationId = 11, Code = "L1-11", Name = "Thu Duc", Line = "MRT Line 1", Mode = TransportMode.MRT };
    private static readonly TicketType Single = new() { TicketTypeId = 1, Name = "Single trip", MaxRides = 1 };

    private static Order NewOrder(int qty = 2) => new(BenThanh, ThuDuc, Single, qty, 16000);

    [Fact]
    public void Order_total_is_unit_price_times_quantity()
    {
        Assert.Equal(32000, NewOrder(2).TotalAmount);
        Assert.Equal(160000, NewOrder(10).TotalAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Order_rejects_quantity_outside_1_to_10(int qty) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewOrder(qty));

    [Fact]
    public void Order_rejects_same_origin_and_destination() =>
        Assert.Throws<ArgumentException>(() => new Order(BenThanh, BenThanh, Single, 1, 7000));

    [Fact]
    public void Tickets_cannot_be_issued_before_payment() =>
        Assert.Throws<InvalidOperationException>(() => NewOrder().IssueTicket("s"));

    [Fact]
    public void Paid_order_issues_unique_signed_tickets_valid_until_end_of_day()
    {
        var o = NewOrder(2);
        var p = new CardGateway().Authorize(o, PaymentMethod.Card, "4242 4242 4242 4242");
        o.MarkPaid(p);
        var t1 = o.IssueTicket("secret");
        var t2 = o.IssueTicket("secret");
        Assert.NotEqual(t1.TicketId, t2.TicketId);
        Assert.Equal(20, t1.TicketId.Length); // fits Ticket.TicketId NVARCHAR(20)
        Assert.Equal(DateTime.Today.AddDays(1).AddSeconds(-1), t1.ValidUntil);
        Assert.True(t1.IsValid(DateTime.Now));
        Assert.Throws<InvalidOperationException>(() => o.IssueTicket("secret")); // only qty tickets
    }

    [Fact]
    public void Ticket_signature_depends_on_secret()
    {
        var o = NewOrder(1);
        o.MarkPaid(new CardGateway().Authorize(o, PaymentMethod.Card, "4242424242424242"));
        var t = o.IssueTicket("secret-A");
        var sigA = t.Signature;
        t.GenerateQrPayload("secret-B");
        Assert.NotEqual(sigA, t.Signature);
        t.GenerateQrPayload("secret-A");
        Assert.Equal(sigA, t.Signature); // deterministic -> gates can verify offline
    }

    [Theory]
    [InlineData("4242424242424242", true)]
    [InlineData("4000000000000002", true)]
    [InlineData("4242424242424241", false)]
    public void Luhn_check(string pan, bool valid) => Assert.Equal(valid, CardGateway.Luhn(pan));

    [Fact]
    public void Card_gateway_approves_valid_card_and_masks_pan()
    {
        var p = (CardPayment)new CardGateway().Authorize(NewOrder(), PaymentMethod.Card, "4242 4242 4242 4242");
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("**** **** **** 4242", p.MaskedPan);
        Assert.Equal("VISA", p.CardScheme);
        Assert.DoesNotContain("424242424242", p.Describe());
    }

    [Theory]
    [InlineData("4000 0000 0000 0002")] // test decline card
    [InlineData("1234")]                // too short
    [InlineData("4242 4242 4242 4241")] // fails Luhn
    public void Card_gateway_declines_invalid_cards(string pan) =>
        Assert.Equal(PaymentStatus.Failed, new CardGateway().Authorize(NewOrder(), PaymentMethod.Card, pan).Status);

    [Fact]
    public void Card_is_captured_only_after_authorization_and_voided_on_refund()
    {
        var gw = new CardGateway();
        var p = gw.Authorize(NewOrder(), PaymentMethod.Card, "4242424242424242");
        Assert.True(gw.Refund(p));
        Assert.Equal(PaymentStatus.Voided, p.Status); // not captured yet -> void, no charge
        Assert.False(gw.Capture(p));
    }

    [Fact]
    public void Qr_payment_stays_pending_until_customer_pays()
    {
        var gw = new QrWalletGateway();
        var p = (QrPayment)gw.Authorize(NewOrder(), PaymentMethod.Momo);
        Assert.Equal(PaymentStatus.Pending, gw.CheckStatus(p));
        Assert.Contains("amount=32000", p.QrContent);
        gw.SimulateCustomerScan(p);
        Assert.Equal(PaymentStatus.Captured, gw.CheckStatus(p));
        Assert.NotNull(p.PaidAt);
    }

    [Fact]
    public void Expired_qr_fails()
    {
        var gw = new QrWalletGateway();
        var p = (QrPayment)gw.Authorize(NewOrder(), PaymentMethod.ZaloPay);
        p.ExpiresAt = DateTime.Now.AddSeconds(-1);
        Assert.Equal(PaymentStatus.Failed, gw.CheckStatus(p));
    }

    [Fact]
    public void Payment_service_routes_to_the_right_gateway()
    {
        var svc = new PaymentService(new CardGateway(), new QrWalletGateway());
        Assert.IsType<CardGateway>(svc.GatewayFor(PaymentMethod.Card));
        Assert.IsType<QrWalletGateway>(svc.GatewayFor(PaymentMethod.VnPay));
        var o = NewOrder();
        svc.CreatePayment(o, PaymentMethod.BankQr);
        Assert.Single(o.Payments);
    }

    [Fact]
    public void Jammed_printer_fails_to_print()
    {
        var o = NewOrder(1);
        o.MarkPaid(new CardGateway().Authorize(o, PaymentMethod.Card, "4242424242424242"));
        var printer = new TicketPrinter { SimulateJam = true };
        Assert.False(printer.Print(o.IssueTicket("s")));
        Assert.False(new TicketPrinter { PaperLevel = 0 }.IsReady());
    }
}
