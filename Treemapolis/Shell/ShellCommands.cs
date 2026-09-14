using ShellN;
using ShellN.Extensions;

namespace Treemapolis.Shell;

// what Explorer would do with an entry, through the shell itself, so every handler and extension installed on the machine takes part.
public sealed class ShellCommands(HWND owner)
{
    private const CMF _contextMenuFlags = CMF.CMF_EXPLORE | CMF.CMF_EXTENDEDVERBS | CMF.CMF_CANRENAME;

    // an entry scanned from disk has no id list of its own, it is bound back through its path.
    public static unsafe ShellItem? CreateItem(NamespaceTree tree, int index)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (index < 0 || index >= tree.Count || (tree[index].Flags & EntryFlags.Synthetic) != 0)
            return null;

        var node = tree.GetShellNode(index);
        if (node?.IdList != null)
        {
            fixed (byte* pidl = node.IdList)
            {
                return ShellItem.FromPidl((nint)pidl, throwOnError: false);
            }
        }

        var path = tree.GetFileSystemPath(index);
        if (path == null)
            return null;

        var item = ShellItem.FromParsingName(path, throwOnError: false);
        if (item != null)
            return item;

        // a file under a namespace junction refuses its own path, bind data describing it lets the parse through.
        using var context = IBindCtxExtensions.CreateBindCtx(path, attributes: tree[index].Attributes, throwOnError: false);
        return ShellItem.FromParsingName(path, context?.Object, throwOnError: false);
    }

    public void Open(NamespaceTree tree, int index)
    {
        ArgumentNullException.ThrowIfNull(tree);
        RunOnShellThread(() => CreateItem(tree, index), item =>
        {
            var hr = item.InvokeDefaultCommand(owner, false);
            if (hr.IsError)
            {
                Application.TraceWarning($"'{item.SIGDN_DESKTOPABSOLUTEPARSING}' could not be opened: {hr}");
            }
        });
    }

    public static void Reveal(NamespaceTree tree, int index)
    {
        ArgumentNullException.ThrowIfNull(tree);
        RunOnShellThread(() => CreateItem(tree, index), RevealItem);
    }

    public static void Reveal(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        RunOnShellThread(() => ShellItem.FromParsingName(path, throwOnError: false), RevealItem);
    }

    // an Explorer window on the parent with the item selected, an item without a parent, the desktop, is simply opened.
    private static void RevealItem(ShellItem item)
    {
        using var itemList = item.GetIdList(false);
        if (itemList is null)
            return;

        using var parent = item.GetParent();
        using var parentList = parent?.GetIdList(false);
        if (parentList is null)
        {
            ShellN.Functions.SHOpenFolderAndSelectItems(itemList.Pointer, 0, 0, 0);
            return;
        }

        unsafe
        {
            var pointer = itemList.Pointer;
            ShellN.Functions.SHOpenFolderAndSelectItems(parentList.Pointer, 1, (nint)(&pointer), 0);
        }
    }

    // launching or revealing can take a while and put up UI of its own, so it never runs on the UI thread.
    // the shell item is bound on that thread itself, a COM object created on the UI thread cannot be called from another apartment,
    // and the thread is STA, as ShellExecute and the handlers it loads expect.
    private static void RunOnShellThread(Func<ShellItem?> create, Action<ShellItem> action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var item = create();
                if (item != null)
                {
                    action(item);
                }
            }
            catch (Exception ex)
            {
                Application.TraceError(ex.ToString());
            }
        })
        {
            IsBackground = true,
            Name = nameof(ShellCommands),
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // returns the parsing name of a folder the menu asked to open, for the landscape to show it rather than Explorer.
    public string? ShowContextMenu(NamespaceTree tree, int index)
    {
        ArgumentNullException.ThrowIfNull(tree);
        using var item = CreateItem(tree, index);
        if (item == null)
            return null;

        // see the remarks of TrackPopupMenu, a menu owned by a window that is not in the foreground does not close.
        DirectN.Functions.SetForegroundWindow(owner);
        using var site = new ContextMenuSite(owner);
        var invoked = false;
        item.ShowContextMenu(site, flags: _contextMenuFlags, invoke: (menu, hwnd, id) =>
        {
            invoked = true;
            site.NavigateToParsingName = null;
            return ShellItem.Invoke(menu, hwnd, id);
        });
        DirectN.Functions.PostMessageW(owner, MessageDecoder.WM_NULL);
        return invoked ? site.NavigateToParsingName : null;
    }

    // a folder, of the file system or of the shell, opens as it is, anything else opens its parent with it selected, as Explorer would.
    public static StartLocation? ResolveStart(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        using var item = ShellItem.FromParsingName(location, throwOnError: false) ?? ShellItem.FromSplitParsingName(location, throwOnError: false);
        if (item == null)
            return null;

        var attributes = item.Attributes;
        if (attributes.HasFlag(SFGAO_FLAGS.SFGAO_FOLDER) && !attributes.HasFlag(SFGAO_FLAGS.SFGAO_STREAM))
        {
            var idList = item.GetIdListAsByteArray(false);
            return idList == null ? null : new StartLocation { IdList = idList };
        }

        using var parent = item.GetParent();
        var parentIdList = parent?.GetIdListAsByteArray(false);
        if (parentIdList == null)
            return null;

        // the tree names a file system item by its file name and a shell item by its display name.
        var path = item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, false);
        var name = path != null ? Path.GetFileName(path) : item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false);
        return new StartLocation { IdList = parentIdList, SelectName = name };
    }

    public static byte[]? GetParentIdList(NamespaceTree tree, int index)
    {
        ArgumentNullException.ThrowIfNull(tree);
        using var item = CreateItem(tree, index);
        using var parent = item?.GetParent();
        return parent?.GetIdListAsByteArray(false);
    }
}
