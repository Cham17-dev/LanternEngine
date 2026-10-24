// Windows CLANG64 build: headers some vendored sources (FSR 3 SDK) expect to arrive
// transitively but newer libc++ no longer includes (_countof, placement new, log2).
#pragma once
#include <cstdio>
#include <cstdlib>
#include <cwchar>
#include <new>
#include <cmath>
#ifndef _countof
#define _countof(a) (sizeof(a) / sizeof((a)[0]))
#endif
