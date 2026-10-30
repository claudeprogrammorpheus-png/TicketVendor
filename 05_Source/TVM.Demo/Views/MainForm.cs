using QRCoder;
using TVM.Demo.Controllers;
using TVM.Demo.Data;
using TVM.Demo.Models;
using TVM.Demo.Services;

namespace TVM.Demo.Views;

/// <summary>Kiosk window. Each step of "Buy a Ticket" is a page rendered into the content panel.</summary>
public partial class MainForm : Form
{
    private const int InactivitySeconds = 60, CountdownSeconds = 10;

    private readonly TicketController _ctl;
    private readonly QrWalletGateway _qrGateway;
    private readonly ReportRepository _reports;

    private readonly Label _title = Ui.Lbl("", 20, FontStyle.Bold, Color.White);
    private readonly Label _headerRight = Ui.Lbl("", 13, FontStyle.Bold, Color.White);
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Ui.Back, Padding = new Padding(32, 20, 32, 20) };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 90, BackColor = Color.White, Padding = new Padding(28, 14, 28, 14) };
    private readonly FlowLayoutPanel _footerLeft = new() { Dock = DockStyle.Fill, WrapContents = false };
    private readonly FlowLayoutPanel _footerRight = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
    private readonly Label _timeoutLabel = Ui.Lbl("", 11, FontStyle.Regular, Ui.Grey);

    private readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _qrTimer = new() { Interval = 1000 };
    private int _idleSeconds;
    private int _qrTick;

    public MainForm(TicketController ctl, QrWalletGateway qrGateway, ReportRepository reports)
    {
        _ctl = ctl;
        _qrGateway = qrGateway;
        _reports = reports;

        Text = $"Smart Ticketing - TVM {_ctl.Machine.MachineId}";
        ClientSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Ui.Back;
        Font = Ui.F(11);

        var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Ui.Navy };
        var logo = new Label { Text = "M", Font = Ui.F(20, FontStyle.Bold), BackColor = Ui.Gold, ForeColor = Ui.Navy, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(50, 50), Location = new Point(28, 15) };
        _title.Location = new Point(92, 22);
        _headerRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.AddRange([logo, _title, _headerRight]);
        header.Resize += (_, _) => _headerRight.Location = new Point(header.Width - _headerRight.Width - 28, 26);
        _headerRight.TextChanged += (_, _) => _headerRight.Location = new Point(header.Width - _headerRight.Width - 28, 26);

        _footer.Controls.Add(_footerLeft);
        _footer.Controls.Add(_footerRight);
        _title.UseMnemonic = false;
        _headerRight.UseMnemonic = false;
        Controls.Add(_content);
        Controls.Add(_footer);
        Controls.Add(header);

        _idleTimer.Tick += (_, _) => OnIdleTick();
        _qrTimer.Tick += (_, _) => OnQrTick();
        // Any touch/key resets the inactivity timer (FR-12).
        Application.AddMessageFilter(new ActivityFilter(() => _idleSeconds = 0));

        ShowWelcome();
    }

    // ------------------------------------------------------------------ pages

    private void ShowWelcome()
    {
        Page("Smart Ticketing - " + _ctl.Origin.Name + " station", $"{_ctl.Machine.MachineId} · In service", timed: false);
        var start = Ui.Btn("START", Ui.Gold, Color.FromArgb(0x1F, 0x2D, 0x3D), 380, 120);
        start.Font = Ui.F(36, FontStyle.Bold);
        start.Click += (_, _) => DoStart();

        var col = Column(
            Ui.Lbl("Buy Bus & MRT tickets", 34, FontStyle.Bold),
            Ui.Lbl("Touch START to begin. Cashless machine: Card · VietQR · MoMo · VNPay · ZaloPay", 14, FontStyle.Regular, Ui.Grey),
            Spacer(20), start);
        Center(col);

        var report = Ui.Ghost("Sales report (admin)", 240, 50);
        report.Font = Ui.F(11, FontStyle.Bold);
        report.Click += (_, _) => new SalesReportForm(_reports).ShowDialog(this);
        var jam = new CheckBox { Text = "Simulate printer jam on next ticket (test UC-08)", AutoSize = true, Font = Ui.F(10), Margin = new Padding(20, 16, 0, 0), Checked = _ctl.Machine.Printer.SimulateJam };
        jam.CheckedChanged += (_, _) => _ctl.Machine.Printer.SimulateJam = jam.Checked;
        _footerLeft.Controls.AddRange([report, jam]);
    }

    private void DoStart()
    {
        var destinations = _ctl.StartSession();
        if (_ctl.State == TvmState.OutOfService) { ShowOutOfService(); return; }
        ShowDestinations(destinations);
    }

    private void ShowDestinations(List<Station> all)
    {
        Page("Step 1 of 3 - Choose your destination", "From: " + _ctl.Origin.Name);
        var search = new TextBox { Font = Ui.F(18), Width = 700, PlaceholderText = "Search station name..." };
        var list = new ListBox { Font = Ui.F(16), Width = 700, Height = 420, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        void Fill()
        {
            var q = search.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var s in all.Where(s => q.Length < 2 || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase))) list.Items.Add(s);
            list.EndUpdate();
        }
        Fill();
        search.TextChanged += (_, _) => Fill();

        var popular = Column(Ui.Lbl("POPULAR", 12, FontStyle.Bold, Ui.Grey));
        foreach (var name in new[] { "Suoi Tien Bus Terminal", "National University", "Thao Dien", "Tan Son Nhat Airport" })
        {
            var st = all.FirstOrDefault(s => s.Name == name);
            if (st is null) continue;
            var b = Ui.Btn(name, Color.FromArgb(0xFF, 0xF7, 0xE0), Color.FromArgb(0x7F, 0x60, 0x00), 330, 54);
            b.Font = Ui.F(12, FontStyle.Bold);
            b.Click += (_, _) => Choose(st);
            popular.Controls.Add(b);
        }

        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(Column(search, list));
        popular.Margin = new Padding(30, 0, 0, 0);
        row.Controls.Add(popular);
        _content.Controls.Add(row);

        var next = Ui.Primary("Next →");
        next.Click += (_, _) => { if (list.SelectedItem is Station s) Choose(s); else Toast("Please select a destination."); };
        list.DoubleClick += (_, _) => { if (list.SelectedItem is Station s) Choose(s); };
        FooterLeft(MakeCancelButton());
        FooterRight(next);
        search.Focus();

        void Choose(Station s) => ShowTicketOptions(_ctl.SelectDestination(s));
    }

    private void ShowTicketOptions(List<TicketType> types)
    {
        Page("Step 2 of 3 - Ticket type & quantity", $"{_ctl.Origin.Name} → {_ctl.Destination!.Name}");
        var options = Column(Ui.Lbl($"{_ctl.Origin.Name}  →  {_ctl.Destination.Name}", 22, FontStyle.Bold));
        var summary = Ui.Lbl("", 15, FontStyle.Regular);
        var total = Ui.Lbl("", 26, FontStyle.Bold, Ui.Navy);
        var qty = new NumericUpDown { Minimum = 1, Maximum = Order.MaxQuantity, Value = 1, Font = Ui.F(26, FontStyle.Bold), Width = 120, TextAlign = HorizontalAlignment.Center };
        var prices = types.ToDictionary(t => t, t => _ctl.GetUnitFare(t));
        TicketType selected = types[0];

        void Recalc()
        {
            decimal unit = prices[selected];
            summary.Text = $"Ticket:    {selected.Name}\nUnit fare: {unit:N0} ₫\nQuantity:  {qty.Value}";
            total.Text = $"Total: {unit * qty.Value:N0} ₫";
        }

        foreach (var t in types)
        {
            var rb = new RadioButton
            {
                Text = $"{t.Name}  ·  {prices[t]:N0} ₫   ({(t.MaxRides == 0 ? "unlimited rides" : "one entry")}, until 23:59)",
                Font = Ui.F(13, FontStyle.Bold), Appearance = Appearance.Button, AutoSize = false, Width = 560, Height = 90,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Checked = t == selected, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0), Margin = new Padding(0, 10, 0, 0),
            };
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(0xDE, 0xEB, 0xF7);
            rb.FlatAppearance.BorderColor = Ui.Blue;
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { selected = t; Recalc(); } };
            options.Controls.Add(rb);
        }
        var qtyRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 24, 0, 0) };
        qtyRow.Controls.Add(Ui.Lbl("Quantity (1-10):", 16, FontStyle.Bold));
        qtyRow.Controls.Add(qty);
        options.Controls.Add(qtyRow);
        qty.ValueChanged += (_, _) => Recalc();

        var card = Column(Ui.Lbl("Order summary", 16, FontStyle.Bold), summary, Spacer(10), total);
        card.BackColor = Color.White;
        card.Padding = new Padding(24);
        card.Margin = new Padding(40, 0, 0, 0);

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([options, card]);
        _content.Controls.Add(row);
        Recalc();

        var back = Ui.Ghost("← Back");
        back.Click += (_, _) => { _ctl.Back(); ShowDestinations(_ctl.GetDestinations()); };
        var next = Ui.Primary("Continue to payment →", 300);
        next.Click += (_, _) => ShowPaymentMethods(_ctl.SelectTicket(selected, (int)qty.Value));
        FooterLeft(MakeCancelButton(), back);
        FooterRight(next);
    }

    private void ShowPaymentMethods(Order order)
    {
        Page("Step 3 of 3 - Choose payment method", $"Total {order.TotalAmount:N0} ₫");
        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Width = 1120, Height = 440 };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var methods = new (PaymentMethod? m, string text, Color c)[]
        {
            (PaymentMethod.Card, "CARD\nCredit / Debit card\nInsert or tap", Ui.Navy),
            (PaymentMethod.BankQr, "VietQR\nBank QR\nAny banking app", Color.FromArgb(0x00, 0xA6, 0x5A)),
            (PaymentMethod.Momo, "MoMo\nMoMo wallet", Color.FromArgb(0xA5, 0x00, 0x64)),
            (PaymentMethod.VnPay, "VNPAY\nVNPay QR", Color.FromArgb(0x00, 0x5B, 0xAA)),
            (PaymentMethod.ZaloPay, "ZaloPay\nZaloPay wallet", Color.FromArgb(0x00, 0x68, 0xFF)),
            (null, "CASH\nNot supported", Color.Gray),
        };
        foreach (var (m, text, c) in methods)
        {
            var b = Ui.Btn(text, c, Color.White, 350, 190);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(10);
            b.Font = Ui.F(16, FontStyle.Bold);
            if (m is null) b.Enabled = false;
            else b.Click += (_, _) => Pay(m.Value);
            grid.Controls.Add(b);
        }
        _content.Controls.Add(grid);
        var back = Ui.Ghost("← Back");
        back.Click += (_, _) => { _ctl.Back(); ShowTicketOptions(_ctl.SelectDestination(_ctl.Destination!)); };
        FooterLeft(MakeCancelButton(), back);
        FooterRight(Ui.Lbl($"Attempt {_ctl.Attempts + 1} of {TicketController.MaxAttempts}", 12, FontStyle.Regular, Ui.Grey));
    }

    private void Pay(PaymentMethod m)
    {
        var p = _ctl.ChooseMethod(m);
        if (_ctl.State == TvmState.WaitingForCard) ShowCard();
        else ShowQr((QrPayment)p!);
    }

    private void ShowCard()
    {
        Page("Pay by card", $"Total {_ctl.CurrentOrder!.TotalAmount:N0} ₫");
        var number = new TextBox { Font = Ui.F(20), Width = 520, PlaceholderText = "Card number (simulates the card reader)" };
        var insert = Ui.Primary("Insert / tap card", 300, 70);
        insert.Click += (_, _) =>
        {
            Cursor = Cursors.WaitCursor;
            bool ok = _ctl.CardPresented(number.Text);
            Cursor = Cursors.Default;
            AfterPayment(ok);
        };
        number.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) insert.PerformClick(); };
        var col = Column(
            Ui.Lbl("Insert or tap your card", 28, FontStyle.Bold),
            Ui.Lbl("Use the card reader below the screen. Enter your PIN only if asked.", 14, FontStyle.Regular, Ui.Grey),
            Spacer(20), number, Spacer(10), insert, Spacer(20),
            Ui.Lbl("Demo test cards:  4242 4242 4242 4242 → approved     4000 0000 0000 0002 → declined", 11, FontStyle.Regular, Ui.Grey));
        Center(col);
        FooterLeft(MakeCancelButton("Cancel payment", 220));
        number.Focus();
    }

    private void ShowQr(QrPayment p)
    {
        Page($"Scan to pay with {p.Provider}", $"Total {p.Amount:N0} ₫");
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(p.QrContent, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var pic = new PictureBox { Image = Image.FromStream(new MemoryStream(png)), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(380, 380), BackColor = Color.White };
        var countdown = Ui.Lbl("", 44, FontStyle.Bold, Color.FromArgb(0xC5, 0x5A, 0x11));
        countdown.Name = "countdown";
        var paid = Ui.Btn("Simulate: passenger paid in app", Color.FromArgb(0xE2, 0xEF, 0xDA), Color.FromArgb(0x38, 0x57, 0x23), 420, 56);
        paid.Font = Ui.F(12, FontStyle.Bold);
        paid.Click += (_, _) => { _qrGateway.SimulateCustomerScan(p); paid.Enabled = false; paid.Text = "Payment sent - waiting for gateway..."; };

        var steps = Column(
            Ui.Lbl("QR code expires in", 14, FontStyle.Regular, Ui.Grey), countdown,
            Ui.Lbl($"1. Open the {p.Provider} / banking app\n2. Tap Scan QR and point at the code\n3. Confirm payment of {p.Amount:N0} ₫\n4. Your ticket prints automatically", 15),
            Spacer(10), Ui.Lbl($"Gateway ref: {p.GatewayRef}", 11, FontStyle.Regular, Ui.Grey), Spacer(10), paid);
        steps.Margin = new Padding(50, 0, 0, 0);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([pic, steps]);
        Center(row);
        FooterLeft(MakeCancelButton("Cancel payment", 220));

        _qrTick = 0;
        UpdateCountdown(p, countdown);
        _qrTimer.Start();
    }

    private void OnQrTick()
    {
        if (_ctl.CurrentPayment is not QrPayment p) { _qrTimer.Stop(); return; }
        if (_content.Controls.Find("countdown", true).FirstOrDefault() is Label l) UpdateCountdown(p, l);
        // Poll the gateway every 3 s (fallback for the IPN webhook, FR-09).
        if (++_qrTick % 3 != 0 && !p.IsExpired()) return;
        var s = _ctl.PollQr();
        if (s is PaymentStatus.Captured or PaymentStatus.Failed)
        {
            _qrTimer.Stop();
            AfterPayment(_ctl.State == TvmState.Completed);
        }
    }

    private static void UpdateCountdown(QrPayment p, Label l)
    {
        var left = p.ExpiresAt - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        l.Text = $"{left.Minutes:00}:{left.Seconds:00}";
    }

    private void AfterPayment(bool ok)
    {
        if (ok) ShowSuccess();
        else if (_ctl.State == TvmState.Refunding || _ctl.CurrentOrder?.Status == OrderStatus.Refunded) ShowRefund();
        else ShowFailed();
    }

    private void ShowFailed()
    {
        Page("Payment not completed", "");
        var col = Column(
            Ui.Lbl("!", 60, FontStyle.Bold, Ui.Red),
            Ui.Lbl(_ctl.LastError ?? "Payment failed.", 18, FontStyle.Bold),
            Ui.Lbl(_ctl.CanRetry
                ? $"You can try again or choose another method. Attempts remaining: {TicketController.MaxAttempts - _ctl.Attempts}"
                : "Maximum number of attempts reached. The session will end.", 14, FontStyle.Regular, Ui.Grey));
        Center(col);
        FooterLeft(MakeCancelButton());
        if (_ctl.CanRetry)
        {
            var retry = Ui.Primary("Try again / other method", 320);
            retry.Click += (_, _) => ShowPaymentMethods(_ctl.CurrentOrder!);
            FooterRight(retry);
        }
    }

    private void ShowRefund()
    {
        Page("Sorry - ticket could not be printed", "");
        Center(Column(
            Ui.Lbl("Printer error", 30, FontStyle.Bold, Ui.Red),
            Ui.Lbl(_ctl.LastError ?? "", 15),
            Ui.Lbl("This machine is now out of service. Please use another machine.", 14, FontStyle.Regular, Ui.Grey)));
        var ok = Ui.Primary("OK");
        ok.Click += (_, _) => { _ctl.Finish(); ShowOutOfService(); };
        FooterRight(ok);
    }

    private void ShowSuccess()
    {
        var order = _ctl.CurrentOrder!;
        Page("Payment successful", order.Payments.Last().Describe());
        var left = Column(
            Ui.Lbl("✓", 60, FontStyle.Bold, Ui.Green),
            Ui.Lbl("Thank you!", 30, FontStyle.Bold),
            Ui.Lbl($"Please collect your {order.Quantity} ticket(s) from the tray.", 15, FontStyle.Regular, Ui.Grey),
            Ui.Lbl($"Order: {order.OrderId.ToString()[..8].ToUpper()}   Total: {order.TotalAmount:N0} ₫", 13, FontStyle.Bold));
        left.Width = 430;

        var tickets = new FlowLayoutPanel { AutoScroll = true, Width = 640, Height = 520, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        using var gen = new QRCodeGenerator();
        foreach (var t in order.Tickets)
        {
            var box = new Panel { Width = 600, Height = 236, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12) };
            using var data = gen.CreateQrCode(t.QrPayload, QRCodeGenerator.ECCLevel.M);
            var pic = new PictureBox { Image = Image.FromStream(new MemoryStream(new PngByteQRCode(data).GetGraphic(5))), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(180, 180), Location = new Point(405, 14) };
            var text = new Label
            {
                Font = new Font("Consolas", 10), AutoSize = true, Location = new Point(14, 10),
                Text = $"SMART TICKETING - {order.Destination.Line.ToUpper()}\nTICKET  {t.TicketId}\nFROM    {order.Origin.Name}\nTO      {order.Destination.Name}\nTYPE    {order.TicketType.Name}\nFARE    {order.UnitPrice:N0} VND\nISSUED  {t.IssuedAt:dd/MM/yyyy HH:mm}\nVALID   until {t.ValidUntil:dd/MM/yyyy HH:mm}\nSIG     {t.Signature[..16]}",
            };
            box.Controls.AddRange([text, pic]);
            tickets.Controls.Add(box);
        }
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([left, tickets]);
        _content.Controls.Add(row);

        var done = Ui.Primary("Done");
        done.Click += (_, _) => { _ctl.Finish(); ShowWelcome(); };
        FooterRight(done);
        _idleSeconds = InactivitySeconds - 15; // auto-return to idle after ~15 s
    }

    private void ShowOutOfService()
    {
        Page("Temporarily out of service", _ctl.Machine.MachineId);
        Center(Column(
            Ui.Lbl("Temporarily out of service", 30, FontStyle.Bold, Ui.Red),
            Ui.Lbl("Please use another ticket machine. We apologise for the inconvenience.", 15, FontStyle.Regular, Ui.Grey)));
        var tech = Ui.Ghost("Technician: clear printer & put in service", 420);
        tech.Font = Ui.F(11, FontStyle.Bold);
        tech.Click += (_, _) =>
        {
            _ctl.Machine.Printer.SimulateJam = false;
            _ctl.Machine.Printer.PaperLevel = 500;
            _ctl.Machine.CheckDevices();
            _ctl.Finish();
            ShowWelcome();
        };
        FooterLeft(tech);
    }

    // ------------------------------------------------------------------ helpers

    private void Page(string title, string right, bool timed = true)
    {
        _qrTimer.Stop();
        _content.SuspendLayout();
        foreach (Control c in _content.Controls) c.Dispose();
        _content.Controls.Clear();
        _footerLeft.Controls.Clear();
        _footerRight.Controls.Clear();
        _content.ResumeLayout();
        _title.Text = title;
        _headerRight.Text = right;
        _idleSeconds = 0;
        _timeoutLabel.Text = "";
        if (timed) _idleTimer.Start(); else _idleTimer.Stop();
    }

    private void OnIdleTick()
    {
        // Payment in progress with a card being processed: don't interrupt.
        if (_ctl.State is TvmState.Idle or TvmState.OutOfService or TvmState.PrintingTicket) return;
        _idleSeconds++;
        if (_ctl.State == TvmState.Completed)
        {
            if (_idleSeconds >= InactivitySeconds) { _ctl.Finish(); ShowWelcome(); }
            return;
        }
        int left = InactivitySeconds + CountdownSeconds - _idleSeconds;
        _timeoutLabel.Text = _idleSeconds >= InactivitySeconds ? $"No activity - cancelling in {left} s" : "";
        if (left <= 0) { _ctl.Cancel(); ShowWelcome(); }
    }

    private Button MakeCancelButton(string text = "Cancel", int w = 160)
    {
        var b = Ui.Danger(text, w);
        b.Click += (_, _) => { _ctl.Cancel(); ShowWelcome(); };
        return b;
    }

    private void FooterLeft(params Control[] cs)
    {
        foreach (var c in cs) { c.Margin = new Padding(0, 0, 14, 0); _footerLeft.Controls.Add(c); }
        _timeoutLabel.Margin = new Padding(10, 20, 0, 0);
        _footerLeft.Controls.Add(_timeoutLabel);
    }

    private void FooterRight(Control c)
    {
        c.Margin = c is Label ? new Padding(0, 20, 0, 0) : new Padding(0);
        _footerRight.Controls.Add(c);
    }

    private static FlowLayoutPanel Column(params Control[] cs)
    {
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        foreach (var c in cs) { c.Margin = new Padding(0, 6, 0, 6); p.Controls.Add(c); }
        return p;
    }

    private static Control Spacer(int h) => new Panel { Height = h, Width = 1 };

    private void Center(Control c)
    {
        _content.Controls.Add(c);
        void Place() => c.Location = new Point(Math.Max(0, (_content.ClientSize.Width - c.Width) / 2), Math.Max(0, (_content.ClientSize.Height - c.Height) / 2));
        c.SizeChanged += (_, _) => Place();
        Place();
    }

    private void Toast(string msg) => MessageBox.Show(this, msg, "TVM", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _idleTimer.Dispose();
        _qrTimer.Dispose();
        base.OnFormClosed(e);
    }

    private sealed class ActivityFilter(Action onActivity) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            // WM_KEYDOWN, WM_LBUTTONDOWN, WM_MOUSEWHEEL
            if (m.Msg is 0x0100 or 0x0201 or 0x020A) onActivity();
            return false;
        }
    }
}
