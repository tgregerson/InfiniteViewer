#include "pch.h"

#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <sstream>
#include <string>
#include <vector>

#include <wil\resource.h>  // Windows Implementation Library

using namespace Microsoft::WRL;

// This implements a basic "Open With..." context menu entry.

// Update these for each application
//
// This UUID must match the com server in the app manifest. It can be randomly generated.
#define __UUID_STR "E1742A23-ED4B-4AED-9DD2-30963A98BFF7"
// Location of application keys in HKEY_CLASSES_ROOT
constexpr auto __REGISTRY_APP_ROOT = L"AppXt9vt9mttzxph799cagb17s7qxywnxpfn\\Application";
constexpr auto __EXECUTABLE_PATH = L"C:\\Users\\conve\\AppData\\Local\\WindowsApps\\InfiniteViewer.exe";
constexpr auto __MENU_TITLE = L"Browse2";

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
        return E_NOTIMPL;
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

            DWORD count;
            RETURN_IF_FAILED(selection->GetCount(&count));

            /*
            wchar_t appname_buf[1028];
            DWORD size = sizeof(appname_buf);
            RETURN_IF_FAILED(RegGetValue(
                HKEY_CLASSES_ROOT, __REGISTRY_APP_ROOT, L"AppUserModelID",
                RRF_RT_REG_EXPAND_SZ | RRF_NOEXPAND, NULL, appname_buf, &size
            ));

            std::wstring executable_path = L"explorer.exe shell:appsFolder\\" + std::wstring(appname_buf);
            */
            std::wstring executable_path = __EXECUTABLE_PATH;
            for (DWORD i = 0; i < count; ++i) {
                IShellItem* psi;
                LPWSTR itemName;
                selection->GetItemAt(i, &psi);
                RETURN_IF_FAILED(psi->GetDisplayName(SIGDN_FILESYSPATH, &itemName));

                std::wstring command = executable_path + L" " + std::wstring(itemName);

                STARTUPINFO si = {};
                PROCESS_INFORMATION pi = {};

                CreateProcess(
                    nullptr, (LPWSTR)command.c_str(),
                    nullptr, nullptr, false, 0, nullptr, nullptr, &si, &pi);
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