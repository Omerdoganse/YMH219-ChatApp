using ChatApp.Core.Models;
using ChatApp.Core.Network;
using ChatApp.Core.Protocol;
using ChatClient.Controls;

namespace ChatClient;

/// <summary>
/// Sohbet istemcisi ana penceresi: sunucuya kullanici adiyla baglanir, mesaj gonderir/alir.
/// Ag islemleri ChatClientService sinifinda; form yalnizca olaylari dinleyip arayuzu gunceller.
/// </summary>
public class ClientForm : Form
{
    private readonly ChatClientService _client = new();

    // --- Baglanti paneli ---
    private readonly TextBox txtHost = new();
    private readonly NumericUpDown numPort = new();
    private readonly TextBox txtUserName = new();
    private readonly Button btnConnect = new();
    private readonly Button btnDisconnect = new();
    private readonly Label lblStatus = new();

    // --- Mesajlar ve kullanicilar ---
    private readonly MessageListBox lstMessages = new();
    private readonly GroupBox grpUsers = new();
    private readonly ListBox lstUsers = new();

    // --- Mesaj yazma ---
    private readonly TextBox txtMessage = new();
    private readonly Button btnSend = new();
    private readonly Label lblCharCount = new();

    private bool _busy; // baglanma islemi suruyor mu?

    public ClientForm()
    {
        Text = "YMH219 - Sohbet Istemcisi";
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);

        BuildLayout();
        WireClientEvents();
        UpdateUiState();
    }

    // ================= Arayuz kurulumu =================

    private void BuildLayout()
    {
        // Ust panel: sunucu adresi, port, kullanici adi, baglan/kes
        var top = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 4) };

        var lblHost = new Label { Text = "Sunucu:", AutoSize = true, Location = new Point(10, 16) };
        txtHost.SetBounds(68, 12, 120, 27);
        txtHost.Text = "127.0.0.1";

        var lblPort = new Label { Text = "Port:", AutoSize = true, Location = new Point(198, 16) };
        numPort.SetBounds(238, 12, 75, 27);
        numPort.Minimum = 1024;
        numPort.Maximum = 65535;
        numPort.Value = ChatRules.DefaultPort;

        var lblUser = new Label { Text = "Kullanici adi:", AutoSize = true, Location = new Point(324, 16) };
        txtUserName.SetBounds(418, 12, 120, 27);
        txtUserName.MaxLength = ChatRules.MaxUserNameLength;
        txtUserName.Text = "Kullanici" + Random.Shared.Next(10, 99);
        txtUserName.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnConnect.PerformClick(); }
        };

        btnConnect.Text = "Baglan";
        btnConnect.SetBounds(550, 10, 90, 32);
        btnConnect.Click += BtnConnect_Click;

        btnDisconnect.Text = "Baglantiyi Kes";
        btnDisconnect.SetBounds(646, 10, 120, 32);
        btnDisconnect.Click += BtnDisconnect_Click;

        lblStatus.AutoSize = true;
        lblStatus.Font = new Font(Font, FontStyle.Bold);
        lblStatus.Location = new Point(776, 16);

        top.Controls.AddRange([lblHost, txtHost, lblPort, numPort, lblUser, txtUserName, btnConnect, btnDisconnect, lblStatus]);

        // Alt panel: mesaj yazma
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 6, 10, 8) };
        txtMessage.Dock = DockStyle.Fill;
        txtMessage.MaxLength = ChatRules.MaxMessageLength;
        txtMessage.PlaceholderText = "Mesajinizi yazin ve Enter'a basin...";
        txtMessage.KeyDown += TxtMessage_KeyDown;
        txtMessage.TextChanged += (_, _) => UpdateCharCount();

        lblCharCount.Dock = DockStyle.Right;
        lblCharCount.Width = 70;
        lblCharCount.TextAlign = ContentAlignment.MiddleCenter;
        lblCharCount.ForeColor = Color.DimGray;

        btnSend.Text = "Gonder";
        btnSend.Dock = DockStyle.Right;
        btnSend.Width = 100;
        btnSend.Click += BtnSend_Click;

        bottom.Controls.Add(txtMessage);
        bottom.Controls.Add(lblCharCount);
        bottom.Controls.Add(btnSend);

        // Orta: mesaj listesi + cevrimici kullanicilar
        lstMessages.Dock = DockStyle.Fill;
        var grpMessages = new GroupBox { Text = "Mesajlar", Dock = DockStyle.Fill, Padding = new Padding(8) };
        grpMessages.Controls.Add(lstMessages);

        lstUsers.Dock = DockStyle.Fill;
        lstUsers.IntegralHeight = false;
        grpUsers.Text = "Cevrimici (0)";
        grpUsers.Dock = DockStyle.Right;
        grpUsers.Width = 190;
        grpUsers.Padding = new Padding(8);
        grpUsers.Controls.Add(lstUsers);

        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        middle.Controls.Add(grpMessages);
        middle.Controls.Add(grpUsers);

        Controls.Add(middle);
        Controls.Add(bottom);
        Controls.Add(top);

        FormClosing += ClientForm_FormClosing;
        UpdateCharCount();
    }

    // ================= Istemci olaylari =================

    private void WireClientEvents()
    {
        _client.PacketReceived += (_, packet) => RunOnUi(() => lstMessages.AddMessage(packet));
        _client.UserListReceived += (_, users) => RunOnUi(() => UpdateUserList(users));
        _client.Disconnected += (_, reason) => RunOnUi(() =>
        {
            lstMessages.AddMessage(ChatPacket.SystemNotice(reason));
            UpdateUserList([]);
            UpdateUiState();
        });
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

    private async void BtnConnect_Click(object? sender, EventArgs e)
    {
        _busy = true;
        UpdateUiState();
        lblStatus.Text = "● Baglaniyor...";
        lblStatus.ForeColor = Color.DarkOrange;

        try
        {
            lstMessages.CurrentUserName = txtUserName.Text.Trim();
            await _client.ConnectAsync(txtHost.Text, (int)numPort.Value, txtUserName.Text);
            txtMessage.Focus();
        }
        catch (ChatException ex)
        {
            MessageBox.Show(ex.Message, "Baglanti hatasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Beklenmeyen baglanti hatasi:\n" + ex.Message, "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateUiState();
        }
    }

    private async void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        btnDisconnect.Enabled = false;
        await _client.DisconnectAsync();
        UpdateUiState();
    }

    private async void BtnSend_Click(object? sender, EventArgs e) => await SendCurrentMessageAsync();

    private async void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await SendCurrentMessageAsync();
        }
    }

    /// <summary>
    /// Mesaj kutusundaki metni sunucuya gonderir. Mesaj, sunucudan geri geldiginde listeye eklenir.
    /// </summary>
    private async Task SendCurrentMessageAsync()
    {
        if (!_client.IsConnected)
            return;

        try
        {
            await _client.SendMessageAsync(txtMessage.Text);
            txtMessage.Clear();
        }
        catch (ChatException ex)
        {
            MessageBox.Show(ex.Message, "Mesaj gonderilemedi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        txtMessage.Focus();
    }

    private void ClientForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            if (_client.IsConnected)
                _client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
            _client.Dispose();
        }
        catch (Exception)
        {
            // Kapanis sirasindaki ag hatalari onemsiz.
        }
    }

    // ================= Yardimci metotlar =================

    private void UpdateUserList(IReadOnlyList<string> users)
    {
        lstUsers.BeginUpdate();
        lstUsers.Items.Clear();
        foreach (string user in users)
            lstUsers.Items.Add(user);
        lstUsers.EndUpdate();
        grpUsers.Text = $"Cevrimici ({users.Count})";
    }

    private void UpdateCharCount() =>
        lblCharCount.Text = $"{txtMessage.TextLength}/{ChatRules.MaxMessageLength}";

    private void UpdateUiState()
    {
        bool connected = _client.IsConnected;
        bool editable = !connected && !_busy;

        txtHost.Enabled = editable;
        numPort.Enabled = editable;
        txtUserName.Enabled = editable;
        btnConnect.Enabled = editable;
        btnDisconnect.Enabled = connected;
        txtMessage.Enabled = connected;
        btnSend.Enabled = connected;

        if (_busy)
            return;

        lblStatus.Text = connected ? "● Bagli" : "● Bagli degil";
        lblStatus.ForeColor = connected ? Color.ForestGreen : Color.Firebrick;
        Text = connected
            ? $"YMH219 - Sohbet Istemcisi - {_client.UserName} @ {_client.ServerAddress}"
            : "YMH219 - Sohbet Istemcisi";
    }
}
