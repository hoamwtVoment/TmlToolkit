#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

using hostfxr_handle = void *;
using hostfxr_initialize_for_runtime_config_fn = int32_t(__stdcall *)(const wchar_t *, const void *, hostfxr_handle *);
using hostfxr_get_runtime_delegate_fn = int32_t(__stdcall *)(hostfxr_handle, int, void **);
using hostfxr_close_fn = int32_t(__stdcall *)(hostfxr_handle);
using load_assembly_and_get_function_pointer_fn = int32_t(__stdcall *)(
	const wchar_t *,
	const wchar_t *,
	const wchar_t *,
	const wchar_t *,
	void *,
	void **);
using component_entry_point_fn = int32_t(__stdcall *)(void *, int32_t);

static HMODULE g_module;

static std::wstring BaseDirectory()
{
	wchar_t path[MAX_PATH] = {};
	GetModuleFileNameW(g_module, path, MAX_PATH);
	std::wstring result(path);
	const size_t slash = result.find_last_of(L"\\/");
	result.resize(slash == std::wstring::npos ? 0 : slash + 1);
	return result;
}

static void WriteStatus(const wchar_t *text)
{
	std::wstring path = BaseDirectory() + L"tml_bootstrap_status.txt";
	HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
	if (file == INVALID_HANDLE_VALUE)
		return;
	DWORD written = 0;
	WriteFile(file, text, static_cast<DWORD>(wcslen(text) * sizeof(wchar_t)), &written, nullptr);
	CloseHandle(file);
}

static DWORD WINAPI LoadTmlToolkit(LPVOID)
{
	std::wstring base = BaseDirectory();
	HMODULE hostfxr = GetModuleHandleW(L"hostfxr.dll");
	bool loadedHere = false;
	if (!hostfxr) {
		hostfxr = LoadLibraryW((base + L"hostfxr.dll").c_str());
		loadedHere = hostfxr != nullptr;
	}
	if (!hostfxr) {
		WriteStatus(L"hostfxr-missing");
		return 1;
	}

	auto initialize = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
		GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
	auto getDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
		GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
	auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));
	if (!initialize || !getDelegate || !close) {
		WriteStatus(L"hostfxr-api-missing");
		return 2;
	}

	hostfxr_handle context = nullptr;
	int32_t rc = initialize((base + L"TerrariaTmlToolkit.runtimeconfig.json").c_str(), nullptr, &context);
	if (rc < 0 || !context) {
		WriteStatus(L"runtime-init-failed");
		return 3;
	}

	load_assembly_and_get_function_pointer_fn loadAssembly = nullptr;
	rc = getDelegate(context, 5, reinterpret_cast<void **>(&loadAssembly));
	close(context);
	if (rc < 0 || !loadAssembly) {
		WriteStatus(L"delegate-failed");
		return 4;
	}

	component_entry_point_fn start = nullptr;
	const wchar_t *unmanagedCallersOnly = reinterpret_cast<const wchar_t *>(static_cast<intptr_t>(-1));
	rc = loadAssembly(
		(base + L"TerrariaTmlToolkit.Bootstrap.dll").c_str(),
		L"TerrariaTmlToolkit.EntryPoint, TerrariaTmlToolkit.Bootstrap",
		L"Start",
		unmanagedCallersOnly,
		nullptr,
		reinterpret_cast<void **>(&start));
	if (rc < 0 || !start) {
		WriteStatus(L"assembly-load-failed");
		return 5;
	}

	int result = start(nullptr, 0);
	WriteStatus(result == 0 ? L"ok" : L"managed-error");
	if (loadedHere)
		FreeLibrary(hostfxr);
	return result == 0 ? 0 : 6;
}

static DWORD WINAPI BootstrapThread(LPVOID)
{
	// The native DLL is only a one-shot bridge into the already running CoreCLR.
	// Keeping it mapped makes a later launcher run call LoadLibrary successfully
	// without executing DllMain again.  Unload after the managed entry point has
	// returned so a retry can actually run the bridge, while
	// FreeLibraryAndExitThread guarantees no instruction executes from an
	// unmapped image.
	DWORD result = LoadTmlToolkit(nullptr);
	if (result != 0) {
		// A failed load must be retryable after the underlying problem is
		// corrected, so unload only on failure.
		FreeLibraryAndExitThread(g_module, result);
	}
	// Keep the successfully loaded bridge mapped as an unambiguous per-process
	// marker.  The injector can now reject a second load instead of silently
	// reusing an old managed UI assembly with the same identity.
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
