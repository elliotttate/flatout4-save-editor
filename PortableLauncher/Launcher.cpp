// The top-level FlatOut4SaveEditor.exe of the portable ZIP. The WinUI app and its runtime (hundreds of
// files) live in the App folder beside it; this starts App\FlatOut4SaveEditor.exe with the same
// arguments and exits. It shows no window of its own unless something is missing.
#include <windows.h>
#include <string>
#include <vector>

namespace {
const wchar_t* const Caption = L"FlatOut 4 Save Editor";

int Fail(const std::wstring& message, DWORD code) {
    MessageBoxW(nullptr, message.c_str(), Caption, MB_OK | MB_ICONERROR);
    return static_cast<int>(code);
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int) {
    // Resolve beside this exe: Explorer, shortcuts and file associations pick any working directory.
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size())
        return Fail(L"Couldn't find where FlatOut 4 Save Editor is running from.", ERROR_BAD_PATHNAME);
    const std::wstring self(buffer.data(), length);
    const auto slash = self.find_last_of(L"\\/");
    if (slash == std::wstring::npos)
        return Fail(L"Couldn't find FlatOut 4 Save Editor's folder.", ERROR_BAD_PATHNAME);

    const std::wstring appFolder = self.substr(0, slash + 1) + L"App";
    const std::wstring app = appFolder + L"\\FlatOut4SaveEditor.exe";
    const DWORD attributes = GetFileAttributesW(app.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
        return Fail(L"The App folder is missing or incomplete.\n\n"
                    L"Extract the whole ZIP and keep the App folder next to FlatOut4SaveEditor.exe.",
                    ERROR_FILE_NOT_FOUND);

    std::wstring command = L"\"" + app + L"\"";
    if (arguments && *arguments)
        command += L" " + std::wstring(arguments);

    // Let the editor's window come to the front even though this process started it.
    AllowSetForegroundWindow(ASFW_ANY);
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(app.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        appFolder.c_str(), &startup, &process)) {
        const DWORD code = GetLastError();
        return Fail(L"Couldn't start FlatOut 4 Save Editor (Windows error " + std::to_wstring(code) +
                        L").\n\nExtract the whole ZIP and keep the App folder next to FlatOut4SaveEditor.exe.",
                    code);
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}
