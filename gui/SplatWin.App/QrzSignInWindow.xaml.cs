using System.Windows;
using System.Windows.Input;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>
/// The user types their QRZ.com password here. It is tried against QRZ first and only saved to
/// Windows Credential Manager if QRZ accepts it. It is never shown, logged or written to a file.
/// </summary>
public partial class QrzSignInWindow : Window
{
    /// <summary>What to show in the main window's status line after this closes.</summary>
    public string? Outcome { get; private set; }

    public QrzSignInWindow()
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Help, (_, _) => App.ShowHelp(SplatWin.Core.HelpTopics.Qrz)));
        var saved = App.QrzSaved();
        SavedText.Text = saved is { } s ? $"Signed in as {s.User}." : "Not signed in.";
        UserBox.Text = saved?.User ?? "";
        SignOutButton.IsEnabled = saved != null;
        Loaded += (_, _) => { if (UserBox.Text.Length == 0) UserBox.Focus(); else PassBox.Focus(); };
    }

    void PassBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; SignIn_Click(sender, e); }
    }

    async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        string user = UserBox.Text.Trim();
        string pass = PassBox.Password;
        if (user.Length == 0 || pass.Length == 0) { ErrorText.Text = "Type your QRZ.com user name and password."; return; }
        SignInButton.IsEnabled = SignOutButton.IsEnabled = false;
        ErrorText.Text = "Signing in…";
        try
        {
            // Try the typed password on its own client first, so a wrong one replaces nothing.
            var trial = new Qrz.Client(App.Http, () => (user, pass), App.QrzService);
            var s = await trial.SignInAsync();
            WinCredentials.Save(App.QrzTarget, user, pass);
            App.ResetQrz();
            App.QrzClient.UseSession(s);   // QRZ asks clients to sign in once and reuse the session
            Outcome = s.IsSubscriber
                ? $"Signed in to QRZ.com as {user}. XML Logbook Data subscription until {s.SubExp}."
                : $"Signed in to QRZ.com as {user}, but this account has no XML Logbook Data subscription: QRZ lookups won't give positions.";
            PassBox.Clear();
            DialogResult = true;
        }
        catch (Exception ex) when (ex is QrzSignInException or LookupException)
        {
            ErrorText.Text = ex.Message;
            PassBox.Clear();
            PassBox.Focus();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "The sign-in couldn't be saved in Windows Credential Manager: " + ex.Message;
        }
        finally
        {
            pass = "";
            SignInButton.IsEnabled = true;
            SignOutButton.IsEnabled = App.QrzSaved() != null;
        }
    }

    void SignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WinCredentials.Delete(App.QrzTarget);
            App.ResetQrz();
            Outcome = "Signed out of QRZ.com; the saved password was removed from Windows Credential Manager. Call sign lookups use callook.info (US only).";
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = "Couldn't remove the saved sign-in: " + ex.Message; }
    }
}
