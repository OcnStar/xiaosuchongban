#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
BUILD_DIR="$SCRIPT_DIR/.build"
SRC_ROOT="$REPO_ROOT/Verification/FUMI43_LlmStreaming"

DOTNET_HOST="${DOTNET_HOST:-}"
ROSLYN_CSC_DLL="${ROSLYN_CSC_DLL:-}"
NETCORE_APP_DIR="${NETCORE_APP_DIR:-}"
UNITY_NETSTANDARD_DLL="${UNITY_NETSTANDARD_DLL:-}"

try_dotnet_root() {
    candidate_root="$1"
    [ -d "$candidate_root" ] || return 1
    [ -x "$candidate_root/dotnet" ] || return 1

    latest_csc=""
    for csc_path in "$candidate_root"/sdk/*/Roslyn/bincore/csc.dll; do
        [ -f "$csc_path" ] && latest_csc="$csc_path"
    done
    [ -n "$latest_csc" ] || return 1

    latest_app=""
    for app_dir in "$candidate_root"/shared/Microsoft.NETCore.App/*; do
        [ -d "$app_dir" ] && latest_app="$app_dir"
    done
    [ -n "$latest_app" ] || return 1

    DOTNET_HOST="$candidate_root/dotnet"
    ROSLYN_CSC_DLL="$latest_csc"
    NETCORE_APP_DIR="$latest_app"
    return 0
}

try_unity_contents() {
    u_contents="$1"
    [ -d "$u_contents" ] || return 1
    [ -x "$u_contents/NetCoreRuntime/dotnet" ] || return 1
    [ -f "$u_contents/DotNetSdkRoslyn/csc.dll" ] || return 1

    latest_app=""
    for app_dir in "$u_contents"/NetCoreRuntime/shared/Microsoft.NETCore.App/*; do
        [ -d "$app_dir" ] && latest_app="$app_dir"
    done
    [ -n "$latest_app" ] || return 1

    DOTNET_HOST="$u_contents/NetCoreRuntime/dotnet"
    ROSLYN_CSC_DLL="$u_contents/DotNetSdkRoslyn/csc.dll"
    NETCORE_APP_DIR="$latest_app"
    if [ -f "$u_contents/NetStandard/ref/2.1.0/netstandard.dll" ]; then
        UNITY_NETSTANDARD_DLL="$u_contents/NetStandard/ref/2.1.0/netstandard.dll"
    fi
    return 0
}

if [ -z "$DOTNET_HOST" ] || [ -z "$ROSLYN_CSC_DLL" ] || [ -z "$NETCORE_APP_DIR" ]; then
    if command -v dotnet >/dev/null 2>&1; then
        bin_dotnet=$(command -v dotnet)
        real_dir=$(CDPATH= cd -- "$(dirname -- "$bin_dotnet")" && pwd)
        try_dotnet_root "$real_dir" || true
    fi
fi

if [ -z "$DOTNET_HOST" ] || [ -z "$ROSLYN_CSC_DLL" ] || [ -z "$NETCORE_APP_DIR" ]; then
    for d_root in "/opt/homebrew/share/dotnet" "/usr/local/share/dotnet" "${HOME:-}/.dotnet"; do
        [ -n "$d_root" ] || continue
        if try_dotnet_root "$d_root"; then
            break
        fi
    done
fi

if [ -z "$DOTNET_HOST" ] || [ -z "$ROSLYN_CSC_DLL" ] || [ -z "$NETCORE_APP_DIR" ]; then
    if [ -n "${UNITY_EDITOR_CONTENTS:-}" ]; then
        try_unity_contents "$UNITY_EDITOR_CONTENTS" || true
    fi
fi

if [ -z "$DOTNET_HOST" ] || [ -z "$ROSLYN_CSC_DLL" ] || [ -z "$NETCORE_APP_DIR" ]; then
    for u_dir in \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents \
        "${HOME:-}/Unity/Hub/Editor"/*/Editor/Data \
        /opt/unity/Editor/Data
    do
        [ -d "$u_dir" ] || continue
        try_unity_contents "$u_dir" || true
    done
fi

# Even when standalone .NET SDK is used, detect Unity NetStandard 2.1 ref if present for strict API verification.
if [ -z "$UNITY_NETSTANDARD_DLL" ]; then
    for ns_candidate in \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/NetStandard/ref/2.1.0/netstandard.dll \
        "${HOME:-}/Unity/Hub/Editor"/*/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll
    do
        if [ -f "$ns_candidate" ]; then
            UNITY_NETSTANDARD_DLL="$ns_candidate"
        fi
    done
fi

if [ -z "$DOTNET_HOST" ] || [ -z "$ROSLYN_CSC_DLL" ] || [ -z "$NETCORE_APP_DIR" ]; then
    echo "Error: Could not locate a .NET SDK or Unity Hub bundled NetCoreRuntime + Roslyn compiler." >&2
    echo "Install .NET SDK 6+ or Unity Editor, or set DOTNET_HOST, ROSLYN_CSC_DLL, and NETCORE_APP_DIR." >&2
    exit 1
fi

mkdir -p "$BUILD_DIR"
OUT_DLL="$BUILD_DIR/LlmStreamingProbe.dll"
RUNTIME_VER=$(basename -- "$NETCORE_APP_DIR")
MAJOR_VER=$(printf "%s" "$RUNTIME_VER" | cut -d. -f1)

NEEDS_BUILD=0
if [ ! -f "$OUT_DLL" ]; then
    NEEDS_BUILD=1
elif [ -n "$(find "$SRC_ROOT" -name '*.cs' -newer "$OUT_DLL" 2>/dev/null)" ]; then
    NEEDS_BUILD=1
fi

XIAOSU_NETSTANDARD21_VERIFIED=0
if [ -f "$BUILD_DIR/Xiaosu.CompanionAI.LLM.netstandard21.dll" ] && [ "$NEEDS_BUILD" -eq 0 ]; then
    XIAOSU_NETSTANDARD21_VERIFIED=1
fi

if [ "$NEEDS_BUILD" -eq 1 ]; then
    if [ -n "$UNITY_NETSTANDARD_DLL" ] && [ -f "$UNITY_NETSTANDARD_DLL" ]; then
        "$DOTNET_HOST" "$ROSLYN_CSC_DLL" \
            -nologo \
            -langversion:9.0 \
            -nostdlib \
            -r:"$UNITY_NETSTANDARD_DLL" \
            -target:library \
            -out:"$BUILD_DIR/Xiaosu.CompanionAI.LLM.netstandard21.dll" \
            "$SRC_ROOT"/Runtime/*.cs
        XIAOSU_NETSTANDARD21_VERIFIED=1
    fi

    RSP_FILE="$BUILD_DIR/refs.rsp"
    : > "$RSP_FILE"
    for dll in "$NETCORE_APP_DIR"/System.*.dll "$NETCORE_APP_DIR"/Microsoft.Win32.*.dll "$NETCORE_APP_DIR"/netstandard.dll "$NETCORE_APP_DIR"/Microsoft.CSharp.dll; do
        [ -f "$dll" ] || continue
        base_dll=$(basename -- "$dll")
        case "$base_dll" in
            *.Native.dll) ;;
            *) printf -- '-r:"%s"\n' "$dll" >> "$RSP_FILE" ;;
        esac
    done

    "$DOTNET_HOST" "$ROSLYN_CSC_DLL" \
        -nologo \
        -nostdlib \
        @"$RSP_FILE" \
        -target:exe \
        -out:"$OUT_DLL" \
        "$SRC_ROOT"/Runtime/*.cs \
        "$SRC_ROOT"/Probe/*.cs

    cat > "$BUILD_DIR/LlmStreamingProbe.runtimeconfig.json" <<EOF
{
  "runtimeOptions": {
    "tfm": "net${MAJOR_VER}.0",
    "rollForward": "LatestMajor",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "${RUNTIME_VER}"
    }
  }
}
EOF
fi

export XIAOSU_NETSTANDARD21_VERIFIED
exec "$DOTNET_HOST" "$OUT_DLL" "$@"
