using System.Text.Json;
using Microsoft.Data.SqlClient;
using TVM.Demo.Controllers;
using TVM.Demo.Data;
using TVM.Demo.Models;
using TVM.Demo.Services;
using TVM.Demo.Views;

namespace TVM.Demo;

internal static class Program
{
    private record Settings(string ConnectionString, string MachineId, string TicketSecret);

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var cfg = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))!;

        try
        {
            var db = new Db(cfg.ConnectionString);
            var repo = new TransactionRepository(db);
            var fares = new FareService(db);

            if (repo.GetMachineStation(cfg.MachineId, out int stationId) is null)
                throw new InvalidOperationException($"Machine '{cfg.MachineId}' is not registered in table Machine.");

            var machine = new TicketVendingMachine(cfg.MachineId, fares.GetStation(stationId), new TicketPrinter());
            var qr = new QrWalletGateway();
            var payments = new PaymentService(new CardGateway(), qr);
            var controller = new TicketController(machine, fares, payments, repo, cfg.TicketSecret);

            var form = new MainForm(controller, qr, new ReportRepository(db));
            var args = Environment.GetCommandLineArgs();
            if (args.Length >= 3 && args[1] == "--screenshots")
            {
                form.Shown += (_, _) =>
                {
                    try { form.RunScreenshotTour(args[2]); }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(args[2], "tour_error.txt"), ex.ToString()); }
                    form.Close();
                };
            }
            Application.Run(form);
        }
        catch (SqlException ex)
        {
            MessageBox.Show(
                "Cannot connect to the TVM_DB database.\n\n" +
                "1. Run Database\\TVM_DB.sql on your SQL Server.\n" +
                "2. Check ConnectionString in appsettings.json.\n\n" + ex.Message,
                "TVM - database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
