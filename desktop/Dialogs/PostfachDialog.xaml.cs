using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>IMAP-Zugang einrichten — mit Verbindungstest und Voreinstellungen gängiger Anbieter.</summary>
public partial class PostfachDialog : Window
{
    private sealed record Anbieter(string Name, string Server, int Port, string Hinweis)
    {
        public override string ToString() => Name;
    }

    private static readonly List<Anbieter> Anbieterliste = new()
    {
        new("Eigener Server / sonstiger Anbieter", "", 993, ""),
        new("IONOS", "imap.ionos.de", 993, ""),
        new("Strato", "imap.strato.de", 993, ""),
        new("Microsoft 365 / Outlook.com", "outlook.office365.com", 993,
            "Microsoft 365 erlaubt IMAP mit Passwort oft nur, wenn der Administrator es freigibt bzw. mit App-Passwort."),
        new("Gmail / Google Workspace", "imap.gmail.com", 993, "Gmail braucht ein App-Passwort (Google-Konto → Sicherheit → App-Passwörter)."),
        new("GMX", "imap.gmx.net", 993, "Bei GMX muss IMAP in den Einstellungen des Postfachs aktiviert sein."),
        new("WEB.DE", "imap.web.de", 993, "Bei WEB.DE muss IMAP in den Einstellungen des Postfachs aktiviert sein."),
        new("T-Online", "secureimap.t-online.de", 993, "T-Online braucht ein eigenes E-Mail-Passwort (nicht das Kundencenter-Passwort)."),
    };

    private readonly AppSettings _settings;

    public PostfachDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        AnbieterBox.ItemsSource = Anbieterliste;
        AnbieterBox.SelectedItem = Anbieterliste.FirstOrDefault(a => a.Server.Length > 0 && a.Server == settings.PostfachServer)
                                   ?? Anbieterliste[0];
        ServerBox.Text = settings.PostfachServer;
        PortBox.Text = settings.PostfachPort.ToString();
        SslBox.IsChecked = settings.PostfachSsl;
        BenutzerBox.Text = settings.PostfachBenutzer;
        OrdnerBox.Text = settings.PostfachOrdner;
        MinutenBox.Text = settings.PostfachAbrufMinuten.ToString();
        AktivBox.IsChecked = settings.PostfachAktiv || settings.PostfachServer.Length == 0;
        if (settings.PostfachPasswortGeschuetzt.Length == 0)
            PasswortHinweis.Text = "Wird verschlüsselt nur für Ihren Windows-Benutzer gespeichert.";
    }

    private void OnAnbieter(object sender, SelectionChangedEventArgs e)
    {
        if (AnbieterBox.SelectedItem is not Anbieter a || StatusText is null) return;
        if (a.Server.Length > 0)
        {
            ServerBox.Text = a.Server;
            PortBox.Text = a.Port.ToString();
            SslBox.IsChecked = true;
        }
        StatusText.Text = a.Hinweis;
    }

    private string Passwort() =>
        PasswortBox.Password.Length > 0 ? PasswortBox.Password : PostfachService.Entschuetzen(_settings.PostfachPasswortGeschuetzt);

    private async void OnTesten(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port)) { StatusText.Text = "Port ist keine Zahl."; return; }
        TestBtn.IsEnabled = false;
        StatusText.Text = "⏳ Verbinde …";
        var fehler = await PostfachService.TestenAsync(ServerBox.Text.Trim(), port, SslBox.IsChecked == true,
            BenutzerBox.Text.Trim(), Passwort(), OrdnerBox.Text.Trim());
        StatusText.Text = fehler is null ? "✓ Verbindung erfolgreich." : $"✗ {fehler}";
        TestBtn.IsEnabled = true;
    }

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port)) { StatusText.Text = "Port ist keine Zahl."; return; }
        if (ServerBox.Text.Trim().Length == 0 || BenutzerBox.Text.Trim().Length == 0)
        {
            StatusText.Text = "Bitte Server und Benutzername eingeben.";
            return;
        }
        _settings.PostfachServer = ServerBox.Text.Trim();
        _settings.PostfachPort = port;
        _settings.PostfachSsl = SslBox.IsChecked == true;
        _settings.PostfachBenutzer = BenutzerBox.Text.Trim();
        if (PasswortBox.Password.Length > 0)
            _settings.PostfachPasswortGeschuetzt = PostfachService.Schuetzen(PasswortBox.Password);
        _settings.PostfachOrdner = OrdnerBox.Text.Trim().Length > 0 ? OrdnerBox.Text.Trim() : "INBOX";
        _settings.PostfachAbrufMinuten = int.TryParse(MinutenBox.Text, out var min) && min > 0 ? min : 5;
        _settings.PostfachAktiv = AktivBox.IsChecked == true;
        try { _settings.Save(); } catch { }
        DialogResult = true;
    }
}
