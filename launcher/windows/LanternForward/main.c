/* Bloodborne Launcher.exe: the launcher's name before 0.9.34 beta 8, kept as this small forwarder
 * so shortcuts, taskbar pins and older launchers that update themselves by that name still open
 * the launcher, now "Lantern Engine.exe" beside this file. Arguments are passed on. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR arguments, int show) {
    (void)instance; (void)previous; (void)show;
    static wchar_t folder[32768], launcher[32768], line[32768 + 1024];
    const DWORD n = GetModuleFileNameW(NULL, folder, sizeof(folder) / sizeof(folder[0]));
    if (n == 0 || n >= sizeof(folder) / sizeof(folder[0])) return 1;
    wchar_t *slash = wcsrchr(folder, L'\\');
    if (slash) slash[1] = 0;
    if (_snwprintf(launcher, sizeof(launcher) / sizeof(launcher[0]), L"%lsLantern Engine.exe", folder) < 0 ||
        _snwprintf(line, sizeof(line) / sizeof(line[0]), L"\"%ls\" %ls", launcher, arguments ? arguments : L"") < 0)
        return 1;
    STARTUPINFOW startup;
    ZeroMemory(&startup, sizeof(startup));
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process;
    if (!CreateProcessW(launcher, line, NULL, NULL, FALSE, 0, NULL, folder, &startup, &process)) {
        MessageBoxW(NULL, L"Lantern Engine.exe was not found in this folder.\n\n"
                          L"Download the latest release and unpack it over this folder.",
                    L"Lantern Engine", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}
