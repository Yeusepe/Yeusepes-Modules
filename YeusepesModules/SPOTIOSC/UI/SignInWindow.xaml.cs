using System.Windows;
using VRCOSC.App.UI.Core;

namespace YeusepesModules.SPOTIOSC.UI;

public partial class SignInWindow : IManagedWindow
{
    private readonly SpotiOSC _module;
    private object _comparer;

    public SignInWindow(SpotiOSC module)
    {
        InitializeComponent();
        _module = module;
        _comparer = module;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _comparer = new object();
    }

    public object GetComparer() => _comparer;

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var setting = _module.GetSetting(SpotiOSC.SpotiSettings.SignInButton);
        MainGrid.Children.Insert(0, new SignIn(_module, setting));
        MainGrid.Children.Add(new AdvancedCredentials());
    }
}
