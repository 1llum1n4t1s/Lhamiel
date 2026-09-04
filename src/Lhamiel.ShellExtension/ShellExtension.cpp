#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>

#include <new>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

#include <winrt/base.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Management.Deployment.h>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "windowsapp.lib")

namespace
{
    constexpr wchar_t ExtractMenuText[] = L"Lhamielで展開";
    constexpr wchar_t CompressMenuText[] = L"Lhamielで圧縮";
    constexpr wchar_t ApplicationFileName[] = L"Lhamiel.exe";
    constexpr wchar_t ContextMenuStateKey[] = L"Software\\Classes\\Lhamiel.ContextMenu";
    constexpr wchar_t ExtractEnabledValueName[] = L"ExtractEnabled";
    constexpr wchar_t CompressEnabledValueName[] = L"CompressEnabled";
    constexpr CLSID ExtractCommandClsid =
    { 0xabb8423c, 0xa40b, 0x4259, { 0x9f, 0x8a, 0x6c, 0x62, 0x43, 0x5c, 0x29, 0xca } };
    constexpr CLSID CompressCommandClsid =
    { 0xe1856df5, 0x177c, 0x4a12, { 0xa9, 0xb5, 0xf3, 0xd1, 0xc6, 0x3c, 0x9d, 0x1b } };

    enum class OperationMode
    {
        Extract,
        Compress,
    };

    HMODULE moduleHandle = nullptr;
    long moduleReferenceCount = 0;

    void AddModuleReference() noexcept
    {
        InterlockedIncrement(&moduleReferenceCount);
    }

    void ReleaseModuleReference() noexcept
    {
        InterlockedDecrement(&moduleReferenceCount);
    }

    HRESULT GetApplicationPath(std::wstring& applicationPath)
    {
        std::wstring modulePath(32768, L'\0');
        const DWORD length = GetModuleFileNameW(moduleHandle, modulePath.data(), static_cast<DWORD>(modulePath.size()));
        if (length == 0)
            return HRESULT_FROM_WIN32(GetLastError());
        if (length >= modulePath.size())
            return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);

        modulePath.resize(length);
        const auto separator = modulePath.find_last_of(L"\\/");
        if (separator == std::wstring::npos)
            return E_UNEXPECTED;

        applicationPath.assign(modulePath, 0, separator + 1);
        applicationPath.append(ApplicationFileName);
        return S_OK;
    }

    std::wstring QuoteCommandLineArgument(std::wstring_view argument)
    {
        std::wstring quoted;
        quoted.reserve(argument.size() + 2);
        quoted.push_back(L'"');

        size_t backslashCount = 0;
        for (const wchar_t character : argument)
        {
            if (character == L'\\')
            {
                ++backslashCount;
                continue;
            }

            if (character == L'"')
            {
                quoted.append(backslashCount * 2 + 1, L'\\');
                quoted.push_back(L'"');
            }
            else
            {
                quoted.append(backslashCount, L'\\');
                quoted.push_back(character);
            }
            backslashCount = 0;
        }

        quoted.append(backslashCount * 2, L'\\');
        quoted.push_back(L'"');
        return quoted;
    }

    bool IsOperationEnabled(OperationMode mode) noexcept
    {
        DWORD enabled = 0;
        DWORD size = sizeof(enabled);
        const wchar_t* valueName = mode == OperationMode::Extract
            ? ExtractEnabledValueName
            : CompressEnabledValueName;
        return RegGetValueW(
            HKEY_CURRENT_USER,
            ContextMenuStateKey,
            valueName,
            RRF_RT_REG_DWORD,
            nullptr,
            &enabled,
            &size) == ERROR_SUCCESS
            && enabled != 0;
    }

    HRESULT LaunchLhamiel(IShellItemArray* selectedItems, OperationMode mode)
    {
        if (selectedItems == nullptr)
            return E_INVALIDARG;

        DWORD itemCount = 0;
        HRESULT result = selectedItems->GetCount(&itemCount);
        if (FAILED(result) || itemCount == 0)
            return FAILED(result) ? result : E_INVALIDARG;

        std::wstring applicationPath;
        result = GetApplicationPath(applicationPath);
        if (FAILED(result))
            return result;

        std::wstring commandLine = QuoteCommandLineArgument(applicationPath);
        commandLine.append(mode == OperationMode::Extract ? L" --extract" : L" --compress");
        for (DWORD index = 0; index < itemCount; ++index)
        {
            IShellItem* item = nullptr;
            result = selectedItems->GetItemAt(index, &item);
            if (FAILED(result))
                return result;

            PWSTR itemPath = nullptr;
            result = item->GetDisplayName(SIGDN_FILESYSPATH, &itemPath);
            item->Release();
            if (FAILED(result) || itemPath == nullptr)
            {
                CoTaskMemFree(itemPath);
                return FAILED(result) ? result : E_UNEXPECTED;
            }

            const std::wstring quotedPath = QuoteCommandLineArgument(itemPath);
            CoTaskMemFree(itemPath);

            // CreateProcessW のコマンドライン上限（終端 NUL を含め 32,767 文字）を守る。
            if (commandLine.size() + quotedPath.size() + 2 >= 32767)
                return HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);

            commandLine.push_back(L' ');
            commandLine.append(quotedPath);
        }

        std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
        mutableCommandLine.push_back(L'\0');

        std::wstring workingDirectory = applicationPath;
        const auto separator = workingDirectory.find_last_of(L"\\/");
        workingDirectory.resize(separator);

        STARTUPINFOW startupInfo{ sizeof(startupInfo) };
        PROCESS_INFORMATION processInformation{};
        if (!CreateProcessW(
            applicationPath.c_str(),
            mutableCommandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_UNICODE_ENVIRONMENT,
            nullptr,
            workingDirectory.c_str(),
            &startupInfo,
            &processInformation))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        CloseHandle(processInformation.hThread);
        CloseHandle(processInformation.hProcess);
        return S_OK;
    }

    class ExplorerCommand final : public IExplorerCommand
    {
    public:
        explicit ExplorerCommand(OperationMode mode) noexcept : mode_(mode)
        {
            AddModuleReference();
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
        {
            if (object == nullptr)
                return E_POINTER;

            *object = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown) || IsEqualIID(interfaceId, __uuidof(IExplorerCommand)))
                *object = static_cast<IExplorerCommand*>(this);
            else
                return E_NOINTERFACE;

            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return InterlockedIncrement(&referenceCount_);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const long referenceCount = InterlockedDecrement(&referenceCount_);
            if (referenceCount == 0)
                delete this;
            return referenceCount;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            if (title == nullptr)
                return E_POINTER;
            return SHStrDupW(mode_ == OperationMode::Extract ? ExtractMenuText : CompressMenuText, title);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr)
                return E_POINTER;

            *icon = nullptr;
            try
            {
                std::wstring applicationPath;
                const HRESULT result = GetApplicationPath(applicationPath);
                if (FAILED(result))
                    return result;

                applicationPath.append(L",0");
                return SHStrDupW(applicationPath.c_str(), icon);
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_FAIL;
            }
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* toolTip) override
        {
            if (toolTip != nullptr)
                *toolTip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* commandName) override
        {
            if (commandName == nullptr)
                return E_POINTER;
            *commandName = mode_ == OperationMode::Extract ? ExtractCommandClsid : CompressCommandClsid;
            return S_OK;
        }

        IFACEMETHODIMP GetState(IShellItemArray* selectedItems, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr)
                return E_POINTER;

            DWORD itemCount = 0;
            *state = IsOperationEnabled(mode_)
                && selectedItems != nullptr
                && SUCCEEDED(selectedItems->GetCount(&itemCount))
                && itemCount > 0
                ? ECS_ENABLED
                : ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* selectedItems, IBindCtx*) override
        {
            try
            {
                return LaunchLhamiel(selectedItems, mode_);
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_FAIL;
            }
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr)
                return E_POINTER;
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands != nullptr)
                *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        ~ExplorerCommand()
        {
            ReleaseModuleReference();
        }

        long referenceCount_ = 1;
        OperationMode mode_;
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        explicit ClassFactory(OperationMode mode) noexcept : mode_(mode)
        {
            AddModuleReference();
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** object) override
        {
            if (object == nullptr)
                return E_POINTER;

            *object = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown) || IsEqualIID(interfaceId, IID_IClassFactory))
                *object = static_cast<IClassFactory*>(this);
            else
                return E_NOINTERFACE;

            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return InterlockedIncrement(&referenceCount_);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const long referenceCount = InterlockedDecrement(&referenceCount_);
            if (referenceCount == 0)
                delete this;
            return referenceCount;
        }

        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID interfaceId, void** object) override
        {
            if (outer != nullptr)
                return CLASS_E_NOAGGREGATION;
            if (object == nullptr)
                return E_POINTER;

            *object = nullptr;
            auto* command = new (std::nothrow) ExplorerCommand(mode_);
            if (command == nullptr)
                return E_OUTOFMEMORY;

            const HRESULT result = command->QueryInterface(interfaceId, object);
            command->Release();
            return result;
        }

        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            lock ? AddModuleReference() : ReleaseModuleReference();
            return S_OK;
        }

    private:
        ~ClassFactory()
        {
            ReleaseModuleReference();
        }

        long referenceCount_ = 1;
        OperationMode mode_;
    };

    template<typename Operation>
    HRESULT RunPackageOperationOnMta(Operation&& operation)
    {
        HRESULT result = E_FAIL;
        try
        {
            std::thread worker([&result, operation = std::forward<Operation>(operation)]() mutable
            {
                bool apartmentInitialized = false;
                try
                {
                    winrt::init_apartment(winrt::apartment_type::multi_threaded);
                    apartmentInitialized = true;
                    result = operation();
                }
                catch (const winrt::hresult_error& error)
                {
                    result = error.code();
                }
                catch (...)
                {
                    result = E_FAIL;
                }

                if (apartmentInitialized)
                    winrt::uninit_apartment();
            });
            worker.join();
        }
        catch (const std::bad_alloc&)
        {
            result = E_OUTOFMEMORY;
        }
        catch (...)
        {
            result = E_FAIL;
        }
        return result;
    }

    HRESULT GetDeploymentResult(const winrt::Windows::Management::Deployment::DeploymentResult& deploymentResult)
    {
        const HRESULT extendedError = deploymentResult.ExtendedErrorCode();
        return FAILED(extendedError) ? extendedError : S_OK;
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleHandle = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

__control_entrypoint(DllExport)
STDAPI DllCanUnloadNow(void)
{
    return moduleReferenceCount == 0 ? S_OK : S_FALSE;
}

_Check_return_
STDAPI DllGetClassObject(
    _In_ REFCLSID classId,
    _In_ REFIID interfaceId,
    _Outptr_ LPVOID FAR* object)
{
    if (object == nullptr)
        return E_POINTER;
    *object = nullptr;
    OperationMode mode;
    if (IsEqualCLSID(classId, ExtractCommandClsid))
        mode = OperationMode::Extract;
    else if (IsEqualCLSID(classId, CompressCommandClsid))
        mode = OperationMode::Compress;
    else
        return CLASS_E_CLASSNOTAVAILABLE;

    auto* factory = new (std::nothrow) ClassFactory(mode);
    if (factory == nullptr)
        return E_OUTOFMEMORY;

    const HRESULT result = factory->QueryInterface(interfaceId, object);
    factory->Release();
    return result;
}

extern "C" __declspec(dllexport) HRESULT WINAPI LhamielRegisterSparsePackage(
    PCWSTR packageUri,
    PCWSTR externalLocationUri)
{
    if (packageUri == nullptr || externalLocationUri == nullptr)
        return E_INVALIDARG;

    try
    {
        const std::wstring packageUriCopy(packageUri);
        const std::wstring externalLocationUriCopy(externalLocationUri);
        return RunPackageOperationOnMta([packageUriCopy, externalLocationUriCopy]
        {
            using namespace winrt::Windows::Foundation;
            using namespace winrt::Windows::Management::Deployment;

            PackageManager packageManager;
            AddPackageOptions options;
            options.ExternalLocationUri(Uri(externalLocationUriCopy));
            options.ForceUpdateFromAnyVersion(true);
            options.DeferRegistrationWhenPackagesAreInUse(true);
            return GetDeploymentResult(packageManager.AddPackageByUriAsync(Uri(packageUriCopy), options).get());
        });
    }
    catch (const std::bad_alloc&)
    {
        return E_OUTOFMEMORY;
    }
    catch (...)
    {
        return E_FAIL;
    }
}

extern "C" __declspec(dllexport) HRESULT WINAPI LhamielUnregisterSparsePackage(PCWSTR packageFamilyName)
{
    if (packageFamilyName == nullptr || *packageFamilyName == L'\0')
        return E_INVALIDARG;

    try
    {
        const std::wstring packageFamilyNameCopy(packageFamilyName);
        return RunPackageOperationOnMta([packageFamilyNameCopy]
        {
            using namespace winrt::Windows::Management::Deployment;

            PackageManager packageManager;
            for (const auto& package : packageManager.FindPackagesForUserWithPackageTypes(
                L"", packageFamilyNameCopy, PackageTypes::Main))
            {
                const HRESULT result = GetDeploymentResult(packageManager.RemovePackageAsync(
                    package.Id().FullName(),
                    RemovalOptions::DeferRemovalWhenPackagesAreInUse).get());
                if (FAILED(result))
                    return result;
            }
            return S_OK;
        });
    }
    catch (const std::bad_alloc&)
    {
        return E_OUTOFMEMORY;
    }
    catch (...)
    {
        return E_FAIL;
    }
}
