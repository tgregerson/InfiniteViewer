#include "pch.h"

#include <libloaderapi.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <sstream>
#include <string>
#include <vector>
#include <winuser.h>

#include <wil\resource.h>  // Windows Implementation Library

using namespace Microsoft::WRL;

// Update these for each application
//
// This UUID must match the com server in the app manifest. It can be randomly generated.
#define __UUID_STR "E1742A23-ED4B-4AED-9DD2-30963A98BFF7"
constexpr auto kMenuTitle = L"Open in InfiniteViewer";

// Paths are relative to the location of the DLL in the package.
constexpr auto kExecutableRelativePath = L"InfiniteViewer.exe";
// It might also be possible to use Assets\\Square44x44Logo.scale-100.png to avoid the need to
// manually update the icon file.
constexpr auto kIconRelativePath = L"icon.ico";

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD ul_reason_for_call,
    LPVOID lpReserved)
{
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

std::wstring GetLastErrorAsString()
{
    DWORD errorMessageID = ::GetLastError();
    if (errorMessageID == 0) {
        return std::wstring(L"No error");
    }
    LPWSTR messageBuffer = nullptr;
    size_t size = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        NULL, errorMessageID, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&messageBuffer, 0, NULL);
    std::wstring message(messageBuffer, size);
    LocalFree(messageBuffer);
    return message;
}

HRESULT GetRootDir(std::wstring* out_path, HWND parent) {
    wchar_t path[MAX_PATH];
    HMODULE hm = NULL;

    if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPWSTR) &DllMain, &hm) == 0)
    {
        std::wstring error = GetLastErrorAsString() + L" - GetModuleHandle";
        MessageBoxW(parent, error.c_str(), kMenuTitle, MB_OK);
        return E_FAIL;
    }
    if (GetModuleFileNameW(hm, path, sizeof(path) / 2) == 0)
    {
        std::wstring error = GetLastErrorAsString() + L" - GetModuleFileName";
        MessageBoxW(parent, error.c_str(), kMenuTitle, MB_OK);
        return E_FAIL;
    }
    const std::wstring path_str(path);
    const std::wstring dir = path_str.substr(0, path_str.find_last_of(L"/\\"));
    *out_path = dir + L"\\";
    return S_OK;
}

HRESULT GetExecutablePath(std::wstring* out_path, HWND parent) {
    std::wstring dir;
    RETURN_IF_FAILED(GetRootDir(&dir, parent));
    *out_path = dir + kExecutableRelativePath;
    return S_OK;
}

HRESULT GetIconPath(std::wstring* out_path, HWND parent) {
    std::wstring dir;
    RETURN_IF_FAILED(GetRootDir(&dir, parent));
    *out_path = dir + kIconRelativePath;
    return S_OK;
}

HRESULT RunExecutable(HWND parent, std::wstring itemName) {
    // std::wstring icon_path;
    // RETURN_IF_FAILED(GetIconPath(&icon_path, parent));
    // MessageBoxW(nullptr, icon_path.c_str(), L"Icon Path", MB_OK);

    const std::wstring executable_path = kExecutableRelativePath;

    STARTUPINFO si = {};
    PROCESS_INFORMATION pi = {};

    std::wstring params = itemName;
    // std::wstring debug_msg = L"Command:\n" + executable_path + L"\n\nParams:\n" + params;
    // MessageBoxW(parent, debug_msg.c_str(), L"Executing...", MB_OK);
    if ((INT_PTR)ShellExecuteW(parent, L"open", executable_path.c_str(), params.c_str(), nullptr, SW_SHOWNORMAL) > 32) {
        return S_OK;
    }
    return E_ABORT;
}

class ExplorerCommandBase : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite> {
public:
    virtual const wchar_t* Title() = 0;
    virtual const EXPCMDFLAGS Flags() { return ECF_DEFAULT; }
    virtual const EXPCMDSTATE State(_In_opt_ IShellItemArray* selection) { return ECS_ENABLED; }
    virtual HRESULT DoInvoke(_In_opt_ IShellItemArray* selection) = 0;

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(_In_opt_ IShellItemArray* items, _Outptr_result_nullonfailure_ PWSTR* name)
    {
        *name = nullptr;
        auto title = wil::make_cotaskmem_string_nothrow(Title());
        RETURN_IF_NULL_ALLOC(title);
        *name = title.release();
        return S_OK;
    }
    IFACEMETHODIMP GetIcon(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* icon)
    {
        std::wstring val;
        RETURN_IF_FAILED(GetIconPath(&val, nullptr));
        SHStrDupW(val.c_str(), icon);
        if (*icon == 0) {
            return E_NOTIMPL;
        }
        return S_OK;
    }
    IFACEMETHODIMP GetToolTip(_In_opt_ IShellItemArray*, _Outptr_result_nullonfailure_ PWSTR* infoTip)
    {
        *infoTip = nullptr;
        return E_NOTIMPL;
    }
    IFACEMETHODIMP GetCanonicalName(_Out_ GUID* guidCommandName)
    {
        *guidCommandName = GUID_NULL;
        return S_OK;
    }
    IFACEMETHODIMP GetState(_In_opt_ IShellItemArray* selection, _In_ BOOL okToBeSlow, _Out_ EXPCMDSTATE* cmdState)
    {
        *cmdState = State(selection);
        return S_OK;
    }
    IFACEMETHODIMP Invoke(_In_opt_ IShellItemArray* selection, _In_opt_ IBindCtx*) noexcept try
    {
        return DoInvoke(selection);
    }
    CATCH_RETURN();

    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS* flags)
    {
        *flags = Flags();
        return S_OK;
    }
    IFACEMETHODIMP EnumSubCommands(_COM_Outptr_ IEnumExplorerCommand** enumCommands)
    {
        *enumCommands = nullptr;
        return E_NOTIMPL;
    }

    // IObjectWithSite
    IFACEMETHODIMP SetSite(_In_ IUnknown* site) noexcept
    {
        m_site = site;
        return S_OK;
    }
    IFACEMETHODIMP GetSite(_In_ REFIID riid, _COM_Outptr_ void** site) noexcept { return m_site.CopyTo(riid, site); }

protected:
    ComPtr<IUnknown> m_site;
};

class __declspec(uuid(__UUID_STR)) ExplorerCommandHandler final : public ExplorerCommandBase {
public:
    const wchar_t* Title() override { return kMenuTitle; }
    HRESULT DoInvoke(_In_opt_ IShellItemArray* selection) override {
        HWND parent = nullptr;
        if (m_site) {
            ComPtr<IOleWindow> oleWindow;
            RETURN_IF_FAILED(m_site.As(&oleWindow));
            RETURN_IF_FAILED(oleWindow->GetWindow(&parent));
        }

        if (selection) {
            DWORD count;
            RETURN_IF_FAILED(selection->GetCount(&count));
            for (DWORD i = 0; i < count; ++i) {
                IShellItem* psi;
                LPWSTR itemName;
                selection->GetItemAt(i, &psi);
                RETURN_IF_FAILED(psi->GetDisplayName(SIGDN_FILESYSPATH, &itemName));

                if (RunExecutable(parent, itemName) != S_OK) {
                    const std::wstring error = GetLastErrorAsString();
                    MessageBoxW(parent, error.c_str(), L"Failed to execute", MB_OK);
                }
            }
        }

        return S_OK;
    }
};

CoCreatableClass(ExplorerCommandHandler)
CoCreatableClassWrlCreatorMapInclude(ExplorerCommandHandler)

STDAPI DllGetActivationFactory(_In_ HSTRING activatableClassId, _COM_Outptr_ IActivationFactory** factory)
{
    return Module<ModuleType::InProc>::GetModule().GetActivationFactory(activatableClassId, factory);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _COM_Outptr_ void** instance)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, instance);
}