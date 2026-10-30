using System.Data;
using System.Text;
using TVM.Demo.Data;
using TVM.Demo.Models;

namespace TVM.Demo.Views;

/// <summary>Output report: sales per payment method and transaction list for a date range (UC-07).</summary>
public class SalesReportForm : Form
{
    private readonly ReportRepository _repo;
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly ComboBox _method = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly DataGridView _summary = Grid();
    private readonly DataGridView _detail = Grid();
    private readonly Label _totals = Ui.Lbl("", 13, FontStyle.Bold, Ui.Navy);
    private DataTable? _detailData;

    public SalesReportForm(ReportRepository repo)
    {
        _repo = repo;
        Text = "Sales Report - Smart Ticketing";
        ClientSize = new Size(1400, 780);
        StartPosition = FormStartPosition.CenterParent;
        Font = Ui.F(9);
        BackColor = Ui.Back;

        _from.Value = DateTime.Today.AddDays(-7);
        _to.Value = DateTime.Today;
        _method.Items.Add("All");
        foreach (var m in Enum.GetNames<PaymentMethod>()) _method.Items.Add(m);
        _method.SelectedIndex = 0;

        var load = Ui.Primary("Run report", 130, 40);
        load.Font = Ui.F(10, FontStyle.Bold);
        load.Click += (_, _) => LoadReport();
        var export = Ui.Ghost("Export CSV", 130, 40);
        export.Font = Ui.F(10, FontStyle.Bold);
        export.Click += (_, _) => ExportCsv();

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(12, 10, 12, 0), BackColor = Color.White };
        filters.Controls.AddRange([Cap("From"), _from, Cap("To"), _to, Cap("Method"), _method, load, export]);
        foreach (Control c in filters.Controls) c.Margin = new Padding(4, 2, 8, 0);

        var title1 = Ui.Lbl("Summary by payment method", 12, FontStyle.Bold);
        var title2 = Ui.Lbl("Transactions", 12, FontStyle.Bold);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(title1, 0, 0);
        layout.Controls.Add(_summary, 0, 1);
        layout.Controls.Add(_totals, 0, 2);
        layout.Controls.Add(title2, 0, 3);
        layout.Controls.Add(_detail, 0, 4);

        Controls.Add(layout);
        Controls.Add(filters);
        Load += (_, _) => LoadReport();
    }

    private void LoadReport()
    {
        string? m = _method.SelectedIndex <= 0 ? null : (string)_method.SelectedItem!;
        var sum = _repo.GetSummary(_from.Value, _to.Value, m);
        _detailData = _repo.GetSales(_from.Value, _to.Value, m);
        _summary.DataSource = sum;
        _detail.DataSource = _detailData;
        Format(_summary, "Revenue (VND)");
        Format(_detail, "Unit price", "Total");
        decimal revenue = sum.AsEnumerable().Sum(r => r.Field<decimal?>("Revenue (VND)") ?? 0);
        int tickets = sum.AsEnumerable().Sum(r => r.Field<int?>("Tickets") ?? 0);
        _totals.Text = $"{_from.Value:dd/MM/yyyy} - {_to.Value:dd/MM/yyyy}:  {_detailData.Rows.Count} orders · {tickets} tickets · revenue {revenue:N0} VND";
    }

    private void ExportCsv()
    {
        if (_detailData is null) return;
        using var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"SalesReport_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", _detailData.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow r in _detailData.Rows)
            sb.AppendLine(string.Join(",", r.ItemArray.Select(v => $"\"{v}\"")));
        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
    }

    private static void Format(DataGridView g, params string[] money)
    {
        foreach (var name in money)
            if (g.Columns[name] is { } c) { c.DefaultCellStyle.Format = "N0"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
        if (g.Columns["Time"] is { } t) t.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
    }

    private static Label Cap(string t) => new() { Text = t, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, BorderStyle = BorderStyle.None,
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Ui.Navy, ForeColor = Color.White, Font = Ui.F(9, FontStyle.Bold) },
        DefaultCellStyle = new DataGridViewCellStyle { Font = Ui.F(9) },
    };
}
