#include "pch.h"

#include <libloaderapi.h>
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

// This implements a basic "Open With..." context menu entry.

// Update these for each application
//
// This UUID must match the com server in the app manifest. It can be randomly generated.
#define __UUID_STR "E1742A23-ED4B-4AED-9DD2-30963A98BFF7"

constexpr auto __MENU_TITLE = L"InfiniteViewer";
constexpr auto kExecutable = L"C:\\Users\\conve\\AppData\\Local\\Microsoft\\WindowsApps\\InfiniteViewer.exe";
//constexpr auto kExecutable = L"%HOMEDRIVE%%HOMEPATH%\\AppData\\Local\\Microsoft\\WindowsApps\\InfiniteViewer.exe";
constexpr auto kShellPath = L"Shell:AppsFolder\3efc360b-74f9-4448-843e-0815d95c1b9d_3rjdpzxdvax94!App";

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

HRESULT GetExecutableDir(std::wstring* out_path, HWND parent) {
    wchar_t path[MAX_PATH];
    HMODULE hm = NULL;

    if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPWSTR) &DllMain, &hm) == 0)
    {
        int ret = GetLastError();
        fprintf(stderr, "GetModuleHandle failed, error = %d\n", ret);
        MessageBox(parent, L"GetModuleHandle failed", __MENU_TITLE, MB_OK);
        return E_FAIL;
    }
    if (GetModuleFileNameW(hm, path, sizeof(path) / 2) == 0)
    {
        int ret = GetLastError();
        fprintf(stderr, "GetModuleFileName failed, error = %d\n", ret);
        MessageBox(parent, L"GetModuleFileName failed", __MENU_TITLE, MB_OK);
        return E_FAIL;
    }
    const std::wstring path_str(path);
    const std::wstring dir = path_str.substr(0, path_str.find_last_of(L"/\\"));
    *out_path = dir;
    return S_OK;
}

HRESULT GetExecutablePath(std::wstring* out_path, HWND parent) {
    std::wstring dir;
    RETURN_IF_FAILED(GetExecutableDir(&dir, parent));
    *out_path = dir + L"\\InfiniteViewer.exe";
    *out_path = kExecutable;
    return S_OK;
}

HRESULT GetIconPath(std::wstring* out_path, HWND parent) {
    std::wstring executable;
    RETURN_IF_FAILED(GetExecutablePath(&executable, parent));
    *out_path = executable + L",-1";
    return S_OK;
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
        *icon = nullptr;
        std::wstring val;
        RETURN_IF_FAILED(GetIconPath(&val, nullptr));
        SHStrDupW(val.c_str(), icon);
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
    const wchar_t* Title() override { return __MENU_TITLE; }
    HRESULT DoInvoke(_In_opt_ IShellItemArray* selection) override {
        HWND parent = nullptr;
        if (m_site) {
            ComPtr<IOleWindow> oleWindow;
            RETURN_IF_FAILED(m_site.As(&oleWindow));
            RETURN_IF_FAILED(oleWindow->GetWindow(&parent));
        }



        if (selection) {
            std::wstring executable_path;
            RETURN_IF_FAILED(GetExecutablePath(&executable_path, parent));

            DWORD count;
            RETURN_IF_FAILED(selection->GetCount(&count));
            for (DWORD i = 0; i < count; ++i) {
                IShellItem* psi;
                LPWSTR itemName;
                selection->GetItemAt(i, &psi);
                RETURN_IF_FAILED(psi->GetDisplayName(SIGDN_FILESYSPATH, &itemName));

                executable_path = kExecutable;
                std::wstring command;
                if (command.find(L" ") != std::wstring::npos) {
                    command = L"\"" + executable_path + L"\"";
                }
                else {
                    command = executable_path;
                }

                STARTUPINFO si = {};
                PROCESS_INFORMATION pi = {};

                std::wstring params = command + L" " + itemName;
                // MessageBoxW(parent, params.c_str(), command.c_str(), MB_OK);
                if (CreateProcessW(
                    command.c_str(), (LPWSTR)params.c_str(),
                    nullptr, nullptr, false, 0, nullptr, nullptr, &si, &pi)) {
                    // MessageBoxW(parent, L"CreateProcessW OK", L"CreateProcessW OK", MB_OK);
                    WaitForSingleObject(pi.hProcess, INFINITE);
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                }
                else {
                    const std::wstring error = GetLastErrorAsString();
                    MessageBoxW(parent, L"Failed to CreateProcessW", error.c_str(), MB_OK);
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