using ShellN.Extensions;

namespace Treemapolis.Shell;

// the site a shell context menu asks for while it is up. opening a folder from the menu is caught here and shown in the landscape instead of Explorer.
[GeneratedComClass]
public sealed partial class ContextMenuSite(HWND owner) :
    DirectN.IServiceProvider,
    IObjectWithSite,
    IOleWindow,
    ShellN.IHandlerActivationHost,
    IDisposable
{
    private nint _site;

    public string? NavigateToParsingName { get; set; }

    public HRESULT QueryService(in Guid guidService, in Guid riid, out nint ppvObject)
    {
        ppvObject = DirectN.Extensions.Com.ComObject.GetOrCreateComInstance(this, riid, CreateComInterfaceFlags.None);
        return ppvObject == 0 ? Constants.E_NOINTERFACE : Constants.S_OK;
    }

    public HRESULT GetSite(in Guid riid, out nint ppvSite)
    {
        if (_site != 0)
            return Marshal.QueryInterface(_site, riid, out ppvSite);

        ppvSite = 0;
        return Constants.E_NOINTERFACE;
    }

    public HRESULT SetSite(nint pUnkSite)
    {
        Dispose();
        if (pUnkSite != 0)
        {
            Marshal.AddRef(pUnkSite);
        }

        _site = pUnkSite;
        return Constants.S_OK;
    }

    public HRESULT GetWindow(out HWND phwnd)
    {
        phwnd = owner;
        return Constants.S_OK;
    }

    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => Constants.E_NOTIMPL;

    public HRESULT BeforeCoCreateInstance(in Guid clsidHandler, ShellN.IShellItemArray itemsBeingActivated, ShellN.IHandlerInfo handlerInfo)
    {
        if (clsidHandler != ShellN.Constants.ExecuteFolder)
            return Constants.S_OK;

        foreach (var item in itemsBeingActivated.Enumerate(true))
        {
            var parsingName = item.SIGDN_DESKTOPABSOLUTEPARSING;
            item.Dispose();
            if (!string.IsNullOrEmpty(parsingName))
            {
                NavigateToParsingName = parsingName;

                // the shell would open a single window anyway, so the first folder is the one.
                return Constants.ERROR_CANCELLED;
            }
        }
        return Constants.S_OK;
    }

    public HRESULT BeforeCreateProcess(PWSTR applicationPath, PWSTR commandLine, ShellN.IHandlerInfo handlerInfo) => Constants.S_OK;

    public void Dispose()
    {
        var site = Interlocked.Exchange(ref _site, 0);
        if (site != 0)
        {
            Marshal.Release(site);
        }
    }
}
