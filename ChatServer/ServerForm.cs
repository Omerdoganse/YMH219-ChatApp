using System.Net;
using System.Net.Sockets;
using ChatApp.Core.Models;
using ChatApp.Core.Network;
using ChatApp.Core.Protocol;

namespace ChatServer;

/// <summary>
/// Sohbet sunucusu ana penceresi: sunucuyu baslatir/durdurur, bagli istemcileri ve sunucu gunlugunu gosterir.
/// Ag islemleri ChatServerService sinifinda; form yalnizca olaylari dinleyip arayuzu gunceller.
/// </summary>
public class ServerForm : Form
{
    private readonly ChatServerService _server = new();

    // --- Ust panel ---
    private readonly NumericUpDown numPort = new();
    private readonly Button btnStart = new();
    private readonly Button btnStop = new();
    private readonly Label lblStatus = new();
    private readonly Label lblAddresses = new();

    // --- Istemci listesi ---
    private readonly GroupBox grpClients = new();
    private readonly ListView lvClients = new();
    private readonly Button btnKick = new();

    // --- Gunluk ve duyuru ---
    private readonly ListBox lstLog = new();
    private readonly TextBox txtAnnouncement = new();
    private readonly Button btnAnnounce = new();

    // --- Durum cubugu ---
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel lblStatusBar = new();

    public ServerForm()
    {
        Text = "YMH219 - Sohbet Sunucusu";
        ClientSize = new Size(960, 580);
        MinimumSize = new Size(820, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);

        BuildLayout();
        WireServerEvents();
        UpdateUiState();
    }

    // ================= Arayuz kurulumu =================

    private void BuildLayout()
    {
        // Ust panel: port, baslat/durdur, durum
        var top = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(10, 8, 10, 4) };

        var lblPort = new Label { Text = "Port:", AutoSize = true, Location = new Point(12, 16) };
        numPort.SetBounds(52, 12, 80, 27);
        numPort.Minimum = 1024;
        numPort.Maximum = 65535;
        numPort.Value = ChatRules.DefaultPort;

        btnStart.Text = "Sunucuyu Baslat";
        btnStart.SetBounds(146, 10, 140, 32);
        btnStart.Click += BtnStart_Click;

        btnStop.Text = "Durdur";
        btnStop.SetBounds(294, 10, 100, 32);
        btnStop.Click += BtnStop_Click;

        lblStatus.AutoSize = true;
        lblStatus.Font = new Font(Font, FontStyle.Bold);
        lblStatus.Location = new Point(410, 16);

        lblAddresses.AutoSize = true;
        lblAddresses.ForeColor = Color.DimGray;
        lblAddresses.Location = new Point(12, 50);
        lblAddresses.Text = "Bu bilgisayarin adresleri: " + GetLocalAddresses();

        top.Controls.AddRange([lblPort, numPort, btnStart, btnStop, lblStatus, lblAddresses]);

        // Sol: bagli istemciler
        lvClients.Dock = DockStyle.Fill;
        lvClients.View = View.Details;
        lvClients.FullRowSelect = true;
        lvClients.MultiSelect = false;
        lvClients.HideSelection = false;
        lvClients.Columns.Add("No", 40);
        lvClients.Columns.Add("Kullanici", 110);
        lvClients.Columns.Add("Adres", 130);
        lvClients.Columns.Add("Baglanma", 75);
        lvClients.Columns.Add("Mesaj", 55, HorizontalAlignment.Right);
        lvClients.SelectedIndexChanged += (_, _) => UpdateUiState();

        btnKick.Text = "Secili Istemcinin Baglantisini Kes";
        btnKick.Dock = DockStyle.Bottom;
        btnKick.Height = 34;
        btnKick.Click += BtnKick_Click;

        grpClients.Text = "Bagli Istemciler (0)";
        grpClients.Dock = DockStyle.Fill;
        grpClients.Padding = new Padding(8);
        grpClients.Controls.Add(lvClients);
        grpClients.Controls.Add(btnKick);

        // Sag: gunluk + duyuru
        lstLog.Dock = DockStyle.Fill;
        lstLog.IntegralHeight = false;
        lstLog.HorizontalScrollbar = true;
        lstLog.Font = new Font("Consolas", 9.5F);

        var announcePanel = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 6, 0, 0) };
        txtAnnouncement.Dock = DockStyle.Fill;
        txtAnnouncement.PlaceholderText = "Tum istemcilere duyuru yazin...";
        txtAnnouncement.MaxLength = ChatRules.MaxMessageLength;
        txtAnnouncement.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnAnnounce.PerformClick(); }
        };
        btnAnnounce.Text = "Duyuru Gonder";
        btnAnnounce.Dock = DockStyle.Right;
        btnAnnounce.Width = 120;
        btnAnnounce.Click += BtnAnnounce_Click;
        announcePanel.Controls.Add(txtAnnouncement);
        announcePanel.Controls.Add(btnAnnounce);

        var grpLog = new GroupBox { Text = "Sunucu Gunlugu", Dock = DockStyle.Fill, Padding = new Padding(8) };
        grpLog.Controls.Add(lstLog);
        grpLog.Controls.Add(announcePanel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Padding = new Padding(8, 0, 8, 4)
        };
        split.Panel1.Controls.Add(grpClients);
        split.Panel2.Controls.Add(grpLog);

        statusStrip.Items.Add(lblStatusBar);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(statusStrip);

        Load += (_, _) => split.SplitterDistance = 440;
        FormClosing += ServerForm_FormClosing;
    }

    // ================= Sunucu olaylari =================

    private void WireServerEvents()
    {
        _server.ClientConnected += (_, _) => RunOnUi(RefreshClientList);
        _server.ClientDisconnected += (_, _) => RunOnUi(RefreshClientList);
        _server.MessageRelayed += (_, packet) => RunOnUi(() =>
        {
            AddLog(packet.ToDisplayString());
            RefreshClientList(); // mesaj sayilarini guncelle
        });
        _server.Log += (_, message) => RunOnUi(() => AddLog($"[{DateTime.Now:HH:mm:ss}] (bilgi) {message}"));
    }

    /// <summary>
    /// Servis olaylari arka plan is parcacigindan gelir; arayuz kontrollerine yalnizca UI is parcacigindan erisilir.
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
            return;
        try
        {
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Form kapanirken gelen olaylar yok sayilir.
        }
    }

    // ================= Buton olaylari =================

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        try
        {
            _server.Start((int)numPort.Value);
        }
        catch (ChatException ex)
        {
            MessageBox.Show(ex.Message, "Sunucu baslatilamadi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateUiState();
    }

    private async void BtnStop_Click(object? sender, EventArgs e)
    {
        btnStop.Enabled = false;
        try
        {
            await _server.StopAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Sunucu durdurulurken hata olustu:\n" + ex.Message, "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshClientList();
        UpdateUiState();
    }

    private async void BtnKick_Click(object? sender, EventArgs e)
    {
        if (lvClients.SelectedItems.Count == 0 || lvClients.SelectedItems[0].Tag is not ConnectedClientInfo client)
            return;

        var answer = MessageBox.Show($"{client.UserName} kullanicisinin baglantisi kesilsin mi?", "Onay",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            await _server.KickAsync(client.Id);
        }
        catch (ChatException ex)
        {
            MessageBox.Show(ex.Message, "Islem yapilamadi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void BtnAnnounce_Click(object? sender, EventArgs e)
    {
        try
        {
            await _server.SendAnnouncementAsync(txtAnnouncement.Text);
            txtAnnouncement.Clear();
        }
        catch (ChatException ex)
        {
            MessageBox.Show(ex.Message, "Duyuru gonderilemedi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ServerForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            _server.Dispose(); // calisiyorsa istemcilere haber verip durdurur
        }
        catch (Exception)
        {
            // Kapanis sirasindaki ag hatalari onemsiz.
        }
    }

    // ================= Yardimci metotlar =================

    private void RefreshClientList()
    {
        int? selectedId = (lvClients.SelectedItems.Count > 0 ? lvClients.SelectedItems[0].Tag as ConnectedClientInfo : null)?.Id;
        IReadOnlyList<ConnectedClientInfo> clients = _server.IsRunning ? _server.Clients : [];

        lvClients.BeginUpdate();
        lvClients.Items.Clear();
        foreach (ConnectedClientInfo c in clients)
        {
            var item = new ListViewItem(c.Id.ToString()) { Tag = c };
            item.SubItems.Add(c.UserName);
            item.SubItems.Add(c.RemoteEndPoint);
            item.SubItems.Add(c.ConnectedAt.ToString("HH:mm:ss"));
            item.SubItems.Add(c.MessageCount.ToString());
            item.Selected = c.Id == selectedId;
            lvClients.Items.Add(item);
        }
        lvClients.EndUpdate();

        grpClients.Text = $"Bagli Istemciler ({clients.Count})";
        UpdateUiState();
    }

    private void AddLog(string line)
    {
        lstLog.Items.Add(line);
        lstLog.TopIndex = Math.Max(0, lstLog.Items.Count - 1);
    }

    private void UpdateUiState()
    {
        bool running = _server.IsRunning;
        btnStart.Enabled = !running;
        btnStop.Enabled = running;
        numPort.Enabled = !running;
        btnAnnounce.Enabled = running;
        txtAnnouncement.Enabled = running;
        btnKick.Enabled = running && lvClients.SelectedItems.Count > 0;

        lblStatus.Text = running ? $"● Calisiyor (port {_server.Port})" : "● Durduruldu";
        lblStatus.ForeColor = running ? Color.ForestGreen : Color.Firebrick;
        lblStatusBar.Text = running
            ? $"Sunucu calisiyor - {lvClients.Items.Count} istemci bagli"
            : "Sunucu durduruldu - baslatmak icin 'Sunucuyu Baslat' butonuna basin";
    }

    /// <summary>
    /// Yerel agdan baglanacak istemciler icin bu bilgisayarin IPv4 adreslerini dondurur.
    /// </summary>
    private static string GetLocalAddresses()
    {
        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Prepend("127.0.0.1")
                .Distinct();
            return string.Join(", ", addresses);
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }
}
