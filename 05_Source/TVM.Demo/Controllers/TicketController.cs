using TVM.Demo.Data;
using TVM.Demo.Models;
using TVM.Demo.Services;

namespace TVM.Demo.Controllers;

/// <summary>States of the TVM during a transaction (matches the state chart diagram).</summary>
public enum TvmState
{
    Idle, OutOfService, SelectingDestination, SelectingTicket, SelectingPayment,
    WaitingForCard, DisplayingQR, PaymentFailed, PrintingTicket, Completed, Refunding,
}

/// <summary>Controller of the "Buy a Ticket" use case (MVC). Views call it; it updates the model.</summary>
public class TicketController
{
    public const int MaxAttempts = 3;

    private readonly TicketVendingMachine _machine;
    private readonly FareService _fares;
    private readonly PaymentService _payments;
    private readonly TransactionRepository _repo;
    private readonly string _ticketSecret;

    public TvmState State { get; private set; } = TvmState.Idle;
    public Guid SessionId { get; private set; }
    public Station? Destination { get; private set; }
    public Order? CurrentOrder { get; private set; }
    public Payment? CurrentPayment { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public event Action<TvmState>? StateChanged;

    public TicketController(TicketVendingMachine machine, FareService fares, PaymentService payments,
                            TransactionRepository repo, string ticketSecret)
    {
        _machine = machine;
        _fares = fares;
        _payments = payments;
        _repo = repo;
        _ticketSecret = ticketSecret;
    }

    public Station Origin => _machine.Location;
    public TicketVendingMachine Machine => _machine;

    public List<Station> StartSession()
    {
        Reset();
        SessionId = Guid.NewGuid();
        if (!_machine.CheckDevices())
        {
            SetState(TvmState.OutOfService);
            return new();
        }
        Log("StartSession");
        SetState(TvmState.SelectingDestination);
        return _fares.GetDestinations(Origin.StationId);
    }

    public List<Station> GetDestinations() => _fares.GetDestinations(Origin.StationId);

    public List<TicketType> SelectDestination(Station station)
    {
        Require(TvmState.SelectingDestination, TvmState.SelectingTicket);
        Destination = station;
        Log("SelectDestination", station.Name);
        SetState(TvmState.SelectingTicket);
        return _fares.GetTicketTypes();
    }

    public decimal GetUnitFare(TicketType type) =>
        _fares.GetFare(Origin.StationId, Destination!.StationId, type.TicketTypeId);

    public Order SelectTicket(TicketType type, int quantity)
    {
        Require(TvmState.SelectingTicket, TvmState.SelectingPayment);
        CurrentOrder = new Order(Origin, Destination!, type, quantity, GetUnitFare(type));
        Log("SelectTicket", $"{type.Name} x{quantity} = {CurrentOrder.TotalAmount:N0}");
        SetState(TvmState.SelectingPayment);
        return CurrentOrder;
    }

    public void Back()
    {
        if (State == TvmState.SelectingTicket) SetState(TvmState.SelectingDestination);
        else if (State is TvmState.SelectingPayment or TvmState.PaymentFailed) SetState(TvmState.SelectingTicket);
    }

    /// <summary>Choose a method. Card -> WaitingForCard; QR/wallet -> creates QR and DisplayingQR.</summary>
    public Payment? ChooseMethod(PaymentMethod method)
    {
        Require(TvmState.SelectingPayment, TvmState.PaymentFailed);
        CurrentOrder!.MarkAwaitingPayment();
        Attempts++;
        Log("ChooseMethod", $"{method} attempt {Attempts}");
        if (method == PaymentMethod.Card)
        {
            CurrentPayment = null;
            SetState(TvmState.WaitingForCard);
            return null;
        }
        CurrentPayment = _payments.CreatePayment(CurrentOrder, method);
        SetState(TvmState.DisplayingQR);
        return CurrentPayment;
    }

    /// <summary>Card inserted / tapped (simulated by typing the card number).</summary>
    public bool CardPresented(string cardNumber)
    {
        Require(TvmState.WaitingForCard);
        CurrentPayment = _payments.CreatePayment(CurrentOrder!, PaymentMethod.Card, cardNumber);
        Log("CardAuthorization", $"{((CardPayment)CurrentPayment).MaskedPan} -> {CurrentPayment.Status}");
        if (CurrentPayment.Status == PaymentStatus.Authorized) return Issue();
        return Fail("Your card was declined by the bank. You have not been charged.");
    }

    /// <summary>Polled every few seconds while the QR is displayed (fallback for the gateway IPN).</summary>
    public PaymentStatus PollQr()
    {
        if (State != TvmState.DisplayingQR || CurrentPayment is null) return PaymentStatus.Pending;
        var s = _payments.CheckStatus(CurrentPayment);
        if (s == PaymentStatus.Captured) Issue();
        else if (s == PaymentStatus.Failed) Fail("The QR code expired before payment was received.");
        return s;
    }

    public bool CanRetry => Attempts < MaxAttempts;

    public void Cancel()
    {
        if (CurrentPayment is { Status: PaymentStatus.Pending or PaymentStatus.Authorized })
        {
            CurrentPayment.Status = PaymentStatus.Voided;
        }
        CurrentOrder?.Cancel();
        Log("Cancel", State.ToString());
        Reset();
        SetState(TvmState.Idle);
    }

    public void Finish()
    {
        Reset();
        SetState(_machine.Status == MachineStatus.InService ? TvmState.Idle : TvmState.OutOfService);
    }

    private bool Issue()
    {
        var order = CurrentOrder!;
        order.MarkPaid(CurrentPayment!);
        SetState(TvmState.PrintingTicket);
        for (int i = 0; i < order.Quantity; i++)
        {
            var t = order.IssueTicket(_ticketSecret);
            if (!_machine.Printer.Print(t))
            {
                // UC-08: printing failed after payment -> refund and go out of service.
                SetState(TvmState.Refunding);
                _payments.Refund(CurrentPayment!);
                order.MarkRefunded();
                order.Tickets.Clear();
                _repo.SaveOrQueue(_machine.MachineId, order);
                Log("PrintFailed", $"refund {CurrentPayment!.GatewayRef}");
                _machine.SetStatus(MachineStatus.OutOfService);
                LastError = $"The printer failed. Your payment has been refunded (ref {CurrentPayment!.GatewayRef}).";
                return false;
            }
        }
        _payments.Capture(CurrentPayment!);
        order.Complete();
        _repo.SaveOrQueue(_machine.MachineId, order);
        Log("Completed", $"{order.Tickets.Count} tickets, {order.TotalAmount:N0} VND");
        SetState(TvmState.Completed);
        return true;
    }

    private bool Fail(string reason)
    {
        LastError = reason;
        Log("PaymentFailed", reason);
        SetState(TvmState.PaymentFailed);
        return false;
    }

    private void Reset()
    {
        Destination = null;
        CurrentOrder = null;
        CurrentPayment = null;
        Attempts = 0;
        LastError = null;
    }

    private void Require(params TvmState[] allowed)
    {
        if (!allowed.Contains(State)) throw new InvalidOperationException($"Action not allowed in state {State}.");
    }

    private void SetState(TvmState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    private void Log(string step, string? detail = null) => _repo.Log(SessionId, _machine.MachineId, step, detail);
}
