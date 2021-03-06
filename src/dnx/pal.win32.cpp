// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include "stdafx.h"
#include "dnx.h"
#include "xplat.h"
#include "TraceWriter.h"
#include "utils.h"
#include <sstream>

std::wstring GetNativeBootstrapperDirectory()
{
    wchar_t buffer[MAX_PATH];
    DWORD dirLength = GetModuleFileName(NULL, buffer, MAX_PATH);
    for (dirLength--; dirLength >= 0 && buffer[dirLength] != _T('\\'); dirLength--);
    buffer[dirLength + 1] = _T('\0');
    return std::wstring(buffer);
}

void WaitForDebuggerToAttach()
{
    if (!IsDebuggerPresent())
    {
        ::_tprintf_s(_T("Process Id: %ld\r\n"), GetCurrentProcessId());
        ::_tprintf_s(_T("Waiting for the debugger to attach...\r\n"));

        // Do not use getchar() like in corerun because it doesn't work well with remote sessions
        while (!IsDebuggerPresent())
        {
            Sleep(250);
        }

        ::_tprintf_s(_T("Debugger attached.\r\n"));
    }
}

bool IsTracingEnabled()
{
    TCHAR szTrace[2];
    DWORD nEnvTraceSize = GetEnvironmentVariable(_T("DNX_TRACE"), szTrace, 2);
    bool m_fVerboseTrace = (nEnvTraceSize == 1);
    if (m_fVerboseTrace)
    {
        szTrace[1] = _T('\0');
        return _tcsnicmp(szTrace, _T("1"), 1) == 0;
    }

    return false;
}

void SetConsoleHost()
{
    TCHAR szConsoleHost[2];
    DWORD nEnvConsoleHostSize = GetEnvironmentVariable(_T("DNX_CONSOLE_HOST"), szConsoleHost, 2);
    if (nEnvConsoleHostSize == 0)
    {
        SetEnvironmentVariable(_T("DNX_CONSOLE_HOST"), _T("1"));
    }
}

BOOL GetAppBasePathFromEnvironment(LPTSTR pszAppBase)
{
    DWORD dwAppBase = GetEnvironmentVariable(_T("DNX_APPBASE"), pszAppBase, MAX_PATH);
    return dwAppBase != 0 && dwAppBase < MAX_PATH;
}

BOOL GetFullPath(LPCTSTR szPath, LPTSTR pszNormalizedPath)
{
    DWORD dwFullAppBase = GetFullPathName(szPath, MAX_PATH, pszNormalizedPath, nullptr);
    if (!dwFullAppBase)
    {
        ::_tprintf_s(_T("Failed to get full path of application base: %s\r\n"), szPath);
        return FALSE;
    }
    else if (dwFullAppBase > MAX_PATH)
    {
        ::_tprintf_s(_T("Full path of application base is too long\r\n"));
        return FALSE;
    }

    return TRUE;
}

int CallApplicationMain(const wchar_t* moduleName, const char* functionName, CALL_APPLICATION_MAIN_DATA* data, TraceWriter traceWriter)
{
    HMODULE hostModule;
    try
    {
        hostModule = LoadLibraryEx(moduleName, NULL, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!hostModule)
        {
            throw std::runtime_error(std::string("Failed to load: ")
                .append(dnx::utils::to_string(moduleName)));
        }

        traceWriter.Write(std::wstring(L"Loaded module: ").append(moduleName), true);

        auto pfnCallApplicationMain = reinterpret_cast<FnCallApplicationMain>(GetProcAddress(hostModule, functionName));
        if (!pfnCallApplicationMain)
        {
            std::ostringstream oss;
            oss << "Failed to find export '" << functionName << "' in " << dnx::utils::to_string(moduleName);
            throw std::runtime_error(oss.str());
        }

        traceWriter.Write(std::wstring(L"Found export: ").append(dnx::utils::to_wstring(functionName)), true);

        HRESULT hr = pfnCallApplicationMain(data);
        FreeLibrary(hostModule);
        return SUCCEEDED(hr) ? data->exitcode : hr;
    }
    catch (...)
    {
        if (hostModule)
        {
            FreeLibrary(hostModule);
        }

        throw;
    }
}
