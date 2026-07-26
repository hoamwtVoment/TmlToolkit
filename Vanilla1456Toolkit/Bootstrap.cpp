#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <string>

struct ICLRRuntimeHost : public IUnknown
{
	virtual HRESULT STDMETHODCALLTYPE Start() = 0;
	virtual HRESULT STDMETHODCALLTYPE Stop() = 0;
	virtual HRESULT STDMETHODCALLTYPE SetHostControl(IUnknown *hostControl) = 0;
	virtual HRESULT STDMETHODCALLTYPE GetCLRControl(void **clrControl) = 0;
	virtual HRESULT STDMETHODCALLTYPE UnloadAppDomain(DWORD appDomainId, BOOL waitUntilDone) = 0;
	virtual HRESULT STDMETHODCALLTYPE ExecuteInAppDomain(DWORD appDomainId, void *callback, void *cookie) = 0;
	virtual HRESULT STDMETHODCALLTYPE GetCurrentAppDomainId(DWORD *appDomainId) = 0;
	virtual HRESULT STDMETHODCALLTYPE ExecuteApplication(
		LPCWSTR appFullName,
		DWORD manifestPathCount,
		LPCWSTR *manifestPaths,
		DWORD activationDataCount,
		LPCWSTR *activationData,
		int *returnValue) = 0;
	virtual HRESULT STDMETHODCALLTYPE ExecuteInDefaultAppDomain(
		LPCWSTR assemblyPath,
		LPCWSTR typeName,
		LPCWSTR methodName,
		LPCWSTR argument,
		DWORD *returnValue) = 0;
};

typedef HRESULT(STDAPICALLTYPE *CorBindToRuntimeExFn)(
	LPCWSTR version,
	LPCWSTR buildFlavor,
	DWORD startupFlags,
	REFCLSID clsid,
	REFIID iid,
	LPVOID *object);

static const GUID CLSID_CLRRuntimeHost_Local =
	{0x90F1A06E, 0x7712, 0x4762, {0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02}};
static const GUID IID_ICLRRuntimeHost_Local =
	{0x90F1A06C, 0x7712, 0x4762, {0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02}};

static HMODULE g_module;

static void WriteBootstrapStatus(DWORD code)
{
	wchar_t modulePath[MAX_PATH] = {};
	if (!GetModuleFileNameW(g_module, modulePath, MAX_PATH))
		return;
	std::wstring path(modulePath);
	const size_t slash = path.find_last_of(L"\\/");
	path.resize(slash == std::wstring::npos ? 0 : slash + 1);
	path += L"bootstrap_status.txt";
	HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
	if (file == INVALID_HANDLE_VALUE)
		return;
	char text[32] = {};
	int length = wsprintfA(text, "%08lX", code);
	DWORD written = 0;
	WriteFile(file, text, static_cast<DWORD>(length), &written, nullptr);
	CloseHandle(file);
}

static DWORD WINAPI LoadManagedToolkit(LPVOID)
{
	wchar_t modulePath[MAX_PATH] = {};
	if (!GetModuleFileNameW(g_module, modulePath, MAX_PATH)) {
		WriteBootstrapStatus(101);
		return 1;
	}

	std::wstring managedPath(modulePath);
	const size_t slash = managedPath.find_last_of(L"\\/");
	managedPath.resize(slash == std::wstring::npos ? 0 : slash + 1);
	managedPath += L"Terraria1456Toolkit.Managed.dll";

	HMODULE mscoree = LoadLibraryW(L"mscoree.dll");
	if (!mscoree) {
		WriteBootstrapStatus(102);
		return 2;
	}
	CorBindToRuntimeExFn bindRuntime = reinterpret_cast<CorBindToRuntimeExFn>(
		GetProcAddress(mscoree, "CorBindToRuntimeEx"));
	if (!bindRuntime) {
		FreeLibrary(mscoree);
		WriteBootstrapStatus(103);
		return 2;
	}

	ICLRRuntimeHost *runtime = nullptr;
	HRESULT hr = bindRuntime(
		L"v4.0.30319",
		L"wks",
		0,
		CLSID_CLRRuntimeHost_Local,
		IID_ICLRRuntimeHost_Local,
		reinterpret_cast<void **>(&runtime));
	if (FAILED(hr) || runtime == nullptr) {
		FreeLibrary(mscoree);
		WriteBootstrapStatus(static_cast<DWORD>(hr));
		return 2;
	}

	hr = runtime->Start();
	if (FAILED(hr)) {
		runtime->Release();
		FreeLibrary(mscoree);
		WriteBootstrapStatus(static_cast<DWORD>(hr));
		return 3;
	}

	DWORD result = 0;
	hr = runtime->ExecuteInDefaultAppDomain(
		managedPath.c_str(),
		L"Terraria1456Toolkit.EntryPoint",
		L"Start",
		L"",
		&result);
	runtime->Release();
	FreeLibrary(mscoree);
	DWORD status = FAILED(hr) ? static_cast<DWORD>(hr) : result;
	WriteBootstrapStatus(status);
	return FAILED(hr) ? 4 : result;
}

static DWORD WINAPI BootstrapThread(LPVOID parameter)
{
	const DWORD result = LoadManagedToolkit(parameter);
	HMODULE module = g_module;
	// The bootstrap has no native callbacks after EntryPoint.Start returns.
	// Drop the LoadLibrary reference so another launcher invocation can enter
	// DllMain again and either restore the existing form or retry a failed start.
	FreeLibraryAndExitThread(module, result);
	return result;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
	if (reason == DLL_PROCESS_ATTACH) {
		g_module = instance;
		DisableThreadLibraryCalls(instance);
		HANDLE thread = CreateThread(nullptr, 0, BootstrapThread, nullptr, 0, nullptr);
		if (thread)
			CloseHandle(thread);
	}
	return TRUE;
}
