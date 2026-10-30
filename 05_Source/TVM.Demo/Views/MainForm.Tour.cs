using TVM.Demo.Models;

namespace TVM.Demo.Views;

/// <summary>
/// Automated walkthrough used to verify the main flow end to end and to capture screenshots for the report.
/// Run with:  TVM.Demo.exe --screenshots &lt;folder&gt;
/// It clicks the same buttons a passenger would (PerformClick), so it exercises the real UI code.
/// </summary>
public partial class MainForm
{
    public void RunScreenshotTour(string dir)
    {
        Directory.CreateDirectory(dir);
        int n = 0;
        void Snap(string name)
        {
            for (int i = 0; i < 5; i++) Application.DoEvents();
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(Path.Combine(dir, $"{++n:00}_{name}.png"));
        }

        // 1. Happy path: MoMo wallet, 2 single-trip tickets Ben Thanh -> Thu Duc
        Snap("welcome");
        Press("START");
        Find<ListBox>().SelectedItem = Find<ListBox>().Items.Cast<Station>().First(s => s.Name == "Thu Duc");
        Snap("destination");
        Press("Next");
        Find<NumericUpDown>().Value = 2;
        Snap("ticket_options");
        Press("Continue to payment");
        Snap("payment_methods");
        Press("MoMo");
        Snap("qr_payment");
        Press("Simulate: passenger paid");
        for (int i = 0; i < 3; i++) OnQrTick();
        Snap("success_qr");
        Press("Done");

        // 2. Card declined, then retry with a valid card
        Press("START");
        Find<ListBox>().SelectedItem = Find<ListBox>().Items.Cast<Station>().First(s => s.Name == "Suoi Tien Bus Terminal");
        Press("Next");
        Press("Continue to payment");
        Press("CARD");
        Find<TextBox>().Text = "4000 0000 0000 0002";
        Snap("card_payment");
        Press("Insert / tap card");
        Snap("card_declined");
        Press("Try again");
        Press("CARD");
        Find<TextBox>().Text = "4242 4242 4242 4242";
        Press("Insert / tap card");
        Snap("success_card");
        Press("Done");

        // 3. Printer jams after a wallet payment -> automatic refund (UC-08), machine goes out of service
        Find<CheckBox>().Checked = true;
        Press("START");
        Find<ListBox>().SelectedItem = Find<ListBox>().Items.Cast<Station>().First(s => s.Name == "Ba Son");
        Press("Next");
        Press("Continue to payment");
        Press("ZaloPay");
        Press("Simulate: passenger paid");
        for (int i = 0; i < 3; i++) OnQrTick();
        Snap("printer_error_refund");
        Press("OK");
        Snap("out_of_service");
        Press("Technician");

        // 4. Output report
        using (var report = new SalesReportForm(_reports))
        {
            report.Show(this);
            for (int i = 0; i < 5; i++) Application.DoEvents();
            using var bmp = new Bitmap(report.Width, report.Height);
            report.DrawToBitmap(bmp, new Rectangle(0, 0, report.Width, report.Height));
            bmp.Save(Path.Combine(dir, $"{++n:00}_sales_report.png"));
            report.Close();
        }
    }

    private T Find<T>() where T : Control => All(this).OfType<T>().First();

    private void Press(string textStart)
    {
        var b = All(this).OfType<Button>().FirstOrDefault(x => x.Enabled && x.Text.StartsWith(textStart, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Button '{textStart}' not found on page '{_title.Text}'.");
        b.PerformClick();
        Application.DoEvents();
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in All(c)) yield return d;
        }
    }
}
