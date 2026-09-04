// 製品と同じネイティブ実装を直接検証する。登録・プロセス起動は行わない。
#include "ShellExtension.cpp"
#include <iostream>
#include <stdexcept>

void Require(bool value, const char* message)
{
    if (!value)
        throw std::runtime_error(message);
}

int wmain()
{
    try
    {
        const std::wstring app = L"C:\\Program Files\\Lhamiel\\Lhamiel.exe";
        std::wstring command;
        {
            SelectionFile file;
            Require(SUCCEEDED(BuildLaunchCommand(app, OperationMode::Extract,
                { L"C:\\日本語 空白\\archive.exe" }, command, file)), "short command");
            Require(file.path.empty(), "short command must not use a file");
            Require(command == QuoteCommandLineArgument(app) + L" --extract \"C:\\日本語 空白\\archive.exe\"",
                "quoted short command");
        }
        std::vector<std::wstring> paths;
        {
            const auto prefix = QuoteCommandLineArgument(app) + L" --compress";
            SelectionFile exact;
            const std::wstring boundary(32766 - prefix.size() - 3, L'a');
            Require(SUCCEEDED(BuildLaunchCommand(app, OperationMode::Compress,
                { boundary }, command, exact)), "exact command limit");
            Require(command.size() == 32766 && exact.path.empty(), "exact limit stays direct");
        }
        std::wstring expected;
        for (int i = 0; i < 2000; ++i)
        {
            paths.push_back(L"C:\\日本語のフォルダー\\選択 " + std::to_wstring(i) + L".txt");
            expected.append(paths.back()).push_back(L'\0');
        }
        std::wstring generated;
        for (const auto mode : { OperationMode::Extract, OperationMode::Compress })
        {
            {
                SelectionFile file;
                Require(SUCCEEDED(BuildLaunchCommand(app, mode, paths, command, file)), "large selection");
                Require(!file.path.empty() && command.size() + 1 <= 32767, "bounded command");
                Require(command.find(L" --shell-selection ") != std::wstring::npos, "selection argument");
                generated = file.path;
                winrt::file_handle input(CreateFileW(file.path.c_str(), GENERIC_READ, FILE_SHARE_READ,
                    nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
                Require(bool(input), "open selection");
                std::wstring actual(expected.size(), L'\0');
                DWORD read = 0;
                const auto size = static_cast<DWORD>(actual.size() * sizeof(wchar_t));
                Require(ReadFile(input.get(), actual.data(), size, &read, nullptr) && read == size,
                    "read selection");
                Require(actual == expected, "complete ordered UTF-16 selection");
            }
            Require(GetFileAttributesW(generated.c_str()) == INVALID_FILE_ATTRIBUTES, "failure cleanup");
        }
        Require(LhamielIsSparsePackageCurrent(L"Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4",
            L"C:\\not-lhamiel", 0) == S_FALSE, "wrong registration is not current");
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        winrt::Windows::Management::Deployment::PackageManager manager;
        for (const auto& package : manager.FindPackagesForUserWithPackageTypes(
            L"", L"Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4",
            winrt::Windows::Management::Deployment::PackageTypes::Main))
        {
            const auto version = package.Id().Version();
            const UINT64 packed = (UINT64(version.Major) << 48) | (UINT64(version.Minor) << 32)
                | (UINT64(version.Build) << 16) | version.Revision;
            const auto directory = package.EffectiveExternalPath();
            const auto result = LhamielIsSparsePackageCurrent(
                L"Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4", directory.c_str(), packed);
            Require(result == (package.Status().VerifyIsOK() ? S_OK : S_FALSE), "installed package comparison");
            Require(LhamielIsSparsePackageCurrent(L"Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4",
                L"C:\\not-lhamiel", packed) == S_FALSE, "external path mismatch");
            Require(LhamielIsSparsePackageCurrent(L"Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4",
                directory.c_str(), 0) == S_FALSE, "version mismatch");
            std::cout << "PASS: installed package identity/path/version/status (read-only)\n";
        }
        std::cout << "PASS: short/large extract+compress, quoting, file format, cleanup, registration query\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
